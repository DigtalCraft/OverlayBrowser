using System.Windows;

namespace OverlayBrowser.Form;

/// <summary>
/// 日本語と英語で操作方法を表示するヘルプ画面。
/// </summary>
public partial class HelpWindow : Window
{
    /// <summary>
    /// ヘルプ画面を初期化する。
    /// </summary>
    public HelpWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 閉じるボタンでヘルプ画面を閉じる。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
