using System.IO;
using Microsoft.Win32;

namespace WinSetup.Services;

/// <summary>One program found in the Windows uninstall registry, with an honest verification result.</summary>
public sealed record InstalledRecord(
    string DisplayName,
    string Version,
    string Publisher,
    string InstallLocation,
    string Source,
    bool Verified,
    string VerificationNote);

/// <summary>
/// Reads the Windows uninstall registry (HKLM 64/32-bit and HKCU). A record is "verified" only when an
/// actual executable is evidenced on disk; a registry entry or an existing folder alone is not enough.
/// Blocking I/O: call from a background thread.
/// </summary>
public static class InstalledAppsScanner
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<InstalledRecord> Scan(Func<string, bool>? nameFilter = null, bool hideSystemEntries = true, CancellationToken token = default)
    {
        var result = new List<InstalledRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ScanHive(RegistryHive.LocalMachine, RegistryView.Registry64, "Реестр · HKLM (64-бит)", nameFilter, hideSystemEntries, result, seen, token);
        ScanHive(RegistryHive.LocalMachine, RegistryView.Registry32, "Реестр · HKLM (32-бит)", nameFilter, hideSystemEntries, result, seen, token);
        ScanHive(RegistryHive.CurrentUser, RegistryView.Default, "Реестр · HKCU (пользователь)", nameFilter, hideSystemEntries, result, seen, token);
        return result;
    }

    private static void ScanHive(RegistryHive hive, RegistryView view, string source, Func<string, bool>? nameFilter, bool hideSystemEntries, List<InstalledRecord> result, HashSet<string> seen, CancellationToken token)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(UninstallPath);
            if (uninstall is null) return;
            foreach (var subName in uninstall.GetSubKeyNames())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var item = uninstall.OpenSubKey(subName);
                    if (item is null) continue;
                    var record = ReadRecord(item, source, nameFilter, hideSystemEntries);
                    if (record is not null && seen.Add($"{record.DisplayName}|{record.Version}|{record.InstallLocation}"))
                        result.Add(record);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    private static InstalledRecord? ReadRecord(RegistryKey item, string source, Func<string, bool>? nameFilter, bool hideSystemEntries)
    {
        var name = (item.GetValue("DisplayName") as string)?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (nameFilter is not null && !nameFilter(name)) return null;

        if (hideSystemEntries)
        {
            if (item.GetValue("SystemComponent") is int component && component == 1) return null;
            if (!string.IsNullOrEmpty(item.GetValue("ParentKeyName") as string)) return null;
            var releaseType = item.GetValue("ReleaseType") as string;
            if (releaseType is "Update" or "Hotfix" or "Security Update" or "Update Rollup" or "Service Pack") return null;
            if (name.StartsWith("Update for ", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Security Update for ", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Hotfix for ", StringComparison.OrdinalIgnoreCase)) return null;
        }

        var version = (item.GetValue("DisplayVersion") as string)?.Trim() ?? "";
        var publisher = (item.GetValue("Publisher") as string)?.Trim() ?? "";
        var location = (item.GetValue("InstallLocation") as string)?.Trim().Trim('"') ?? "";
        var iconExecutable = ResolveIconExecutable(item.GetValue("DisplayIcon") as string);

        var locationExists = location.Length > 0 && Directory.Exists(location);
        if (!locationExists && iconExecutable is not null)
        {
            var iconDirectory = Path.GetDirectoryName(iconExecutable);
            if (!string.IsNullOrWhiteSpace(iconDirectory) && Directory.Exists(iconDirectory)) { location = iconDirectory; locationExists = true; }
        }

        bool verified;
        string note;
        if (iconExecutable is not null && File.Exists(iconExecutable))
        {
            verified = true;
            note = "Исполняемый файл найден (значок программы)";
        }
        else if (locationExists && ContainsExecutable(location))
        {
            verified = true;
            note = "В папке установки найден .exe";
        }
        else if (locationExists)
        {
            verified = false;
            note = "Папка есть, но исполняемый файл не найден";
        }
        else if (location.Length > 0)
        {
            verified = false;
            note = "Папка не найдена — возможна устаревшая запись";
        }
        else
        {
            verified = false;
            note = "Путь установки в реестре не указан";
        }

        return new InstalledRecord(name, version, publisher, locationExists ? location : "", source, verified, note);
    }

    /// <summary>Extracts an .exe path from DisplayIcon. Ignores cache icons under Windows\Installer, which can outlive the program.</summary>
    private static string? ResolveIconExecutable(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        icon = icon.Trim();
        if (icon.StartsWith('"'))
        {
            var closing = icon.IndexOf('"', 1);
            if (closing > 1) icon = icon[1..closing];
        }
        else
        {
            var comma = icon.LastIndexOf(',');
            if (comma > 0 && int.TryParse(icon[(comma + 1)..], out _)) icon = icon[..comma].Trim();
        }
        try
        {
            if (!Path.IsPathFullyQualified(icon) || !icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            if (icon.Contains(@"\Windows\Installer\", StringComparison.OrdinalIgnoreCase)) return null;
            return icon;
        }
        catch (ArgumentException) { return null; }
    }

    private static bool ContainsExecutable(string directory)
    {
        try
        {
            if (Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly).Any()) return true;
            foreach (var sub in Directory.EnumerateDirectories(directory).Take(40))
                if (Directory.EnumerateFiles(sub, "*.exe", SearchOption.TopDirectoryOnly).Any()) return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return false;
    }
}
