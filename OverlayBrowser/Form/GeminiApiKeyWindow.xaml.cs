using System.Windows;
using OverlayBrowser.Service;
using OverlayBrowser.ViewModel;

namespace OverlayBrowser.Form;

/// <summary>
/// Gemini APIキーをWindows資格情報マネージャーへ登録する画面。
/// </summary>
public partial class GeminiApiKeyWindow : Window
{
    private readonly GeminiApiKeyWindowViewModel viewModel;

    /// <summary>
    /// APIキー設定画面を初期化する。
    /// </summary>
    /// <param name="apiKeyStore">APIキーの保存処理。</param>
    public GeminiApiKeyWindow(GeminiApiKeyStore apiKeyStore)
    {
        InitializeComponent();
        viewModel = new GeminiApiKeyWindowViewModel(apiKeyStore);
        viewModel.CloseRequested += ViewModel_CloseRequested;
        viewModel.PasswordClearRequested += ViewModel_PasswordClearRequested;
        DataContext = viewModel;
    }

    /// <summary>
    /// PasswordBoxの入力値をViewModelへ渡して保存する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // PasswordBoxは安全な双方向Bindingを標準で提供しないため、入力値だけ画面側で受け渡す。
        viewModel.SaveApiKey(ApiKeyPasswordBox.Password);
    }

    /// <summary>
    /// ViewModelからの依頼でパスワード入力欄を消去する。
    /// </summary>
    /// <param name="sender">APIキー画面ViewModel。</param>
    /// <param name="e">イベント情報。</param>
    private void ViewModel_PasswordClearRequested(object? sender, EventArgs e)
    {
        ApiKeyPasswordBox.Clear();
    }

    /// <summary>
    /// ViewModelからの依頼で画面を閉じる。
    /// </summary>
    /// <param name="sender">APIキー画面ViewModel。</param>
    /// <param name="e">呼び出し元へ返す結果。</param>
    private void ViewModel_CloseRequested(object? sender, DialogCloseRequestedEventArgs e)
    {
        DialogResult = e.DialogResult;
    }
}
