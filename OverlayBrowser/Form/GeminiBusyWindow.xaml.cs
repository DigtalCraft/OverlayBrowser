using System.Windows;

namespace OverlayBrowser.Form;

/// <summary>
/// Gemini APIの混雑時に再試行方法を選択する画面。
/// </summary>
public partial class GeminiBusyWindow : Window
{
    /// <summary>
    /// 混雑時の選択結果。
    /// </summary>
    public GeminiBusyWindowResult Result { get; private set; } = GeminiBusyWindowResult.Close;

    /// <summary>
    /// 混雑時のダイアログを初期化する。
    /// </summary>
    /// <param name="message">APIから返された利用者向けメッセージ。</param>
    /// <param name="showAlternativeModel">代替モデルの選択肢を表示するかどうか。</param>
    public GeminiBusyWindow(string message, bool showAlternativeModel)
    {
        InitializeComponent();
        MessageTextBlock.Text = message;
        AlternativeButton.Visibility = showAlternativeModel ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 同じモデルで再試行する。
    /// </summary>
    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        Result = GeminiBusyWindowResult.Retry;
        DialogResult = true;
    }

    /// <summary>
    /// 代替モデルで再試行する。
    /// </summary>
    private void AlternativeButton_Click(object sender, RoutedEventArgs e)
    {
        Result = GeminiBusyWindowResult.UseAlternativeModel;
        DialogResult = true;
    }

    /// <summary>
    /// ダイアログを閉じる。
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Result = GeminiBusyWindowResult.Close;
        DialogResult = false;
    }
}

/// <summary>
/// Gemini API混雑時の選択結果。
/// </summary>
public enum GeminiBusyWindowResult
{
    /// <summary>同じモデルで再試行する。</summary>
    Retry,

    /// <summary>代替モデルで再試行する。</summary>
    UseAlternativeModel,

    /// <summary>翻訳を中止する。</summary>
    Close
}
