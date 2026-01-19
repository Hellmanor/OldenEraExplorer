#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetExtractor.Pipeline;

/// <summary>
/// Application settings persisted to JSON file.
/// </summary>
public sealed class AppSettings
{
    [JsonPropertyName("lastGamePath")]
    public string? LastGamePath { get; set; }
}

/// <summary>
/// Service for loading and saving application settings.
/// Settings are stored in unity-asset-to-glb-settings.json next to the executable (portable mode).
/// </summary>
public sealed class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings;

    /// <summary>
    /// Initialize settings service.
    /// Settings file is created next to the executable for portability.
    /// </summary>
    public SettingsService()
    {
        var exeDirectory = AppContext.BaseDirectory;
        _settingsPath = Path.Combine(exeDirectory, "unity-asset-to-glb-settings.json");
        _settings = LoadSettings();
    }

    /// <summary>
    /// Get the last used game path.
    /// </summary>
    public string? GetLastGamePath()
    {
        return _settings.LastGamePath;
    }

    /// <summary>
    /// Save the last used game path.
    /// </summary>
    public void SetLastGamePath(string? path)
    {
        _settings.LastGamePath = path;
        SaveSettings();
    }

    /// <summary>
    /// Load settings from disk. Returns empty settings if file doesn't exist.
    /// </summary>
    private AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings ?? new AppSettings();
        }
        catch
        {
            // Silent failure - return default settings
            return new AppSettings();
        }
    }

    /// <summary>
    /// Save settings to disk.
    /// </summary>
    private void SaveSettings()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(_settings, options);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Silent failure - settings won't persist
        }
    }
}
