using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CefSharp;
using CefSharp.Wpf;
using OverlayBrowser.Service;

namespace OverlayBrowser;

/// <summary>
/// アプリケーション全体のエントリポイントを管理する。
/// </summary>
public partial class App : System.Windows.Application
{
    private const string ClearBrowserDataArgumentPrefix = "--clear-browser-data=";
    private const string WaitForParentArgumentPrefix = "--wait-for-parent=";
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;
    private readonly string applicationDirectory;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    /// <summary>
    /// アプリ生成時にChromiumを初期化する。
    /// </summary>
    public App()
    {
        DeleteRequestedBrowserData();

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(Window_Loaded));

        applicationDirectory = GetApplicationDirectory();
        var startedFromWindows = Environment.GetCommandLineArgs().Any(argument =>
            string.Equals(
                argument,
                WindowsStartupService.StartupArgument,
                StringComparison.OrdinalIgnoreCase));
        WriteStartupLog(
            applicationDirectory,
            startedFromWindows ? "Windows自動起動を開始" : "通常起動を開始");

        if (startedFromWindows)
        {
            WriteStartupLog(applicationDirectory, "Chromium初期化を画面表示時まで保留");
            return;
        }

        EnsureChromiumInitialized();
    }

    /// <summary>
    /// Chromiumが未初期化の場合に初期化する。
    ///
    /// Windows自動起動時はタスクトレイを先に表示し、
    /// メイン画面を表示する時までChromiumの起動を遅らせる。
    /// </summary>
    /// <returns>Chromiumを利用できる場合はtrue。</returns>
    public bool EnsureChromiumInitialized()
    {
        if (Cef.IsInitialized == true)
        {
            return true;
        }

        var cefSettings = new CefSettings
        {
            CachePath = Path.Combine(applicationDirectory, "CefSharpCache"),
            LogFile = Path.Combine(applicationDirectory, "cef.log"),
            LogSeverity = LogSeverity.Warning,
            AcceptLanguageList = "ja-JP,ja,en-US,en",
            WindowlessRenderingEnabled = true,
            BackgroundColor = Cef.ColorSetARGB(0, 0, 0, 0)
        };

        WriteStartupLog(applicationDirectory, "Chromium初期化を開始");
        if (!Cef.Initialize(cefSettings, performDependencyCheck: true, browserProcessHandler: null))
        {
            WriteStartupLog(applicationDirectory, "Chromium初期化に失敗");
            MessageBox.Show(
                "Chromiumブラウザを初期化できませんでした。アプリを再起動してください。",
                "ブラウザの準備ができません",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return false;
        }

        WriteStartupLog(applicationDirectory, "Chromium初期化が完了");
        return true;
    }

    /// <summary>
    /// アプリ固有データの保存先を取得する。
    /// </summary>
    /// <returns>ローカルアプリデータ内の保存先。</returns>
    private static string GetApplicationDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OverlayBrowser");
    }

    /// <summary>
    /// 起動処理の進行状況を診断ログへ記録する。
    /// </summary>
    /// <param name="applicationDirectory">アプリ固有データの保存先。</param>
    /// <param name="message">記録する内容。</param>
    private static void WriteStartupLog(string applicationDirectory, string message)
    {
        try
        {
            Directory.CreateDirectory(applicationDirectory);
            var logPath = Path.Combine(applicationDirectory, "startup.log");
            var logLine = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} " +
                          $"[PID:{Environment.ProcessId}] {message}{Environment.NewLine}";
            File.AppendAllText(logPath, logLine);
        }
        catch (IOException)
        {
            // 診断ログを書けない場合も、アプリの起動処理は継続する。
        }
        catch (UnauthorizedAccessException)
        {
            // 診断ログを書けない場合も、アプリの起動処理は継続する。
        }
    }

    /// <summary>
    /// 再起動時に、指定された履歴またはCookieのファイルをCEF初期化前に削除する。
    /// </summary>
    private static void DeleteRequestedBrowserData()
    {
        var arguments = Environment.GetCommandLineArgs();
        var dataArgument = arguments.FirstOrDefault(argument =>
            argument.StartsWith(ClearBrowserDataArgumentPrefix, StringComparison.OrdinalIgnoreCase));
        if (dataArgument is null)
        {
            return;
        }

        var parentArgument = arguments.FirstOrDefault(argument =>
            argument.StartsWith(WaitForParentArgumentPrefix, StringComparison.OrdinalIgnoreCase));
        if (parentArgument is not null &&
            int.TryParse(parentArgument[WaitForParentArgumentPrefix.Length..], out var parentProcessId))
        {
            WaitForParentProcess(parentProcessId);
        }

        var profileDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OverlayBrowser",
            "CefSharpCache",
            "Default");
        var dataType = dataArgument[ClearBrowserDataArgumentPrefix.Length..];
        var deleteSucceeded = dataType.Equals("history", StringComparison.OrdinalIgnoreCase)
            ? DeleteBrowserDataFiles(profileDirectory, "History", "History-journal", "History-wal", "History-shm")
            : dataType.Equals("cookies", StringComparison.OrdinalIgnoreCase)
                ? DeleteBrowserDataFiles(
                    Path.Combine(profileDirectory, "Network"),
                    "Cookies",
                    "Cookies-journal",
                    "Cookies-wal",
                    "Cookies-shm")
                : true;

        if (!deleteSucceeded)
        {
            MessageBox.Show(
                "対象のブラウザデータを削除できませんでした。アプリを終了してから手動で削除してください。",
                "データの削除",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 親プロセスの終了を待つ。
    /// </summary>
    /// <param name="processId">終了を待つ親プロセス番号。</param>
    private static void WaitForParentProcess(int processId)
    {
        try
        {
            using var parentProcess = Process.GetProcessById(processId);
            parentProcess.WaitForExit(15000);
        }
        catch (ArgumentException)
        {
            // 親プロセスがすでに終了している場合は、そのまま削除処理を続ける。
        }
        catch (InvalidOperationException)
        {
            // 親プロセスが取得できない場合は、そのまま削除処理を続ける。
        }
    }

    /// <summary>
    /// 指定したブラウザデータファイルを削除する。
    /// </summary>
    /// <param name="directoryPath">ファイルが保存されているフォルダ。</param>
    /// <param name="fileNames">削除するファイル名。</param>
    /// <returns>すべて削除できた場合はtrue。</returns>
    private static bool DeleteBrowserDataFiles(string directoryPath, params string[] fileNames)
    {
        var succeeded = true;
        foreach (var fileName in fileNames)
        {
            var filePath = Path.Combine(directoryPath, fileName);
            if (!File.Exists(filePath))
            {
                continue;
            }

            try
            {
                File.Delete(filePath);
            }
            catch (IOException)
            {
                succeeded = false;
            }
            catch (UnauthorizedAccessException)
            {
                succeeded = false;
            }
        }

        return succeeded;
    }

    /// <summary>
    /// 標準タイトルバーを使用するサブ画面へアプリ共通色を反映する。
    /// </summary>
    /// <param name="sender">表示されたウィンドウ。</param>
    /// <param name="e">Loadedイベントの引数。</param>
    private static void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window || window.WindowStyle == WindowStyle.None)
        {
            return;
        }

        var windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        // Windows 11の標準タイトルバーへ、メイン画面と同じ青黒・シアン配色を設定する。
        var darkMode = 1;
        var captionColor = 0x0024130B;
        var textColor = 0x00FFF6ED;
        var borderColor = 0x00F4D865;
        _ = DwmSetWindowAttribute(windowHandle, DwmUseImmersiveDarkMode, ref darkMode, sizeof(int));
        _ = DwmSetWindowAttribute(windowHandle, DwmCaptionColor, ref captionColor, sizeof(int));
        _ = DwmSetWindowAttribute(windowHandle, DwmTextColor, ref textColor, sizeof(int));
        _ = DwmSetWindowAttribute(windowHandle, DwmBorderColor, ref borderColor, sizeof(int));
    }

    /// <summary>
    /// アプリ終了時にChromiumのプロセスとキャッシュを安全に閉じる。
    /// </summary>
    /// <param name="e">終了時の引数。</param>
    protected override void OnExit(ExitEventArgs e)
    {
        if (Cef.IsInitialized == true)
        {
            Cef.Shutdown();
        }

        base.OnExit(e);
    }
}
