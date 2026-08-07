namespace OverlayBrowser.ViewModel;

/// <summary>
/// メイン画面で実行する画面固有操作。
/// </summary>
public enum MainWindowRequestType
{
    Navigate,
    CreateTab,
    OpenInDefaultBrowser,
    Reload,
    GoBack,
    GoForward,
    AddBookmark,
    ImportChromeBookmarks,
    ImportEdgeBookmarks,
    ImportHtmlBookmarks,
    ExportBookmarks,
    ClearBookmarks,
    ClearHistory,
    ClearCookies,
    ShowGeminiApiKey,
    ShowTranslationPersonalization,
    ShowTranslationHelp,
    ShowHelp,
    Exit
}

/// <summary>
/// ViewModelからメイン画面へ依頼する操作内容。
/// </summary>
public sealed class MainWindowRequestEventArgs : EventArgs
{
    /// <summary>
    /// 画面操作の依頼を初期化する。
    /// </summary>
    /// <param name="requestType">依頼する操作。</param>
    /// <param name="value">操作へ渡す値。</param>
    public MainWindowRequestEventArgs(MainWindowRequestType requestType, string? value = null)
    {
        RequestType = requestType;
        Value = value;
    }

    /// <summary>
    /// 依頼する操作。
    /// </summary>
    public MainWindowRequestType RequestType { get; }

    /// <summary>
    /// URLなど操作へ渡す値。
    /// </summary>
    public string? Value { get; }
}

/// <summary>
/// 利用者へ表示するメッセージの種類。
/// </summary>
public enum UserMessageType
{
    Information,
    Warning
}

/// <summary>
/// ViewModelから画面へ表示するメッセージ。
/// </summary>
public sealed class UserMessageEventArgs : EventArgs
{
    /// <summary>
    /// メッセージを初期化する。
    /// </summary>
    /// <param name="title">画面タイトル。</param>
    /// <param name="message">表示内容。</param>
    /// <param name="messageType">メッセージの種類。</param>
    public UserMessageEventArgs(string title, string message, UserMessageType messageType)
    {
        Title = title;
        Message = message;
        MessageType = messageType;
    }

    /// <summary>
    /// 画面タイトル。
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// 表示内容。
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// メッセージの種類。
    /// </summary>
    public UserMessageType MessageType { get; }
}
