using System.IO;
using System.Windows;
using Microsoft.Win32;
using WinSetup.Models;
using WinSetup.Services;

namespace WinSetup;

public partial class MainWindow
{
    private void RefreshProfiles(string? selectName = null)
    {
        if (ProfileCombo == null) return;
        ProfileCombo.ItemsSource = null;
        ProfileCombo.ItemsSource = _profiles.Select(p => p.Name).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (!string.IsNullOrWhiteSpace(selectName)) ProfileCombo.SelectedItem = selectName;
        else if (ProfileCombo.Items.Count > 0) ProfileCombo.SelectedIndex = 0;
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        var name = (TaliaDialog.Prompt("Название профиля установки:", "Сохранить профиль", "Мой набор") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        if (name.Length > 80) { TaliaDialog.Show("Название профиля не должно быть длиннее 80 символов.", "Talia", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var selected = _apps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0) { TaliaDialog.Show("Сначала выбери программы для профиля.", "Talia"); return; }

        var profile = _profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (profile != null &&
            TaliaDialog.Show($"Профиль «{profile.Name}» уже существует. Заменить его набор программ?", "Talia", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var isNew = profile == null;
        List<string>? previousIds = profile?.PackageIds.ToList();
        List<string>? previousNames = profile?.AppNames.ToList();
        var previousUpdatedAt = profile?.UpdatedAt ?? DateTime.Now;
        if (profile == null) { profile = new InstallProfile { Name = name }; _profiles.Add(profile); }
        profile.PackageIds = selected.Where(a => !string.IsNullOrWhiteSpace(a.WingetId)).Select(a => a.WingetId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        profile.AppNames = selected.Select(a => a.Name).ToList();
        profile.UpdatedAt = DateTime.Now;
        try
        {
            await _settingsStore.SaveProfilesAsync(_profiles);
            RefreshProfiles(profile.Name);
            StatusText.Text = $"Профиль «{profile.Name}» сохранён";
        }
        catch (Exception ex)
        {
            if (isNew) _profiles.Remove(profile);
            else if (previousIds is not null && previousNames is not null) { profile.PackageIds = previousIds; profile.AppNames = previousNames; profile.UpdatedAt = previousUpdatedAt; }
            TaliaDialog.Show($"Не удалось сохранить профиль: {ex.Message}", "Talia", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        if (ProfileCombo.SelectedItem is not string name) { TaliaDialog.Show("Выбери профиль.", "Talia"); return; }
        var profile = _profiles.FirstOrDefault(p => p.Name == name);
        if (profile == null) return;
        var keys = profile.PackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = profile.AppNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var app in _apps) app.IsSelected = (keys.Contains(app.WingetId) && !string.IsNullOrWhiteSpace(app.WingetId)) || names.Contains(app.Name);
        UpdateCount();
        var missing = profile.PackageIds.Count(id => !_apps.Any(a => a.WingetId.Equals(id, StringComparison.OrdinalIgnoreCase)));
        StatusText.Text = $"Загружен профиль: {name}";
        DetailStatusText.Text = missing > 0
            ? $"{missing} из программ профиля нет в твоём каталоге — они не выбраны. Добавь их через «Добавить программу»."
            : "Выбор программ обновлён. Открой «Программы» или сразу запусти установку.";
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling || ProfileCombo.SelectedItem is not string name) return;
        if (TaliaDialog.Show($"Удалить профиль «{name}»?", "Talia", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var removed = _profiles.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        _profiles.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        try { await _settingsStore.SaveProfilesAsync(_profiles); RefreshProfiles(); StatusText.Text = $"Профиль «{name}» удалён"; }
        catch (Exception ex)
        {
            _profiles.AddRange(removed);
            RefreshProfiles(name);
            TaliaDialog.Show(ex.Message, "Talia", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- Import / export ----------

    private async void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        if (_profiles.Count == 0) { TaliaDialog.Show("Нет сохранённых профилей для экспорта.", "Talia", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        var exportAll = true;
        string? selectedName = ProfileCombo.SelectedItem as string;
        if (selectedName is not null)
        {
            var choice = TaliaDialog.Show($"Экспортировать только профиль «{selectedName}»?\n\nДа — только выбранный.\nНет — все профили.", "Экспорт профилей", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) return;
            exportAll = choice == MessageBoxResult.No;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var baseName = exportAll ? "talia-profiles" : "talia-" + new string(selectedName!.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        var dialog = new SaveFileDialog
        {
            Title = "Экспорт профилей Talia",
            Filter = "Профили Talia (*.json)|*.json|Все файлы (*.*)|*.*",
            DefaultExt = ".json",
            FileName = baseName + ".json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var profiles = exportAll ? _profiles : _profiles.Where(p => p.Name == selectedName).ToList();
            await _settingsStore.ExportProfilesAsync(dialog.FileName, profiles);
            StatusText.Text = "Профили экспортированы";
            DetailStatusText.Text = $"{profiles.Count} шт. → {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TaliaDialog.Show($"Не удалось записать файл: {ex.Message}", "Экспорт профилей", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        var dialog = new OpenFileDialog
        {
            Title = "Импорт профилей Talia",
            Filter = "Профили Talia (*.json)|*.json|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        ProfileImportResult result;
        try { result = await _settingsStore.ReadProfilesFileAsync(dialog.FileName); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            TaliaDialog.Show($"Файл не удалось импортировать.\n\n{ex.Message}", "Импорт профилей", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var working = _profiles.ToList();
        int added = 0, replaced = 0, renamed = 0, skipped = 0;
        foreach (var incoming in result.Profiles)
        {
            var index = working.FindIndex(p => p.Name.Equals(incoming.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0) { working.Add(incoming); added++; continue; }

            var choice = TaliaDialog.Show(
                $"Профиль «{incoming.Name}» уже существует ({working[index].PackageIds.Count} → {incoming.PackageIds.Count} пакетов).\n\nДа — заменить существующий.\nНет — сохранить оба (импортируемый получит суффикс «импорт»).\nОтмена — пропустить.",
                "Дубликат профиля", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Yes) { working[index] = incoming; replaced++; }
            else if (choice == MessageBoxResult.No)
            {
                var baseName = incoming.Name.Length > 60 ? incoming.Name[..60] : incoming.Name;
                var candidate = $"{baseName} (импорт)";
                for (var n = 2; working.Any(p => p.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)); n++) candidate = $"{baseName} (импорт {n})";
                incoming.Name = candidate;
                working.Add(incoming);
                renamed++;
            }
            else skipped++;
        }

        if (added + replaced + renamed == 0) { StatusText.Text = "Импорт: ничего не добавлено"; return; }
        try
        {
            await _settingsStore.SaveProfilesAsync(working);
        }
        catch (Exception ex)
        {
            TaliaDialog.Show($"Не удалось сохранить профили: {ex.Message}\nТекущие профили не изменены.", "Импорт профилей", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _profiles = working;
        RefreshProfiles();

        var summary = $"Добавлено: {added} · заменено: {replaced} · сохранено как копия: {renamed} · пропущено: {skipped}";
        StatusText.Text = "Профили импортированы";
        DetailStatusText.Text = summary;
        if (result.Problems.Count > 0)
            TaliaDialog.Show(summary + "\n\nЗамечания к файлу:\n" + string.Join("\n", result.Problems.Take(10)) + (result.Problems.Count > 10 ? $"\n… и ещё {result.Problems.Count - 10}" : ""), "Импорт профилей", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
