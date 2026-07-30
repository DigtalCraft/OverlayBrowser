namespace OverlayBrowser.Model;

/// <summary>
/// アプリ終了後も引き継ぐ表示設定とブックマークを保持する。
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// 前回表示していた URL。
    /// </summary>
    public string LastUrl { get; set; } = "https://www.google.com/";

    /// <summary>
    /// 起動時とホーム操作で開く URL。未指定の場合は前回の URL を使用する。
    /// </summary>
    public string HomeUrl { get; set; } = string.Empty;

    /// <summary>
    /// 常に前面へ表示するかどうか。
    /// </summary>
    public bool IsTopmost { get; set; }

    /// <summary>
    /// ウィンドウ全体の不透明度。0.35 ～ 1.00 の範囲で保存する。
    /// </summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// Windowsへのサインイン時にタスクトレイへ常駐するかどうか。
    /// </summary>
    public bool IsStartWithWindows { get; set; }

    /// <summary>
    /// Geminiへ渡す翻訳結果の文体や補足方針。
    /// </summary>
    public string TranslationPersonalization { get; set; } = string.Empty;

    /// <summary>
    /// 保存済みのブックマーク一覧。
    /// </summary>
    public List<BookmarkItem> Bookmarks { get; set; } = [];
}
