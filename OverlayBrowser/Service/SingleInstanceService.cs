using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace OverlayBrowser.Service;

/// <summary>
/// 同じWindowsセッションで起動するアプリを1つに制限し、後からの起動要求を渡す。
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\OverlayBrowser.SingleInstance";
    private readonly Mutex mutex = new(false, MutexName);
    private readonly CancellationTokenSource cancellation = new();
    private bool ownsMutex;

    /// <summary>
    /// このプロセスが最初の起動か確認し、起動中は占有する。
    /// </summary>
    public bool TryAcquire()
    {
        try
        {
            ownsMutex = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        return ownsMutex;
    }

    /// <summary>
    /// 最初のプロセスへ画面表示と起動対象を依頼する。
    /// </summary>
    public bool TrySend(string? launchTarget)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", GetPipeName(), PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            using var writer = new StreamWriter(client);
            writer.WriteLine(JsonSerializer.Serialize(launchTarget));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// 後から起動したプロセスの依頼を受け取る。
    /// </summary>
    public void StartListening(Action<string?> onOpenRequested)
    {
        _ = Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        GetPipeName(), PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(cancellation.Token);
                    using var reader = new StreamReader(server);
                    var request = await reader.ReadLineAsync(cancellation.Token);
                    if (request is not null)
                    {
                        onOpenRequested(JsonSerializer.Deserialize<string?>(request));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    // 送信側が途中で終了しても、次の起動要求を受け付ける。
                }
                catch (JsonException)
                {
                    // 不正な起動要求は破棄し、待ち受けを継続する。
                }
            }
        });
    }

    /// <summary>
    /// 待ち受けを止め、アプリの占有を解除する。
    /// </summary>
    public void Dispose()
    {
        cancellation.Cancel();
        cancellation.Dispose();
        if (ownsMutex)
        {
            mutex.ReleaseMutex();
        }

        mutex.Dispose();
    }

    private static string GetPipeName()
    {
        return $"OverlayBrowser.SingleInstance.{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
    }
}
