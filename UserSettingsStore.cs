using System.IO;
using System.Text.Json;
using WinSetup.Models;

namespace WinSetup.Services;

/// <summary>Result of reading an exported profile file: valid profiles plus human-readable problems.</summary>
public sealed record ProfileImportResult(List<InstallProfile> Profiles, List<string> Problems);

public sealed class UserSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ImportOptions = new() { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
    private const long MaxImportBytes = 2L * 1024 * 1024;
    private const int MaxProfilesPerFile = 200;
    private const int MaxPackagesPerProfile = 300;

    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinSetup");
    private bool _foldersBlocked;
    private bool _profilesBlocked;

    private string FoldersFile => Path.Combine(_root, "folders.json");
    private string ProfilesFile => Path.Combine(_root, "Profiles", "profiles.json");
    private string InstallRootFile => Path.Combine(_root, "install-root.txt");

    /// <summary>Warnings produced while loading settings (corrupted files, unreadable files). Shown once at startup.</summary>
    public List<string> Warnings { get; } = [];

    public async Task<string> LoadInstallRootAsync(CancellationToken token = default)
    {
        try { return File.Exists(InstallRootFile) ? (await File.ReadAllTextAsync(InstallRootFile, token)).Trim() : ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warnings.Add($"Не удалось прочитать сохранённую папку установки: {ex.Message}");
            return "";
        }
    }

    public Task SaveInstallRootAsync(string path, CancellationToken token = default)
        => SafeJsonFile.SaveTextAsync(InstallRootFile, path.Trim(), token);

    public async Task<List<string>> LoadFoldersAsync(CancellationToken token = default)
    {
        var result = await SafeJsonFile.LoadAsync(FoldersFile, Options, () => new List<string>(), token);
        if (result.Warning is not null) Warnings.Add(result.Warning);
        _foldersBlocked = result.WriteBlocked;
        return result.Value;
    }

    public Task SaveFoldersAsync(IEnumerable<string> folders, CancellationToken token = default)
    {
        if (_foldersBlocked) throw new InvalidOperationException("Список папок не удалось прочитать при запуске; сохранение отключено, чтобы не затереть файл: " + FoldersFile);
        return SafeJsonFile.SaveAsync(FoldersFile, folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), Options, token);
    }

    public async Task<List<InstallProfile>> LoadProfilesAsync(CancellationToken token = default)
    {
        var result = await SafeJsonFile.LoadAsync(ProfilesFile, Options, () => new List<InstallProfile>(), token);
        if (result.Warning is not null) Warnings.Add(result.Warning);
        _profilesBlocked = result.WriteBlocked;
        return result.Value;
    }

    public Task SaveProfilesAsync(IEnumerable<InstallProfile> profiles, CancellationToken token = default)
    {
        if (_profilesBlocked) throw new InvalidOperationException("Профили не удалось прочитать при запуске; сохранение отключено, чтобы не затереть файл: " + ProfilesFile);
        return SafeJsonFile.SaveAsync(ProfilesFile, profiles.ToList(), Options, token);
    }

    // ---------- Import / export ----------

    public Task ExportProfilesAsync(string path, IEnumerable<InstallProfile> profiles, CancellationToken token = default)
        => SafeJsonFile.SaveAsync(path, new ProfileExportFile { Profiles = profiles.ToList() }, Options, token);

    /// <summary>
    /// Reads and validates an exported profile file. Accepts the Talia export format, a bare array of profiles,
    /// or a single profile object. Invalid profiles are skipped and reported; nothing is executed or trusted blindly.
    /// </summary>
    public async Task<ProfileImportResult> ReadProfilesFileAsync(string path, CancellationToken token = default)
    {
        var problems = new List<string>();
        var info = new FileInfo(path);
        if (!info.Exists) throw new InvalidDataException("Файл не найден.");
        if (info.Length is 0 or > MaxImportBytes) throw new InvalidDataException("Файл пуст или слишком большой (максимум 2 МБ).");

        List<InstallProfile> raw;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, token);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 16 });
            var root = document.RootElement;
            switch (root.ValueKind)
            {
                case JsonValueKind.Array:
                    raw = root.Deserialize<List<InstallProfile>>(ImportOptions) ?? [];
                    break;
                case JsonValueKind.Object when root.TryGetProperty("profiles", out _) || root.TryGetProperty("Profiles", out _):
                    var file = root.Deserialize<ProfileExportFile>(ImportOptions) ?? new ProfileExportFile();
                    if (!string.IsNullOrWhiteSpace(file.Format) && !file.Format.Equals("talia-profiles", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Неизвестный формат файла: «{file.Format}». Нужен экспорт профилей Talia.");
                    if (file.Version > 1) throw new InvalidDataException($"Файл создан более новой версией Talia (формат {file.Version}). Обнови приложение.");
                    raw = file.Profiles;
                    break;
                case JsonValueKind.Object:
                    raw = [root.Deserialize<InstallProfile>(ImportOptions) ?? new InstallProfile()];
                    break;
                default:
                    throw new InvalidDataException("Файл не похож на экспорт профилей Talia.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Файл повреждён или не является JSON: {ex.Message}");
        }

        if (raw.Count == 0) throw new InvalidDataException("В файле нет профилей.");
        if (raw.Count > MaxProfilesPerFile) { problems.Add($"В файле больше {MaxProfilesPerFile} профилей — лишние пропущены."); raw = raw.Take(MaxProfilesPerFile).ToList(); }

        var valid = new List<InstallProfile>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in raw)
        {
            var name = (profile?.Name ?? "").Trim();
            if (name.Length is 0 or > 80 || name.Any(char.IsControl)) { problems.Add($"Профиль пропущен: недопустимое имя «{Shorten(name)}»."); continue; }
            if (!names.Add(name)) { problems.Add($"Профиль «{name}» встречается в файле повторно — оставлен первый."); continue; }

            var ids = new List<string>();
            foreach (var id in (profile!.PackageIds ?? []).Take(MaxPackagesPerProfile))
            {
                var trimmed = (id ?? "").Trim();
                if (!InstallerService.IsValidPackageId(trimmed)) { problems.Add($"«{name}»: недопустимый Package ID «{Shorten(trimmed)}» пропущен."); continue; }
                if (!ids.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) ids.Add(trimmed);
            }
            var appNames = (profile.AppNames ?? []).Select(n => (n ?? "").Trim()).Where(n => n.Length is > 0 and <= 120 && !n.Any(char.IsControl)).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxPackagesPerProfile).ToList();
            if (ids.Count == 0 && appNames.Count == 0) { problems.Add($"Профиль «{name}» пропущен: в нём нет программ."); continue; }
            valid.Add(new InstallProfile { Name = name, PackageIds = ids, AppNames = appNames, UpdatedAt = DateTime.Now });
        }

        if (valid.Count == 0) throw new InvalidDataException("В файле нет ни одного корректного профиля.\n" + string.Join("\n", problems.Take(8)));
        return new ProfileImportResult(valid, problems);
    }

    private static string Shorten(string value) => value.Length <= 40 ? value : value[..40] + "…";
}
