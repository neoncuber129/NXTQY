using System.IO;
using System.Text.Json;

namespace Inventory.App;

public sealed class WordTemplateSettingsService
{
    public const string ReceiptTemplateFileName = "Phieunhap.docx";
    public const string IssueTemplateFileName = "Phieuxuat.docx";
    public const string BalanceTemplateFileName = "Phieucandoi.docx";

    private sealed class WordTemplateSettings
    {
        public string TemplateFolderPath { get; set; } = string.Empty;
        public string ReceiptTemplatePath { get; set; } = string.Empty;
        public string IssueTemplatePath { get; set; } = string.Empty;
        public string BalanceTemplatePath { get; set; } = string.Empty;
    }

    private readonly string _settingsPath;

    public WordTemplateSettingsService()
        : this(settingsPath: null)
    {
    }

    public WordTemplateSettingsService(string? settingsPath)
    {
        if (!string.IsNullOrWhiteSpace(settingsPath))
        {
            _settingsPath = settingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!);
            return;
        }

        var settingsFileName = "word-template-settings.json";
        _settingsPath = Path.Combine(AppContext.BaseDirectory, settingsFileName);
    }

    public (
        string TemplateFolderPath,
        string ReceiptTemplatePath,
        string IssueTemplatePath,
        string BalanceTemplatePath) Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty);
        }

        WordTemplateSettings? settings;
        try
        {
            var json = File.ReadAllText(_settingsPath);
            settings = JsonSerializer.Deserialize<WordTemplateSettings>(json);
        }
        catch (JsonException)
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty);
        }

        var folder = settings?.TemplateFolderPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(folder))
        {
            return (
                folder,
                Path.Combine(folder, ReceiptTemplateFileName),
                Path.Combine(folder, IssueTemplateFileName),
                Path.Combine(folder, BalanceTemplateFileName));
        }

        return (
            string.Empty,
            settings?.ReceiptTemplatePath ?? string.Empty,
            settings?.IssueTemplatePath ?? string.Empty,
            settings?.BalanceTemplatePath ?? string.Empty);
    }

    public void SaveFolder(string templateFolderPath)
    {
        var folder = templateFolderPath?.Trim() ?? string.Empty;
        var settings = new WordTemplateSettings
        {
            TemplateFolderPath = folder,
            ReceiptTemplatePath = string.IsNullOrEmpty(folder)
                ? string.Empty
                : Path.Combine(folder, ReceiptTemplateFileName),
            IssueTemplatePath = string.IsNullOrEmpty(folder)
                ? string.Empty
                : Path.Combine(folder, IssueTemplateFileName),
            BalanceTemplatePath = string.IsNullOrEmpty(folder)
                ? string.Empty
                : Path.Combine(folder, BalanceTemplateFileName)
        };
        SaveSettings(settings);
    }

    public void Save(string receiptTemplatePath, string issueTemplatePath, string balanceTemplatePath)
    {
        var settings = new WordTemplateSettings
        {
            ReceiptTemplatePath = receiptTemplatePath ?? string.Empty,
            IssueTemplatePath = issueTemplatePath ?? string.Empty,
            BalanceTemplatePath = balanceTemplatePath ?? string.Empty
        };
        SaveSettings(settings);
    }

    public void Reset()
    {
        if (File.Exists(_settingsPath))
        {
            File.Delete(_settingsPath);
        }
    }

    private void SaveSettings(WordTemplateSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }
}
