using System.IO;
using System.Text.Json;

namespace Inventory.App;

public static class UiPreferenceService
{
    private sealed class UiPreferences
    {
        public bool ShowBalanceUnitCodes { get; set; } = true;
        public int BalanceUnitHeaderPreferenceVersion { get; set; }
        public double UiScale { get; set; } = 1d;
        public bool IsModeB { get; set; }
        public bool OmitZeroStockAtIssueDate { get; set; } = true;
    }

    private static readonly object SyncRoot = new();
    private static readonly string SettingsPath = CreateSettingsPath();

    public static bool LoadShowBalanceUnitCodes(string? settingsPath = null)
    {
        lock (SyncRoot)
        {
            try
            {
                var path = ResolveSettingsPath(settingsPath);
                var settings = LoadSettings(path);
                if (settings.BalanceUnitHeaderPreferenceVersion < 1)
                {
                    // Nâng thiết lập cũ: từ phiên bản này mã đơn vị là chế độ mặc định.
                    settings.ShowBalanceUnitCodes = true;
                    settings.BalanceUnitHeaderPreferenceVersion = 1;
                    SaveSettings(path, settings);
                }

                return settings.ShowBalanceUnitCodes;
            }
            catch
            {
                return true;
            }
        }
    }

    public static void SaveShowBalanceUnitCodes(bool value, string? settingsPath = null)
    {
        lock (SyncRoot)
        {
            var path = ResolveSettingsPath(settingsPath);
            var settings = LoadSettings(path);
            settings.ShowBalanceUnitCodes = value;
            settings.BalanceUnitHeaderPreferenceVersion = 1;
            SaveSettings(path, settings);
        }
    }

    public static double LoadUiScale(string? settingsPath = null)
    {
        lock (SyncRoot)
        {
            var scale = LoadSettings(ResolveSettingsPath(settingsPath)).UiScale;
            return scale is >= 0.75d and <= 1.75d ? scale : 1d;
        }
    }

    public static void SaveUiScale(double scale, string? settingsPath = null)
    {
        if (scale is < 0.75d or > 1.75d)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        lock (SyncRoot)
        {
            var path = ResolveSettingsPath(settingsPath);
            var settings = LoadSettings(path);
            settings.UiScale = scale;
            SaveSettings(path, settings);
        }
    }

    public static bool LoadIsModeB(string? settingsPath = null)
    {
        lock (SyncRoot)
        {
            try
            {
                return LoadSettings(ResolveSettingsPath(settingsPath)).IsModeB;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void SaveIsModeB(bool value, string? settingsPath = null)
    {
        lock (SyncRoot)
        {
            var path = ResolveSettingsPath(settingsPath);
            var settings = LoadSettings(path);
            settings.IsModeB = value;
            SaveSettings(path, settings);
        }
    }

    public static bool LoadOmitZeroStockAtIssueDate(string? settingsPath = null)
    {
        lock (SyncRoot)
        {
            try
            {
                var path = ResolveSettingsPath(settingsPath);
                return LoadSettings(path).OmitZeroStockAtIssueDate;
            }
            catch
            {
                return true;
            }
        }
    }

    public static void SaveOmitZeroStockAtIssueDate(bool value, string? settingsPath = null)
    {
        lock (SyncRoot)
        {
            var path = ResolveSettingsPath(settingsPath);
            var settings = LoadSettings(path);
            settings.OmitZeroStockAtIssueDate = value;
            SaveSettings(path, settings);
        }
    }

    public static void Reset(string? settingsPath = null)
    {
        lock (SyncRoot)
        {
            var path = ResolveSettingsPath(settingsPath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string ResolveSettingsPath(string? settingsPath) =>
        string.IsNullOrWhiteSpace(settingsPath) ? SettingsPath : settingsPath;

    private static UiPreferences LoadSettings(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(path)) ?? new UiPreferences()
                : new UiPreferences();
        }
        catch
        {
            return new UiPreferences();
        }
    }

    private static void SaveSettings(string path, UiPreferences settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static string CreateSettingsPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "ui-preferences.json");
    }
}
