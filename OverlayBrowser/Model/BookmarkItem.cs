namespace OverlayBrowser.Model;

/// <summary>
/// ユーザーが登録したブラウザのブックマークを表す。
/// </summary>
public sealed class BookmarkItem
{
    /// <summary>
    /// ブックマーク一覧に表示する名前。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 遷移先の URL。
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// フォルダとして扱うかどうか。
    /// </summary>
    public bool IsFolder { get; set; }

    /// <summary>
    /// フォルダに含まれるブックマークまたは子フォルダ。
    /// </summary>
    public List<BookmarkItem> Children { get; set; } = [];
}
