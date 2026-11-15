using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Win32;
using WinSetup.Models;

namespace WinSetup.Services;

/// <summary>WinGet operations and the guarded first-run bootstrap flow.</summary>
public sealed class InstallerService
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All
    }) { Timeout = TimeSpan.FromMinutes(15) };

    private const string WingetBootstrapUrl = "https://aka.ms/getwinget";
    private const long MaxBootstrapBytes = 150L * 1024 * 1024;
    private readonly SemaphoreSlim _logGate = new(1, 1);

    public string TempRoot { get; } = Path.Combine(Path.GetTempPath(), "WinSetup", Guid.NewGuid().ToString("N"));
    public string LogPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinSetup", "Logs", $"install-{DateTime.Now:yyyyMMdd-HHmmss}.log");

    public string? GetWingetPath() => FindWinget();

    public async Task<string> GetWingetVersionAsync(CancellationToken token)
    {
        var winget = FindWinget() ?? throw new InvalidOperationException("WinGet не найден. Нажми «Установить WinGet», чтобы скачать официальный App Installer от Microsoft.");
        return await RunCaptureAsync(winget, ["--version"], token);
    }

    /// <summary>
    /// Downloads the stable App Installer bundle from Microsoft's aka.ms endpoint,
    /// verifies its Authenticode signature and opens the native MSIX installer.
    /// The user remains in control of the actual package installation.
    /// </summary>
    public async Task BootstrapWingetAsync(IProgress<string> progress, CancellationToken token)
    {
        if (FindWinget() is not null)
        {
            var current = await GetWingetVersionAsync(token);
            progress.Report($"WinGet уже установлен: {current}");
            return;
        }

        Directory.CreateDirectory(TempRoot);
        var packagePath = Path.Combine(TempRoot, "Microsoft.DesktopAppInstaller.msixbundle");
        await AppendLogAsync("WINGET BOOTSTRAP | Download started | https://aka.ms/getwinget");
        progress.Report("Скачиваем официальный App Installer от Microsoft...");

        using (var response = await Http.GetAsync(WingetBootstrapUrl, HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && (length <= 0 || length > MaxBootstrapBytes))
                throw new InvalidOperationException($"Неожиданный размер пакета App Installer: {length:N0} байт.");

            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(packagePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, token)) > 0)
            {
                total += read;
                if (total > MaxBootstrapBytes) throw new InvalidOperationException("Пакет App Installer превышает допустимый размер 150 МБ.");
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
            if (total < 1024 * 1024) throw new InvalidOperationException("Скачанный пакет слишком мал и, вероятно, повреждён.");
        }

        progress.Report("Проверяем цифровую подпись Microsoft...");
        await VerifyMicrosoftSignatureAsync(packagePath, token);
        await AppendLogAsync("WINGET BOOTSTRAP | Signature valid | Microsoft Corporation");

        progress.Report("Открываем установщик App Installer. Подтверди установку в окне Windows...");
        var start = new ProcessStartInfo(packagePath) { UseShellExecute = true };
        var installerProcess = Process.Start(start);
        if (installerProcess is null)
            throw new InvalidOperationException("Windows не смогла открыть пакет App Installer.");
        installerProcess.Dispose();
        await AppendLogAsync("WINGET BOOTSTRAP | Native MSIX installer launched; user confirmation required.");
    }

    private async Task VerifyMicrosoftSignatureAsync(string packagePath, CancellationToken token)
    {
        // Use PowerShell's Authenticode verifier; do not trust the download URL alone.
        var pathToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(packagePath));
        // Valid Authenticode status AND signer CN exactly "Microsoft Corporation" (a substring match would also accept look-alike subjects).
        var script = $"$p = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{pathToken}')); $s = Get-AuthenticodeSignature -LiteralPath $p; if ($s.Status -ne 'Valid' -or $null -eq $s.SignerCertificate -or $s.SignerCertificate.Subject -notmatch '^CN=Microsoft Corporation(,|$)') {{ exit 41 }}; exit 0";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var powershell = FindOnPath("powershell.exe") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new InvalidOperationException("Не найден Windows PowerShell для проверки цифровой подписи.");
        var result = await RunProcessAsync(powershell, ["-NoProfile", "-NonInteractive", "-EncodedCommand", encoded], null, token, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Проверка цифровой подписи App Installer не пройдена. Пакет не будет запущен. Удали временный файл и попробуй позже.");
    }

    public async Task InstallAsync(AppEntry app, IProgress<string> progress, CancellationToken token, string? installRoot = null)
    {
        token.ThrowIfCancellationRequested();
        app.Status = "Подготовка...";
        Directory.CreateDirectory(TempRoot);
        if (string.IsNullOrWhiteSpace(app.WingetId))
            throw new NotSupportedException($"Для {app.Name} пока не настроен безопасный сценарий установки. Не запускаю неизвестный архив или скрипт автоматически.");
        if (!IsValidPackageId(app.WingetId))
            throw new InvalidOperationException($"Package ID программы «{app.Name}» имеет недопустимый формат. Проверь личный каталог.");

        var winget = FindWinget() ?? throw new InvalidOperationException("WinGet не найден. Сначала установи официальный App Installer через кнопку «Установить WinGet».");
        string? normalizedRoot = null;
        string? requestedAppDirectory = null;
        if (!string.IsNullOrWhiteSpace(installRoot))
        {
            if (!Path.IsPathFullyQualified(installRoot))
                throw new InvalidOperationException("Путь установки должен быть абсолютным.");
            normalizedRoot = Path.GetFullPath(installRoot);
            requestedAppDirectory = Path.GetFullPath(Path.Combine(normalizedRoot, GetSafeAppFolderName(app.Name)));
            if (!IsSameOrChildPath(requestedAppDirectory, normalizedRoot) ||
                string.Equals(requestedAppDirectory.TrimEnd(Path.DirectorySeparatorChar), normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Не удалось безопасно сформировать отдельную папку программы внутри выбранного каталога.");
            Directory.CreateDirectory(requestedAppDirectory);
        }

        progress.Report($"Проверяем, установлена ли {app.Name}...");
        await AppendLogAsync($"PRE-INSTALL CHECK | {app.Name} | {app.WingetId}");

        // A selected install root is an explicit request to install/reinstall there.
        // Do not treat a package registration in WinGet as proof that the app files
        // actually exist: stale ARP/WinGet records can survive manual removal.
        var (existing, staleFound) = await Task.Run(() => FindInstalledApp(app), token);
        if (existing is null && staleFound)
            await AppendLogAsync($"STALE OR INCOMPLETE INSTALL RECORD | {app.Name} | registry entry without a verified executable");

        if (existing is not null)
        {
            var alreadyInRequestedDirectory = requestedAppDirectory is not null && IsSameOrChildPath(existing.InstallLocation, requestedAppDirectory);
            if (requestedAppDirectory is null || alreadyInRequestedDirectory)
            {
                app.InstalledPath = existing.InstallLocation;
                app.InstalledVersion = existing.Version;
                app.Status = "Уже установлена";
                await AppendLogAsync($"ALREADY INSTALLED | {app.Name} | {existing.InstallLocation} | {existing.Version} | requested root: {normalizedRoot ?? "<default>"}");
                progress.Report($"Программа уже установлена. Фактический путь: {existing.InstallLocation}");
                return;
            }

            await AppendLogAsync($"REINSTALL REQUESTED | {app.Name} | existing: {existing.InstallLocation} | target directory: {requestedAppDirectory}");
            progress.Report($"Программа найдена в другом месте. Повторно запускаем установщик с отдельной папкой: {requestedAppDirectory}");
        }

        var alreadyListed = await IsPackageListedAsync(winget, app.WingetId, token);
        if (alreadyListed)
        {
            // Keep going. WinGet's installed-package metadata is not enough to prove
            // that the application executable is present, and the user explicitly
            // asked for an installation into the selected root.
            await AppendLogAsync($"WINGET LIST MATCH BEFORE INSTALL | {app.Name} | executable not verified | continuing with --force");
        }

        progress.Report($"Подготовка установки: {app.Name}");
        await AppendLogAsync($"START | {app.Name} | {app.WingetId} | Requested root: {normalizedRoot ?? "<default>"} | Requested app directory: {requestedAppDirectory ?? "<default>"}");
        if (requestedAppDirectory is not null)
            progress.Report($"Устанавливаем {app.Name} в отдельную папку: {requestedAppDirectory}");
        else
            progress.Report($"Устанавливаем {app.Name} через WinGet...");

        // Each app gets its own directory under the selected root. WinGet's --location
        // is supplied as a generic hint, while known installers also receive their
        // documented vendor-specific directory argument.
        var args = new List<string>
        {
            "install", "--id", app.WingetId, "--exact", "--source", "winget",
            "--force", "--silent", "--accept-package-agreements", "--accept-source-agreements",
            "--disable-interactivity"
        };
        if (requestedAppDirectory is not null)
        {
            args.Add("--location");
            args.Add(requestedAppDirectory);
            var id = app.WingetId.Trim();
            if (id.Equals("Mozilla.Firefox", StringComparison.OrdinalIgnoreCase))
            {
                // Mozilla Full Installer documents /InstallDirectoryPath. Since
                // --override replaces manifest switches, /S is included explicitly.
                args.Add("--installer-type");
                args.Add("exe");
                args.Add("--override");
                args.Add($"/S /InstallDirectoryPath=\"{requestedAppDirectory}\"");
                await AppendLogAsync($"INSTALLER STRATEGY | Firefox | EXE | /InstallDirectoryPath={requestedAppDirectory}");
            }
            else if (id.Equals("VideoLAN.VLC", StringComparison.OrdinalIgnoreCase))
            {
                // VLC's WiX MSI defines APPLICATIONFOLDER as its directory property.
                args.Add("--installer-type");
                args.Add("wix");
                args.Add("--override");
                args.Add($"/quiet APPLICATIONFOLDER=\"{requestedAppDirectory}\"");
                await AppendLogAsync($"INSTALLER STRATEGY | VLC | WiX MSI | APPLICATIONFOLDER={requestedAppDirectory}");
            }
        }
        app.Status = "Установка...";
        var result = await RunProcessAsync(winget, args, progress, token, TimeSpan.FromMinutes(60));
        await AppendLogAsync($"EXIT {result.ExitCode} | {app.Name} | {TrimForLog(result.Output)}");
        app.Status = "Проверка установки...";
        progress.Report("Проверяем фактическую установку...");

        // A successful process exit alone is not proof that the application is installed.
        // First inspect uninstall registry entries / known executable locations, then ask WinGet.
        InstalledAppInfo? detected = null;
        for (var attempt = 0; attempt < 5 && detected is null; attempt++)
        {
            token.ThrowIfCancellationRequested();
            detected = (await Task.Run(() => FindInstalledApp(app), token)).Info;
            if (detected is null && attempt < 4) await Task.Delay(TimeSpan.FromSeconds(1.5), token);
        }

        var wingetListConfirmed = false;
        if (detected is null)
        {
            try
            {
                wingetListConfirmed = await IsPackageListedAsync(winget, app.WingetId, token);
                await AppendLogAsync($"POST-INSTALL WINGET LIST | {app.Name} | Confirmed: {wingetListConfirmed}");
            }
            catch (Exception ex)
            {
                await AppendLogAsync($"POST-INSTALL LIST CHECK FAILED | {app.Name} | {ex.Message}");
            }
        }

        if (detected is null && !wingetListConfirmed)
            throw new InvalidOperationException($"WinGet завершился с кодом {result.ExitCode}, но установленную программу подтвердить не удалось. Успех не засчитан. Подробности записаны в журнал: {LogPath}");

        if (detected is not null)
        {
            var pathWasRequested = requestedAppDirectory is not null;
            var isInRequestedRoot = pathWasRequested && IsSameOrChildPath(detected.InstallLocation, requestedAppDirectory!);
            var exitAccepted = result.ExitCode is 0 or 3010;

            // A failed installer run must never be reported as installed just because an OLD installation exists elsewhere.
            if (!exitAccepted && pathWasRequested && !isInRequestedRoot)
            {
                await AppendLogAsync($"INSTALL FAILED, OLD COPY FOUND | {app.Name} | exit {result.ExitCode} | requested: {requestedAppDirectory} | existing: {detected.InstallLocation}");
                throw new InvalidOperationException($"WinGet завершился с кодом {result.ExitCode}: установка в {requestedAppDirectory} не выполнена. В системе осталась прежняя установка: {detected.InstallLocation}. Она не изменялась. Подробности: {LogPath}");
            }

            app.InstalledPath = detected.InstallLocation;
            app.InstalledVersion = detected.Version;
            if (pathWasRequested && !isInRequestedRoot)
            {
                // Installed and verified, but the vendor installer ignored the requested folder. Report the real path; never move files.
                app.Status = "Установлена · другой путь";
                app.Detail = $"Установщик не поддерживает выбранную папку. Запрошено: {requestedAppDirectory}. Фактически: {detected.InstallLocation}. Программа не перемещалась.";
                progress.Report($"Установлена, но не в выбранную папку. Фактический путь: {detected.InstallLocation}");
                await AppendLogAsync($"LOCATION NOT HONORED | {app.Name} | requested: {requestedAppDirectory} | actual: {detected.InstallLocation}");
            }
            else if (result.ExitCode == 3010)
            {
                app.Status = "Установлена · нужна перезагрузка";
                progress.Report($"Установка подтверждена. Нужна перезагрузка. Фактический путь: {detected.InstallLocation}");
            }
            else if (result.ExitCode != 0)
            {
                app.Status = "Установлена · предупреждение установщика";
                app.Detail = $"WinGet вернул код {result.ExitCode}, но программа обнаружена в системе. Подробности в журнале.";
                progress.Report($"Программа обнаружена в системе, хотя WinGet вернул код {result.ExitCode}. Путь: {detected.InstallLocation}");
            }
            else
            {
                app.Status = "Установлена";
                progress.Report($"Установка подтверждена. Путь: {detected.InstallLocation}");
            }
            await AppendLogAsync($"DETECTED | {app.Name} | {detected.InstallLocation} | {detected.Version}");
        }
        else
        {
            app.InstalledPath = "";
            app.InstalledVersion = "";
            if (normalizedRoot is not null)
            {
                app.Status = "Ошибка: путь установки не подтверждён";
                await AppendLogAsync($"TARGET LOCATION UNVERIFIED | {app.Name} | requested: {requestedAppDirectory} | winget list: {wingetListConfirmed} | exit: {result.ExitCode}");
                throw new InvalidOperationException($"WinGet не смог подтвердить фактический путь установки в отдельной папке {requestedAppDirectory}. Программа не будет засчитана как успешно установленная.");
            }

            app.Status = result.ExitCode == 3010
                ? "Найдена в WinGet · путь не подтверждён · нужна перезагрузка"
                : "Найдена в WinGet · путь не подтверждён";
            app.Detail = "WinGet знает об этом пакете, но исполняемый файл на диске подтвердить не удалось.";
            progress.Report(result.ExitCode == 3010
                ? "WinGet подтвердил наличие пакета. Для завершения требуется перезагрузка; фактический путь пока не определён."
                : "WinGet подтвердил наличие пакета, но Windows не предоставила фактический путь установки.");
            await AppendLogAsync($"DETECTED BY WINGET LIST | {app.Name} | path unknown | exit {result.ExitCode}");
        }
    }

    public async Task<bool> IsInstalledAsync(AppEntry app, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        app.InstalledPath = "";
        app.InstalledVersion = "";
        var info = (await Task.Run(() => FindInstalledApp(app), token)).Info;
        if (info is not null)
        {
            app.InstalledPath = info.InstallLocation;
            app.InstalledVersion = info.Version;
            return true;
        }

        if (string.IsNullOrWhiteSpace(app.WingetId)) return false;
        if (!IsValidPackageId(app.WingetId)) throw new InvalidOperationException($"Недопустимый Package ID: {app.WingetId}");
        var winget = FindWinget() ?? throw new InvalidOperationException("WinGet не найден. Проверка через WinGet недоступна.");
        var args = new[] { "list", "--id", app.WingetId, "--exact", "--source", "winget", "--disable-interactivity" };
        var result = await RunProcessAsync(winget, args, null, token, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0) throw new InvalidOperationException($"WinGet list завершился с кодом {result.ExitCode}: {TrimForLog(result.Output)}");
        var found = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Contains(app.WingetId, StringComparison.OrdinalIgnoreCase));
        if (found) app.Status = "Найдена в WinGet · путь не подтверждён";
        return found;
    }

    private async Task<bool> IsPackageListedAsync(string winget, string packageId, CancellationToken token)
    {
        if (!IsValidPackageId(packageId)) return false;
        var result = await RunProcessAsync(winget,
            ["list", "--id", packageId, "--exact", "--disable-interactivity"], null, token, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0) return false;
        return result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Contains(packageId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds a VERIFIED installation (an executable evidenced on disk). Returns staleFound=true when registry entries
    /// matched the app but none could be verified (stale uninstall records). Blocking I/O: call on a background thread.
    /// </summary>
    private static (InstalledAppInfo? Info, bool StaleFound) FindInstalledApp(AppEntry app)
    {
        var keys = GetDisplayNameAliases(app).Select(Normalize).Where(k => k.Length > 0).ToList();
        var expectedExecutable = GetExpectedExecutable(app);
        var records = InstalledAppsScanner.Scan(name =>
        {
            var normalized = Normalize(name);
            return keys.Any(key => normalized == key || (key.Length >= 12 && normalized.Contains(key, StringComparison.Ordinal)));
        }, hideSystemEntries: false);

        foreach (var record in records.Where(r => r.Verified && r.InstallLocation.Length > 0))
        {
            // For well-known apps the exact executable must be present, not just "some .exe".
            if (expectedExecutable is not null && !File.Exists(Path.Combine(record.InstallLocation, expectedExecutable))) continue;
            return (new InstalledAppInfo(record.DisplayName, record.InstallLocation, record.Version), false);
        }

        // Last resort for a few well-known apps with stable default locations (not an assumption about the chosen root).
        foreach (var candidate in GetKnownExecutableCandidates(app))
        {
            if (!File.Exists(candidate)) continue;
            var directory = Path.GetDirectoryName(candidate);
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var version = FileVersionInfo.GetVersionInfo(candidate).FileVersion ?? "";
            return (new InstalledAppInfo(app.Name, directory, version), false);
        }
        return (null, records.Count > 0);
    }

    private static string? GetExpectedExecutable(AppEntry app) => app.WingetId.ToLowerInvariant() switch
    {
        "mozilla.firefox" => "firefox.exe",
        "videolan.vlc" => "vlc.exe",
        "google.chrome" => "chrome.exe",
        _ => null
    };

    private static IEnumerable<string> GetKnownExecutableCandidates(AppEntry app)
    {
        // ProgramFiles can resolve to the x86 directory when the process is 32-bit.
        // Include ProgramW6432 / the native Windows path so 32-bit builds can still
        // discover 64-bit applications such as Firefox installed under C:\Program Files.
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFilesNative = Environment.GetEnvironmentVariable("ProgramW6432");
        if (string.IsNullOrWhiteSpace(programFilesNative))
            programFilesNative = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System).Split(Path.DirectorySeparatorChar)[0] + Path.DirectorySeparatorChar, "Program Files");
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var id = app.WingetId.ToLowerInvariant();
        if (id == "mozilla.firefox")
        {
            yield return Path.Combine(programFilesNative, "Mozilla Firefox", "firefox.exe");
            yield return Path.Combine(programFiles, "Mozilla Firefox", "firefox.exe");
            yield return Path.Combine(programFilesX86, "Mozilla Firefox", "firefox.exe");
            yield return Path.Combine(local, "Mozilla Firefox", "firefox.exe");
        }
        else if (id == "videolan.vlc")
        {
            yield return Path.Combine(programFilesNative, "VideoLAN", "VLC", "vlc.exe");
            yield return Path.Combine(programFiles, "VideoLAN", "VLC", "vlc.exe");
            yield return Path.Combine(programFilesX86, "VideoLAN", "VLC", "vlc.exe");
            yield return Path.Combine(local, "Programs", "VideoLAN", "VLC", "vlc.exe");
        }
        else if (id == "google.chrome")
        {
            yield return Path.Combine(programFilesNative, "Google", "Chrome", "Application", "chrome.exe");
            yield return Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe");
            yield return Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe");
            yield return Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe");
        }
    }

    private static IReadOnlyList<string> GetDisplayNameAliases(AppEntry app)
    {
        var aliases = new List<string> { app.Name };
        var id = app.WingetId.ToLowerInvariant();
        if (id == "mozilla.firefox") aliases.AddRange(["Mozilla Firefox", "Firefox"]);
        else if (id == "videolan.vlc") aliases.AddRange(["VLC media player", "VideoLAN VLC"]);
        else if (id == "google.chrome") aliases.AddRange(["Google Chrome", "Chrome"]);
        else if (id == "discord.discord") aliases.Add("Discord");
        else if (id == "telegram.telegramdesktop") aliases.AddRange(["Telegram Desktop", "Telegram"]);
        else if (id == "valve.steam") aliases.Add("Steam");
        else if (id == "epicgames.epicgameslauncher") aliases.AddRange(["Epic Games Launcher", "Epic Games"]);
        else if (id == "sharex.sharex") aliases.Add("ShareX");
        return aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string GetSafeAppFolderName(string appName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = appName.Trim().Select(ch => invalid.Contains(ch) || ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar ? '_' : ch).ToArray();
        var name = new string(chars).Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(name)) name = "Application";
        if (name.Length > 80) name = name[..80].TrimEnd('.', ' ');
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };
        if (reserved.Contains(name)) name = "_" + name;
        return name;
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static bool IsSameOrChildPath(string actual, string requested)
    {
        try
        {
            var root = Path.GetFullPath(requested).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(actual).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || string.Equals(root, path, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private sealed record InstalledAppInfo(string DisplayName, string InstallLocation, string Version);

    public async Task OpenLogAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        if (!File.Exists(LogPath)) await File.WriteAllTextAsync(LogPath, "Talia log" + Environment.NewLine);
        Process.Start(new ProcessStartInfo(LogPath) { UseShellExecute = true });
    }

    public async Task CreateFolderAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Task.Run(() => Directory.CreateDirectory(path), token);
    }

    public static bool IsValidPackageId(string value)
    {
        if (value.Length is < 2 or > 120 || !char.IsAsciiLetterOrDigit(value[0])) return false;
        return value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
    }

    private static string? FindWinget()
    {
        var fromPath = FindOnPath("winget.exe");
        if (fromPath is not null) return fromPath;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidate = Path.Combine(local, "Microsoft", "WindowsApps", "winget.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? FindOnPath(string file)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { var candidate = Path.Combine(dir.Trim('"'), file); if (File.Exists(candidate)) return candidate; }
            catch { /* Ignore malformed PATH entries. */ }
        }
        return null;
    }

    private async Task<string> RunCaptureAsync(string exe, IEnumerable<string> args, CancellationToken token)
    {
        var result = await RunProcessAsync(exe, args, null, token, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0) throw new InvalidOperationException(result.Output);
        return result.Output.Trim();
    }

    private async Task<(int ExitCode, string Output)> RunProcessAsync(string exe, IEnumerable<string> args, IProgress<string>? progress, CancellationToken token, TimeSpan? timeout = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (timeout is { } limit) linked.CancelAfter(limit);
        var ct = linked.Token;

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!process.Start()) throw new InvalidOperationException("Не удалось запустить процесс установки.");

        var output = new StringBuilder();
        var stdoutTask = PumpOutputAsync(process.StandardOutput, output, progress, ct);
        var stderrTask = PumpOutputAsync(process.StandardError, output, progress, ct);
        try
        {
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch (OperationCanceledException)
        {
            // Kill the whole tree: winget spawns the real installer (msiexec, setup.exe) as child processes.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { }
            if (!token.IsCancellationRequested)
            {
                await AppendLogAsync($"TIMEOUT | {Path.GetFileName(exe)} stopped after {timeout?.TotalMinutes:N0} min");
                throw new TimeoutException($"Процесс не завершился за {timeout?.TotalMinutes:N0} мин и был остановлен.");
            }
            throw;
        }
        return (process.ExitCode, output.ToString());
    }

    private async Task PumpOutputAsync(StreamReader reader, StringBuilder output, IProgress<string>? progress, CancellationToken token)
    {
        while (await reader.ReadLineAsync(token) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            lock (output)
            {
                output.AppendLine(line);
                if (output.Length > 200_000) output.Remove(0, output.Length - 150_000);
            }
            progress?.Report(line.Length > 350 ? line[^350..] : line);
            await AppendLogAsync(line);
        }
    }

    private static string TrimForLog(string value) => value.Length > 12_000 ? value[^12_000..] : value;

    private async Task AppendLogAsync(string line)
    {
        await _logGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            await File.AppendAllTextAsync(LogPath, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        }
        finally { _logGate.Release(); }
    }

    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, true);
            var parent = Path.GetDirectoryName(TempRoot);
            if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
        }
        catch { /* Best-effort cleanup. */ }
    }

    /// <summary>Removes leftovers of crashed earlier runs (own temp folders older than one day).</summary>
    public void CleanupStaleTemp()
    {
        try
        {
            var parent = Path.GetDirectoryName(TempRoot);
            if (parent is null || !Directory.Exists(parent)) return;
            foreach (var directory in Directory.EnumerateDirectories(parent))
            {
                if (string.Equals(directory, TempRoot, StringComparison.OrdinalIgnoreCase)) continue;
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(directory) < TimeSpan.FromDays(1)) continue;
                try { Directory.Delete(directory, true); } catch { }
            }
        }
        catch { /* Best-effort cleanup. */ }
    }
}
