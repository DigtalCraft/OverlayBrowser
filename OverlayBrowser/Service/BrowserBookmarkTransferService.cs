using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OverlayBrowser.Helper;
using OverlayBrowser.Model;

namespace OverlayBrowser.Service;

/// <summary>
/// Chrome、Edge、およびHTMLファイルとのブックマーク入出力を行う。
/// </summary>
public sealed class BrowserBookmarkTransferService
{
    private static readonly Regex HtmlBookmarkTagRegex = new(
        "<DT>\\s*<H3[^>]*>(?<folder>.*?)</H3>|<DT>\\s*<A\\s+[^>]*HREF=\"(?<url>[^\"]+)\"[^>]*>(?<name>.*?)</A>|</DL\\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Chrome または Edge の既定プロファイルからブックマークを読み込む。
    /// </summary>
    /// <param name="browserType">読み込み元のブラウザ。</param>
    /// <returns>読み込んだブックマーク一覧。</returns>
    public IReadOnlyList<BookmarkItem> ImportFromBrowser(BrowserType browserType)
    {
        return ImportFromChromiumFile(GetBrowserBookmarkFilePath(browserType));
    }

    /// <summary>
    /// Chrome または Edge が出力した HTML ブックマークファイルを読み込む。
    /// </summary>
    /// <param name="filePath">読み込み対象の HTML ファイルパス。</param>
    /// <returns>読み込んだブックマーク一覧。</returns>
    public IReadOnlyList<BookmarkItem> ImportFromHtml(string filePath)
    {
        var html = File.ReadAllText(filePath);
        var bookmarks = new List<BookmarkItem>();
        var destinationLists = new Stack<ICollection<BookmarkItem>>();
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        BookmarkItem? pendingFolder = null;
        destinationLists.Push(bookmarks);

        foreach (Match match in HtmlBookmarkTagRegex.Matches(html))
        {
            if (match.Groups["folder"].Success)
            {
                OpenPendingFolder(destinationLists, ref pendingFolder);
                pendingFolder = new BookmarkItem
                {
                    Name = ReadHtmlText(match.Groups["folder"].Value),
                    IsFolder = true
                };
                destinationLists.Peek().Add(pendingFolder);
                continue;
            }

            if (match.Groups["url"].Success)
            {
                OpenPendingFolder(destinationLists, ref pendingFolder);
                AddBookmark(
                    destinationLists.Peek(),
                    urls,
                    ReadHtmlText(match.Groups["name"].Value),
                    WebUtility.HtmlDecode(match.Groups["url"].Value).Trim());
                continue;
            }

            if (pendingFolder is not null)
            {
                // 空フォルダは子リストを開かず、そのまま階層に残す。
                pendingFolder = null;
            }
            else if (destinationLists.Count > 1)
            {
                destinationLists.Pop();
            }
        }

        return bookmarks;
    }

    /// <summary>
    /// ブックマークをChromeとEdgeが取り込める標準HTML形式で保存する。
    /// </summary>
    /// <param name="filePath">保存先の HTML ファイルパス。</param>
    /// <param name="bookmarks">書き出すブックマーク一覧。</param>
    public void ExportToHtml(string filePath, IEnumerable<BookmarkItem> bookmarks)
    {
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
        html.AppendLine("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">");
        html.AppendLine("<TITLE>Overlay Browser Bookmarks</TITLE>");
        html.AppendLine("<H1>Overlay Browser Bookmarks</H1>");
        html.AppendLine("<DL><p>");
        WriteHtmlBookmarks(html, bookmarks, 1);
        html.AppendLine("</DL><p>");

        File.WriteAllText(filePath, html.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>
    /// 指定の階層をHTMLブックマーク形式へ追加する。
    /// </summary>
    /// <param name="html">出力先の文字列。</param>
    /// <param name="bookmarks">出力対象のブックマーク。</param>
    /// <param name="depth">現在の入れ子の深さ。</param>
    private static void WriteHtmlBookmarks(StringBuilder html, IEnumerable<BookmarkItem> bookmarks, int depth)
    {
        var indent = new string(' ', depth * 4);
        foreach (var bookmark in bookmarks)
        {
            if (bookmark.IsFolder)
            {
                html.Append(indent)
                    .Append("<DT><H3>")
                    .Append(WebUtility.HtmlEncode(bookmark.Name))
                    .AppendLine("</H3>");
                html.Append(indent).AppendLine("<DL><p>");
                WriteHtmlBookmarks(html, bookmark.Children, depth + 1);
                html.Append(indent).AppendLine("</DL><p>");
                continue;
            }

            if (!UrlHelper.TryCreateUrl(bookmark.Url, out var normalizedUrl))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(bookmark.Name) ? normalizedUrl : bookmark.Name;
            html.Append(indent)
                .Append("<DT><A HREF=\"")
                .Append(WebUtility.HtmlEncode(normalizedUrl))
                .Append("\">")
                .Append(WebUtility.HtmlEncode(name))
                .AppendLine("</A>");
        }
    }

    /// <summary>
    /// Chromium形式のBookmarks JSONを再帰的に読み込む。
    /// </summary>
    /// <param name="filePath">読み込み対象のBookmarksファイルパス。</param>
    /// <returns>読み込んだブックマーク一覧。</returns>
    private static IReadOnlyList<BookmarkItem> ImportFromChromiumFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("roots", out var roots))
        {
            throw new InvalidDataException("Chrome または Edge のブックマークファイルではありません。");
        }

        var bookmarks = new List<BookmarkItem>();
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.EnumerateObject())
        {
            var bookmark = ReadNode(root.Value, urls);
            if (bookmark is not null)
            {
                bookmarks.Add(bookmark);
            }
        }

        return bookmarks;
    }

    /// <summary>
    /// ブックマークフォルダまたはURLノードを読み込み、表示用の階層へ変換する。
    /// </summary>
    /// <param name="node">現在処理中のJSONノード。</param>
    /// <param name="urls">重複を判定するURL一覧。</param>
    /// <returns>変換したブックマーク。対象外の場合はnull。</returns>
    private static BookmarkItem? ReadNode(JsonElement node, ISet<string> urls)
    {
        var type = node.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        var name = node.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;

        if (type == "url")
        {
            var url = node.TryGetProperty("url", out var urlElement) ? urlElement.GetString() ?? string.Empty : string.Empty;
            return CreateBookmark(urls, name, url);
        }

        if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var folder = new BookmarkItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? "ブックマーク" : name,
            IsFolder = true
        };
        foreach (var child in children.EnumerateArray())
        {
            var bookmark = ReadNode(child, urls);
            if (bookmark is not null)
            {
                folder.Children.Add(bookmark);
            }
        }

        return folder;
    }

