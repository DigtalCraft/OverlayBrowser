using System.ComponentModel;
using System.Diagnostics;

namespace OverlayBrowser.Service;

/// <summary>
/// 同じWindowsセッションで起動するアプリを1つに制限する。
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\OverlayBrowser.SingleInstance";
    private readonly Mutex mutex = new(false, MutexName);
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
    /// Mutexを持たない旧バージョンが更新前から動いている場合も検出する。
    /// </summary>
    public static bool HasEarlierInstance()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var name in new[] { "OverlayBrowser", "GameOverlayBrowser" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        // 同時起動で後から来たプロセスを先行プロセスと誤認しない。
                        if (process.Id != current.Id && process.SessionId == current.SessionId &&
                            process.StartTime <= current.StartTime)
                        {
                            return true;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // 調査中に終了したプロセスは対象外。
                    }
                    catch (Win32Exception)
                    {
                        // 別の権限で保護されているプロセスは読み取れない。
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// アプリの占有を解除する。
    /// </summary>
    public void Dispose()
    {
        if (ownsMutex)
        {
            mutex.ReleaseMutex();
        }

        mutex.Dispose();
    }
}
