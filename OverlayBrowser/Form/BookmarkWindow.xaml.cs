using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OverlayBrowser.Model;
using OverlayBrowser.Service;
using OverlayBrowser.ViewModel;
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
    private readonly BookmarkWindowViewModel viewModel;
    private WpfPoint dragStartPoint;
    private BookmarkItem? draggedBookmark;

    /// <summary>
    /// ユーザーが選択したブックマークのURL。
    /// </summary>
    public string SelectedUrl => viewModel.SelectedUrl;

    /// <summary>
    /// 追加先として選択したフォルダ。
    /// </summary>
    public BookmarkItem? SelectedFolder => viewModel.SelectedFolder;

    /// <summary>
    /// 追加先としてトップ階層を選択したかどうか。
    /// </summary>
    public bool IsRootDestinationSelected => viewModel.IsRootDestinationSelected;

    /// <summary>
    /// ドラッグ＆ドロップで構成を変更したかどうか。
    /// </summary>
    public bool HasChanges => viewModel.HasChanges;

    /// <summary>
    /// ブックマーク一覧画面を初期化する。
    /// </summary>
    /// <param name="bookmarks">表示するブックマーク一覧。</param>
    /// <param name="isDestinationSelection">追加先フォルダを選択する画面として開く場合はtrue。</param>
    public BookmarkWindow(IList<BookmarkItem> bookmarks, bool isDestinationSelection = false)
    {
        InitializeComponent();
        viewModel = new BookmarkWindowViewModel(
            bookmarks,
            isDestinationSelection,
            new BookmarkService());
        viewModel.CloseRequested += ViewModel_CloseRequested;
        DataContext = viewModel;
    }

    /// <summary>
    /// ViewModelが確定した結果で画面を閉じる。
    /// </summary>
    /// <param name="sender">ブックマーク画面ViewModel。</param>
    /// <param name="e">呼び出し元へ返す結果。</param>
    private void ViewModel_CloseRequested(object? sender, DialogCloseRequestedEventArgs e)
    {
        DialogResult = e.DialogResult;
    }

    /// <summary>
    /// ツリーの選択項目をViewModelへ通知する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">選択前後の項目情報。</param>
    private void BookmarkTreeView_SelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        viewModel.SelectedItem = e.NewValue as BookmarkItem;
    }

    /// <summary>
    /// ダブルクリックした項目の選択を確定する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">マウス操作のイベント情報。</param>
    private void BookmarkTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (viewModel.ConfirmCommand.CanExecute(null))
        {
            viewModel.ConfirmCommand.Execute(null);
        }
    }

    /// <summary>
    /// ドラッグ開始位置を記録する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">マウス操作のイベント情報。</param>
    private void BookmarkTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragStartPoint = e.GetPosition(BookmarkTreeView);
        draggedBookmark = FindBookmarkItem(e.OriginalSource as DependencyObject);
    }

    /// <summary>
    /// 一覧操作時に選択中のブックマークをドラッグする。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">マウス操作のイベント情報。</param>
    private void BookmarkTreeView_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (viewModel.IsDestinationSelection || e.LeftButton != MouseButtonState.Pressed ||
            draggedBookmark is not BookmarkItem bookmark)
        {
            return;
        }

        var currentPoint = e.GetPosition(BookmarkTreeView);
        if (Math.Abs(currentPoint.X - dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        draggedBookmark = null;
        DragDrop.DoDragDrop(
            BookmarkTreeView,
            new WpfDataObject(typeof(BookmarkItem), bookmark),
            WpfDragDropEffects.Move);
    }

    /// <summary>
    /// ドロップ先として受け入れ可能か判定する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">ドラッグ操作のイベント情報。</param>
    private void BookmarkTreeView_DragOver(object sender, WpfDragEventArgs e)
    {
        e.Effects = !viewModel.IsDestinationSelection && e.Data.GetDataPresent(typeof(BookmarkItem))
            ? WpfDragDropEffects.Move
            : WpfDragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// ブックマークを画面上で指定した位置へ移動する。
    /// </summary>
    /// <param name="sender">イベントの発生元。</param>
    /// <param name="e">ドラッグ操作のイベント情報。</param>
    private void BookmarkTreeView_Drop(object sender, WpfDragEventArgs e)
    {
        if (e.Data.GetData(typeof(BookmarkItem)) is not BookmarkItem source)
        {
            return;
        }

        var target = FindBookmarkItem(e.OriginalSource as DependencyObject);
        if (!viewModel.MoveBookmark(source, target))
        {
            return;
        }

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
    /// リスト構成変更後にツリー表示を更新する。
    /// </summary>
    private void RefreshTreeView()
    {
        BookmarkTreeView.ItemsSource = null;
        BookmarkTreeView.ItemsSource = viewModel.Bookmarks;
    }
}
