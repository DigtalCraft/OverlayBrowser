using System.IO;
using System.Text.Json;
using OverlayBrowser.Model;

namespace OverlayBrowser.Service;

/// <summary>
/// ユーザー設定をローカル JSON ファイルへ保存・復元する。
/// </summary>
public sealed class SettingsService
{
    private const string SettingsFileName = "settings.json";
    private const string ApplicationDirectoryName = "OverlayBrowser";
    private const string LegacyApplicationDirectoryName = "GameOverlayBrowser";
    private readonly string settingsFilePath;
    private readonly string legacySettingsFilePath;

    /// <summary>
    /// 設定サービスを初期化する。
    /// </summary>
    public SettingsService()
    {
        var directoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationDirectoryName);
        settingsFilePath = Path.Combine(directoryPath, SettingsFileName);
        legacySettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LegacyApplicationDirectoryName,
            SettingsFileName);
    }

    /// <summary>
    /// 保存済みの設定を読み込む。読み込みに失敗した場合は初期値を返す。
    /// </summary>
    /// <returns>読み込んだアプリ設定。</returns>
    public AppSettings Load()
    {
        try
        {
            var loadFilePath = File.Exists(settingsFilePath)
                ? settingsFilePath
                : legacySettingsFilePath;
            if (!File.Exists(loadFilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(loadFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// 現在の設定を JSON ファイルへ保存する。
    /// </summary>
    /// <param name="settings">保存対象の設定。</param>
    public void Save(AppSettings settings)
    {
        var directoryPath = Path.GetDirectoryName(settingsFilePath)!;
        Directory.CreateDirectory(directoryPath);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(settingsFilePath, json);
    }
}
