using CefSharp.Handler;

namespace OverlayBrowser.Service;

/// <summary>
/// CEFの再起動要求をアプリで処理し、既定のChrome風ウィンドウを開かせない。
/// </summary>
public sealed class OverlayBrowserProcessHandler(Action onDuplicateLaunch) : BrowserProcessHandler
{
    /// <summary>
    /// CEFスレッドからアプリ側へ二重起動を通知する。
    /// </summary>
    protected override bool OnAlreadyRunningAppRelaunch(
        IReadOnlyDictionary<string, string> commandLine, string currentDirectory)
    {
        onDuplicateLaunch();
        return true;
    }
}
