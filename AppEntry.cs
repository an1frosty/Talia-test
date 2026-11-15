using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace WinSetup.Models;

/// <summary>
/// Catalog entry. Name / Category / Description / WingetId are editable in place (custom apps),
/// so editing never has to replace the object and lose runtime state.
/// Runtime state (selection, status, detected path/version, detail) is NOT persisted: it must be
/// re-verified every session instead of being trusted from an old file.
/// </summary>
public sealed class AppEntry : INotifyPropertyChanged
{
    private string _name = "";
    private string _category = "";
    private string _description = "";
    private string _wingetId = "";
    private bool _isSelected;
    private string _status = "Не установлено";
    private string _installedPath = "";
    private string _installedVersion = "";
    private string _detail = "";

    public required string Name { get => _name; set => Set(ref _name, value); }
    public required string Category { get => _category; set => Set(ref _category, value); }
    public required string Description { get => _description; set => Set(ref _description, value); }
    public required string WingetId { get => _wingetId; set => Set(ref _wingetId, value); }
    public string? GitHubRepo { get; init; }
    public bool IsCustom { get; init; }

    [JsonIgnore] public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    [JsonIgnore] public string Status { get => _status; set => Set(ref _status, value); }
    [JsonIgnore] public string InstalledPath { get => _installedPath; set => Set(ref _installedPath, value); }
    [JsonIgnore] public string InstalledVersion { get => _installedVersion; set => Set(ref _installedVersion, value); }

    /// <summary>Presentation-only error / warning text shown in the queue.</summary>
    [JsonIgnore] public string Detail { get => _detail; set => Set(ref _detail, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
