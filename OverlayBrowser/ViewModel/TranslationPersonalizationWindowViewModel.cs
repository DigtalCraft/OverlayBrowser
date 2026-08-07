using OverlayBrowser.Command;
using OverlayBrowser.Service;

namespace OverlayBrowser.ViewModel;

/// <summary>
/// Gemini翻訳のカスタマイズ入力と保存結果を管理する。
/// </summary>
public sealed class TranslationPersonalizationWindowViewModel : ViewModelBase
{
    private string personalization;

    /// <summary>
    /// 翻訳カスタマイズ画面ViewModelを初期化する。
    /// </summary>
    /// <param name="personalization">現在保存されている翻訳時の指示。</param>
    public TranslationPersonalizationWindowViewModel(string personalization)
    {
        this.personalization = string.IsNullOrWhiteSpace(personalization)
            ? GeminiTranslationService.DefaultTranslationPersonalization
            : personalization;
        RestoreDefaultCommand = new RelayCommand(_ => RestoreDefault());
        SaveCommand = new RelayCommand(_ => Save());
        CloseCommand = new RelayCommand(_ => CloseRequested?.Invoke(
            this,
            new DialogCloseRequestedEventArgs(false)));
    }

    /// <summary>
    /// 画面を閉じることをViewへ依頼するイベント。
    /// </summary>
    public event EventHandler<DialogCloseRequestedEventArgs>? CloseRequested;

    /// <summary>
    /// 翻訳時にGeminiへ渡す指示。
    /// </summary>
    public string Personalization
    {
        get => personalization;
        set => SetProperty(ref personalization, value);
    }

    /// <summary>
    /// 標準設定へ戻すコマンド。
    /// </summary>
    public RelayCommand RestoreDefaultCommand { get; }

    /// <summary>
    /// 入力内容を保存するコマンド。
    /// </summary>
    public RelayCommand SaveCommand { get; }

    /// <summary>
    /// 変更を保存せず閉じるコマンド。
    /// </summary>
    public RelayCommand CloseCommand { get; }

    /// <summary>
    /// 標準の翻訳方針を入力欄へ戻す。
    /// </summary>
    private void RestoreDefault()
    {
        Personalization = GeminiTranslationService.DefaultTranslationPersonalization;
    }

    /// <summary>
    /// 入力された翻訳方針を確定する。
    /// </summary>
    private void Save()
    {
        Personalization = Personalization.Trim();
        CloseRequested?.Invoke(this, new DialogCloseRequestedEventArgs(true));
    }
}
