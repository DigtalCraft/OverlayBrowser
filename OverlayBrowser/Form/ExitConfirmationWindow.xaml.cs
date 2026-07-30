using System.Windows;

namespace OverlayBrowser.Form;

/// <summary>
/// アプリ終了前の確認画面を表示する。
/// </summary>
public partial class ExitConfirmationWindow : Window
{
    /// <summary>
    /// 終了確認画面を初期化する。
    /// </summary>
    public ExitConfirmationWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 終了を確定して画面を閉じる。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    /// <summary>
    /// 終了を取り消して画面を閉じる。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
