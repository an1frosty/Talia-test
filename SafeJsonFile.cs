using System.IO;
using System.Text.Json;

namespace WinSetup.Services;

/// <summary>Result of a guarded load. <see cref="Warning"/> is user-facing; <see cref="WriteBlocked"/> means the file could not be read and must not be overwritten.</summary>
public sealed record JsonLoadResult<T>(T Value, string? Warning, bool WriteBlocked);

/// <summary>
/// Safe JSON persistence: corrupted files are never deleted or silently replaced.
/// Load: corrupt file -> copied to *.corrupt-timestamp, then the last good *.bak is tried.
/// Save: write temp file (flushed to disk) -> File.Replace (atomic, keeps previous version as *.bak).
/// </summary>
public static class SafeJsonFile
{
    private const long MaxBytes = 20L * 1024 * 1024;

    public static async Task<JsonLoadResult<T>> LoadAsync<T>(string path, JsonSerializerOptions options, Func<T> empty, CancellationToken token = default)
    {
        if (!File.Exists(path)) return new JsonLoadResult<T>(empty(), null, false);
        try
        {
            return new JsonLoadResult<T>(await ReadAsync<T>(path, options, token), null, false);
        }
        catch (JsonException ex)
        {
            return await RecoverAsync(path, options, empty, ex.Message, token);
        }
        catch (InvalidDataException ex)
        {
            return await RecoverAsync(path, options, empty, ex.Message, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new JsonLoadResult<T>(empty(), $"Не удалось прочитать файл данных «{Path.GetFileName(path)}»: {ex.Message}\nЗапись в этот файл отключена до перезапуска, чтобы не потерять данные.\nПуть: {path}", true);
        }
    }

    private static async Task<T> ReadAsync<T>(string path, JsonSerializerOptions options, CancellationToken token)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxBytes) throw new InvalidDataException("Файл данных слишком большой.");
        var bytes = await File.ReadAllBytesAsync(path, token);
        if (bytes.Length == 0 || bytes.All(b => b is 0 or (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t'))
            throw new InvalidDataException("Файл данных пуст.");
        return JsonSerializer.Deserialize<T>(bytes, options) ?? throw new InvalidDataException("Файл данных не содержит значения.");
    }

    private static async Task<JsonLoadResult<T>> RecoverAsync<T>(string path, JsonSerializerOptions options, Func<T> empty, string reason, CancellationToken token)
    {
        string? backupPath = null;
        try
        {
            backupPath = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(path, backupPath, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            backupPath = null;
        }

        var backupText = backupPath is null ? "Не удалось создать копию повреждённого файла." : $"Копия повреждённого файла сохранена: {backupPath}";
        var lastGood = path + ".bak";
        if (File.Exists(lastGood))
        {
            try
            {
                var restored = await ReadAsync<T>(lastGood, options, token);
                return new JsonLoadResult<T>(restored, $"Файл «{Path.GetFileName(path)}» повреждён ({reason}). Данные восстановлены из последней резервной копии. {backupText}", false);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException) { }
        }

        return new JsonLoadResult<T>(empty(), $"Файл «{Path.GetFileName(path)}» повреждён ({reason}). Загружены значения по умолчанию, исходный файл не удалён. {backupText}", false);
    }

    public static async Task SaveAsync<T>(string path, T value, JsonSerializerOptions options, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, value, options, token);
            await stream.FlushAsync(token);
            stream.Flush(true);
        }
        Commit(temporary, path);
    }

    public static async Task SaveTextAsync(string path, string text, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, text, token);
        Commit(temporary, path);
    }

    private static void Commit(string temporary, string path)
    {
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
        else File.Move(temporary, path);
    }
}
