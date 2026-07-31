using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CefSharp;
using CefSharp.Wpf;

namespace OverlayBrowser;

/// <summary>
/// アプリケーション全体のエントリポイントを管理する。
/// </summary>
public partial class App : System.Windows.Application
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

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
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(Window_Loaded));

        var applicationDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OverlayBrowser");
        var cefSettings = new CefSettings
        {
            CachePath = Path.Combine(applicationDirectory, "CefSharpCache"),
            LogFile = Path.Combine(applicationDirectory, "cef.log"),
            LogSeverity = LogSeverity.Warning,
            AcceptLanguageList = "ja-JP,ja,en-US,en",
            WindowlessRenderingEnabled = true,
            BackgroundColor = Cef.ColorSetARGB(0, 0, 0, 0)
        };

        if (!Cef.Initialize(cefSettings, performDependencyCheck: true, browserProcessHandler: null))
        {
            MessageBox.Show(
                "Chromiumブラウザを初期化できませんでした。アプリを再起動してください。",
                "ブラウザの準備ができません",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
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
