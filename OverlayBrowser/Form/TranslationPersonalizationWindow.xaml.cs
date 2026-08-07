using System.Windows;
using OverlayBrowser.ViewModel;

namespace OverlayBrowser.Form;

/// <summary>
/// Gemini翻訳の文体と補足方針を編集する画面。
/// </summary>
public partial class TranslationPersonalizationWindow : Window
{
    private readonly TranslationPersonalizationWindowViewModel viewModel;

    /// <summary>
    /// 保存する翻訳時の指示を取得する。
    /// </summary>
    public string Personalization => viewModel.Personalization;

    /// <summary>
    /// 翻訳カスタマイズ画面を初期化する。
    /// </summary>
    /// <param name="personalization">現在保存されている翻訳時の指示。</param>
    public TranslationPersonalizationWindow(string personalization)
    {
        InitializeComponent();
        viewModel = new TranslationPersonalizationWindowViewModel(personalization);
        viewModel.CloseRequested += ViewModel_CloseRequested;
        DataContext = viewModel;
    }

    /// <summary>
    /// ViewModelが確定した結果で画面を閉じる。
    /// </summary>
    /// <param name="sender">翻訳カスタマイズ画面ViewModel。</param>
    /// <param name="e">呼び出し元へ返す結果。</param>
    private void ViewModel_CloseRequested(object? sender, DialogCloseRequestedEventArgs e)
    {
        DialogResult = e.DialogResult;
    }
}
