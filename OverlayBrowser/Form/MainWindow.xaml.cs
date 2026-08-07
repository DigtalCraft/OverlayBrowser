using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using CefSharp;
using CefSharp.Wpf;
using OverlayBrowser.Helper;
using OverlayBrowser.Model;
using OverlayBrowser.Service;
using OverlayBrowser.ViewModel;
using WpfMouseButtonEventHandler = System.Windows.Input.MouseButtonEventHandler;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseEventHandler = System.Windows.Input.MouseEventHandler;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace OverlayBrowser.Form;

/// <summary>
/// オーバーレイ表示用ブラウザのメイン画面を制御する。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 固定ブックマークバーで項目を挿入する位置。
    /// </summary>
    private enum BookmarkBarDropPosition
    {
        Before,
        After,
        Inside
    }

    private const string ApplicationBookmarkFolderName = "このアプリで追加";
    private readonly BrowserBookmarkTransferService bookmarkTransferService = new();
    private readonly BookmarkService bookmarkService = new();
    private readonly PageTranslationScriptService pageTranslationScriptService = new();
    private readonly BrowserPopupLifeSpanHandler browserPopupLifeSpanHandler = new();
    private readonly TrayIconService trayIconService = new();
    private readonly GeminiApiKeyStore geminiApiKeyStore = new();
    private readonly GeminiTranslationService geminiTranslationService;
    private readonly BrowserContextMenuHandler browserContextMenuHandler;
    private readonly MainWindowViewModel viewModel;
    private readonly Dictionary<TabItem, BrowserTabState> browserTabs = [];
    private readonly string? launchTarget;
    private readonly bool startHidden;
    private bool isExitConfirmed;
    private WpfPoint bookmarkBarDragStartPoint;
    private BookmarkItem? draggedBookmarkBarItem;
    private MenuItem? draggedBookmarkBarMenuItem;
    private BookmarkItem? bookmarkBarDropTarget;
    private MenuItem? bookmarkBarDropTargetMenuItem;
    private BookmarkBarDropPosition bookmarkBarDropPosition;
    private bool suppressBookmarkBarClick;
    private int openedPopupCount;
    private bool restoreTopmostAfterPopup;
    private readonly CultureInfo translationTargetCulture = CultureInfo.CurrentUICulture;

    /// <summary>
    /// 現在選択されているブラウザタブを取得する。
    /// </summary>
    /// <returns>選択中のタブ状態。タブがない場合はnull。</returns>
    private BrowserTabState? ActiveBrowserTab
    {
        get
        {
            return BrowserTabControl.SelectedItem is TabItem tabItem &&
                   browserTabs.TryGetValue(tabItem, out var browserTab)
                ? browserTab
                : null;
        }
    }

    /// <summary>
    /// メイン画面を初期化し、保存済みの表示設定を復元する。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        viewModel = new MainWindowViewModel(new SettingsService(), new WindowsStartupService());
        DataContext = viewModel;
        viewModel.RequestRaised += ViewModel_RequestRaised;
        viewModel.MessageRaised += ViewModel_MessageRaised;
        viewModel.BookmarksChanged += ViewModel_BookmarksChanged;

        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        launchTarget = arguments.FirstOrDefault(argument =>
            !string.Equals(argument, WindowsStartupService.StartupArgument, StringComparison.OrdinalIgnoreCase) &&
            !argument.StartsWith("--clear-browser-data=", StringComparison.OrdinalIgnoreCase) &&
            !argument.StartsWith("--wait-for-parent=", StringComparison.OrdinalIgnoreCase));
        startHidden = arguments.Any(argument =>
            string.Equals(argument, WindowsStartupService.StartupArgument, StringComparison.OrdinalIgnoreCase));
        geminiTranslationService = new GeminiTranslationService(geminiApiKeyStore);
        browserContextMenuHandler = new BrowserContextMenuHandler();
        browserContextMenuHandler.PageTranslationRequested += BrowserContextMenuHandler_PageTranslationRequested;
        browserContextMenuHandler.GeminiPageTranslationRequested += BrowserContextMenuHandler_GeminiPageTranslationRequested;
        browserPopupLifeSpanHandler.PopupOpened += BrowserPopupLifeSpanHandler_PopupOpened;
        browserPopupLifeSpanHandler.PopupClosed += BrowserPopupLifeSpanHandler_PopupClosed;
        trayIconService.ShowRequested += TrayIconService_ShowRequested;
        trayIconService.HelpRequested += TrayIconService_HelpRequested;
        trayIconService.ExitRequested += TrayIconService_ExitRequested;
        Loaded += MainWindow_Loaded;
    }

    /// <summary>
    /// 画面表示後にCefSharpと保存済み設定を初期化する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">画面表示時のイベント情報。</param>
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateBookmarkMenu();
        CreateBrowserTab(viewModel.GetStartupAddress(launchTarget));

        if (startHidden)
        {
            Dispatcher.BeginInvoke(HideToNotificationArea);
        }
    }

    /// <summary>
    /// ViewModelから依頼された画面固有操作を実行する。
    /// </summary>
    /// <param name="sender">メイン画面ViewModel。</param>
    /// <param name="e">依頼された操作内容。</param>
    private void ViewModel_RequestRaised(object? sender, MainWindowRequestEventArgs e)
    {
        switch (e.RequestType)
        {
            case MainWindowRequestType.Navigate:
                NavigateBrowserTo(e.Value);
                break;
            case MainWindowRequestType.CreateTab:
                CreateBrowserTab(e.Value ?? viewModel.GetNewTabAddress());
                break;
            case MainWindowRequestType.OpenInDefaultBrowser:
                OpenInDefaultBrowser(e.Value);
                break;
            case MainWindowRequestType.Reload:
                ReloadBrowser();
                break;
            case MainWindowRequestType.GoBack:
                NavigateBack();
                break;
            case MainWindowRequestType.GoForward:
                NavigateForward();
                break;
            case MainWindowRequestType.AddBookmark:
                AddBookmarkMenuItem_Click(this, new RoutedEventArgs());
                break;
            case MainWindowRequestType.ImportChromeBookmarks:
                ImportFromBrowser(BrowserType.Chrome, "Chrome");
                break;
            case MainWindowRequestType.ImportEdgeBookmarks:
                ImportFromBrowser(BrowserType.Edge, "Edge");
                break;
            case MainWindowRequestType.ImportHtmlBookmarks:
                ImportHtmlMenuItem_Click(this, new RoutedEventArgs());
                break;
            case MainWindowRequestType.ExportBookmarks:
                ExportHtmlMenuItem_Click(this, new RoutedEventArgs());
                break;
            case MainWindowRequestType.ClearBookmarks:
                ClearBookmarksMenuItem_Click(this, new RoutedEventArgs());
                break;
            case MainWindowRequestType.ClearHistory:
                ClearHistoryMenuItem_Click(this, new RoutedEventArgs());
                break;
            case MainWindowRequestType.ClearCookies:
                ClearCookiesMenuItem_Click(this, new RoutedEventArgs());
                break;
            case MainWindowRequestType.ShowGeminiApiKey:
                GeminiApiKeyMenuItem_Click(this, new RoutedEventArgs());
                break;
            case MainWindowRequestType.ShowTranslationPersonalization:
                TranslationPersonalizationMenuItem_Click(this, new RoutedEventArgs());
                break;
            case MainWindowRequestType.ShowTranslationHelp:
                TranslationHelpMenuItem_Click(this, new RoutedEventArgs());
                break;
            case MainWindowRequestType.ShowHelp:
                OpenHelpWindow();
                break;
            case MainWindowRequestType.Exit:
                RequestExit();
                break;
        }
    }

    /// <summary>
    /// ViewModelから通知されたメッセージを表示する。
    /// </summary>
    /// <param name="sender">メイン画面ViewModel。</param>
    /// <param name="e">表示するメッセージ。</param>
    private void ViewModel_MessageRaised(object? sender, UserMessageEventArgs e)
    {
        MessageBox.Show(
            e.Message,
            e.Title,
            MessageBoxButton.OK,
            e.MessageType == UserMessageType.Warning ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    /// <summary>
    /// ViewModelのブックマーク更新をメニューへ反映する。
    /// </summary>
    /// <param name="sender">メイン画面ViewModel。</param>
    /// <param name="e">イベント情報。</param>
    private void ViewModel_BookmarksChanged(object? sender, EventArgs e)
    {
        UpdateBookmarkMenu();
    }

    /// <summary>
    /// ポップアップ表示中だけ常に前面表示を解除し、認証画面を見える位置へ表示する。
    /// </summary>
    /// <param name="sender">ポップアップ管理処理。</param>
    /// <param name="e">イベント情報。</param>
    private void BrowserPopupLifeSpanHandler_PopupOpened(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            openedPopupCount++;
            if (openedPopupCount != 1)
            {
                return;
            }

            restoreTopmostAfterPopup = viewModel.IsTopmost;
            if (restoreTopmostAfterPopup)
            {
                SetCurrentValue(TopmostProperty, false);
            }
        });
    }

    /// <summary>
    /// すべてのポップアップが閉じた後に利用者の常に前面設定を復元する。
    /// </summary>
    /// <param name="sender">ポップアップ管理処理。</param>
    /// <param name="e">イベント情報。</param>
    private void BrowserPopupLifeSpanHandler_PopupClosed(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            openedPopupCount = Math.Max(0, openedPopupCount - 1);
            if (openedPopupCount != 0 || !restoreTopmostAfterPopup)
            {
                return;
            }

            restoreTopmostAfterPopup = false;
            SetCurrentValue(TopmostProperty, viewModel.IsTopmost);
        });
    }

    /// <summary>
    /// 指定URLを選択中のブラウザへ表示する。
    /// </summary>
    /// <param name="url">表示するURL。</param>
    private void NavigateBrowserTo(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var browserTab = ActiveBrowserTab;
        if (browserTab is null)
        {
            CreateBrowserTab(url);
            return;
        }

        browserTab.Browser.Address = url;
    }

    /// <summary>
    /// 埋め込みブラウザで利用できないページをWindowsの標準ブラウザで開く。
    /// </summary>
    /// <param name="url">標準ブラウザで開くURL。</param>
    private void OpenInDefaultBrowser(string? url)
    {
        if (!UrlHelper.TryCreateUrl(url, out var browserUrl))
        {
            MessageBox.Show(
                "標準ブラウザで開けるページがありません。",
                "標準ブラウザで開く",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(browserUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            System.Diagnostics.Debug.WriteLine($"Open default browser failed: {exception}");
            MessageBox.Show(
                "標準ブラウザを起動できませんでした。",
                "標準ブラウザで開く",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 選択中のブラウザを前のページへ戻す。
    /// </summary>
    private void NavigateBack()
    {
        if (ActiveBrowserTab?.Browser is { CanGoBack: true } browser)
        {
            browser.Back();
        }
    }

    /// <summary>
    /// 選択中のブラウザを次のページへ進める。
    /// </summary>
    private void NavigateForward()
    {
        if (ActiveBrowserTab?.Browser is { CanGoForward: true } browser)
        {
            browser.Forward();
        }
    }

    /// <summary>
    /// 現在入力されているURLをホームURLとして保存する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void SetHomeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        viewModel.SetCurrentAddressAsHome();
    }

    /// <summary>
    /// 新規タブで最初に表示するURLを取得する。
    /// </summary>
    /// <returns>新規タブで開くURL。</returns>
    private string GetNewTabUrl()
    {
        return viewModel.GetNewTabAddress();
    }

    /// <summary>
    /// CefSharpの表示中ページを再読み込みする。
    /// </summary>
    private void ReloadBrowser()
    {
        var browserTab = ActiveBrowserTab;
        if (browserTab is null || !browserTab.Browser.IsBrowserInitialized)
        {
            return;
        }

        browserTab.Browser.Reload();
    }

    /// <summary>
    /// 保存されている履歴を削除してアプリを再起動するか確認する。
    /// </summary>
    /// <param name="sender">クリックされたメニュー項目。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void ClearHistoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var confirmationWindow = new DataClearConfirmationWindow(
            "履歴の削除",
            "保存されている閲覧履歴を削除して、OverlayBrowserを再起動しますか？")
        {
            Owner = this
        };
        if (confirmationWindow.ShowDialog() != true)
        {
            return;
        }

        RestartAfterBrowserDataDeletion("history");
    }

    /// <summary>
    /// 保存されているCookieを削除するか確認する。
    /// </summary>
    /// <param name="sender">クリックされたメニュー項目。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void ClearCookiesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var confirmationWindow = new DataClearConfirmationWindow(
            "Cookieの削除",
            "保存されているCookieをすべて削除して、OverlayBrowserを再起動しますか？")
        {
            Owner = this
        };
        if (confirmationWindow.ShowDialog() != true)
        {
            return;
        }

        RestartAfterBrowserDataDeletion("cookies");
    }

    /// <summary>
    /// 指定したブラウザデータを削除するため、アプリを終了して再起動する。
    /// </summary>
    /// <param name="dataType">削除対象のデータ種別。</param>
    private void RestartAfterBrowserDataDeletion(string dataType)
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("アプリケーションの実行ファイルを取得できませんでした。");
            }

            var startInfo = new System.Diagnostics.ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add($"--clear-browser-data={dataType}");
            startInfo.ArgumentList.Add($"--wait-for-parent={Environment.ProcessId}");
            System.Diagnostics.Process.Start(startInfo);

            isExitConfirmed = true;
            Close();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Browser data deletion restart failed: {exception}");
            var messageWindow = new TranslationMessageWindow(
                "データの削除",
                "OverlayBrowserを再起動できなかったため、データを削除できませんでした。")
            {
                Owner = this
            };
            messageWindow.ShowDialog();
        }
    }

    /// <summary>
    /// 現在表示中の URL をブックマークに追加する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void AddBookmarkMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!UrlHelper.TryCreateUrl(viewModel.Address, out var url))
        {
            MessageBox.Show("ブックマークに追加できる URL がありません。", "URL を確認してください", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (bookmarkService.GetUrls(viewModel.Bookmarks).Any(itemUrl =>
                string.Equals(itemUrl, url, StringComparison.OrdinalIgnoreCase)))
        {
            var messageWindow = new TranslationMessageWindow(
                "ブックマーク",
                "このURLはすでにブックマークへ登録されています。")
            {
                Owner = this
            };
            messageWindow.ShowDialog();
            return;
        }

        var name = ActiveBrowserTab?.Browser.Title;
        var destinationWindow = new BookmarkWindow(viewModel.Bookmarks, isDestinationSelection: true)
        {
            Owner = this
        };
        if (destinationWindow.ShowDialog() != true)
        {
            return;
        }

        var bookmark = new BookmarkItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? url : name,
            Url = url
        };
        if (destinationWindow.SelectedFolder is not null)
        {
            destinationWindow.SelectedFolder.Children.Add(bookmark);
        }
        else
        {
            viewModel.Bookmarks.Add(bookmark);
        }

        viewModel.SaveBookmarks();
    }

    /// <summary>
    /// ブックマークメニューを開く直前に最新の内容を反映する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">サブメニューを開くイベント情報。</param>
    private void BookmarkMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        UpdateBookmarkMenu();
    }

    /// <summary>
    /// HTML形式で保存されたChromeまたはEdgeのブックマークを取り込む。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void ImportHtmlMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chrome または Edge のブックマークHTMLを選択",
            Filter = "HTMLファイル (*.html;*.htm)|*.html;*.htm|すべてのファイル (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            MergeImportedBookmarks(bookmarkTransferService.ImportFromHtml(dialog.FileName), "HTMLファイル");
        }
        catch (IOException)
        {
            MessageBox.Show("ブックマークファイルを読み込めませんでした。", "インポート", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 現在のブックマークをChromeとEdge用のHTMLファイルとして書き出す。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void ExportHtmlMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Chrome / Edge 用ブックマークを保存",
            FileName = "OverlayBrowser-Bookmarks.html",
            Filter = "HTMLファイル (*.html)|*.html"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            bookmarkTransferService.ExportToHtml(dialog.FileName, viewModel.Bookmarks);
            MessageBox.Show("Chrome と Edge で読み込めるHTML形式で書き出しました。", "エクスポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (IOException)
        {
            MessageBox.Show("ブックマークファイルを書き出せませんでした。", "エクスポート", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// メニューバーからGemini APIキーの設定画面を表示する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void GeminiApiKeyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var apiKeyWindow = new GeminiApiKeyWindow(geminiApiKeyStore) { Owner = this };
        apiKeyWindow.ShowDialog();
    }

    /// <summary>
    /// メニューバーから翻訳時の文体と補足方針を編集する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void TranslationPersonalizationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var personalizationWindow = new TranslationPersonalizationWindow(viewModel.TranslationPersonalization)
        {
            Owner = this
        };
        if (personalizationWindow.ShowDialog() != true)
        {
            return;
        }

        viewModel.SaveTranslationPersonalization(personalizationWindow.Personalization);
    }

    /// <summary>
    /// メニューバーから右クリック翻訳の説明画面を表示する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void TranslationHelpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var translationHelpWindow = new TranslationHelpWindow { Owner = this };
        translationHelpWindow.ShowDialog();
    }

    /// <summary>
    /// タスクトレイからメイン画面を表示する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">トレイ操作のイベント情報。</param>
    private void TrayIconService_ShowRequested(object? sender, EventArgs e)
    {
        ShowWindowFromTray();
    }

    /// <summary>
    /// タスクトレイからヘルプを表示する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">トレイ操作のイベント情報。</param>
    private void TrayIconService_HelpRequested(object? sender, EventArgs e)
    {
        ShowWindowFromTray();
        OpenHelpWindow();
    }

    /// <summary>
    /// タスクトレイからアプリを終了する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">トレイ操作のイベント情報。</param>
    private void TrayIconService_ExitRequested(object? sender, EventArgs e)
    {
        ShowWindowFromTray();
        RequestExit();
    }

    /// <summary>
    /// 非表示のメイン画面をタスクトレイから復元する。
    /// </summary>
    private void ShowWindowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>
    /// Windows自動起動時にメイン画面を隠してタスクトレイへ常駐する。
    /// </summary>
    private void HideToNotificationArea()
    {
        ShowInTaskbar = false;
        Hide();
    }

    /// <summary>
    /// 日本語と英語の操作説明をモーダル画面で表示する。
    /// </summary>
    private void OpenHelpWindow()
    {
        var helpWindow = new HelpWindow { Owner = this };
        helpWindow.ShowDialog();
    }

    /// <summary>
    /// タイトルバーの最小化ボタンでウィンドウを最小化する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// タイトルバーの最大化ボタンで最大化と通常サイズを切り替える。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>
    /// タイトルバーの閉じるボタンで画面を隠し、タスクトレイへ常駐する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        HideToNotificationArea();
    }

    /// <summary>
    /// OverlayBrowserを終了する。
    /// </summary>
    private void RequestExit()
    {
        isExitConfirmed = true;
        Close();
    }

    /// <summary>
    /// ブックマークメニューの項目を再構築する。
    /// </summary>
    private void UpdateBookmarkMenu()
    {
        BookmarkMenuItem.Items.Clear();
        if (viewModel.Bookmarks.Count == 0)
        {
            BookmarkMenuItem.Items.Add(new MenuItem { Header = "ブックマークがありません", IsEnabled = false });
        }
        else
        {
            var openBookmarkListMenuItem = new MenuItem
            {
                Header = $"ブックマーク一覧を開く（{bookmarkService.CountUrls(viewModel.Bookmarks)}件）"
            };
            openBookmarkListMenuItem.Click += OpenBookmarkListMenuItem_Click;
            BookmarkMenuItem.Items.Add(openBookmarkListMenuItem);
        }

        BookmarkMenuItem.Items.Add(new Separator());
        AddBookmarkOperationMenuItem("このページをブックマークに追加", AddBookmarkMenuItem_Click);
        AddBookmarkOperationMenuItem("現在のページをホームページに設定", SetHomeMenuItem_Click);
        UpdateBookmarkBar();
    }

    /// <summary>
    /// 保存済みブックマークから固定表示用の横並びメニューを再構築する。
    /// </summary>
    private void UpdateBookmarkBar()
    {
        BookmarkBarMenu.Items.Clear();
        if (viewModel.Bookmarks.Count == 0)
        {
            BookmarkBarMenu.Items.Add(new MenuItem
            {
                Header = "ブックマークがありません",
                IsEnabled = false,
                Tag = "BookmarkBar"
            });
            return;
        }

        foreach (var bookmark in viewModel.Bookmarks)
        {
            BookmarkBarMenu.Items.Add(CreateBookmarkBarMenuItem(bookmark));
        }
    }

    /// <summary>
    /// ブックマークまたはフォルダを固定バー用メニューへ変換する。
    /// </summary>
    /// <param name="bookmark">表示するブックマーク。</param>
    /// <returns>固定バーへ追加するメニュー項目。</returns>
    private MenuItem CreateBookmarkBarMenuItem(BookmarkItem bookmark)
    {
        var menuItem = new MenuItem
        {
            Header = CreateBookmarkBarHeader(bookmark),
            Tag = bookmark,
            ContextMenu = CreateBookmarkBarContextMenu(bookmark)
        };
        AttachBookmarkBarDragHandlers(menuItem);

        if (!bookmark.IsFolder)
        {
            menuItem.CommandParameter = bookmark.Url;
            menuItem.ToolTip = bookmark.Url;
            return menuItem;
        }

        if (bookmark.Children.Count == 0)
        {
            menuItem.Items.Add(new MenuItem
            {
                Header = "フォルダは空です",
                IsEnabled = false,
                Tag = "BookmarkBar"
            });
            return menuItem;
        }

        foreach (var child in bookmark.Children)
        {
            menuItem.Items.Add(CreateBookmarkBarMenuItem(child));
        }

        return menuItem;
    }

    /// <summary>
    /// 固定ブックマークバーの項目へドラッグ操作を設定する。
    /// </summary>
    /// <param name="menuItem">操作対象のメニュー項目。</param>
    private void AttachBookmarkBarDragHandlers(MenuItem menuItem)
    {
        menuItem.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new WpfMouseButtonEventHandler(BookmarkBarItem_PreviewMouseLeftButtonDown),
            handledEventsToo: true);
        menuItem.AddHandler(
            UIElement.PreviewMouseMoveEvent,
            new WpfMouseEventHandler(BookmarkBarItem_PreviewMouseMove),
            handledEventsToo: true);
        menuItem.AddHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new WpfMouseButtonEventHandler(BookmarkBarItem_PreviewMouseLeftButtonUp),
            handledEventsToo: true);
        menuItem.AddHandler(
            UIElement.PreviewMouseRightButtonUpEvent,
            new WpfMouseButtonEventHandler(BookmarkBarItem_PreviewMouseRightButtonUp),
            handledEventsToo: true);
    }

    /// <summary>
    /// 固定ブックマークバーの右クリックメニューを作成する。
    /// </summary>
    /// <param name="bookmark">操作対象のブックマーク。</param>
    /// <returns>削除操作を含む右クリックメニュー。</returns>
    private System.Windows.Controls.ContextMenu CreateBookmarkBarContextMenu(BookmarkItem bookmark)
    {
        var deleteMenuItem = new MenuItem
        {
            Header = bookmark.IsFolder ? "フォルダを削除" : "ブックマークを削除",
            Tag = bookmark,
            Style = (Style)FindResource(typeof(MenuItem))
        };
        deleteMenuItem.Click += DeleteBookmarkBarItem_Click;

        var contextMenu = new System.Windows.Controls.ContextMenu
        {
            Background = (System.Windows.Media.Brush)FindResource("PanelBackgroundBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("MainTextBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };
        contextMenu.Items.Add(deleteMenuItem);
        return contextMenu;
    }

    /// <summary>
    /// 右クリックで選択したブックマークまたはフォルダを削除する。
    /// </summary>
    /// <param name="sender">選択された削除メニュー。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void DeleteBookmarkBarItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: BookmarkItem bookmark })
        {
            return;
        }

        var itemName = string.IsNullOrWhiteSpace(bookmark.Name)
            ? bookmark.IsFolder ? "名前のないフォルダ" : bookmark.Url
            : bookmark.Name;
        var title = bookmark.IsFolder ? "フォルダの削除" : "ブックマークの削除";
        var message = bookmark.IsFolder
            ? $"フォルダ「{itemName}」と、その中のブックマークをすべて削除しますか？\nこの操作は元に戻せません。"
            : $"ブックマーク「{itemName}」を削除しますか？\nこの操作は元に戻せません。";
        var confirmationWindow = new DataClearConfirmationWindow(title, message)
        {
            Owner = this
        };
        if (confirmationWindow.ShowDialog() != true ||
            !bookmarkService.Remove(viewModel.Bookmarks, bookmark))
        {
            return;
        }

        viewModel.SaveBookmarks();
    }

    /// <summary>
    /// 固定ブックマークバーの右クリックメニューを表示する。
    /// </summary>
    /// <param name="sender">右クリックされたメニュー項目。</param>
    /// <param name="e">マウス操作のイベント情報。</param>
    private void BookmarkBarItem_PreviewMouseRightButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        var menuItem = FindBookmarkBarMenuItem(e.OriginalSource as DependencyObject);
        if (menuItem?.ContextMenu is not { } contextMenu)
        {
            return;
        }

        contextMenu.PlacementTarget = menuItem;
        contextMenu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>
    /// 固定ブックマークバーでドラッグ開始位置を記録する。
    /// </summary>
    /// <param name="sender">クリックされたメニュー項目。</param>
    /// <param name="e">マウス操作のイベント情報。</param>
    private void BookmarkBarItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var sourceItem = FindBookmarkBarMenuItem(e.OriginalSource as DependencyObject);
        if (sourceItem is null || sourceItem.Tag is not BookmarkItem bookmark)
        {
            draggedBookmarkBarItem = null;
            return;
        }

        var menuItem = sourceItem;
        bookmarkBarDragStartPoint = e.GetPosition(this);
        draggedBookmarkBarItem = bookmark;
        draggedBookmarkBarMenuItem = menuItem;
        bookmarkBarDropTarget = null;
        ClearBookmarkBarDropIndicator();
        suppressBookmarkBarClick = false;
        menuItem.IsSubmenuOpen = bookmark.IsFolder;
        e.Handled = true;
    }

    /// <summary>
    /// 固定ブックマークバーの項目をドラッグする。
    /// </summary>
    /// <param name="sender">操作中のメニュー項目。</param>
    /// <param name="e">マウス操作のイベント情報。</param>
    private void BookmarkBarItem_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (draggedBookmarkBarMenuItem is not MenuItem menuItem ||
            e.LeftButton != MouseButtonState.Pressed ||
            draggedBookmarkBarItem is not BookmarkItem bookmark)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        if (Math.Abs(currentPoint.X - bookmarkBarDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - bookmarkBarDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var targetMenuItem = FindBookmarkBarMenuItem(Mouse.DirectlyOver as DependencyObject) ??
                             FindBookmarkBarMenuItem(e.OriginalSource as DependencyObject);
        var target = targetMenuItem?.Tag as BookmarkItem;
        if (target is not null &&
            !ReferenceEquals(bookmark, target) &&
            !bookmarkService.Contains(bookmark, target))
        {
            ClearBookmarkBarDropIndicator();
            bookmarkBarDropTarget = target;
            bookmarkBarDropTargetMenuItem = targetMenuItem;
            bookmarkBarDropPosition = GetBookmarkBarDropPosition(targetMenuItem!, target);
            if (target.IsFolder && bookmarkBarDropPosition == BookmarkBarDropPosition.Inside)
            {
                targetMenuItem!.IsSubmenuOpen = true;
            }

            ShowBookmarkBarDropIndicator(targetMenuItem!, bookmarkBarDropPosition);
        }
        else
        {
            bookmarkBarDropTarget = null;
            ClearBookmarkBarDropIndicator();
        }

        suppressBookmarkBarClick = true;
        Mouse.OverrideCursor = System.Windows.Input.Cursors.SizeAll;
        e.Handled = true;
    }

    /// <summary>
    /// 固定ブックマークバーでマウス捕捉を解除する。
    /// </summary>
    /// <param name="sender">操作中のメニュー項目。</param>
    /// <param name="e">マウス操作のイベント情報。</param>
    private void BookmarkBarItem_PreviewMouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        if (draggedBookmarkBarMenuItem is not MenuItem menuItem)
        {
            return;
        }

        var source = draggedBookmarkBarItem;
        var target = bookmarkBarDropTarget;
        var dropPosition = bookmarkBarDropPosition;
        var wasDragged = suppressBookmarkBarClick;
        ResetBookmarkBarDragState();

        if (wasDragged)
        {
            if (source is not null && target is not null)
            {
                MoveBookmarkBarItem(source, target, dropPosition);
            }

            e.Handled = true;
            return;
        }

        if (menuItem.CommandParameter is string bookmarkUrl)
        {
            OpenBookmarkBarBookmark(bookmarkUrl);
            CloseBookmarkBarSubmenus();
        }

        e.Handled = true;
    }

    /// <summary>
    /// マウス位置から項目の前後またはフォルダ内への挿入位置を取得する。
    /// </summary>
    /// <param name="menuItem">移動先のメニュー項目。</param>
    /// <param name="bookmark">移動先のブックマーク。</param>
    /// <returns>項目を挿入する位置。</returns>
    private static BookmarkBarDropPosition GetBookmarkBarDropPosition(
        MenuItem menuItem,
        BookmarkItem bookmark)
    {
        var parentItems = ItemsControl.ItemsControlFromItemContainer(menuItem);
        var mousePoint = Mouse.GetPosition(menuItem);
        var isHorizontal = parentItems is System.Windows.Controls.Menu;
        var itemLength = isHorizontal ? menuItem.ActualWidth : menuItem.ActualHeight;
        var mousePosition = isHorizontal ? mousePoint.X : mousePoint.Y;

        if (mousePosition <= itemLength * 0.3)
        {
            return BookmarkBarDropPosition.Before;
        }

        if (mousePosition >= itemLength * 0.7)
        {
            return BookmarkBarDropPosition.After;
        }

        if (bookmark.IsFolder)
        {
            return BookmarkBarDropPosition.Inside;
        }

        return mousePosition < itemLength / 2
            ? BookmarkBarDropPosition.Before
            : BookmarkBarDropPosition.After;
    }

    /// <summary>
    /// 固定ブックマークバーへ前後の挿入位置を示す線を表示する。
    /// </summary>
    /// <param name="menuItem">線を表示するメニュー項目。</param>
    /// <param name="dropPosition">項目を挿入する位置。</param>
    private void ShowBookmarkBarDropIndicator(
        MenuItem menuItem,
        BookmarkBarDropPosition dropPosition)
    {
        if (dropPosition == BookmarkBarDropPosition.Inside)
        {
            return;
        }

        var parentItems = ItemsControl.ItemsControlFromItemContainer(menuItem);
        var isHorizontal = parentItems is System.Windows.Controls.Menu;
        menuItem.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
        menuItem.BorderThickness = isHorizontal
            ? dropPosition == BookmarkBarDropPosition.Before
                ? new Thickness(3, 0, 0, 0)
                : new Thickness(0, 0, 3, 0)
            : dropPosition == BookmarkBarDropPosition.Before
                ? new Thickness(0, 3, 0, 0)
                : new Thickness(0, 0, 0, 3);
    }

    /// <summary>
    /// 固定ブックマークバーの挿入位置を示す線を消去する。
    /// </summary>
    private void ClearBookmarkBarDropIndicator()
    {
        if (bookmarkBarDropTargetMenuItem is null)
        {
            return;
        }

        bookmarkBarDropTargetMenuItem.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
        bookmarkBarDropTargetMenuItem.ClearValue(System.Windows.Controls.Control.BorderThicknessProperty);
        bookmarkBarDropTargetMenuItem = null;
    }

    /// <summary>
    /// 固定ブックマークバーのドラッグ状態を解除する。
    /// </summary>
    private void ResetBookmarkBarDragState()
    {
        ClearBookmarkBarDropIndicator();
        draggedBookmarkBarItem = null;
        draggedBookmarkBarMenuItem = null;
        bookmarkBarDropTarget = null;
        bookmarkBarDropPosition = BookmarkBarDropPosition.Before;
        suppressBookmarkBarClick = false;
        Mouse.OverrideCursor = null;
    }

    /// <summary>
    /// マウス位置にある固定ブックマークバーのメニュー項目を取得する。
    /// </summary>
    /// <param name="element">検索開始する画面要素。</param>
    /// <returns>対象のメニュー項目。見つからない場合はnull。</returns>
    private static MenuItem? FindBookmarkBarMenuItem(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is MenuItem menuItem && menuItem.Tag is BookmarkItem)
            {
                return menuItem;
            }

            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    /// <summary>
    /// 固定ブックマークバーから対象のブックマーク項目を取得する。
    /// </summary>
    /// <param name="itemsControl">検索対象のメニュー。</param>
    /// <param name="target">検索するブックマーク。</param>
    /// <returns>対象のメニュー項目。見つからない場合はnull。</returns>
    private static MenuItem? FindBookmarkBarMenuItem(ItemsControl itemsControl, BookmarkItem target)
    {
        foreach (var menuItem in itemsControl.Items.OfType<MenuItem>())
        {
            if (ReferenceEquals(menuItem.Tag, target))
            {
                return menuItem;
            }

            if (menuItem.Tag is BookmarkItem { IsFolder: true } &&
                FindBookmarkBarMenuItem(menuItem, target) is { } childMenuItem)
            {
                return childMenuItem;
            }
        }

        return null;
    }

    /// <summary>
    /// 固定ブックマークバーの項目を指定位置へ移動する。
    /// </summary>
    /// <param name="source">移動するブックマーク。</param>
    /// <param name="target">移動先のブックマーク。</param>
    /// <param name="dropPosition">項目を挿入する位置。</param>
    private void MoveBookmarkBarItem(
        BookmarkItem source,
        BookmarkItem target,
        BookmarkBarDropPosition dropPosition)
    {
        if (dropPosition != BookmarkBarDropPosition.Inside &&
            TryMoveBookmarkWithinSameList(source, target, dropPosition))
        {
            viewModel.SaveBookmarks();
            return;
        }

        if (ReferenceEquals(source, target) ||
            bookmarkService.Contains(source, target) ||
            !bookmarkService.Remove(viewModel.Bookmarks, source))
        {
            return;
        }

        if (dropPosition == BookmarkBarDropPosition.Inside && target.IsFolder)
        {
            target.Children.Add(source);
        }
        else if (bookmarkService.FindContainingList(viewModel.Bookmarks, target) is { } targetList)
        {
            var targetIndex = targetList.IndexOf(target);
            if (dropPosition == BookmarkBarDropPosition.After)
            {
                targetIndex++;
            }

            targetList.Insert(targetIndex, source);
        }
        else
        {
            viewModel.Bookmarks.Add(source);
        }

        viewModel.SaveBookmarks();
    }

    /// <summary>
    /// 同じ一覧内でブックマークの順番を入れ替える。
    /// </summary>
    /// <param name="source">移動するブックマーク。</param>
    /// <param name="target">移動先のブックマーク。</param>
    /// <param name="dropPosition">項目を挿入する位置。</param>
    /// <returns>同じ一覧内で入れ替えた場合はtrue。</returns>
    private bool TryMoveBookmarkWithinSameList(
        BookmarkItem source,
        BookmarkItem target,
        BookmarkBarDropPosition dropPosition)
    {
        var sourceList = bookmarkService.FindContainingList(viewModel.Bookmarks, source);
        var targetList = bookmarkService.FindContainingList(viewModel.Bookmarks, target);
        if (sourceList is null ||
            targetList is null ||
            !ReferenceEquals(sourceList, targetList))
        {
            return false;
        }

        var sourceMenuItem = FindBookmarkBarMenuItem(BookmarkBarMenu, source);
        var targetMenuItem = FindBookmarkBarMenuItem(BookmarkBarMenu, target);
        var parentItems = sourceMenuItem is null
            ? null
            : ItemsControl.ItemsControlFromItemContainer(sourceMenuItem);
        var targetParentItems = targetMenuItem is null
            ? null
            : ItemsControl.ItemsControlFromItemContainer(targetMenuItem);
        if (sourceMenuItem is null ||
            targetMenuItem is null ||
            parentItems is null ||
            !ReferenceEquals(parentItems, targetParentItems))
        {
            return false;
        }

        var sourceIndex = sourceList.IndexOf(source);
        var targetIndex = sourceList.IndexOf(target);
        var menuSourceIndex = parentItems.Items.IndexOf(sourceMenuItem);
        var menuTargetIndex = parentItems.Items.IndexOf(targetMenuItem);
        if (sourceIndex < 0 ||
            targetIndex < 0 ||
            menuSourceIndex < 0 ||
            menuTargetIndex < 0)
        {
            return false;
        }

        sourceList.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        if (dropPosition == BookmarkBarDropPosition.After)
        {
            targetIndex++;
        }

        sourceList.Insert(targetIndex, source);
        parentItems.Items.Remove(sourceMenuItem);
        if (menuSourceIndex < menuTargetIndex)
        {
            menuTargetIndex--;
        }

        if (dropPosition == BookmarkBarDropPosition.After)
        {
            menuTargetIndex++;
        }

        parentItems.Items.Insert(menuTargetIndex, sourceMenuItem);
        return true;
    }

    /// <summary>
    /// 固定バーに表示するフォルダまたはページの見出しを作成する。
    /// </summary>
    /// <param name="bookmark">表示するブックマーク。</param>
    /// <returns>アイコンと名前を並べた見出し。</returns>
    private static StackPanel CreateBookmarkBarHeader(BookmarkItem bookmark)
    {
        var header = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = bookmark.IsFolder ? "\uE8B7" : "\uE774",
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(184, 172, 255)),
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(bookmark.Name) ? bookmark.Url : bookmark.Name,
            MaxWidth = 190,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        return header;
    }

    /// <summary>
    /// 固定バーで選択したブックマークを現在のタブへ表示する。
    /// </summary>
    /// <param name="bookmarkUrl">選択したブックマークのURL。</param>
    private void OpenBookmarkBarBookmark(string bookmarkUrl)
    {
        if (!UrlHelper.TryCreateUrl(bookmarkUrl, out var url))
        {
            return;
        }

        viewModel.Address = url;
        viewModel.OpenCommand.Execute(null);
    }

    /// <summary>
    /// 固定ブックマークバーのサブメニューを閉じる。
    /// </summary>
    private void CloseBookmarkBarSubmenus()
    {
        foreach (var menuItem in BookmarkBarMenu.Items.OfType<MenuItem>())
        {
            menuItem.IsSubmenuOpen = false;
        }
    }

    /// <summary>
    /// ブックマークメニューへページ操作用の項目を追加する。
    /// </summary>
    /// <param name="header">メニューに表示する文言。</param>
    /// <param name="clickHandler">選択時の処理。</param>
    private void AddBookmarkOperationMenuItem(string header, RoutedEventHandler clickHandler)
    {
        var menuItem = new MenuItem { Header = header };
        menuItem.Click += clickHandler;
        BookmarkMenuItem.Items.Add(menuItem);
    }

    /// <summary>
    /// 保存済みブックマークを折りたたみ式のツリー画面で表示する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void OpenBookmarkListMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var bookmarkWindow = new BookmarkWindow(viewModel.Bookmarks)
        {
            Owner = this
        };
        var dialogResult = bookmarkWindow.ShowDialog();
        if (bookmarkWindow.HasChanges)
        {
            viewModel.SaveBookmarks();
        }

        if (dialogResult != true ||
            !UrlHelper.TryCreateUrl(bookmarkWindow.SelectedUrl, out var url))
        {
            return;
        }

        viewModel.Address = url;
        viewModel.OpenCommand.Execute(null);
    }

    /// <summary>
    /// 指定ブラウザの既定プロファイルからブックマークを読み込む。
    /// </summary>
    /// <param name="browserType">読み込み元ブラウザ。</param>
    /// <param name="browserName">画面に表示するブラウザ名。</param>
    private void ImportFromBrowser(BrowserType browserType, string browserName)
    {
        try
        {
            MergeImportedBookmarks(bookmarkTransferService.ImportFromBrowser(browserType), browserName);
        }
        catch (FileNotFoundException)
        {
            MessageBox.Show(
                $"{browserName} の既定プロファイルにブックマークファイルが見つかりません。別のプロファイルを使っている場合はHTMLファイルからインポートしてください。",
                "インポート",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (JsonException)
        {
            MessageBox.Show($"{browserName} のブックマークファイルを読み込めませんでした。", "インポート", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (InvalidDataException)
        {
            MessageBox.Show($"{browserName} のブックマークファイルを読み込めませんでした。", "インポート", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (IOException)
        {
            MessageBox.Show($"{browserName} のブックマークファイルを読み込めませんでした。", "インポート", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 保存済みのブックマークをすべて削除する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void ClearBookmarksMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.Bookmarks.Count == 0)
        {
            return;
        }

        var result = MessageBox.Show(
            "保存済みのブックマークをすべて削除します。元に戻せません。",
            "ブックマークを削除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        viewModel.Bookmarks.Clear();
        viewModel.SaveBookmarks();
    }

    /// <summary>
    /// 読み込んだブックマークを置き換えまたは既存一覧へ追加する。
    /// </summary>
    /// <param name="importedBookmarks">読み込んだブックマーク一覧。</param>
    /// <param name="sourceName">読み込み元として表示する名称。</param>
    private void MergeImportedBookmarks(IEnumerable<BookmarkItem> importedBookmarks, string sourceName)
    {
        if (viewModel.Bookmarks.Count > 0)
        {
            var result = MessageBox.Show(
                "既存のブックマークをどうしますか？\n\nはい: 既存の内容を置き換える（ツリー表示にしたい場合はこちら）\nいいえ: 既存の内容へ追加する",
                "ブックマークをインポート",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel)
            {
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                viewModel.ReplaceBookmarks(importedBookmarks.Select(bookmarkService.Clone));
                MessageBox.Show($"{sourceName}の {bookmarkService.CountUrls(viewModel.Bookmarks)} 件のブックマークへ置き換えました。", "インポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        var registeredUrls = new HashSet<string>(bookmarkService.GetUrls(viewModel.Bookmarks), StringComparer.OrdinalIgnoreCase);
        var addedCount = bookmarkService.Merge(importedBookmarks, viewModel.Bookmarks, registeredUrls);

        viewModel.SaveBookmarks();
        MessageBox.Show($"{sourceName}から {addedCount} 件のブックマークを追加しました。", "インポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// 各タブのページURLが変わった時に、選択中のタブだけ入力欄へ反映する。
    /// </summary>
    /// <param name="sender">URLが変わったブラウザ。</param>
    /// <param name="e">変更前後のURL。</param>
    private void BrowserView_AddressChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ChromiumWebBrowser browser ||
            e.NewValue is not string address ||
            string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (ReferenceEquals(ActiveBrowserTab?.Browser, browser))
            {
                viewModel.UpdateAddress(address);
            }
        });
    }

    /// <summary>
    /// ページの読み込み状態に合わせて操作ボタンと準備表示を更新する。
    /// </summary>
    /// <param name="sender">読み込み状態が変わったブラウザ。</param>
    /// <param name="e">履歴と読み込み状態の情報。</param>
    private void BrowserView_LoadingStateChanged(object? sender, LoadingStateChangedEventArgs e)
    {
        if (sender is not ChromiumWebBrowser browser)
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            var browserTab = FindBrowserTab(browser);
            if (browserTab is null)
            {
                return;
            }

            if (!e.IsLoading)
            {
                browserTab.HasCompletedInitialLoad = true;
                browserTab.LoadingOverlay.Visibility = Visibility.Collapsed;
                UpdateTabHeader(browserTab);
            }

            if (ReferenceEquals(ActiveBrowserTab, browserTab))
            {
                UpdateNavigationButtons(e.CanGoBack, e.CanGoForward);
            }
        });
    }

    /// <summary>
    /// Webページをクリックした時にCefSharpへキーボード入力先を戻す。
    /// </summary>
    /// <param name="sender">クリックされたブラウザ。</param>
    /// <param name="e">マウス操作の情報。</param>
    private void BrowserView_PreviewMouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (sender is ChromiumWebBrowser browser)
        {
            RestoreBrowserFocus(browser);
        }
    }

    /// <summary>
    /// WPFとChromiumの両方へキーボード入力のフォーカスを設定する。
    /// </summary>
    /// <param name="browser">入力先へ戻すブラウザ。</param>
    private static void RestoreBrowserFocus(ChromiumWebBrowser browser)
    {
        browser.Focus();
        if (browser.IsBrowserInitialized)
        {
            browser.GetBrowserHost()?.SendFocusEvent(true);
        }
    }

    /// <summary>
    /// 新しいブラウザタブと、そのタブ専用のCefSharpブラウザを追加する。
    /// </summary>
    /// <param name="url">新しいタブで最初に表示するURL。</param>
    private void CreateBrowserTab(string url)
    {
        var headerTextBlock = new TextBlock
        {
            Text = "新しいタブ",
            MaxWidth = 170,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var tabHeader = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        tabHeader.Children.Add(headerTextBlock);

        var tabItem = new TabItem { Header = tabHeader };
        var closeButton = new System.Windows.Controls.Button
        {
            Content = "×",
            ToolTip = "タブを閉じる",
            Tag = tabItem,
            Style = (Style)FindResource("BrowserTabCloseButtonStyle")
        };
        closeButton.Click += CloseTabButton_Click;
        tabHeader.Children.Add(closeButton);

        var browser = new ChromiumWebBrowser
        {
            Background = System.Windows.Media.Brushes.Transparent,
            Opacity = viewModel.BrowserOpacity
        };
        browser.AddressChanged += BrowserView_AddressChanged;
        browser.LoadingStateChanged += BrowserView_LoadingStateChanged;
        browser.PreviewMouseLeftButtonDown += BrowserView_PreviewMouseLeftButtonDown;
        browser.LifeSpanHandler = browserPopupLifeSpanHandler;
        browser.MenuHandler = browserContextMenuHandler;

        var pageBackground = new Border
        {
            // Chromiumの描画が失敗しても透明なウィンドウ越しに背後を操作させない。
            Background = (System.Windows.Media.Brush)FindResource("WindowBackgroundBrush"),
            Opacity = viewModel.BrowserOpacity
        };
        var loadingOverlay = CreateLoadingOverlay();
        var translationOverlay = CreateTranslationOverlay();
        BindBrowserOpacity(browser);
        BindBrowserOpacity(pageBackground);
        BindBrowserOpacity(loadingOverlay);
        BindBrowserOpacity(translationOverlay);
        var browserGrid = new Grid();
        browserGrid.Children.Add(pageBackground);
        browserGrid.Children.Add(browser);
        browserGrid.Children.Add(loadingOverlay);
        browserGrid.Children.Add(translationOverlay);
        tabItem.Content = browserGrid;

        var browserTab = new BrowserTabState(browser, pageBackground, loadingOverlay, translationOverlay, headerTextBlock);
        browserTabs.Add(tabItem, browserTab);
        BrowserTabControl.Items.Add(tabItem);
        BrowserTabControl.SelectedItem = tabItem;

        viewModel.Address = url;
        browser.Address = url;
        _ = ShowInitialLoadingOverlayAsync(browserTab);
    }

    /// <summary>
    /// 動的に生成したブラウザ部品へViewModelの不透明度を反映する。
    /// </summary>
    /// <param name="element">不透明度を設定する画面部品。</param>
    private static void BindBrowserOpacity(UIElement element)
    {
        BindingOperations.SetBinding(
            element,
            UIElement.OpacityProperty,
            new System.Windows.Data.Binding(nameof(MainWindowViewModel.BrowserOpacity)));
    }

    /// <summary>
    /// 新しいタブの初回読み込みが長引いた時だけ準備表示を出す。
    /// </summary>
    /// <param name="browserTab">表示状態を確認するタブ。</param>
    private async Task ShowInitialLoadingOverlayAsync(BrowserTabState browserTab)
    {
        await Task.Delay(400);
        if (browserTab.HasCompletedInitialLoad ||
            !browserTabs.Values.Contains(browserTab) ||
            !browserTab.Browser.IsLoading)
        {
            return;
        }

        browserTab.LoadingOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// タブごとの読み込み中表示を作成する。
    /// </summary>
    /// <returns>読み込み中に表示するオーバーレイ。</returns>
    private Border CreateLoadingOverlay()
    {
        var loadingMessage = new TextBlock
        {
            Text = "ページを準備しています…",
            Foreground = (System.Windows.Media.Brush)FindResource("SubTextBrush"),
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        var loadingPanel = new StackPanel
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        loadingPanel.Children.Add(loadingMessage);
        loadingPanel.Children.Add(CreateIndeterminateProgressBar());

        var loadingCard = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(217, 30, 29, 27)),
            BorderBrush = (System.Windows.Media.Brush)FindResource("AccentSecondaryBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(28, 20, 28, 20),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Child = loadingPanel
        };
        return new Border
        {
            Background = System.Windows.Media.Brushes.Transparent,
            IsHitTestVisible = false,
            Opacity = viewModel.BrowserOpacity,
            Visibility = Visibility.Collapsed,
            Child = loadingCard
        };
    }

    /// <summary>
    /// Geminiのページ翻訳中にだけ表示する進行状況画面を作成する。
    /// </summary>
    /// <returns>翻訳中に操作を受け付けない進行状況画面。</returns>
    private Border CreateTranslationOverlay()
    {
        var title = new TextBlock
        {
            Text = "Geminiでページを翻訳しています…",
            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        var message = new TextBlock
        {
            Text = "ページの文字量により、少し時間がかかる場合があります。",
            Foreground = (System.Windows.Media.Brush)FindResource("SubTextBrush"),
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 12),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        var panel = new StackPanel
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        panel.Children.Add(title);
        panel.Children.Add(message);
        panel.Children.Add(CreateIndeterminateProgressBar());

        var card = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(238, 30, 29, 27)),
            BorderBrush = (System.Windows.Media.Brush)FindResource("AccentSecondaryBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(32, 24, 32, 24),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Child = panel
        };
        return new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(104, 0, 0, 0)),
            Visibility = Visibility.Collapsed,
            Opacity = viewModel.BrowserOpacity,
            Child = card
        };
    }

    /// <summary>
    /// 長さが確定しない処理で使うインジケーターを作成する。
    /// </summary>
    /// <returns>進行状況を示すプログレスバー。</returns>
    private System.Windows.Controls.ProgressBar CreateIndeterminateProgressBar()
    {
        return new System.Windows.Controls.ProgressBar
        {
            IsIndeterminate = true,
            Width = 196,
            Height = 5,
            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 52, 47))
        };
    }

    /// <summary>
    /// タブを切り替えた時にURL欄と履歴操作を更新する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">選択状態のイベント情報。</param>
    private void BrowserTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, BrowserTabControl) || ActiveBrowserTab is not { } browserTab)
        {
            return;
        }

        viewModel.UpdateAddress(browserTab.Browser.Address);
        UpdateNavigationButtons(browserTab.Browser.CanGoBack, browserTab.Browser.CanGoForward);
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            () => RestoreBrowserFocus(browserTab.Browser));
    }

    /// <summary>
    /// タブの閉じるボタンで対象タブを閉じる。
    /// </summary>
    /// <param name="sender">クリックされた閉じるボタン。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void CloseTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TabItem tabItem })
        {
            return;
        }

        CloseBrowserTab(tabItem);
    }

    /// <summary>
    /// 指定タブを閉じ、最後のタブだけはホームURLを開き直す。
    /// </summary>
    /// <param name="tabItem">閉じる対象のタブ。</param>
    private void CloseBrowserTab(TabItem tabItem)
    {
        if (!browserTabs.TryGetValue(tabItem, out var browserTab))
        {
            return;
        }

        if (browserTabs.Count == 1)
        {
            viewModel.Address = GetNewTabUrl();
            viewModel.OpenCommand.Execute(null);
            return;
        }

        var wasSelected = ReferenceEquals(BrowserTabControl.SelectedItem, tabItem);
        BrowserTabControl.Items.Remove(tabItem);
        browserTabs.Remove(tabItem);
        browserTab.Dispose();

        if (wasSelected && BrowserTabControl.Items.OfType<TabItem>().FirstOrDefault() is { } nextTab)
        {
            BrowserTabControl.SelectedItem = nextTab;
        }
    }

    /// <summary>
    /// ブラウザコントロールに対応するタブ状態を取得する。
    /// </summary>
    /// <param name="browser">検索対象のブラウザ。</param>
    /// <returns>対応するタブ状態。見つからない場合はnull。</returns>
    private BrowserTabState? FindBrowserTab(ChromiumWebBrowser browser)
    {
        return browserTabs.Values.FirstOrDefault(tab => ReferenceEquals(tab.Browser, browser));
    }

    /// <summary>
    /// 読み込み完了後のページタイトルをタブ見出しへ反映する。
    /// </summary>
    /// <param name="browserTab">タイトルを更新するタブ。</param>
    private static void UpdateTabHeader(BrowserTabState browserTab)
    {
        browserTab.HeaderTextBlock.Text = string.IsNullOrWhiteSpace(browserTab.Browser.Title)
            ? "新しいタブ"
            : browserTab.Browser.Title;
    }

    /// <summary>
    /// 現在の履歴状態に合わせて前後移動ボタンを更新する。
    /// </summary>
    /// <param name="canGoBack">前のページへ戻れるかどうか。</param>
    /// <param name="canGoForward">次のページへ進めるかどうか。</param>
    private void UpdateNavigationButtons(bool canGoBack, bool canGoForward)
    {
        viewModel.UpdateNavigationState(canGoBack, canGoForward);
        BackButtonIcon.Fill = canGoBack
            ? (System.Windows.Media.Brush)FindResource("AccentSecondaryBrush")
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(122, 115, 107));
        ForwardButtonIcon.Fill = canGoForward
            ? (System.Windows.Media.Brush)FindResource("AccentSecondaryBrush")
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(122, 115, 107));
    }

    /// <summary>
    /// 終了前に現在の画面設定とブックマークを保存する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">終了を取り消せるイベント情報。</param>
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!isExitConfirmed)
        {
            e.Cancel = true;
            HideToNotificationArea();
            return;
        }

        viewModel.SaveBeforeExit();
        trayIconService.Dispose();
        foreach (var browserTab in browserTabs.Values.ToList())
        {
            browserTab.Dispose();
        }

        browserTabs.Clear();
    }
}