    /// <summary>
    /// 有効かつ未登録のURLをブックマーク一覧へ追加する。
    /// </summary>
    /// <param name="bookmarks">追加先のブックマーク一覧。</param>
    /// <param name="urls">重複を判定するURL一覧。</param>
    /// <param name="name">表示名。</param>
    /// <param name="url">URL。</param>
    private static void AddBookmark(ICollection<BookmarkItem> bookmarks, ISet<string> urls, string name, string url)
    {
        var bookmark = CreateBookmark(urls, name, url);
        if (bookmark is not null)
        {
            bookmarks.Add(bookmark);
        }
    }

    /// <summary>
    /// URLを検証し、登録可能なブックマークを作成する。
    /// </summary>
    /// <param name="urls">重複を判定するURL一覧。</param>
    /// <param name="name">表示名。</param>
    /// <param name="url">URL。</param>
    /// <returns>登録できるブックマーク。登録しない場合はnull。</returns>
    private static BookmarkItem? CreateBookmark(ISet<string> urls, string name, string url)
    {
        if (!UrlHelper.TryCreateUrl(url, out var normalizedUrl) || !urls.Add(normalizedUrl))
        {
            return null;
        }

        return new BookmarkItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? normalizedUrl : name,
            Url = normalizedUrl
        };
    }

    /// <summary>
    /// HTML内のタグと文字参照を除いて表示名を取り出す。
    /// </summary>
    /// <param name="value">HTMLから取得した文字列。</param>
    /// <returns>表示に使う文字列。</returns>
    private static string ReadHtmlText(string value)
    {
        var text = Regex.Replace(value, "<.*?>", string.Empty);
        return WebUtility.HtmlDecode(text).Trim();
    }

    /// <summary>
    /// 直前に読み込んだフォルダを子要素の追加先として開く。
    /// </summary>
    /// <param name="destinationLists">現在の追加先一覧。</param>
    /// <param name="pendingFolder">開くフォルダ。</param>
    private static void OpenPendingFolder(Stack<ICollection<BookmarkItem>> destinationLists, ref BookmarkItem? pendingFolder)
    {
        if (pendingFolder is null)
        {
            return;
        }

        destinationLists.Push(pendingFolder.Children);
        pendingFolder = null;
    }

    /// <summary>
    /// 指定ブラウザの既定プロファイルにあるBookmarksファイルのパスを返す。
    /// </summary>
    /// <param name="browserType">対象ブラウザ。</param>
    /// <returns>Bookmarksファイルのパス。</returns>
    private static string GetBrowserBookmarkFilePath(BrowserType browserType)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var relativePath = browserType switch
        {
            BrowserType.Chrome => Path.Combine("Google", "Chrome", "User Data", "Default", "Bookmarks"),
            BrowserType.Edge => Path.Combine("Microsoft", "Edge", "User Data", "Default", "Bookmarks"),
            _ => throw new ArgumentOutOfRangeException(nameof(browserType), browserType, null)
        };

        return Path.Combine(localAppData, relativePath);
    }
}
