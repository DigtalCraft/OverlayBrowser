using OverlayBrowser.Helper;
using OverlayBrowser.Model;

namespace OverlayBrowser.Service;

/// <summary>
/// ブックマーク階層の検索、移動、結合を行う。
/// </summary>
public sealed class BookmarkService
{
    /// <summary>
    /// 指定したブックマークを階層内から削除する。
    /// </summary>
    /// <param name="items">検索対象の一覧。</param>
    /// <param name="target">削除するブックマーク。</param>
    /// <returns>削除できた場合はtrue。</returns>
    public bool Remove(IList<BookmarkItem> items, BookmarkItem target)
    {
        if (items.Remove(target))
        {
            return true;
        }

        return items.Where(item => item.IsFolder).Any(item => Remove(item.Children, target));
    }

    /// <summary>
    /// 指定したブックマークを直接含む一覧を取得する。
    /// </summary>
    /// <param name="items">検索対象の一覧。</param>
    /// <param name="target">検索するブックマーク。</param>
    /// <returns>直接含む一覧。見つからない場合はnull。</returns>
    public IList<BookmarkItem>? FindContainingList(IList<BookmarkItem> items, BookmarkItem target)
    {
        if (items.Contains(target))
        {
            return items;
        }

        foreach (var folder in items.Where(item => item.IsFolder))
        {
            if (FindContainingList(folder.Children, target) is { } childList)
            {
                return childList;
            }
        }

        return null;
    }

    /// <summary>
    /// 親フォルダの配下に対象のブックマークが含まれるか確認する。
    /// </summary>
    /// <param name="parent">親として確認するブックマーク。</param>
    /// <param name="target">検索するブックマーク。</param>
    /// <returns>配下に含まれる場合はtrue。</returns>
    public bool Contains(BookmarkItem parent, BookmarkItem target)
    {
        return parent.Children.Any(child =>
            ReferenceEquals(child, target) || Contains(child, target));
    }

    /// <summary>
    /// 階層内のURLブックマーク件数を取得する。
    /// </summary>
    /// <param name="bookmarks">集計対象のブックマーク一覧。</param>
    /// <returns>URLブックマークの件数。</returns>
    public int CountUrls(IEnumerable<BookmarkItem> bookmarks)
    {
        return bookmarks.Sum(bookmark => bookmark.IsFolder ? CountUrls(bookmark.Children) : 1);
    }

    /// <summary>
    /// 階層内に登録されているURLを列挙する。
    /// </summary>
    /// <param name="bookmarks">検索対象のブックマーク一覧。</param>
    /// <returns>正規化済みのURL。</returns>
    public IEnumerable<string> GetUrls(IEnumerable<BookmarkItem> bookmarks)
    {
        foreach (var bookmark in bookmarks)
        {
            if (bookmark.IsFolder)
            {
                foreach (var childUrl in GetUrls(bookmark.Children))
                {
                    yield return childUrl;
                }

                continue;
            }

            if (UrlHelper.TryCreateUrl(bookmark.Url, out var normalizedUrl))
            {
                yield return normalizedUrl;
            }
        }
    }

    /// <summary>
    /// ブックマークを子要素も含めて複製する。
    /// </summary>
    /// <param name="bookmark">複製元のブックマーク。</param>
    /// <returns>複製したブックマーク。</returns>
    public BookmarkItem Clone(BookmarkItem bookmark)
    {
        return new BookmarkItem
        {
            Name = bookmark.Name,
            Url = bookmark.Url,
            IsFolder = bookmark.IsFolder,
            Children = bookmark.Children.Select(Clone).ToList()
        };
    }

    /// <summary>
    /// 読み込んだブックマークを保存先へ重複なく結合する。
    /// </summary>
    /// <param name="importedBookmarks">読み込んだブックマーク一覧。</param>
    /// <param name="destination">追加先の一覧。</param>
    /// <param name="registeredUrls">登録済みURL。</param>
    /// <returns>追加したURLの件数。</returns>
    public int Merge(
        IEnumerable<BookmarkItem> importedBookmarks,
        ICollection<BookmarkItem> destination,
        ISet<string> registeredUrls)
    {
        var addedCount = 0;
        foreach (var bookmark in importedBookmarks)
        {
            if (bookmark.IsFolder)
            {
                var folderName = string.IsNullOrWhiteSpace(bookmark.Name) ? "フォルダ" : bookmark.Name;
                var destinationFolder = destination.FirstOrDefault(item =>
                    item.IsFolder && string.Equals(item.Name, folderName, StringComparison.CurrentCultureIgnoreCase));
                if (destinationFolder is null)
                {
                    destinationFolder = new BookmarkItem { Name = folderName, IsFolder = true };
                    destination.Add(destinationFolder);
                }

                addedCount += Merge(bookmark.Children, destinationFolder.Children, registeredUrls);
                continue;
            }

            if (!UrlHelper.TryCreateUrl(bookmark.Url, out var normalizedUrl) || !registeredUrls.Add(normalizedUrl))
            {
                continue;
            }

            destination.Add(new BookmarkItem
            {
                Name = string.IsNullOrWhiteSpace(bookmark.Name) ? normalizedUrl : bookmark.Name,
                Url = normalizedUrl
            });
            addedCount++;
        }

        return addedCount;
    }
}
