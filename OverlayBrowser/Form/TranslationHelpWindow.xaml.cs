using System.Diagnostics;
using System.Windows;

namespace OverlayBrowser.Form;

/// <summary>
/// Geminiを使った右クリック翻訳の手順を表示する。
/// </summary>
public partial class TranslationHelpWindow : Window
{
    /// <summary>
    /// 翻訳ヘルプ画面を初期化する。
    /// </summary>
    public TranslationHelpWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Google AI StudioのAPIキー作成画面を開く。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void OpenGoogleAiStudioButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://aistudio.google.com/app/apikey") { UseShellExecute = true });
    }

    /// <summary>
    /// 翻訳ヘルプ画面を閉じる。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
