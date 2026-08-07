using System.Windows.Input;
using OverlayBrowser.Command;
using OverlayBrowser.Helper;
using OverlayBrowser.Model;
using OverlayBrowser.Service;

namespace OverlayBrowser.ViewModel;

/// <summary>
/// メイン画面の表示状態と利用者操作を管理する。
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private const string DefaultHomeUrl = "https://www.google.com/";
    private readonly SettingsService settingsService;
    private readonly WindowsStartupService windowsStartupService;
    private readonly AppSettings settings;
    private string address = string.Empty;
    private bool isTopmost;
    private bool isStartWithWindows;
    private bool isBookmarkBarPinned;
    private double browserOpacity;
    private bool canGoBack;
    private bool canGoForward;

    /// <summary>
    /// メイン画面ViewModelを初期化し、保存済み設定を読み込む。
    /// </summary>
    /// <param name="settingsService">設定の保存処理。</param>
    /// <param name="windowsStartupService">Windows自動起動の設定処理。</param>
    public MainWindowViewModel(
        SettingsService settingsService,
        WindowsStartupService windowsStartupService)
    {
        this.settingsService = settingsService;
        this.windowsStartupService = windowsStartupService;

        settings = settingsService.Load();
        windowsStartupService.MigrateLegacyEntry();
        settings.IsStartWithWindows = windowsStartupService.IsEnabled();

        isTopmost = settings.IsTopmost;
        isStartWithWindows = settings.IsStartWithWindows;
        isBookmarkBarPinned = settings.IsBookmarkBarPinned;
        browserOpacity = Math.Clamp(settings.Opacity, 0.35, 1.0);

        OpenCommand = new RelayCommand(_ => OpenAddress());
        OpenHomeCommand = new RelayCommand(_ => OpenHome());
        NewTabCommand = new RelayCommand(_ => Request(MainWindowRequestType.CreateTab, GetNewTabAddress()));
        OpenInDefaultBrowserCommand = new RelayCommand(
            _ => Request(MainWindowRequestType.OpenInDefaultBrowser, Address));
        ReloadCommand = new RelayCommand(_ => Request(MainWindowRequestType.Reload));
        BackCommand = new RelayCommand(_ => Request(MainWindowRequestType.GoBack), _ => CanGoBack);
        ForwardCommand = new RelayCommand(_ => Request(MainWindowRequestType.GoForward), _ => CanGoForward);
        AddBookmarkCommand = new RelayCommand(_ => Request(MainWindowRequestType.AddBookmark));
        ImportChromeBookmarksCommand = new RelayCommand(_ => Request(MainWindowRequestType.ImportChromeBookmarks));
        ImportEdgeBookmarksCommand = new RelayCommand(_ => Request(MainWindowRequestType.ImportEdgeBookmarks));
        ImportHtmlBookmarksCommand = new RelayCommand(_ => Request(MainWindowRequestType.ImportHtmlBookmarks));
        ExportBookmarksCommand = new RelayCommand(_ => Request(MainWindowRequestType.ExportBookmarks));
        ClearBookmarksCommand = new RelayCommand(_ => Request(MainWindowRequestType.ClearBookmarks));
        ClearHistoryCommand = new RelayCommand(_ => Request(MainWindowRequestType.ClearHistory));
        ClearCookiesCommand = new RelayCommand(_ => Request(MainWindowRequestType.ClearCookies));
        ShowGeminiApiKeyCommand = new RelayCommand(_ => Request(MainWindowRequestType.ShowGeminiApiKey));
        ShowTranslationPersonalizationCommand = new RelayCommand(_ => Request(MainWindowRequestType.ShowTranslationPersonalization));
        ShowTranslationHelpCommand = new RelayCommand(_ => Request(MainWindowRequestType.ShowTranslationHelp));
        ShowHelpCommand = new RelayCommand(_ => Request(MainWindowRequestType.ShowHelp));
        ExitCommand = new RelayCommand(_ => Request(MainWindowRequestType.Exit));
    }

    /// <summary>
    /// 画面固有の処理をViewへ依頼するイベント。
    /// </summary>
    public event EventHandler<MainWindowRequestEventArgs>? RequestRaised;

    /// <summary>
    /// 利用者向けメッセージの表示をViewへ依頼するイベント。
    /// </summary>
    public event EventHandler<UserMessageEventArgs>? MessageRaised;

    /// <summary>
    /// ブックマーク表示の再構築をViewへ通知するイベント。
    /// </summary>
    public event EventHandler? BookmarksChanged;

    /// <summary>
    /// URL入力欄の値。
    /// </summary>
    public string Address
    {
        get => address;
        set => SetProperty(ref address, value);
    }

    /// <summary>
    /// ウィンドウを常に前面へ表示するかどうか。
    /// </summary>
    public bool IsTopmost
    {
        get => isTopmost;
        set
        {
            if (!SetProperty(ref isTopmost, value))
            {
                return;
            }

            settings.IsTopmost = value;
            settingsService.Save(settings);
        }
    }

    /// <summary>
    /// Windowsサインイン時にタスクトレイへ常駐するかどうか。
    /// </summary>
    public bool IsStartWithWindows
    {
        get => isStartWithWindows;
        set => UpdateWindowsStartup(value);
    }

    /// <summary>
    /// ブックマークバーを固定表示するかどうか。
    /// </summary>
    public bool IsBookmarkBarPinned
    {
        get => isBookmarkBarPinned;
        set
        {
            if (!SetProperty(ref isBookmarkBarPinned, value))
            {
                return;
            }

            settings.IsBookmarkBarPinned = value;
            settingsService.Save(settings);
        }
    }

    /// <summary>
    /// ブラウザと操作領域へ適用する不透明度。
    /// </summary>
    public double BrowserOpacity
    {
        get => browserOpacity;
        set
        {
            var normalizedValue = Math.Clamp(value, 0.35, 1.0);
            if (!SetProperty(ref browserOpacity, normalizedValue))
            {
                return;
            }

            settings.Opacity = normalizedValue;
            OnPropertyChanged(nameof(OpacityText));
        }
    }

    /// <summary>
    /// 画面へ表示する不透明度のパーセント表記。
    /// </summary>
    public string OpacityText => $"{BrowserOpacity:P0}";

    /// <summary>
    /// 前のページへ戻れるかどうか。
    /// </summary>
    public bool CanGoBack
    {
        get => canGoBack;
        private set
        {
            if (SetProperty(ref canGoBack, value))
            {
                BackCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 次のページへ進めるかどうか。
    /// </summary>
    public bool CanGoForward
    {
        get => canGoForward;
        private set
        {
            if (SetProperty(ref canGoForward, value))
            {
                ForwardCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 保存済みブックマーク一覧。
    /// </summary>
    public IList<BookmarkItem> Bookmarks => settings.Bookmarks;

    /// <summary>
    /// 保存済みの翻訳カスタマイズ内容。
    /// </summary>
    public string TranslationPersonalization => settings.TranslationPersonalization;

    public RelayCommand OpenCommand { get; }
    public RelayCommand OpenHomeCommand { get; }
    public RelayCommand NewTabCommand { get; }
    public RelayCommand OpenInDefaultBrowserCommand { get; }
    public RelayCommand ReloadCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand ForwardCommand { get; }
    public RelayCommand AddBookmarkCommand { get; }
    public RelayCommand ImportChromeBookmarksCommand { get; }
    public RelayCommand ImportEdgeBookmarksCommand { get; }
    public RelayCommand ImportHtmlBookmarksCommand { get; }
    public RelayCommand ExportBookmarksCommand { get; }
    public RelayCommand ClearBookmarksCommand { get; }
    public RelayCommand ClearHistoryCommand { get; }
    public RelayCommand ClearCookiesCommand { get; }
    public RelayCommand ShowGeminiApiKeyCommand { get; }
    public RelayCommand ShowTranslationPersonalizationCommand { get; }
    public RelayCommand ShowTranslationHelpCommand { get; }
    public RelayCommand ShowHelpCommand { get; }
    public RelayCommand ExitCommand { get; }

    /// <summary>
    /// 起動引数と保存済み設定から最初に開くURLを取得する。
    /// </summary>
    /// <param name="launchTarget">起動引数で指定されたURL。</param>
    /// <returns>最初に開くURL。</returns>
    public string GetStartupAddress(string? launchTarget)
    {
        if (UrlHelper.TryCreateBrowserAddress(launchTarget, out var launchAddress))
        {
            return launchAddress;
        }

        return string.IsNullOrWhiteSpace(settings.HomeUrl)
            ? settings.LastUrl
            : settings.HomeUrl;
    }

    /// <summary>
    /// 現在のURLをホームページとして保存する。
    /// </summary>
    public void SetCurrentAddressAsHome()
    {
        if (!UrlHelper.TryCreateUrl(Address, out var url))
        {
            ShowMessage("URL を確認してください", "ホームに設定できる URL がありません。", UserMessageType.Information);
            return;
        }

        settings.HomeUrl = url;
        settingsService.Save(settings);
        ShowMessage("ホーム設定", "現在の URL をホームに設定しました。", UserMessageType.Information);
    }

    /// <summary>
    /// 選択中タブの履歴操作状態を更新する。
    /// </summary>
    /// <param name="canGoBack">前のページへ戻れるかどうか。</param>
    /// <param name="canGoForward">次のページへ進めるかどうか。</param>
    public void UpdateNavigationState(bool canGoBack, bool canGoForward)
    {
        CanGoBack = canGoBack;
        CanGoForward = canGoForward;
    }

    /// <summary>
    /// ブラウザから通知されたURLを入力欄へ反映する。
    /// </summary>
    /// <param name="newAddress">表示中のURL。</param>
    public void UpdateAddress(string? newAddress)
    {
        if (!string.IsNullOrWhiteSpace(newAddress))
        {
            Address = newAddress;
        }
    }

    /// <summary>
    /// ブックマークを保存し、画面へ再表示を通知する。
    /// </summary>
    public void SaveBookmarks()
    {
        settingsService.Save(settings);
        BookmarksChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 保存済みブックマークを指定した一覧へ置き換える。
    /// </summary>
    /// <param name="bookmarks">保存するブックマーク一覧。</param>
    public void ReplaceBookmarks(IEnumerable<BookmarkItem> bookmarks)
    {
        settings.Bookmarks = bookmarks.ToList();
        SaveBookmarks();
    }

    /// <summary>
    /// 翻訳カスタマイズ内容を保存する。
    /// </summary>
    /// <param name="personalization">保存する翻訳時の指示。</param>
    public void SaveTranslationPersonalization(string personalization)
    {
        settings.TranslationPersonalization = personalization;
        settingsService.Save(settings);
        OnPropertyChanged(nameof(TranslationPersonalization));
    }

    /// <summary>
    /// アプリ終了時の画面状態を保存する。
    /// </summary>
    public void SaveBeforeExit()
    {
        settings.LastUrl = Address;
        settings.IsTopmost = IsTopmost;
        settings.Opacity = BrowserOpacity;
        settingsService.Save(settings);
    }

    /// <summary>
    /// 入力URLを検証してViewへ遷移を依頼する。
    /// </summary>
    private void OpenAddress()
    {
        if (!UrlHelper.TryCreateUrl(Address, out var url))
        {
            ShowMessage(
                "URL を確認してください",
                "http または https のサイト URL を入力してください。",
                UserMessageType.Information);
            return;
        }

        Address = url;
        Request(MainWindowRequestType.Navigate, url);
    }

    /// <summary>
    /// 保存済みのホームURLをViewへ遷移依頼する。
    /// </summary>
    private void OpenHome()
    {
        Address = GetNewTabAddress();
        Request(MainWindowRequestType.Navigate, Address);
    }

    /// <summary>
    /// 新規タブで使用するホームURLを取得する。
    /// </summary>
    /// <returns>新規タブで開くURL。</returns>
    public string GetNewTabAddress()
    {
        return string.IsNullOrWhiteSpace(settings.HomeUrl)
            ? DefaultHomeUrl
            : settings.HomeUrl;
    }

    /// <summary>
    /// Windows自動起動設定を変更する。
    /// </summary>
    /// <param name="value">自動起動を有効にする場合はtrue。</param>
    private void UpdateWindowsStartup(bool value)
    {
        if (value == isStartWithWindows)
        {
            return;
        }

        try
        {
            windowsStartupService.SetEnabled(value);
            SetProperty(ref isStartWithWindows, value, nameof(IsStartWithWindows));
            settings.IsStartWithWindows = value;
            settingsService.Save(settings);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException)
        {
            isStartWithWindows = windowsStartupService.IsEnabled();
            OnPropertyChanged(nameof(IsStartWithWindows));
            ShowMessage("設定", "Windowsの起動設定を変更できませんでした。", UserMessageType.Warning);
        }
    }

    /// <summary>
    /// 画面固有操作をViewへ依頼する。
    /// </summary>
    /// <param name="requestType">依頼する操作。</param>
    /// <param name="value">操作へ渡す値。</param>
    private void Request(MainWindowRequestType requestType, string? value = null)
    {
        RequestRaised?.Invoke(this, new MainWindowRequestEventArgs(requestType, value));
    }

    /// <summary>
    /// 利用者向けメッセージの表示をViewへ依頼する。
    /// </summary>
    /// <param name="title">画面タイトル。</param>
    /// <param name="message">表示内容。</param>
    /// <param name="messageType">メッセージの種類。</param>
    private void ShowMessage(string title, string message, UserMessageType messageType)
    {
        MessageRaised?.Invoke(this, new UserMessageEventArgs(title, message, messageType));
    }
}
