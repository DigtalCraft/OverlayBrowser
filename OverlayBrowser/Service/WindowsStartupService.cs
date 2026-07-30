using Microsoft.Win32;

namespace OverlayBrowser.Service;

/// <summary>
/// Windowsサインイン時のアプリ起動設定を管理する。
/// </summary>
public sealed class WindowsStartupService
{
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApplicationValueName = "OverlayBrowser";
    private const string LegacyApplicationValueName = "GameOverlayBrowser";

    /// <summary>
    /// Windowsの起動設定に登録する起動引数。
    /// </summary>
    public const string StartupArgument = "--startup";

    /// <summary>
    /// Windowsサインイン時にアプリを起動する設定が有効か確認する。
    /// </summary>
    /// <returns>有効な場合はtrue。</returns>
    public bool IsEnabled()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryPath);
        return IsCommandLineRegistered(runKey?.GetValue(ApplicationValueName)) ||
               IsCommandLineRegistered(runKey?.GetValue(LegacyApplicationValueName));
    }

    /// <summary>
    /// 旧アプリ名で登録されている自動起動設定を現在の実行ファイルへ引き継ぐ。
    /// </summary>
    public void MigrateLegacyEntry()
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunRegistryPath, writable: true);
        if (IsCommandLineRegistered(runKey.GetValue(ApplicationValueName)) ||
            !IsCommandLineRegistered(runKey.GetValue(LegacyApplicationValueName)) ||
            string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return;
        }

        runKey.SetValue(
            ApplicationValueName,
            $"\"{Environment.ProcessPath}\" {StartupArgument}",
            RegistryValueKind.String);
        runKey.DeleteValue(LegacyApplicationValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Windowsサインイン時のアプリ起動設定を変更する。
    /// </summary>
    /// <param name="isEnabled">有効にする場合はtrue。</param>
    public void SetEnabled(bool isEnabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunRegistryPath, writable: true);
        if (!isEnabled)
        {
            runKey.DeleteValue(ApplicationValueName, throwOnMissingValue: false);
            runKey.DeleteValue(LegacyApplicationValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("アプリケーションの実行ファイルを取得できませんでした。");
        }

        runKey.SetValue(
            ApplicationValueName,
            $"\"{executablePath}\" {StartupArgument}",
            RegistryValueKind.String);
        runKey.DeleteValue(LegacyApplicationValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// 現在の起動がWindowsサインイン時の自動起動か確認する。
    /// </summary>
    /// <returns>自動起動の場合はtrue。</returns>
    public bool IsStartedFromWindows()
    {
        return Environment.GetCommandLineArgs().Any(argument =>
            string.Equals(argument, StartupArgument, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// レジストリ値に有効な起動コマンドが登録されているか確認する。
    /// </summary>
    /// <param name="registryValue">確認対象のレジストリ値。</param>
    /// <returns>有効なコマンドが登録されている場合はtrue。</returns>
    private static bool IsCommandLineRegistered(object? registryValue)
    {
        return registryValue is string commandLine && !string.IsNullOrWhiteSpace(commandLine);
    }
}
