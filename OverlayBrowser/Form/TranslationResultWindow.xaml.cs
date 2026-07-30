using System.Globalization;
using System.Windows;

namespace OverlayBrowser.Form;

/// <summary>
/// 選択文章の翻訳結果を表示する画面。
/// </summary>
public partial class TranslationResultWindow : Window
{
    /// <summary>
    /// 翻訳結果画面を初期化する。
    /// </summary>
    /// <param name="targetCulture">翻訳先のWindowsカルチャ。</param>
    /// <param name="translatedText">表示する翻訳済み文章。</param>
    public TranslationResultWindow(CultureInfo targetCulture, string translatedText)
    {
        InitializeComponent();
        TitleTextBlock.Text = $"翻訳結果（{targetCulture.NativeName}）";
        TranslationTextBox.Text = translatedText;
    }

    /// <summary>
    /// 翻訳結果をクリップボードへコピーする。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(TranslationTextBox.Text);
    }

    /// <summary>
    /// 翻訳結果画面を閉じる。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
