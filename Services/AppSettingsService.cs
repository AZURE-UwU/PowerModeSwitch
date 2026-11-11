// @file AppSettingsService.cs
// @author Codex
// @module PowerModeSwitch.Services

using System.Text.Json;

namespace PowerModeSwitch.Services;

internal sealed record AppSettings(bool AutomationEnabled, int BatteryDelaySeconds)
{
    public const int MaxBatteryDelaySeconds = 3600;

    public static AppSettings Default { get; } = new(true, 0);
}

internal sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public AppSettingsService(string? settingsDirectory = null)
    {
        var directory = settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerModeSwitch");
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return AppSettings.Default;
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath))
                ?? AppSettings.Default;
            return settings with
            {
                BatteryDelaySeconds = Math.Clamp(
                    settings.BatteryDelaySeconds,
                    0,
                    AppSettings.MaxBatteryDelaySeconds)
            };
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // The current automation state remains valid for this process if persistence is unavailable.
        }
    }
}
