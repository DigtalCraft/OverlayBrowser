using System.ComponentModel;
using OverlayBrowser.Command;
using OverlayBrowser.Service;

namespace OverlayBrowser.ViewModel;

/// <summary>
/// Gemini APIキー画面の登録状態と操作を管理する。
/// </summary>
public sealed class GeminiApiKeyWindowViewModel : ViewModelBase
{
    private readonly GeminiApiKeyStore apiKeyStore;
    private string status = string.Empty;

    /// <summary>
    /// APIキー画面ViewModelを初期化する。
    /// </summary>
    /// <param name="apiKeyStore">APIキーの保存処理。</param>
    public GeminiApiKeyWindowViewModel(GeminiApiKeyStore apiKeyStore)
    {
        this.apiKeyStore = apiKeyStore;
        DeleteCommand = new RelayCommand(_ => DeleteApiKey());
        CloseCommand = new RelayCommand(_ => CloseRequested?.Invoke(
            this,
            new DialogCloseRequestedEventArgs(false)));
        UpdateStatus();
    }

    /// <summary>
    /// 画面を閉じることをViewへ依頼するイベント。
    /// </summary>
    public event EventHandler<DialogCloseRequestedEventArgs>? CloseRequested;

    /// <summary>
    /// パスワード入力欄の消去をViewへ依頼するイベント。
    /// </summary>
    public event EventHandler? PasswordClearRequested;

    /// <summary>
    /// APIキーの登録状態または操作結果。
    /// </summary>
    public string Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    /// <summary>
    /// 保存済みAPIキーを削除するコマンド。
    /// </summary>
    public RelayCommand DeleteCommand { get; }

    /// <summary>
    /// 画面を閉じるコマンド。
    /// </summary>
    public RelayCommand CloseCommand { get; }

    /// <summary>
    /// 入力されたAPIキーをWindows資格情報マネージャーへ保存する。
    /// </summary>
    /// <param name="apiKey">入力されたAPIキー。</param>
    public void SaveApiKey(string apiKey)
    {
        try
        {
            apiKeyStore.SaveApiKey(apiKey);
            PasswordClearRequested?.Invoke(this, EventArgs.Empty);
            Status = "Gemini APIキーを保存しました。";
        }
        catch (ArgumentException)
        {
            Status = "APIキーを入力してください。";
        }
        catch (Win32Exception)
        {
            Status = "APIキーを保存できませんでした。";
        }
    }

    /// <summary>
    /// 保存済みのAPIキーを削除する。
    /// </summary>
    private void DeleteApiKey()
    {
        try
        {
            apiKeyStore.DeleteApiKey();
            PasswordClearRequested?.Invoke(this, EventArgs.Empty);
            Status = "保存済みのGemini APIキーを削除しました。";
        }
        catch (Win32Exception)
        {
            Status = "APIキーを削除できませんでした。";
        }
    }

    /// <summary>
    /// 現在のAPIキー登録状態を読み込む。
    /// </summary>
    private void UpdateStatus()
    {
        try
        {
            Status = apiKeyStore.HasApiKey()
                ? "Gemini APIキーは登録済みです。"
                : "Gemini APIキーは未登録です。";
        }
        catch (Win32Exception)
        {
            Status = "APIキーの登録状態を確認できませんでした。";
        }
    }
}
