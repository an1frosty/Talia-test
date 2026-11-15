using System.IO;
using System.Windows;

namespace WinSetup;

public partial class MainWindow
{
    private async void CheckWinget_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        WingetCheckButton.IsEnabled = false;
        try
        {
            var version = await _installer.GetWingetVersionAsync(CancellationToken.None);
            StatusText.Text = $"WinGet готов: {version}";
            DetailStatusText.Text = "Можно устанавливать приложения из каталога.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "WinGet не готов";
            DetailStatusText.Text = ex.Message;
            TaliaDialog.Show(ex.Message, "Проверка WinGet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { WingetCheckButton.IsEnabled = true; }
    }

    private async void InstallWinget_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        WingetBootstrapButton.IsEnabled = false;
        if (_installer.GetWingetPath() is not null)
        {
            try
            {
                var currentVersion = await _installer.GetWingetVersionAsync(CancellationToken.None);
                StatusText.Text = $"WinGet уже готов: {currentVersion}";
                DetailStatusText.Text = "Устанавливать App Installer повторно не требуется.";
            }
            catch (Exception ex) { StatusText.Text = "WinGet найден, но не запускается"; DetailStatusText.Text = ex.Message; }
            finally { WingetBootstrapButton.IsEnabled = true; }
            return;
        }

        _isBootstrappingWinget = true;
        InstallButton.IsEnabled = false;
        InstalledCheckButton.IsEnabled = false;
        WingetCheckButton.IsEnabled = false;
        StatusText.Text = "Подготовка WinGet...";
        DetailStatusText.Text = "Источник: официальный адрес Microsoft. Перед запуском проверяется цифровая подпись.";
        try
        {
            var progress = new Progress<string>(message => DetailStatusText.Text = message);
            await _installer.BootstrapWingetAsync(progress, CancellationToken.None);
            StatusText.Text = "Установщик App Installer запущен";
            DetailStatusText.Text = "Заверши установку в окне Windows, затем нажми «Проверить WinGet».";
            TaliaDialog.Show("Открылся официальный установщик App Installer.\n\nЗаверши установку в окне Windows, вернись в Talia и нажми «Проверить WinGet».", "Установка WinGet", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось подготовить WinGet";
            DetailStatusText.Text = ex.Message;
            TaliaDialog.Show(ex.Message, "Установка WinGet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _isBootstrappingWinget = false;
            WingetBootstrapButton.IsEnabled = true;
            InstallButton.IsEnabled = true;
            InstalledCheckButton.IsEnabled = true;
            WingetCheckButton.IsEnabled = true;
        }
    }

    private async void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try { await _installer.OpenLogAsync(); }
        catch (Exception ex) { TaliaDialog.Show($"Не удалось открыть журнал: {ex.Message}", "Talia", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ChooseInstallRoot_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Выбери корневую папку для установки программ",
            InitialDirectory = Directory.Exists(_installRoot) ? _installRoot : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            Directory.CreateDirectory(dialog.FolderName);
            await _settingsStore.SaveInstallRootAsync(dialog.FolderName);
            _installRoot = dialog.FolderName;
            UpdateInstallRootText();
            StatusText.Text = "Папка установки сохранена";
            DetailStatusText.Text = "Для каждой программы будет создана отдельная папка внутри выбранного корня. Фактический путь проверяется после установки.";
        }
        catch (Exception ex)
        {
            TaliaDialog.Show($"Не удалось сохранить папку: {ex.Message}", "Talia", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Folders_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        var dialog = new FolderSettingsWindow(_folders) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            try { await _settingsStore.SaveFoldersAsync(_folders); StatusText.Text = "Настройки папок сохранены"; }
            catch (Exception ex) { TaliaDialog.Show($"Не удалось сохранить пути: {ex.Message}", "Talia", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }

    private async void CreateFolders_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        if (_folders.Count == 0)
        {
            var dialog = new FolderSettingsWindow(_folders) { Owner = this };
            if (dialog.ShowDialog() != true || _folders.Count == 0) return;
            try { await _settingsStore.SaveFoldersAsync(_folders); }
            catch (Exception ex) { TaliaDialog.Show($"Не удалось сохранить пути: {ex.Message}", "Talia", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        }
        try
        {
            foreach (var path in _folders) await _installer.CreateFolderAsync(path, CancellationToken.None);
            StatusText.Text = $"Создано папок: {_folders.Count}";
            TaliaDialog.Show("Выбранные папки созданы.", "Talia");
        }
        catch (Exception ex) { TaliaDialog.Show(ex.Message, "Ошибка создания папок", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
