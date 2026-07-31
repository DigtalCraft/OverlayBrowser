using System.Windows;

namespace OverlayBrowser.Form;

/// <summary>
/// ブラウザデータ削除前の確認画面を表示する。
/// </summary>
public partial class DataClearConfirmationWindow : Window
{
    /// <summary>
    /// 削除確認画面を初期化する。
    /// </summary>
    /// <param name="title">画面タイトル。</param>
    /// <param name="message">確認する内容。</param>
    public DataClearConfirmationWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
    }

    /// <summary>
    /// 削除を実行する結果を返す。
    /// </summary>
    /// <param name="sender">クリックされたボタン。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    /// <summary>
    /// 削除を取り消す結果を返す。
    /// </summary>
    /// <param name="sender">クリックされたボタン。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
