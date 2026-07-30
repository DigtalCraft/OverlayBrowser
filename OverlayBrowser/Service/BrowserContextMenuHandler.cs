using CefSharp;
using CefSharp.Handler;

namespace OverlayBrowser.Service;

/// <summary>
/// ブラウザの右クリックメニューへページ翻訳操作を追加する。
/// </summary>
public sealed class BrowserContextMenuHandler : ContextMenuHandler
{
    private const CefMenuCommand TranslateCommand = (CefMenuCommand)((int)CefMenuCommand.UserFirst + 1);
    private const CefMenuCommand GeminiTranslateCommand = (CefMenuCommand)((int)CefMenuCommand.UserFirst + 2);

    /// <summary>
    /// ページ翻訳メニューが選択された時に発生する。
    /// </summary>
    /// <remarks>翻訳対象のブラウザはCefSharpのUIスレッドから渡される。</remarks>
    public event EventHandler<IWebBrowser>? PageTranslationRequested;

    /// <summary>
    /// Geminiによるカスタマイズ翻訳が選択された時に発生する。
    /// </summary>
    public event EventHandler<IWebBrowser>? GeminiPageTranslationRequested;

    /// <summary>
    /// 右クリックメニュー処理を初期化する。
    /// </summary>
    public BrowserContextMenuHandler()
    {
    }

    /// <summary>
    /// ページ翻訳項目を右クリックメニューへ追加する。
    /// </summary>
    /// <param name="chromiumWebBrowser">メニューを表示するブラウザ。</param>
    /// <param name="browser">CefSharpのブラウザインスタンス。</param>
    /// <param name="frame">メニューを開いたフレーム。</param>
    /// <param name="parameters">右クリック時の選択状態。</param>
    /// <param name="model">編集対象の右クリックメニュー。</param>
    protected override void OnBeforeContextMenu(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IContextMenuParams parameters,
        IMenuModel model)
    {
        base.OnBeforeContextMenu(chromiumWebBrowser, browser, frame, parameters, model);
        model.AddSeparator();
        model.AddItem(TranslateCommand, "ページを翻訳");
        model.AddItem(GeminiTranslateCommand, "Geminiでページを翻訳");
    }

    /// <summary>
    /// ページ翻訳メニューの選択をメイン画面へ通知する。
    /// </summary>
    /// <param name="chromiumWebBrowser">メニューを表示したブラウザ。</param>
    /// <param name="browser">CefSharpのブラウザインスタンス。</param>
    /// <param name="frame">メニューを開いたフレーム。</param>
    /// <param name="parameters">右クリック時の選択状態。</param>
    /// <param name="commandId">選択されたメニューコマンド。</param>
    /// <param name="eventFlags">選択時の修飾キー状態。</param>
    /// <returns>翻訳コマンドを処理した場合はtrue。</returns>
    protected override bool OnContextMenuCommand(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IContextMenuParams parameters,
        CefMenuCommand commandId,
        CefEventFlags eventFlags)
    {
        if (commandId == TranslateCommand)
        {
            PageTranslationRequested?.Invoke(this, chromiumWebBrowser);
            return true;
        }

        if (commandId == GeminiTranslateCommand)
        {
            GeminiPageTranslationRequested?.Invoke(this, chromiumWebBrowser);
            return true;
        }

        return base.OnContextMenuCommand(chromiumWebBrowser, browser, frame, parameters, commandId, eventFlags);
    }
}
