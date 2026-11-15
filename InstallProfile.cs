namespace WinSetup.Models;

public sealed class InstallProfile
{
    public string Name { get; set; } = "";
    public List<string> PackageIds { get; set; } = [];
    public List<string> AppNames { get; set; } = [];
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>Portable profile file written by "Export". Version lets future releases migrate the format.</summary>
public sealed class ProfileExportFile
{
    public string Format { get; set; } = "talia-profiles";
    public int Version { get; set; } = 1;
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public List<InstallProfile> Profiles { get; set; } = [];
}
