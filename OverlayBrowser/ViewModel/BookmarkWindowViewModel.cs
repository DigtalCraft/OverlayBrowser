using OverlayBrowser.Command;
using OverlayBrowser.Model;
using OverlayBrowser.Service;

namespace OverlayBrowser.ViewModel;

/// <summary>
/// ブックマーク一覧画面の表示状態と選択結果を管理する。
/// </summary>
public sealed class BookmarkWindowViewModel : ViewModelBase
{
    private readonly BookmarkService bookmarkService;
    private BookmarkItem? selectedItem;
    private string selectionText;
    private bool canConfirm;

    /// <summary>
    /// ブックマーク一覧画面ViewModelを初期化する。
    /// </summary>
    /// <param name="bookmarks">表示するブックマーク一覧。</param>
    /// <param name="isDestinationSelection">追加先選択画面として使用する場合はtrue。</param>
    /// <param name="bookmarkService">ブックマーク階層の操作処理。</param>
    public BookmarkWindowViewModel(
        IList<BookmarkItem> bookmarks,
        bool isDestinationSelection,
        BookmarkService bookmarkService)
    {
        Bookmarks = bookmarks;
        IsDestinationSelection = isDestinationSelection;
        this.bookmarkService = bookmarkService;

        WindowTitle = isDestinationSelection ? "ブックマークの追加先" : "ブックマーク一覧";
        GuideText = isDestinationSelection
            ? "追加先フォルダを選択してください。フォルダを選ばず、トップ階層へ追加することもできます。"
            : "フォルダの ▶ を押して開き、サイト名を選択してください。ダブルクリックでも開けます。";
        PrimaryButtonText = isDestinationSelection ? "ここへ追加" : "開く";
        selectionText = isDestinationSelection
            ? "追加先フォルダを選択してください"
            : "サイトを選択してください";

        ConfirmCommand = new RelayCommand(_ => ConfirmSelection(), _ => CanConfirm);
        SelectRootCommand = new RelayCommand(_ => SelectRootDestination());
        CloseCommand = new RelayCommand(_ => CloseRequested?.Invoke(
            this,
            new DialogCloseRequestedEventArgs(false)));
    }

    /// <summary>
    /// 画面を閉じることをViewへ依頼するイベント。
    /// </summary>
    public event EventHandler<DialogCloseRequestedEventArgs>? CloseRequested;

    /// <summary>
    /// 表示するブックマーク一覧。
    /// </summary>
    public IList<BookmarkItem> Bookmarks { get; }

    /// <summary>
    /// 追加先選択画面として使用するかどうか。
    /// </summary>
    public bool IsDestinationSelection { get; }

    /// <summary>
    /// 画面タイトル。
    /// </summary>
    public string WindowTitle { get; }

    /// <summary>
    /// 操作説明。
    /// </summary>
    public string GuideText { get; }

    /// <summary>
    /// 確定ボタンに表示する文言。
    /// </summary>
    public string PrimaryButtonText { get; }

    /// <summary>
    /// 選択状態を示す文言。
    /// </summary>
    public string SelectionText
    {
        get => selectionText;
        private set => SetProperty(ref selectionText, value);
    }

    /// <summary>
    /// 選択結果を確定できるかどうか。
    /// </summary>
    public bool CanConfirm
    {
        get => canConfirm;
        private set
        {
            if (SetProperty(ref canConfirm, value))
            {
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// ツリーで選択している項目。
    /// </summary>
    public BookmarkItem? SelectedItem
    {
        get => selectedItem;
        set
        {
            if (SetProperty(ref selectedItem, value))
            {
                UpdateSelection();
            }
        }
    }

    /// <summary>
    /// 選択されたブックマークのURL。
    /// </summary>
    public string SelectedUrl { get; private set; } = string.Empty;

    /// <summary>
    /// 追加先として選択されたフォルダ。
    /// </summary>
    public BookmarkItem? SelectedFolder { get; private set; }

    /// <summary>
    /// トップ階層が追加先として選択されたかどうか。
    /// </summary>
    public bool IsRootDestinationSelected { get; private set; }

    /// <summary>
    /// ドラッグ操作でブックマーク階層を変更したかどうか。
    /// </summary>
    public bool HasChanges { get; private set; }

    /// <summary>
    /// 選択結果を確定するコマンド。
    /// </summary>
    public RelayCommand ConfirmCommand { get; }

    /// <summary>
    /// トップ階層を追加先にするコマンド。
    /// </summary>
    public RelayCommand SelectRootCommand { get; }

    /// <summary>
    /// 画面を閉じるコマンド。
    /// </summary>
    public RelayCommand CloseCommand { get; }

    /// <summary>
    /// ブックマークを指定した項目の位置へ移動する。
    /// </summary>
    /// <param name="source">移動するブックマーク。</param>
    /// <param name="target">移動先。トップ階層の場合はnull。</param>
    /// <returns>移動できた場合はtrue。</returns>
    public bool MoveBookmark(BookmarkItem source, BookmarkItem? target)
    {
        if (IsDestinationSelection || ReferenceEquals(source, target) ||
            (target is not null && bookmarkService.Contains(source, target)))
        {
            return false;
        }

        if (!bookmarkService.Remove(Bookmarks, source))
        {
            return false;
        }

        if (target is null)
        {
            Bookmarks.Add(source);
        }
        else if (target.IsFolder)
        {
            target.Children.Add(source);
        }
        else if (bookmarkService.FindContainingList(Bookmarks, target) is { } targetList)
        {
            targetList.Insert(targetList.IndexOf(target), source);
        }
        else
        {
            Bookmarks.Add(source);
        }

        HasChanges = true;
        return true;
    }

    /// <summary>
    /// 選択中の項目に合わせて説明と確定可否を更新する。
    /// </summary>
    private void UpdateSelection()
    {
        if (IsDestinationSelection && SelectedItem is { IsFolder: true } folder)
        {
            CanConfirm = true;
            SelectionText = $"「{folder.Name}」へ追加します";
            return;
        }

        if (!IsDestinationSelection && SelectedItem is { IsFolder: false } bookmark &&
            !string.IsNullOrWhiteSpace(bookmark.Url))
        {
            CanConfirm = true;
            SelectionText = bookmark.Name;
            return;
        }

        CanConfirm = false;
        SelectionText = IsDestinationSelection
            ? "追加先フォルダを選択してください"
            : "サイトを選択してください";
    }

    /// <summary>
    /// 選択中のサイトまたはフォルダを呼び出し元へ返す。
    /// </summary>
    private void ConfirmSelection()
    {
        if (IsDestinationSelection && SelectedItem is { IsFolder: true } folder)
        {
            SelectedFolder = folder;
            IsRootDestinationSelected = false;
        }
        else if (!IsDestinationSelection && SelectedItem is { IsFolder: false } bookmark &&
                 !string.IsNullOrWhiteSpace(bookmark.Url))
        {
            SelectedUrl = bookmark.Url;
        }
        else
        {
            return;
        }

        CloseRequested?.Invoke(this, new DialogCloseRequestedEventArgs(true));
    }

    /// <summary>
    /// トップ階層を新規ブックマークの追加先として返す。
    /// </summary>
    private void SelectRootDestination()
    {
        SelectedFolder = null;
        IsRootDestinationSelected = true;
        CloseRequested?.Invoke(this, new DialogCloseRequestedEventArgs(true));
    }
}
