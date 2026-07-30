using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OverlayBrowser.Model;
using WpfDataObject = System.Windows.DataObject;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace OverlayBrowser.Form;

/// <summary>
/// 保存済みブックマークをフォルダ階層で表示する画面。
/// </summary>
public partial class BookmarkWindow : Window
{
    private readonly IList<BookmarkItem> bookmarks;
    private readonly bool isDestinationSelection;
    private WpfPoint dragStartPoint;

    /// <summary>
    /// ユーザーが選択したブックマークのURL。
    /// </summary>
    public string SelectedUrl { get; private set; } = string.Empty;

    /// <summary>
    /// 追加先として選択したフォルダ。
    /// </summary>
    public BookmarkItem? SelectedFolder { get; private set; }

    /// <summary>
    /// 追加先としてトップ階層を選択したかどうか。
    /// </summary>
    public bool IsRootDestinationSelected { get; private set; }

    /// <summary>
    /// ドラッグ＆ドロップで構成を変更したかどうか。
    /// </summary>
    public bool HasChanges { get; private set; }

    /// <summary>
    /// ブックマーク一覧画面を初期化する。
    /// </summary>
    /// <param name="bookmarks">表示するブックマーク一覧。</param>
    /// <param name="isDestinationSelection">追加先フォルダを選択する画面として開く場合はtrue。</param>
    public BookmarkWindow(IList<BookmarkItem> bookmarks, bool isDestinationSelection = false)
    {
        InitializeComponent();
        this.bookmarks = bookmarks;
        this.isDestinationSelection = isDestinationSelection;
        DataContext = bookmarks;
        ApplyWindowMode();
    }

    /// <summary>
    /// 選択項目に応じて開くボタンの状態を更新する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">選択前後の項目情報。</param>
    private void BookmarkTreeView_SelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (isDestinationSelection && e.NewValue is BookmarkItem { IsFolder: true } folder)
        {
            OpenButton.IsEnabled = true;
            SelectionTextBlock.Text = $"「{folder.Name}」へ追加します";
            return;
        }

        if (!isDestinationSelection && e.NewValue is BookmarkItem { IsFolder: false } bookmark &&
            !string.IsNullOrWhiteSpace(bookmark.Url))
        {
            OpenButton.IsEnabled = true;
            SelectionTextBlock.Text = bookmark.Name;
            return;
        }

