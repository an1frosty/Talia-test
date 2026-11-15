using System.IO;
using System.Text.Json;
using WinSetup.Models;

namespace WinSetup.Services;

/// <summary>Persists user-added catalog entries separately from the built-in catalog.</summary>
public sealed class AppCatalogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private bool _writeBlocked;

    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinSetup", "Catalog", "custom-apps.json");

    /// <summary>Set when the last load found a corrupted or unreadable file. Show it to the user once.</summary>
    public string? LastWarning { get; private set; }

    public async Task<List<AppEntry>> LoadAsync(CancellationToken token = default)
    {
        var result = await SafeJsonFile.LoadAsync(FilePath, JsonOptions, () => new List<AppEntry>(), token);
        LastWarning = result.Warning;
        _writeBlocked = result.WriteBlocked;
        return result.Value;
    }

    public async Task SaveAsync(IEnumerable<AppEntry> entries, CancellationToken token = default)
    {
        if (_writeBlocked)
            throw new InvalidOperationException("Личный каталог не удалось прочитать при запуске, поэтому сохранение отключено, чтобы не затереть исходный файл. Закрой Talia, проверь файл и запусти снова: " + FilePath);
        await SafeJsonFile.SaveAsync(FilePath, entries.Where(x => x.IsCustom).ToList(), JsonOptions, token);
    }
}
