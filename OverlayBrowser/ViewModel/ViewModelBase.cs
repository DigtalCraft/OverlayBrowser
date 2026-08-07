using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OverlayBrowser.ViewModel;

/// <summary>
/// ViewModelのプロパティ変更を画面へ通知する基底クラス。
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>
    /// プロパティの値が変わったことを通知するイベント。
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 値を更新し、変更があった場合だけ画面へ通知する。
    /// </summary>
    /// <typeparam name="T">プロパティの型。</typeparam>
    /// <param name="field">更新するフィールド。</param>
    /// <param name="value">新しい値。</param>
    /// <param name="propertyName">プロパティ名。</param>
    /// <returns>値を変更した場合はtrue。</returns>
    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// 指定したプロパティの変更を画面へ通知する。
    /// </summary>
    /// <param name="propertyName">プロパティ名。</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