        OpenButton.IsEnabled = false;
        SelectionTextBlock.Text = "サイトを選択してください";
    }

    /// <summary>
    /// サイトをダブルクリックした時に選択結果を確定する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">マウス操作のイベント情報。</param>
    private void BookmarkTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (isDestinationSelection)
        {
            SelectDestinationFolder();
            return;
        }

        OpenSelectedBookmark();
    }

    /// <summary>
    /// 開くボタンで選択結果を確定する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (isDestinationSelection)
        {
            SelectDestinationFolder();
            return;
        }

        OpenSelectedBookmark();
    }

    /// <summary>
    /// 選択中のURLをメイン画面へ返す。
    /// </summary>
    private void OpenSelectedBookmark()
    {
        if (BookmarkTreeView.SelectedItem is not BookmarkItem { IsFolder: false } bookmark ||
            string.IsNullOrWhiteSpace(bookmark.Url))
        {
            return;
        }

        SelectedUrl = bookmark.Url;
        DialogResult = true;
    }

    /// <summary>
    /// 選択中のフォルダを新規ブックマークの追加先として返す。
    /// </summary>
    private void SelectDestinationFolder()
    {
        if (BookmarkTreeView.SelectedItem is not BookmarkItem { IsFolder: true } folder)
        {
            return;
        }

        SelectedFolder = folder;
        IsRootDestinationSelected = false;
        DialogResult = true;
    }

    /// <summary>
    /// トップ階層を新規ブックマークの追加先として返す。
    /// </summary>
    /// <param name="sender">クリックされたボタン。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void RootDestinationButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedFolder = null;
        IsRootDestinationSelected = true;
        DialogResult = true;
    }

    /// <summary>
    /// 画面の用途に合わせて説明文と操作ボタンを切り替える。
    /// </summary>
    private void ApplyWindowMode()
    {
        if (!isDestinationSelection)
        {
            return;
        }

        Title = "ブックマークの追加先";
        WindowTitleTextBlock.Text = "ブックマークの追加先";
        GuideTextBlock.Text = "追加先フォルダを選択してください。フォルダを選ばず、トップ階層へ追加することもできます。";
        SelectionTextBlock.Text = "追加先フォルダを選択してください";
        RootDestinationButton.Visibility = Visibility.Visible;
        OpenButton.Content = "ここへ追加";
    }

    /// <summary>
    /// ドラッグ開始位置を記録する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">マウス操作のイベント情報。</param>
    private void BookmarkTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragStartPoint = e.GetPosition(BookmarkTreeView);
    }

    /// <summary>
    /// 一覧操作時に選択中のブックマークをドラッグする。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">マウス操作のイベント情報。</param>
    private void BookmarkTreeView_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (isDestinationSelection || e.LeftButton != MouseButtonState.Pressed ||
            BookmarkTreeView.SelectedItem is not BookmarkItem bookmark)
        {
            return;
        }

        var currentPoint = e.GetPosition(BookmarkTreeView);
        if (Math.Abs(currentPoint.X - dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(BookmarkTreeView, new WpfDataObject(typeof(BookmarkItem), bookmark), WpfDragDropEffects.Move);
    }

    /// <summary>
    /// ドロップ先として受け入れ可能か判定する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">ドラッグ操作のイベント情報。</param>
    private void BookmarkTreeView_DragOver(object sender, WpfDragEventArgs e)
    {
        e.Effects = !isDestinationSelection && e.Data.GetDataPresent(typeof(BookmarkItem))
            ? WpfDragDropEffects.Move
            : WpfDragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// ブックマークをフォルダ内または同じ階層の指定位置へ移動する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">ドラッグ操作のイベント情報。</param>
    private void BookmarkTreeView_Drop(object sender, WpfDragEventArgs e)
    {
        if (isDestinationSelection || e.Data.GetData(typeof(BookmarkItem)) is not BookmarkItem source)
        {
            return;
        }

        var targetItem = FindBookmarkItem(e.OriginalSource as DependencyObject);
        if (ReferenceEquals(source, targetItem) || (targetItem is not null && ContainsBookmark(source, targetItem)))
        {
            return;
        }

        if (!RemoveBookmark(bookmarks, source))
        {
            return;
        }

        if (targetItem is null)
        {
            bookmarks.Add(source);
        }
        else if (targetItem.IsFolder)
        {
            targetItem.Children.Add(source);
        }
        else if (FindBookmarkList(bookmarks, targetItem) is { } targetList)
        {
            targetList.Insert(targetList.IndexOf(targetItem), source);
        }
        else
        {
            bookmarks.Add(source);
        }

        HasChanges = true;
        RefreshTreeView();
    }

    /// <summary>
    /// マウス位置にあるツリー項目のブックマークを取得する。
    /// </summary>
    /// <param name="element">検索開始する画面要素。</param>
    /// <returns>対象のブックマーク。見つからない場合はnull。</returns>
    private static BookmarkItem? FindBookmarkItem(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TreeViewItem { DataContext: BookmarkItem bookmark })
            {
                return bookmark;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
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
    /// リスト構成変更後にツリー表示を更新する。
    /// </summary>
    private void RefreshTreeView()
    {
        BookmarkTreeView.ItemsSource = null;
        BookmarkTreeView.ItemsSource = bookmarks;
    }

    /// <summary>
    /// ブックマークを選択せず画面を閉じる。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">クリック時のイベント情報。</param>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
