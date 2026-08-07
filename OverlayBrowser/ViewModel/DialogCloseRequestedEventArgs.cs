namespace OverlayBrowser.ViewModel;

/// <summary>
/// ViewModelからダイアログを閉じる時の結果を表す。
/// </summary>
public sealed class DialogCloseRequestedEventArgs : EventArgs
{
    /// <summary>
    /// ダイアログの終了結果を初期化する。
    /// </summary>
    /// <param name="dialogResult">呼び出し元へ返す結果。</param>
    public DialogCloseRequestedEventArgs(bool dialogResult)
    {
        DialogResult = dialogResult;
    }

    /// <summary>
    /// 呼び出し元へ返す結果。
    /// </summary>
    public bool DialogResult { get; }
}
