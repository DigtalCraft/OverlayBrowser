using System.Windows;
using OverlayBrowser.Service;

namespace OverlayBrowser.Form;

/// <summary>
/// Gemini翻訳の文体と補足方針を編集する画面。
/// </summary>
public partial class TranslationPersonalizationWindow : Window
{
    /// <summary>
    /// 保存する翻訳時の指示を取得する。
    /// </summary>
    public string Personalization { get; private set; } = string.Empty;

    /// <summary>
    /// 翻訳カスタマイズ画面を初期化する。
    /// </summary>
    /// <param name="personalization">現在保存されている翻訳時の指示。</param>
    public TranslationPersonalizationWindow(string personalization)
    {
        InitializeComponent();
        PersonalizationTextBox.Text = string.IsNullOrWhiteSpace(personalization)
            ? GeminiTranslationService.DefaultTranslationPersonalization
            : personalization;
    }

    /// <summary>
    /// 標準の翻訳方針を入力欄へ戻す。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void RestoreDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        PersonalizationTextBox.Text = GeminiTranslationService.DefaultTranslationPersonalization;
    }

    /// <summary>
    /// 入力された翻訳方針をメイン画面へ返す。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Personalization = PersonalizationTextBox.Text.Trim();
        DialogResult = true;
    }

    /// <summary>
    /// 変更を保存せず画面を閉じる。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
