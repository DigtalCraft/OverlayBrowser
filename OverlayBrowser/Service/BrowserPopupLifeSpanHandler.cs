using CefSharp;
using CefSharp.Handler;

namespace OverlayBrowser.Service;

/// <summary>
/// ブラウザが作成するポップアップ画面の開始と終了を通知する。
/// </summary>
public sealed class BrowserPopupLifeSpanHandler : LifeSpanHandler
{
    /// <summary>
    /// ポップアップ画面が作成された時に発生する。
    /// </summary>
    public event EventHandler? PopupOpened;

    /// <summary>
    /// ポップアップ画面が閉じられる時に発生する。
    /// </summary>
    public event EventHandler? PopupClosed;

    /// <summary>
    /// 作成されたブラウザがポップアップの場合に画面へ通知する。
    /// </summary>
    /// <param name="chromiumWebBrowser">ポップアップを開いたブラウザ。</param>
    /// <param name="browser">作成されたブラウザ。</param>
    protected override void OnAfterCreated(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
        base.OnAfterCreated(chromiumWebBrowser, browser);
        if (browser.IsPopup)
        {
            PopupOpened?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 閉じられるブラウザがポップアップの場合に画面へ通知する。
    /// </summary>
    /// <param name="chromiumWebBrowser">ポップアップを開いたブラウザ。</param>
    /// <param name="browser">閉じられるブラウザ。</param>
    protected override void OnBeforeClose(IWebBrowser chromiumWebBrowser, IBrowser browser)
    {
        if (browser.IsPopup)
        {
            PopupClosed?.Invoke(this, EventArgs.Empty);
        }

        base.OnBeforeClose(chromiumWebBrowser, browser);
    }
}
