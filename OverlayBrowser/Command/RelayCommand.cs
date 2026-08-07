using System.Windows.Input;

namespace OverlayBrowser.Command;

/// <summary>
/// 画面操作から同期処理を実行するコマンド。
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> execute;
    private readonly Predicate<object?>? canExecute;

    /// <summary>
    /// コマンドを初期化する。
    /// </summary>
    /// <param name="execute">実行する処理。</param>
    /// <param name="canExecute">実行可否を判定する処理。</param>
    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    /// <summary>
    /// コマンドの実行可否が変わったことを通知するイベント。
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// 現在の状態でコマンドを実行できるか判定する。
    /// </summary>
    /// <param name="parameter">コマンドパラメーター。</param>
    /// <returns>実行できる場合はtrue。</returns>
    public bool CanExecute(object? parameter)
    {
        return canExecute?.Invoke(parameter) ?? true;
    }

    /// <summary>
    /// 登録された処理を実行する。
    /// </summary>
    /// <param name="parameter">コマンドパラメーター。</param>
    public void Execute(object? parameter)
    {
        execute(parameter);
    }

    /// <summary>
    /// 実行可否を画面へ再評価させる。
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
