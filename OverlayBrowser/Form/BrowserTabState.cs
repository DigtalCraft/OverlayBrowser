using System.Windows.Controls;
using CefSharp.Wpf;

namespace OverlayBrowser.Form;

/// <summary>
/// 1つのブラウザタブで使用する画面部品をまとめて保持する。
/// </summary>
public sealed class BrowserTabState : IDisposable
{
    /// <summary>
    /// タブ状態を初期化する。
    /// </summary>
    /// <param name="browser">タブに表示するブラウザ。</param>
    /// <param name="pageBackground">ブラウザの描画待ち中も表示領域を保持する背景。</param>
    /// <param name="loadingOverlay">読み込み中に表示する画面。</param>
    /// <param name="translationOverlay">Gemini翻訳中に表示する画面。</param>
    /// <param name="headerTextBlock">タブ見出しの表示欄。</param>
    public BrowserTabState(
        ChromiumWebBrowser browser,
        Border pageBackground,
        Border loadingOverlay,
        Border translationOverlay,
        TextBlock headerTextBlock)
    {
        Browser = browser;
        PageBackground = pageBackground;
        LoadingOverlay = loadingOverlay;
        TranslationOverlay = translationOverlay;
        HeaderTextBlock = headerTextBlock;
    }

    /// <summary>
    /// タブに表示するChromiumブラウザ。
    /// </summary>
    public ChromiumWebBrowser Browser { get; }

    /// <summary>
    /// ブラウザの表示領域を保持する背景。
    /// </summary>
    public Border PageBackground { get; }

    /// <summary>
    /// 読み込み中に表示するオーバーレイ。
    /// </summary>
    public Border LoadingOverlay { get; }

    /// <summary>
    /// Gemini翻訳の進行中に表示するオーバーレイ。
    /// </summary>
    public Border TranslationOverlay { get; }

    /// <summary>
    /// タブのタイトルを表示するテキスト欄。
    /// </summary>
    public TextBlock HeaderTextBlock { get; }

    /// <summary>
    /// タブで使用したブラウザを破棄する。
    /// </summary>
    public void Dispose()
    {
        Browser.Dispose();
    }
}
