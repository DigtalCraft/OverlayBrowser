using System.IO;
using System.Windows;
using CefSharp;
using CefSharp.Wpf;

namespace OverlayBrowser;

/// <summary>
/// アプリケーション全体のエントリポイントを管理する。
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>
    /// アプリ生成時にChromiumを初期化する。
    /// </summary>
    public App()
    {
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
