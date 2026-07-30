using System.ComponentModel;
using System.Windows;
using OverlayBrowser.Service;

namespace OverlayBrowser.Form;

/// <summary>
/// Gemini APIキーをWindows資格情報マネージャーへ登録する画面。
/// </summary>
public partial class GeminiApiKeyWindow : Window
{
    private readonly GeminiApiKeyStore apiKeyStore;

    /// <summary>
    /// APIキー設定画面を初期化する。
    /// </summary>
    /// <param name="apiKeyStore">APIキーの保存処理。</param>
    public GeminiApiKeyWindow(GeminiApiKeyStore apiKeyStore)
    {
        this.apiKeyStore = apiKeyStore;
        InitializeComponent();
        UpdateStatus();
    }

    /// <summary>
    /// 入力されたAPIキーを保存する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            apiKeyStore.SaveApiKey(ApiKeyPasswordBox.Password);
            ApiKeyPasswordBox.Clear();
            StatusTextBlock.Text = "Gemini APIキーを保存しました。";
        }
        catch (ArgumentException)
        {
            StatusTextBlock.Text = "APIキーを入力してください。";
        }
        catch (Win32Exception)
        {
            StatusTextBlock.Text = "APIキーを保存できませんでした。";
        }
    }

    /// <summary>
    /// 保存済みのAPIキーを削除する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            apiKeyStore.DeleteApiKey();
            ApiKeyPasswordBox.Clear();
            StatusTextBlock.Text = "保存済みのGemini APIキーを削除しました。";
        }
        catch (Win32Exception)
        {
            StatusTextBlock.Text = "APIキーを削除できませんでした。";
        }
    }

    /// <summary>
    /// 現在のAPIキー登録状態を表示する。
    /// </summary>
    private void UpdateStatus()
    {
        try
        {
            StatusTextBlock.Text = apiKeyStore.HasApiKey()
                ? "Gemini APIキーは登録済みです。"
                : "Gemini APIキーは未登録です。";
        }
        catch (Win32Exception)
        {
            StatusTextBlock.Text = "APIキーの登録状態を確認できませんでした。";
        }
    }

    /// <summary>
    /// 設定画面を閉じる。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
