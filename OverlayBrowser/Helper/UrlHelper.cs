namespace OverlayBrowser.Helper;

/// <summary>
/// ユーザー入力をブラウザで開ける URL に整形する。
/// </summary>
public static class UrlHelper
{
    /// <summary>
    /// URL 入力を検証し、スキームが無い場合は HTTPS を補う。
    /// </summary>
    /// <param name="input">URL 入力欄の値。</param>
    /// <param name="url">遷移に使用できる URL。</param>
    /// <returns>URL を生成できた場合は true。</returns>
    public static bool TryCreateUrl(string? input, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var value = input.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = $"https://{value}";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        url = uri.AbsoluteUri;
        return true;
    }
}
