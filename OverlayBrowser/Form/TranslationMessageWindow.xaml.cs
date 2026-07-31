using System.Windows;

namespace OverlayBrowser.Form;

/// <summary>
/// OverlayBrowserからのお知らせをアプリのテーマで表示する画面。
/// </summary>
public partial class TranslationMessageWindow : Window
{
    /// <summary>
    /// メッセージ画面を初期化する。
    /// </summary>
    /// <param name="title">タイトル。</param>
    /// <param name="message">表示するメッセージ。</param>
    public TranslationMessageWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
    }

    /// <summary>
    /// メッセージ画面を閉じる。
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
