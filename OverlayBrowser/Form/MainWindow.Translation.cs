using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using CefSharp;
using CefSharp.Wpf;
using OverlayBrowser.Helper;
using OverlayBrowser.Service;

namespace OverlayBrowser.Form;

/// <summary>
/// メイン画面のページ翻訳操作を制御する。
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// CefSharpの右クリックメニューからページ翻訳を開始する。
    /// </summary>
    /// <param name="sender">翻訳メニューを通知した処理。</param>
    /// <param name="browser">翻訳対象のブラウザ。</param>
    private void BrowserContextMenuHandler_PageTranslationRequested(object? sender, IWebBrowser browser)
    {
        if (browser is not ChromiumWebBrowser chromiumWebBrowser)
        {
            return;
        }

        Dispatcher.InvokeAsync(() => TranslatePageInBrowser(chromiumWebBrowser));
    }

    /// <summary>
    /// Google翻訳を使い、表示中のタブをページ翻訳表示へ切り替える。
    /// </summary>
    /// <param name="browser">翻訳対象のブラウザ。</param>
    private void TranslatePageInBrowser(ChromiumWebBrowser browser)
    {
        if (!UrlHelper.TryCreateUrl(browser.Address, out var pageUrl))
        {
            ShowTranslationMessage(FindBrowserTab(browser), "このページは翻訳できません。", MessageBoxImage.Information);
            return;
        }

        var targetLanguage = translationTargetCulture.TwoLetterISOLanguageName;
        var translatedUrl = $"https://translate.google.com/translate?sl=auto&tl={Uri.EscapeDataString(targetLanguage)}&u={Uri.EscapeDataString(pageUrl)}";
        browser.Address = translatedUrl;
    }

    /// <summary>
    /// CefSharpの右クリックメニューからGemini翻訳を開始する。
    /// </summary>
    /// <param name="sender">翻訳メニューを通知した処理。</param>
    /// <param name="browser">翻訳対象のブラウザ。</param>
    private void BrowserContextMenuHandler_GeminiPageTranslationRequested(object? sender, IWebBrowser browser)
    {
        if (browser is not ChromiumWebBrowser chromiumWebBrowser)
        {
            return;
        }

        Dispatcher.InvokeAsync(() => _ = TranslatePageWithGeminiAsync(chromiumWebBrowser));
    }

    /// <summary>
    /// 表示中ページの文字だけをGeminiで翻訳し、元の位置へ反映する。
    /// </summary>
    /// <param name="browser">翻訳対象のブラウザ。</param>
    /// <param name="modelName">翻訳に使用するGeminiモデル。</param>
    /// <returns>翻訳処理の完了を表すタスク。</returns>
    private async Task TranslatePageWithGeminiAsync(
        ChromiumWebBrowser browser,
        string modelName = GeminiTranslationService.DefaultModelName)
    {
        var browserTab = FindBrowserTab(browser);
        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            if (browserTab is not null)
            {
                browserTab.TranslationOverlay.Visibility = Visibility.Visible;
            }

            var extraction = await browser.EvaluateScriptAsync(pageTranslationScriptService.CollectPageTextNodesScript);
            if (!extraction.Success || extraction.Result is not string pageTextJson)
            {
                ShowTranslationMessage(browserTab, "このページから翻訳できる文章を取得できませんでした。", MessageBoxImage.Information);
                return;
            }

            var segments = JsonSerializer.Deserialize<List<GeminiTranslationService.PageTextSegment>>(
                pageTextJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (segments is null || segments.Count == 0 || segments.Any(segment => string.IsNullOrWhiteSpace(segment.Text)))
            {
                ShowTranslationMessage(browserTab, "このページから翻訳できる文章を取得できませんでした。", MessageBoxImage.Information);
                return;
            }

            var result = await geminiTranslationService.TranslateSegmentsAsync(
                segments,
                translationTargetCulture,
                viewModel.TranslationPersonalization,
                modelName);
            if (!result.IsSuccess)
            {
                var failureResult = ShowTranslationFailure(browserTab, result.Message, modelName);
                if (failureResult == GeminiBusyWindowResult.Retry)
                {
                    await TranslatePageWithGeminiAsync(browser, modelName);
                }
                else if (failureResult == GeminiBusyWindowResult.UseAlternativeModel)
                {
                    var alternativeModel = modelName == GeminiTranslationService.DefaultModelName
                        ? GeminiTranslationService.AlternativeModelName
                        : GeminiTranslationService.DefaultModelName;
                    await TranslatePageWithGeminiAsync(browser, alternativeModel);
                }

                return;
            }

            var application = await browser.EvaluateScriptAsync(
                pageTranslationScriptService.CreateApplyScript(segments, result.Translations));
            if (!pageTranslationScriptService.TryGetApplicationResult(application, out var appliedCount) || appliedCount == 0)
            {
                ShowTranslationMessage(browserTab, "翻訳結果をページへ反映できませんでした。ページを再読み込みしてから、もう一度試してください。", MessageBoxImage.Warning);
            }
        }
        catch (JsonException)
        {
            ShowTranslationMessage(browserTab, "ページ本文の読み取りに失敗しました。ページを再読み込みしてから、もう一度試してください。", MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Gemini page translation failed: {exception}");
            ShowTranslationMessage(browserTab, "翻訳処理を完了できませんでした。ページを再読み込みしてから、もう一度試してください。", MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            if (browserTab is not null)
            {
                browserTab.TranslationOverlay.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// 翻訳中表示を閉じてから、利用者向けのメッセージを表示する。
    /// </summary>
    /// <param name="browserTab">翻訳対象のタブ。</param>
    /// <param name="message">表示するメッセージ。</param>
    /// <param name="image">メッセージの種別。</param>
    private void ShowTranslationMessage(BrowserTabState? browserTab, string message, MessageBoxImage image)
    {
        if (browserTab is not null)
        {
            browserTab.TranslationOverlay.Visibility = Visibility.Collapsed;
        }

        _ = image;
        var messageWindow = new TranslationMessageWindow("翻訳", message)
        {
            Owner = this
        };
        messageWindow.ShowDialog();
    }

    /// <summary>
    /// Geminiの失敗内容を表示し、混雑時だけ再試行の選択を受け取る。
    /// </summary>
    /// <param name="browserTab">翻訳対象のタブ。</param>
    /// <param name="message">Gemini APIから返された利用者向けメッセージ。</param>
    /// <param name="modelName">翻訳に使用したGeminiモデル。</param>
    /// <returns>混雑時に選択された再試行方法。</returns>
    private GeminiBusyWindowResult ShowTranslationFailure(
        BrowserTabState? browserTab,
        string message,
        string modelName)
    {
        if (browserTab is not null)
        {
            browserTab.TranslationOverlay.Visibility = Visibility.Collapsed;
        }

        if (!message.StartsWith("Gemini APIが混雑しています。", StringComparison.Ordinal))
        {
            var messageWindow = new TranslationMessageWindow("翻訳", message)
            {
                Owner = this
            };
            messageWindow.ShowDialog();
            return GeminiBusyWindowResult.Close;
        }

        var busyWindow = new GeminiBusyWindow(
            message,
            modelName != GeminiTranslationService.AlternativeModelName)
        {
            Owner = this
        };
        busyWindow.ShowDialog();
        return busyWindow.Result;
    }
}
