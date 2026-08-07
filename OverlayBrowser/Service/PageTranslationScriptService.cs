using System.Text.Json;
using CefSharp;

namespace OverlayBrowser.Service;

/// <summary>
/// ページ内文章の抽出と翻訳結果反映に使用するJavaScriptを作成する。
/// </summary>
public sealed class PageTranslationScriptService
{
    /// <summary>
    /// 表示中ページから翻訳対象の文字ノードを取得するスクリプト。
    /// </summary>
    public string CollectPageTextNodesScript { get; } = """
        (() => {
            const excludedTags = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'TEXTAREA', 'INPUT', 'SELECT', 'OPTION', 'CODE', 'PRE', 'SVG']);
            const textNodes = [];
            const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, {
                acceptNode(node) {
                    const parent = node.parentElement;
                    if (!parent || excludedTags.has(parent.tagName) || parent.isContentEditable || !node.nodeValue.trim()) {
                        return NodeFilter.FILTER_REJECT;
                    }

                    const style = window.getComputedStyle(parent);
                    return style.display === 'none' || style.visibility === 'hidden'
                        ? NodeFilter.FILTER_REJECT
                        : NodeFilter.FILTER_ACCEPT;
                }
            });

            while (walker.nextNode()) {
                textNodes.push(walker.currentNode);
            }

            window.__overlayBrowserTextNodes = textNodes;
            return JSON.stringify(textNodes.map((node, index) => ({ id: index, text: node.nodeValue })));
        })();
        """;

    /// <summary>
    /// Geminiの翻訳結果を抽出元の文字ノードへ反映するスクリプトを作成する。
    /// </summary>
    /// <param name="sourceSegments">翻訳前に取得した文字ノード一覧。</param>
    /// <param name="translations">ノードIDに対応した翻訳結果。</param>
    /// <returns>ページへ実行するJavaScript。</returns>
    public string CreateApplyScript(
        IReadOnlyList<GeminiTranslationService.PageTextSegment> sourceSegments,
        IReadOnlyList<GeminiTranslationService.PageTextSegment> translations)
    {
        var sourceTextById = sourceSegments.ToDictionary(segment => segment.Id, segment => segment.Text);
        var replacements = translations
            .Where(translation => sourceTextById.ContainsKey(translation.Id))
            .Select(translation => new PageTextReplacement(
                translation.Id,
                sourceTextById[translation.Id],
                translation.Text))
            .ToList();
        var replacementJson = JsonSerializer.Serialize(
            replacements,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return $$"""
            (() => {
                const textNodes = window.__overlayBrowserTextNodes;
                const replacements = {{replacementJson}};
                const excludedTags = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'TEXTAREA', 'INPUT', 'SELECT', 'OPTION', 'CODE', 'PRE', 'SVG']);
                const usedNodes = new Set();

                const findCurrentTextNode = (sourceText) => {
                    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, {
                        acceptNode(node) {
                            const parent = node.parentElement;
                            if (!parent || excludedTags.has(parent.tagName) || parent.isContentEditable || usedNodes.has(node)) {
                                return NodeFilter.FILTER_REJECT;
                            }

                            return node.nodeValue === sourceText
                                ? NodeFilter.FILTER_ACCEPT
                                : NodeFilter.FILTER_REJECT;
                        }
                    });
                    return walker.nextNode();
                };

                let updatedCount = 0;
                for (const replacement of replacements) {
                    let node = Array.isArray(textNodes) ? textNodes[replacement.id] : null;
                    if (!node || !node.parentElement || node.nodeValue !== replacement.sourceText || usedNodes.has(node)) {
                        node = findCurrentTextNode(replacement.sourceText);
                    }

                    if (node && typeof replacement.translatedText === 'string') {
                        node.nodeValue = replacement.translatedText;
                        usedNodes.add(node);
                        updatedCount++;
                    }
                }

                return JSON.stringify({ updatedCount, requestedCount: replacements.length });
            })();
            """;
    }

    /// <summary>
    /// JavaScriptが返したページ反映件数を読み取る。
    /// </summary>
    /// <param name="application">ページ反映スクリプトの実行結果。</param>
    /// <param name="appliedCount">反映できた文字ノード数。</param>
    /// <returns>実行結果を読み取れた場合はtrue。</returns>
    public bool TryGetApplicationResult(JavascriptResponse application, out int appliedCount)
    {
        appliedCount = 0;
        if (!application.Success || application.Result is not string resultJson)
        {
            return false;
        }

        try
        {
            var result = JsonSerializer.Deserialize<TranslationApplicationResult>(
                resultJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result is null || result.RequestedCount == 0)
            {
                return false;
            }

            appliedCount = result.UpdatedCount;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 元の文字列と翻訳後の文字列を対応付ける。
    /// </summary>
    /// <param name="Id">抽出時の文字ノードID。</param>
    /// <param name="SourceText">翻訳前の文字列。</param>
    /// <param name="TranslatedText">Geminiが返した翻訳後の文字列。</param>
    private sealed record PageTextReplacement(int Id, string SourceText, string TranslatedText);

    /// <summary>
    /// ページ反映スクリプトの結果を表す。
    /// </summary>
    /// <param name="UpdatedCount">翻訳を反映した文字ノード数。</param>
    /// <param name="RequestedCount">反映を依頼した文字ノード数。</param>
    private sealed record TranslationApplicationResult(int UpdatedCount, int RequestedCount);
}
