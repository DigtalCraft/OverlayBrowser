using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CefSharp;
using CefSharp.Wpf;
using OverlayBrowser.Helper;
using OverlayBrowser.Model;
using OverlayBrowser.Service;
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
    private const string CollectPageTextNodesScript = """
        (() => {
            const excludedTags = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'TEXTAREA', 'INPUT', 'SELECT', 'OPTION', 'CODE', 'PRE', 'SVG']);
            const textNodes = [];
            const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, {
                acceptNode(node) {
                    const parent = node.parentElement;
                    if (!parent || excludedTags.has(parent.tagName) || parent.isContentEditable || !node.nodeValue.trim()) {
                        return NodeFilter.FILTER_REJECT;
                    }

                    const style = window.getComputedStyle(parent);
                    return style.display === 'none' || style.visibility === 'hidden'
                        ? NodeFilter.FILTER_REJECT
                        : NodeFilter.FILTER_ACCEPT;
                }
            });

            while (walker.nextNode()) {
                textNodes.push(walker.currentNode);
            }

            window.__overlayBrowserTextNodes = textNodes;
            return JSON.stringify(textNodes.map((node, index) => ({ id: index, text: node.nodeValue })));
        })();
        """;
    private readonly SettingsService settingsService = new();
    private readonly BrowserBookmarkTransferService bookmarkTransferService = new();
    private readonly WindowsStartupService windowsStartupService = new();
    private readonly TrayIconService trayIconService = new();
    private readonly GeminiApiKeyStore geminiApiKeyStore = new();
    private readonly GeminiTranslationService geminiTranslationService;
    private readonly BrowserContextMenuHandler browserContextMenuHandler;
    private readonly Dictionary<TabItem, BrowserTabState> browserTabs = [];
    private readonly string? launchTarget;
    private readonly bool startHidden;
    private AppSettings settings = new();
    private bool isExitConfirmed;
    private WpfPoint bookmarkBarDragStartPoint;
    private BookmarkItem? draggedBookmarkBarItem;
    private MenuItem? draggedBookmarkBarMenuItem;
    private BookmarkItem? bookmarkBarDropTarget;
    private MenuItem? bookmarkBarDropTargetMenuItem;
    private BookmarkBarDropPosition bookmarkBarDropPosition;
    private bool suppressBookmarkBarClick;
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
        settings = settingsService.Load();
        Topmost = settings.IsTopmost;
        TopmostMenuItem.IsChecked = settings.IsTopmost;
        windowsStartupService.MigrateLegacyEntry();
        settings.IsStartWithWindows = windowsStartupService.IsEnabled();
        StartWithWindowsMenuItem.IsChecked = settings.IsStartWithWindows;
        BookmarkBarMenuItem.IsChecked = settings.IsBookmarkBarPinned;
        UpdateBookmarkBarVisibility();
        UpdateBookmarkMenu();
        OpacitySlider.Value = Math.Clamp(settings.Opacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
        CreateBrowserTab(GetStartupUrl());

        if (startHidden)
        {
            Dispatcher.BeginInvoke(HideToNotificationArea);
        }
    }

    /// <summary>
    /// URL入力欄の内容を検証して選択中のタブへ遷移する。
    /// </summary>
    private void NavigateToInputUrl()
    {
        if (!UrlHelper.TryCreateUrl(UrlTextBox.Text, out var url))
        {
            MessageBox.Show("http または https のサイト URL を入力してください。", "URL を確認してください", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        UrlTextBox.Text = url;
        var browserTab = ActiveBrowserTab;
        if (browserTab is null)
        {
            CreateBrowserTab(url);
            return;
        }

        browserTab.Browser.Address = url;
    }

    /// <summary>
    /// 開くボタンから URL 遷移する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToInputUrl();
    }

    /// <summary>
    /// ホームボタンから登録済みのホームURLを開く。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToHome();
    }

    /// <summary>
    /// 新しいタブボタンからホームURLを開くタブを追加する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void NewTabButton_Click(object sender, RoutedEventArgs e)
    {
        CreateBrowserTab(GetNewTabUrl());
    }

    /// <summary>
    /// URL 欄で Enter を押した時にサイトを開く。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">押下されたキーの情報。</param>
    private void UrlTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateToInputUrl();
        }
    }

    /// <summary>
    /// 再読み込みボタンから表示中のページを再読み込みする。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        ReloadBrowser();
    }

    /// <summary>
    /// メニューから表示中のページを再読み込みする。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void ReloadMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ReloadBrowser();
    }

    /// <summary>
    /// メニューからホームURLを開く新しいタブを追加する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void NewTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CreateBrowserTab(GetNewTabUrl());
    }

    /// <summary>
    /// メニューから登録済みのホームURLを開く。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void OpenHomeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        NavigateToHome();
    }

    /// <summary>
    /// 現在入力されているURLをホームURLとして保存する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void SetHomeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!UrlHelper.TryCreateUrl(UrlTextBox.Text, out var url))
        {
            MessageBox.Show("ホームに設定できる URL がありません。", "URL を確認してください", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        settings.HomeUrl = url;
        settingsService.Save(settings);
        MessageBox.Show("現在の URL をホームに設定しました。", "ホーム設定", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// ホームURLを入力欄へ反映して表示する。
    /// </summary>
    private void NavigateToHome()
    {
        UrlTextBox.Text = string.IsNullOrWhiteSpace(settings.HomeUrl)
            ? "https://www.google.com/"
            : settings.HomeUrl;
        NavigateToInputUrl();
    }

    /// <summary>
    /// 起動時に表示するホームURLまたは前回のURLを取得する。
    /// </summary>
    /// <returns>起動時に開くURL。</returns>
    private string GetStartupUrl()
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
    /// 新規タブで最初に表示するURLを取得する。
    /// </summary>
    /// <returns>新規タブで開くURL。</returns>
    private string GetNewTabUrl()
    {
        return string.IsNullOrWhiteSpace(settings.HomeUrl)
            ? "https://www.google.com/"
            : settings.HomeUrl;
    }

    /// <summary>
    /// 前に表示していたページへ戻る。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var browserTab = ActiveBrowserTab;
        if (browserTab is null || !browserTab.Browser.CanGoBack)
        {
            return;
        }

        browserTab.Browser.Back();
    }

    /// <summary>
    /// 次に表示していたページへ進む。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        var browserTab = ActiveBrowserTab;
        if (browserTab is null || !browserTab.Browser.CanGoForward)
        {
            return;
        }

        browserTab.Browser.Forward();
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
    /// 常に前面に表示する設定を切り替える。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Topmost = TopmostMenuItem.IsChecked;
    }

    /// <summary>
    /// Windowsサインイン時にタスクトレイへ常駐する設定を変更する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void StartWithWindowsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            windowsStartupService.SetEnabled(StartWithWindowsMenuItem.IsChecked);
            settings.IsStartWithWindows = StartWithWindowsMenuItem.IsChecked;
            settingsService.Save(settings);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException)
        {
            StartWithWindowsMenuItem.IsChecked = windowsStartupService.IsEnabled();
            MessageBox.Show(
                "Windowsの起動設定を変更できませんでした。",
                "設定",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// ブックマークバーの固定表示を切り替えて設定を保存する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void BookmarkBarMenuItem_Click(object sender, RoutedEventArgs e)
    {
        settings.IsBookmarkBarPinned = BookmarkBarMenuItem.IsChecked;
        UpdateBookmarkBarVisibility();
        settingsService.Save(settings);
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
    /// 透明度スライダーの値を各表示領域へ反映する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">変更前後の透明度。</param>
    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // CefSharpはウィンドウ全体の透明度を継承しないため、各領域へ直接反映する。
        Opacity = 1;
        if (TitleBarSurface is not null)
        {
            TitleBarSurface.Opacity = e.NewValue;
        }

        if (MenuBarSurface is not null)
        {
            MenuBarSurface.Opacity = e.NewValue;
        }

        if (BookmarkBarSurface is not null)
        {
            BookmarkBarSurface.Opacity = e.NewValue;
        }

        if (ToolbarSurface is not null)
        {
            ToolbarSurface.Opacity = e.NewValue;
        }

        if (BrowserWorkspaceBackground is not null)
        {
            BrowserWorkspaceBackground.Opacity = e.NewValue;
        }

        foreach (var browserTab in browserTabs.Values)
        {
            browserTab.PageBackground.Opacity = e.NewValue;
            browserTab.Browser.Opacity = e.NewValue;
            browserTab.LoadingOverlay.Opacity = e.NewValue;
            browserTab.TranslationOverlay.Opacity = e.NewValue;
        }

        if (OpacityTextBlock is not null)
        {
            OpacityTextBlock.Text = $"{e.NewValue:P0}";
        }
    }

    /// <summary>
    /// 現在表示中の URL をブックマークに追加する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void AddBookmarkMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!UrlHelper.TryCreateUrl(UrlTextBox.Text, out var url))
        {
            MessageBox.Show("ブックマークに追加できる URL がありません。", "URL を確認してください", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (GetBookmarkUrls(settings.Bookmarks).Any(itemUrl =>
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
        var destinationWindow = new BookmarkWindow(settings.Bookmarks, isDestinationSelection: true)
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
            settings.Bookmarks.Add(bookmark);
        }

        settingsService.Save(settings);
        UpdateBookmarkMenu();
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
    /// Chromeの既定プロファイルからブックマークを取り込む。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void ImportChromeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ImportFromBrowser(BrowserType.Chrome, "Chrome");
    }

    /// <summary>
    /// Edgeの既定プロファイルからブックマークを取り込む。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void ImportEdgeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ImportFromBrowser(BrowserType.Edge, "Edge");
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
            bookmarkTransferService.ExportToHtml(dialog.FileName, settings.Bookmarks);
            MessageBox.Show("Chrome と Edge で読み込めるHTML形式で書き出しました。", "エクスポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (IOException)
        {
            MessageBox.Show("ブックマークファイルを書き出せませんでした。", "エクスポート", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 日本語と英語の操作説明を表示する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void HelpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenHelpWindow();
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
        var personalizationWindow = new TranslationPersonalizationWindow(settings.TranslationPersonalization)
        {
            Owner = this
        };
        if (personalizationWindow.ShowDialog() != true)
        {
            return;
        }

        settings.TranslationPersonalization = personalizationWindow.Personalization;
        settingsService.Save(settings);
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
    /// メニューバーから終了確認画面を表示する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RequestExit();
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
        if (settings.Bookmarks.Count == 0)
        {
            BookmarkMenuItem.Items.Add(new MenuItem { Header = "ブックマークがありません", IsEnabled = false });
        }
        else
        {
            var openBookmarkListMenuItem = new MenuItem
            {
                Header = $"ブックマーク一覧を開く（{CountBookmarkUrls(settings.Bookmarks)}件）"
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
        if (settings.Bookmarks.Count == 0)
        {
            BookmarkBarMenu.Items.Add(new MenuItem
            {
                Header = "ブックマークがありません",
                IsEnabled = false,
                Tag = "BookmarkBar"
            });
            return;
        }

        foreach (var bookmark in settings.Bookmarks)
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
            !RemoveBookmark(settings.Bookmarks, bookmark))
        {
            return;
        }

        settingsService.Save(settings);
        UpdateBookmarkMenu();
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
            !ContainsBookmark(bookmark, target))
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
    /// マウス位置にある固定ブックマークバーの項目を取得する。
    /// </summary>
    /// <param name="element">検索開始する画面要素。</param>
    /// <returns>対象のブックマーク。見つからない場合はnull。</returns>
    private static BookmarkItem? FindBookmarkBarItem(DependencyObject? element)
    {
        return FindBookmarkBarMenuItem(element)?.Tag as BookmarkItem;
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
            settingsService.Save(settings);
            return;
        }

        if (ReferenceEquals(source, target) ||
            ContainsBookmark(source, target) ||
            !RemoveBookmark(settings.Bookmarks, source))
        {
            return;
        }

        if (dropPosition == BookmarkBarDropPosition.Inside && target.IsFolder)
        {
            target.Children.Add(source);
        }
        else if (FindBookmarkList(settings.Bookmarks, target) is { } targetList)
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
            settings.Bookmarks.Add(source);
        }

        settingsService.Save(settings);
        UpdateBookmarkMenu();
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
        var sourceList = FindBookmarkList(settings.Bookmarks, source);
        var targetList = FindBookmarkList(settings.Bookmarks, target);
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
    /// 指定したブックマークを含む一覧から削除する。
    /// </summary>
    /// <param name="items">検索対象の一覧。</param>
    /// <param name="target">削除するブックマーク。</param>
    /// <returns>削除できた場合はtrue。</returns>
    private static bool RemoveBookmark(IList<BookmarkItem> items, BookmarkItem target)
    {
        if (items.Remove(target))
        {
            return true;
        }

        return items.Where(item => item.IsFolder).Any(item => RemoveBookmark(item.Children, target));
    }

    /// <summary>
    /// 指定したブックマークを直接含む一覧を取得する。
    /// </summary>
    /// <param name="items">検索対象の一覧。</param>
    /// <param name="target">検索するブックマーク。</param>
    /// <returns>直接含む一覧。見つからない場合はnull。</returns>
    private static IList<BookmarkItem>? FindBookmarkList(IList<BookmarkItem> items, BookmarkItem target)
    {
        if (items.Contains(target))
        {
            return items;
        }

        foreach (var folder in items.Where(item => item.IsFolder))
        {
            if (FindBookmarkList(folder.Children, target) is { } childList)
            {
                return childList;
            }
        }

        return null;
    }

    /// <summary>
    /// 指定した親フォルダの配下に対象が含まれるか確認する。
    /// </summary>
    /// <param name="parent">親として確認するブックマーク。</param>
    /// <param name="target">検索するブックマーク。</param>
    /// <returns>配下に含まれる場合はtrue。</returns>
    private static bool ContainsBookmark(BookmarkItem parent, BookmarkItem target)
    {
        return parent.Children.Any(child =>
            ReferenceEquals(child, target) || ContainsBookmark(child, target));
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

        UrlTextBox.Text = url;
        NavigateToInputUrl();
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
    /// 保存済み設定に合わせてブックマークバーの表示状態を更新する。
    /// </summary>
    private void UpdateBookmarkBarVisibility()
    {
        BookmarkBarSurface.Visibility = settings.IsBookmarkBarPinned
            ? Visibility.Visible
            : Visibility.Collapsed;
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
        var bookmarkWindow = new BookmarkWindow(settings.Bookmarks)
        {
            Owner = this
        };
        var dialogResult = bookmarkWindow.ShowDialog();
        if (bookmarkWindow.HasChanges)
        {
            settingsService.Save(settings);
            UpdateBookmarkMenu();
        }

        if (dialogResult != true ||
            !UrlHelper.TryCreateUrl(bookmarkWindow.SelectedUrl, out var url))
        {
            return;
        }

        UrlTextBox.Text = url;
        NavigateToInputUrl();
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
        if (settings.Bookmarks.Count == 0)
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

        settings.Bookmarks.Clear();
        settingsService.Save(settings);
        UpdateBookmarkMenu();
    }

    /// <summary>
    /// 読み込んだブックマークを置き換えまたは既存一覧へ追加する。
    /// </summary>
    /// <param name="importedBookmarks">読み込んだブックマーク一覧。</param>
    /// <param name="sourceName">読み込み元として表示する名称。</param>
    private void MergeImportedBookmarks(IEnumerable<BookmarkItem> importedBookmarks, string sourceName)
    {
        if (settings.Bookmarks.Count > 0)
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
                settings.Bookmarks = importedBookmarks.Select(CloneBookmark).ToList();
                settingsService.Save(settings);
                UpdateBookmarkMenu();
                MessageBox.Show($"{sourceName}の {CountBookmarkUrls(settings.Bookmarks)} 件のブックマークへ置き換えました。", "インポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        var registeredUrls = new HashSet<string>(GetBookmarkUrls(settings.Bookmarks), StringComparer.OrdinalIgnoreCase);
        var addedCount = MergeBookmarkItems(importedBookmarks, settings.Bookmarks, registeredUrls);

        settingsService.Save(settings);
        UpdateBookmarkMenu();
        MessageBox.Show($"{sourceName}から {addedCount} 件のブックマークを追加しました。", "インポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// ブックマークを子要素も含めて複製する。
    /// </summary>
    /// <param name="bookmark">複製元のブックマーク。</param>
    /// <returns>複製したブックマーク。</returns>
    private static BookmarkItem CloneBookmark(BookmarkItem bookmark)
    {
        return new BookmarkItem
        {
            Name = bookmark.Name,
            Url = bookmark.Url,
            IsFolder = bookmark.IsFolder,
            Children = bookmark.Children.Select(CloneBookmark).ToList()
        };
    }

    /// <summary>
    /// 階層内のURLブックマーク件数を数える。
    /// </summary>
    /// <param name="bookmarks">集計対象のブックマーク一覧。</param>
    /// <returns>URLブックマークの件数。</returns>
    private static int CountBookmarkUrls(IEnumerable<BookmarkItem> bookmarks)
    {
        return bookmarks.Sum(bookmark => bookmark.IsFolder ? CountBookmarkUrls(bookmark.Children) : 1);
    }

    /// <summary>
    /// 読み込んだブックマークを保存済みの同名フォルダへ結合する。
    /// </summary>
    /// <param name="importedBookmarks">読み込んだブックマーク一覧。</param>
    /// <param name="destination">追加先のブックマーク一覧。</param>
    /// <param name="registeredUrls">重複を判定するURL一覧。</param>
    /// <returns>追加したURLの件数。</returns>
    private static int MergeBookmarkItems(
        IEnumerable<BookmarkItem> importedBookmarks,
        ICollection<BookmarkItem> destination,
        ISet<string> registeredUrls)
    {
        var addedCount = 0;
        foreach (var bookmark in importedBookmarks)
        {
            if (bookmark.IsFolder)
            {
                var folderName = string.IsNullOrWhiteSpace(bookmark.Name) ? "フォルダ" : bookmark.Name;
                var destinationFolder = destination.FirstOrDefault(item =>
                    item.IsFolder && string.Equals(item.Name, folderName, StringComparison.CurrentCultureIgnoreCase));
                if (destinationFolder is null)
                {
                    destinationFolder = new BookmarkItem { Name = folderName, IsFolder = true };
                    destination.Add(destinationFolder);
                }

                addedCount += MergeBookmarkItems(bookmark.Children, destinationFolder.Children, registeredUrls);
                continue;
            }

            if (!UrlHelper.TryCreateUrl(bookmark.Url, out var normalizedUrl) || !registeredUrls.Add(normalizedUrl))
            {
                continue;
            }

            destination.Add(new BookmarkItem
            {
                Name = string.IsNullOrWhiteSpace(bookmark.Name) ? normalizedUrl : bookmark.Name,
                Url = normalizedUrl
            });
            addedCount++;
        }

        return addedCount;
    }

    /// <summary>
    /// 階層内に登録済みのURLを列挙する。
    /// </summary>
    /// <param name="bookmarks">検索対象のブックマーク一覧。</param>
    /// <returns>登録済みURL。</returns>
    private static IEnumerable<string> GetBookmarkUrls(IEnumerable<BookmarkItem> bookmarks)
    {
        foreach (var bookmark in bookmarks)
        {
            if (bookmark.IsFolder)
            {
                foreach (var childUrl in GetBookmarkUrls(bookmark.Children))
                {
                    yield return childUrl;
                }

                continue;
            }

            if (UrlHelper.TryCreateUrl(bookmark.Url, out var normalizedUrl))
            {
                yield return normalizedUrl;
            }
        }
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
                UrlTextBox.Text = address;
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
            Opacity = OpacitySlider.Value
        };
        browser.AddressChanged += BrowserView_AddressChanged;
        browser.LoadingStateChanged += BrowserView_LoadingStateChanged;
        browser.MenuHandler = browserContextMenuHandler;

        var pageBackground = new Border
        {
            // Chromiumの描画が失敗しても透明なウィンドウ越しに背後を操作させない。
            Background = (System.Windows.Media.Brush)FindResource("WindowBackgroundBrush"),
            Opacity = OpacitySlider.Value
        };
        var loadingOverlay = CreateLoadingOverlay();
        var translationOverlay = CreateTranslationOverlay();
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

        UrlTextBox.Text = url;
        browser.Address = url;
        _ = ShowInitialLoadingOverlayAsync(browserTab);
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
            Opacity = OpacitySlider.Value,
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
            Opacity = OpacitySlider.Value,
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
    /// CefSharpの右クリックメニューからページ翻訳を開始する。
    /// </summary>
    /// <param name="sender">翻訳メニューを通知した処理。</param>
    /// <param name="browser">翻訳対象のブラウザ。</param>
    private void BrowserContextMenuHandler_PageTranslationRequested(object? sender, IWebBrowser browser)
    {
        if (browser is not ChromiumWebBrowser chromiumWebBrowser)
        {
            return;
        }

        Dispatcher.InvokeAsync(() => TranslatePageInBrowser(chromiumWebBrowser));
    }

    /// <summary>
    /// Google翻訳を使い、表示中のタブをページ翻訳表示へ切り替える。
    /// </summary>
    /// <param name="browser">翻訳対象のブラウザ。</param>
    private void TranslatePageInBrowser(ChromiumWebBrowser browser)
    {
        if (!UrlHelper.TryCreateUrl(browser.Address, out var pageUrl))
        {
            ShowTranslationMessage(FindBrowserTab(browser), "このページは翻訳できません。", MessageBoxImage.Information);
            return;
        }

        var targetLanguage = translationTargetCulture.TwoLetterISOLanguageName;
        var translatedUrl = $"https://translate.google.com/translate?sl=auto&tl={Uri.EscapeDataString(targetLanguage)}&u={Uri.EscapeDataString(pageUrl)}";
        browser.Address = translatedUrl;
    }

    /// <summary>
    /// CefSharpの右クリックメニューからGemini翻訳を開始する。
    /// </summary>
    /// <param name="sender">翻訳メニューを通知した処理。</param>
    /// <param name="browser">翻訳対象のブラウザ。</param>
    private void BrowserContextMenuHandler_GeminiPageTranslationRequested(object? sender, IWebBrowser browser)
    {
        if (browser is not ChromiumWebBrowser chromiumWebBrowser)
        {
            return;
        }

        Dispatcher.InvokeAsync(() => _ = TranslatePageWithGeminiAsync(chromiumWebBrowser));
    }

    /// <summary>
    /// 表示中ページの文字だけをGeminiで翻訳し、元の位置へ反映する。
    /// </summary>
    /// <param name="browser">翻訳対象のブラウザ。</param>
    /// <returns>翻訳処理の完了を表すタスク。</returns>
    private async Task TranslatePageWithGeminiAsync(
        ChromiumWebBrowser browser,
        string modelName = GeminiTranslationService.DefaultModelName)
    {
        var browserTab = FindBrowserTab(browser);
        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            if (browserTab is not null)
            {
                browserTab.TranslationOverlay.Visibility = Visibility.Visible;
            }

            var extraction = await browser.EvaluateScriptAsync(CollectPageTextNodesScript);
            if (!extraction.Success || extraction.Result is not string pageTextJson)
            {
                ShowTranslationMessage(browserTab, "このページから翻訳できる文章を取得できませんでした。", MessageBoxImage.Information);
                return;
            }

            var segments = JsonSerializer.Deserialize<List<GeminiTranslationService.PageTextSegment>>(
                pageTextJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (segments is null || segments.Count == 0 || segments.Any(segment => string.IsNullOrWhiteSpace(segment.Text)))
            {
                ShowTranslationMessage(browserTab, "このページから翻訳できる文章を取得できませんでした。", MessageBoxImage.Information);
                return;
            }

            var result = await geminiTranslationService.TranslateSegmentsAsync(
                segments,
                translationTargetCulture,
                settings.TranslationPersonalization,
                modelName);
            if (!result.IsSuccess)
            {
                var failureResult = ShowTranslationFailure(browserTab, result.Message, modelName);
                if (failureResult == GeminiBusyWindowResult.Retry)
                {
                    await TranslatePageWithGeminiAsync(browser, modelName);
                }
                else if (failureResult == GeminiBusyWindowResult.UseAlternativeModel)
                {
                    var alternativeModel = modelName == GeminiTranslationService.DefaultModelName
                        ? GeminiTranslationService.AlternativeModelName
                        : GeminiTranslationService.DefaultModelName;
                    await TranslatePageWithGeminiAsync(browser, alternativeModel);
                }

                return;
            }

            var application = await browser.EvaluateScriptAsync(
                CreateApplyPageTranslationScript(segments, result.Translations));
            if (!TryGetTranslationApplicationResult(application, out var appliedCount) || appliedCount == 0)
            {
                ShowTranslationMessage(browserTab, "翻訳結果をページへ反映できませんでした。ページを再読み込みしてから、もう一度試してください。", MessageBoxImage.Warning);
            }
        }
        catch (JsonException)
        {
            ShowTranslationMessage(browserTab, "ページ本文の読み取りに失敗しました。ページを再読み込みしてから、もう一度試してください。", MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Gemini page translation failed: {exception}");
            ShowTranslationMessage(browserTab, "翻訳処理を完了できませんでした。ページを再読み込みしてから、もう一度試してください。", MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            if (browserTab is not null)
            {
                browserTab.TranslationOverlay.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// 翻訳中表示を閉じてから、利用者向けのメッセージを表示する。
    /// </summary>
    /// <param name="browserTab">翻訳対象のタブ。</param>
    /// <param name="message">表示するメッセージ。</param>
    /// <param name="image">メッセージの種別。</param>
    private void ShowTranslationMessage(BrowserTabState? browserTab, string message, MessageBoxImage image)
    {
        if (browserTab is not null)
        {
            browserTab.TranslationOverlay.Visibility = Visibility.Collapsed;
        }

        _ = image;
        var messageWindow = new TranslationMessageWindow("翻訳", message)
        {
            Owner = this
        };
        messageWindow.ShowDialog();
    }

    /// <summary>
    /// Geminiの失敗内容を表示し、混雑時だけ再試行の選択を受け取る。
    /// </summary>
    /// <param name="browserTab">翻訳対象のタブ。</param>
    /// <param name="message">Gemini APIから返された利用者向けメッセージ。</param>
    /// <returns>すぐに再試行する場合はtrue。</returns>
    private GeminiBusyWindowResult ShowTranslationFailure(
        BrowserTabState? browserTab,
        string message,
        string modelName)
    {
        if (browserTab is not null)
        {
            browserTab.TranslationOverlay.Visibility = Visibility.Collapsed;
        }

        if (!message.StartsWith("Gemini APIが混雑しています。", StringComparison.Ordinal))
        {
            var messageWindow = new TranslationMessageWindow("翻訳", message)
            {
                Owner = this
            };
            messageWindow.ShowDialog();
            return GeminiBusyWindowResult.Close;
        }

        var busyWindow = new GeminiBusyWindow(
            message,
            modelName != GeminiTranslationService.AlternativeModelName)
        {
            Owner = this
        };
        busyWindow.ShowDialog();
        return busyWindow.Result;
    }

    /// <summary>
    /// Geminiの翻訳結果を、抽出時に保持したページ内の文字ノードへ反映するスクリプトを作成する。
    /// </summary>
    /// <param name="sourceSegments">翻訳前に取得した文字ノード一覧。</param>
    /// <param name="translations">ノードIDに対応した翻訳結果。</param>
    /// <returns>ページへ実行するJavaScript。</returns>
    private static string CreateApplyPageTranslationScript(
        IReadOnlyList<GeminiTranslationService.PageTextSegment> sourceSegments,
        IReadOnlyList<GeminiTranslationService.PageTextSegment> translations)
    {
        var sourceTextById = sourceSegments.ToDictionary(segment => segment.Id, segment => segment.Text);
        var replacements = translations
            .Where(translation => sourceTextById.ContainsKey(translation.Id))
            .Select(translation => new PageTextReplacement(
                translation.Id,
                sourceTextById[translation.Id],
                translation.Text))
            .ToList();
        var replacementJson = JsonSerializer.Serialize(
            replacements,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return $$"""
            (() => {
                const textNodes = window.__overlayBrowserTextNodes;
                const replacements = {{replacementJson}};
                const excludedTags = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'TEXTAREA', 'INPUT', 'SELECT', 'OPTION', 'CODE', 'PRE', 'SVG']);
                const usedNodes = new Set();

                const findCurrentTextNode = (sourceText) => {
                    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, {
                        acceptNode(node) {
                            const parent = node.parentElement;
                            if (!parent || excludedTags.has(parent.tagName) || parent.isContentEditable || usedNodes.has(node)) {
                                return NodeFilter.FILTER_REJECT;
                            }

                            return node.nodeValue === sourceText
                                ? NodeFilter.FILTER_ACCEPT
                                : NodeFilter.FILTER_REJECT;
                        }
                    });
                    return walker.nextNode();
                };

                let updatedCount = 0;
                for (const replacement of replacements) {
                    let node = Array.isArray(textNodes) ? textNodes[replacement.id] : null;
                    if (!node || !node.parentElement || node.nodeValue !== replacement.sourceText || usedNodes.has(node)) {
                        node = findCurrentTextNode(replacement.sourceText);
                    }

                    if (node && typeof replacement.translatedText === 'string') {
                        node.nodeValue = replacement.translatedText;
                        usedNodes.add(node);
                        updatedCount++;
                    }
                }

                return JSON.stringify({ updatedCount, requestedCount: replacements.length });
            })();
            """;
    }

    /// <summary>
    /// JavaScriptが返したページ反映件数を読み取る。
    /// </summary>
    /// <param name="application">ページ反映スクリプトの実行結果。</param>
    /// <param name="appliedCount">反映できた文字ノード数。</param>
    /// <returns>実行結果を読み取れた場合はtrue。</returns>
    private static bool TryGetTranslationApplicationResult(JavascriptResponse application, out int appliedCount)
    {
        appliedCount = 0;
        if (!application.Success || application.Result is not string resultJson)
        {
            return false;
        }

        try
        {
            var result = JsonSerializer.Deserialize<TranslationApplicationResult>(
                resultJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result is null || result.RequestedCount == 0)
            {
                return false;
            }

            appliedCount = result.UpdatedCount;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 元の文字列と翻訳後の文字列を対応付ける。
    /// </summary>
    /// <param name="Id">抽出時の文字ノードID。</param>
    /// <param name="SourceText">翻訳前の文字列。</param>
    /// <param name="TranslatedText">Geminiが返した翻訳後の文字列。</param>
    private sealed record PageTextReplacement(int Id, string SourceText, string TranslatedText);

    /// <summary>
    /// ページ反映スクリプトの結果を表す。
    /// </summary>
    /// <param name="UpdatedCount">翻訳を反映した文字ノード数。</param>
    /// <param name="RequestedCount">反映を依頼した文字ノード数。</param>
    private sealed record TranslationApplicationResult(int UpdatedCount, int RequestedCount);

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

        UrlTextBox.Text = browserTab.Browser.Address;
        UpdateNavigationButtons(browserTab.Browser.CanGoBack, browserTab.Browser.CanGoForward);
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
            UrlTextBox.Text = GetNewTabUrl();
            NavigateToInputUrl();
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
        BackButton.IsEnabled = canGoBack;
        ForwardButton.IsEnabled = canGoForward;
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

        settings.LastUrl = UrlTextBox.Text;
        settings.IsTopmost = Topmost;
        settings.Opacity = OpacitySlider.Value;
        settingsService.Save(settings);
        trayIconService.Dispose();
        foreach (var browserTab in browserTabs.Values.ToList())
        {
            browserTab.Dispose();
        }

        browserTabs.Clear();
    }
}
