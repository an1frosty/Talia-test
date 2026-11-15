using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WinSetup.Services;
using WinSetup.UI;

namespace WinSetup;

/// <summary>Display model for one row of the Installed view. Property names are used by XAML bindings.</summary>
public sealed record InstalledRow(string Name, string Version, string Path, string Note, bool Verified, string CatalogLabel);

public partial class MainWindow
{
    private List<InstalledRow> _installedAll = [];
    private bool _installedLoaded;
    private bool _installedLoading;

    private static string NormalizeName(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private async Task RefreshInstalledAsync()
    {
        if (_installedLoading) return;
        _installedLoading = true;
        InstalledRefreshButton.IsEnabled = false;
        InstalledSummaryText.Text = "Читаю реестр Windows…";
        var catalog = _apps.Select(a => (Name: a.Name, Key: NormalizeName(a.Name))).Where(x => x.Key.Length > 0).ToList();
        try
        {
            var records = await Task.Run(() => InstalledAppsScanner.Scan());
            _installedAll = records
                .OrderBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(r =>
                {
                    var normalized = NormalizeName(r.DisplayName);
                    var match = catalog.FirstOrDefault(c => normalized == c.Key || (c.Key.Length >= 5 && normalized.Contains(c.Key, StringComparison.Ordinal)));
                    var note = (string.IsNullOrWhiteSpace(r.Publisher) ? "" : r.Publisher + " · ") + r.Source + " · " + r.VerificationNote;
                    return new InstalledRow(r.DisplayName, r.Version, r.InstallLocation, note, r.Verified, match.Name is null ? "" : "В каталоге Talia");
                })
                .ToList();
            _installedLoaded = true;
            ApplyInstalledFilter();
        }
        catch (Exception ex)
        {
            InstalledSummaryText.Text = $"Не удалось прочитать список: {ex.Message}";
        }
        finally
        {
            _installedLoading = false;
            InstalledRefreshButton.IsEnabled = true;
        }
    }

    private void ApplyInstalledFilter()
    {
        if (InstalledList == null) return;
        var tokens = InstalledSearchBox.Text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var onlyVerified = InstalledOnlyVerified.IsChecked == true;
        var filtered = _installedAll
            .Where(r => (!onlyVerified || r.Verified) && tokens.All(t => r.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || r.Path.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        InstalledList.ItemsSource = filtered;
        var verified = _installedAll.Count(r => r.Verified);
        InstalledSummaryText.Text = $"Найдено в реестре: {_installedAll.Count} · подтверждено на диске: {verified} · показано: {filtered.Count}";
        InstalledEmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        InstalledEmptyText.Text = _installedAll.Count == 0 ? "Программы не найдены." : "Ничего не найдено. Измени запрос или отключи фильтр.";
    }

    private async void RefreshInstalled_Click(object sender, RoutedEventArgs e) => await RefreshInstalledAsync();
    private void InstalledSearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (IsInitialized && _installedLoaded) ApplyInstalledFilter(); }
    private void InstalledFilter_Click(object sender, RoutedEventArgs e) { if (_installedLoaded) ApplyInstalledFilter(); }

    private void OpenInstalledFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: InstalledRow row } || !Directory.Exists(row.Path)) return;
        try
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            start.ArgumentList.Add(row.Path);
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex)
        {
            TaliaDialog.Show($"Не удалось открыть папку: {ex.Message}", "Talia", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- Catalog verification (Settings -> "Проверить программы") ----------

    private async void CheckInstalled_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        var targets = _apps.Where(a => !string.IsNullOrWhiteSpace(a.WingetId)).ToList();
        if (targets.Count == 0) return;
        _isCheckingInstalled = true;
        InstalledCheckButton.IsEnabled = false;
        InstallButton.IsEnabled = false;
        WingetCheckButton.IsEnabled = false;
        WingetBootstrapButton.IsEnabled = false;
        InstallProgress.Maximum = targets.Count;
        InstallProgress.Value = 0;
        int installed = 0, missing = 0, errors = 0, current = 0;
        try
        {
            foreach (var app in targets)
            {
                current++;
                StatusText.Text = $"Проверка {current} из {targets.Count}: {app.Name}";
                try
                {
                    var found = await _installer.IsInstalledAsync(app, CancellationToken.None);
                    if (!found) app.Status = "Не установлена";
                    else if (!string.IsNullOrWhiteSpace(app.InstalledPath)) app.Status = "Установлена"; // verified executable
                    // else: the engine already set "Найдена в WinGet · путь не подтверждён"
                    if (found) installed++; else missing++;
                }
                catch (Exception ex)
                {
                    app.Status = "Не удалось проверить";
                    DetailStatusText.Text = ex.Message;
                    errors++;
                }
                InstallProgress.Value = current;
            }
            StatusText.Text = "Проверка завершена";
            DetailStatusText.Text = $"Найдено: {installed} · не установлено: {missing} · ошибки проверки: {errors}";
        }
        finally
        {
            _isCheckingInstalled = false;
            InstalledCheckButton.IsEnabled = true;
            InstallButton.IsEnabled = true;
            WingetCheckButton.IsEnabled = true;
            WingetBootstrapButton.IsEnabled = true;
        }
    }
}
