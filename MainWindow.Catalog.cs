using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinSetup.Models;
using WinSetup.Services;

namespace WinSetup;

public partial class MainWindow
{
    private void LoadBuiltInCatalog()
    {
        void Add(string name, string category, string description, string id, string? repo = null)
        {
            var app = new AppEntry { Name = name, Category = category, Description = description, WingetId = id, GitHubRepo = repo };
            Track(app);
            _apps.Add(app);
        }
        Add("Discord", "Общение", "Голос, видео и текстовые чаты.", "Discord.Discord");
        Add("Telegram", "Общение", "Мессенджер для Windows.", "Telegram.TelegramDesktop");
        Add("Google Chrome", "Браузеры", "Браузер Google.", "Google.Chrome");
        Add("Mozilla Firefox", "Браузеры", "Браузер Mozilla.", "Mozilla.Firefox");
        Add("Steam", "Игры", "Игровая платформа Valve.", "Valve.Steam");
        Add("Epic Games Launcher", "Игры", "Магазин и игровой лаунчер Epic Games.", "EpicGames.EpicGamesLauncher");
        Add("Zapret Discord", "Сеть", "GitHub-проект Flowseal. Нужен отдельный сценарий распаковки и настройки.", "");
        Add("tg-ws-proxy", "Сеть", "Прокси-проект Flowseal. Нужен отдельный сценарий установки.", "");
        Add("Happ", "Сеть", "Клиент для подключения к прокси-серверам. Package ID нужно подтвердить через WinGet.", "");
        Add("ExitLag", "Сеть", "Оптимизация маршрута игрового трафика. Package ID нужно подтвердить через WinGet.", "");
        Add("GearUP Booster", "Сеть", "Игровой сетевой ускоритель. Package ID нужно подтвердить через WinGet.", "");
        Add("ShareX", "Утилиты", "Скриншоты и запись экрана.", "ShareX.ShareX");
    }

    private void Track(AppEntry app) => app.PropertyChanged += OnAppPropertyChanged;
    private void Untrack(AppEntry app) => app.PropertyChanged -= OnAppPropertyChanged;

    private void OnAppPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppEntry.IsSelected)) UpdateCount();
    }

    private void BuildCategories()
    {
        var categories = _categories.Concat(_apps.Select(a => a.Category)).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!categories.Contains(_activeCategory, StringComparer.OrdinalIgnoreCase)) _activeCategory = "Все программы";
        CategoryPanel.Children.Clear();
        foreach (var category in categories)
        {
            var active = string.Equals(category, _activeCategory, StringComparison.OrdinalIgnoreCase);
            var button = new Button { Content = category, Style = (Style)FindResource(active ? "ChipButtonActive" : "ChipButton"), Tag = category, IsEnabled = !_isInstalling };
            button.Click += (_, _) => { _activeCategory = category; BuildCategories(); ApplyFilter(); };
            CategoryPanel.Children.Add(button);
        }
    }

    // ---------- Search / sort ----------

    private void ApplyFilter()
    {
        var tokens = SearchBox.Text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IEnumerable<AppEntry> query = _apps.Where(a =>
            (_activeCategory == "Все программы" || string.Equals(a.Category, _activeCategory, StringComparison.OrdinalIgnoreCase)) &&
            tokens.All(t => a.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                            a.Description.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                            a.Category.Contains(t, StringComparison.OrdinalIgnoreCase)));
        query = _sortMode switch
        {
            1 => query.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => query.OrderBy(a => a.Category, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => query
        };
        _visibleApps = query.ToList(); // same AppEntry objects: selection state is never lost by filtering
        AppsList.ItemsSource = _visibleApps;
        if (CatalogEmptyText != null) CatalogEmptyText.Visibility = _visibleApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCount();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (IsInitialized) ApplyFilter(); }

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        _sortMode = (_sortMode + 1) % 3;
        SortButton.Content = _sortMode switch { 1 => "Порядок: название", 2 => "Порядок: категория", _ => "Порядок: каталог" };
        ApplyFilter();
    }

    private void UpdateCount()
    {
        if (SelectedCount != null)
        {
            var selected = _apps.Count(a => a.IsSelected);
            SelectedCount.Text = selected.ToString();
            var hidden = selected - _visibleApps.Count(a => a.IsSelected);
            SelectedHiddenText.Text = hidden > 0 ? $"· скрыто фильтром: {hidden}" : "";
            SelectedHiddenText.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateHomeSummary();
    }

    private static DependencyObject? ParentOf(DependencyObject d)
        => d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isInstalling) return;
        if (sender is not FrameworkElement { DataContext: AppEntry app }) return;
        for (var node = e.OriginalSource as DependencyObject; node != null && !ReferenceEquals(node, sender); node = ParentOf(node))
            if (node is System.Windows.Controls.Primitives.ButtonBase) return; // checkbox / edit / delete handle their own clicks
        app.IsSelected = !app.IsSelected;
    }

    // ---------- Custom applications ----------

    private Task SaveCatalogAsync() => _catalogStore.SaveAsync(_apps.Concat(_preservedCustom));

    private async Task LoadCustomAppsAsync()
    {
        var saved = await _catalogStore.LoadAsync();
        foreach (var app in saved)
        {
            // Entries that cannot be displayed (blank or duplicate name) are kept aside and written back on save, never dropped.
            if (string.IsNullOrWhiteSpace(app.Name) || _apps.Any(x => x.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _preservedCustom.Add(app);
                continue;
            }
            Track(app);
            _apps.Add(app);
        }
        BuildCategories();
        ApplyFilter();
    }

    private AppEntry? FindById(string id, AppEntry? except = null)
        => string.IsNullOrWhiteSpace(id) ? null : _apps.FirstOrDefault(x => !ReferenceEquals(x, except) && x.WingetId.Equals(id, StringComparison.OrdinalIgnoreCase));

    private async void AddApp_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling) return;
        var dialog = new AddAppWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        var app = dialog.Result;
        if (_apps.Any(x => x.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase)))
        {
            TaliaDialog.Show("Программа с таким названием уже есть в каталоге.", "Talia", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (FindById(app.WingetId) is { } sameId)
        {
            TaliaDialog.Show($"Package ID «{app.WingetId}» уже используется программой «{sameId.Name}».", "Talia", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Track(app);
        _apps.Add(app);
        try
        {
            await SaveCatalogAsync();
            _activeCategory = app.Category;
            BuildCategories();
            ApplyFilter();
            StatusText.Text = $"Добавлена программа: {app.Name}";
        }
        catch (Exception ex)
        {
            Untrack(app);
            _apps.Remove(app);
            ApplyFilter();
            TaliaDialog.Show($"Не удалось сохранить каталог: {ex.Message}", "Talia", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Edits the existing object in place: selection, status, detected path and version are all preserved.</summary>
    private async void EditCustomApp_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling || sender is not Button { DataContext: AppEntry existing } || !existing.IsCustom) return;
        var dialog = new AddAppWindow(existing) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        var updated = dialog.Result;
        if (_apps.Any(x => !ReferenceEquals(x, existing) && x.Name.Equals(updated.Name, StringComparison.OrdinalIgnoreCase)))
        {
            TaliaDialog.Show("Программа с таким названием уже есть в каталоге.", "Talia", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (FindById(updated.WingetId, existing) is { } sameId)
        {
            TaliaDialog.Show($"Package ID «{updated.WingetId}» уже используется программой «{sameId.Name}».", "Talia", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var before = (existing.Name, existing.Category, existing.Description, existing.WingetId);
        var idChanged = !before.WingetId.Equals(updated.WingetId, StringComparison.OrdinalIgnoreCase);
        existing.Name = updated.Name;
        existing.Category = updated.Category;
        existing.Description = updated.Description;
        existing.WingetId = updated.WingetId;
        try
        {
            await SaveCatalogAsync();
            BuildCategories();
            ApplyFilter();
            StatusText.Text = $"Изменена программа: {existing.Name}";
            DetailStatusText.Text = idChanged ? "Package ID изменён. Статус установки не сбрасывался — проверь программу кнопкой «Проверить программы»." : "Изменения сохранены.";
        }
        catch (Exception ex)
        {
            (existing.Name, existing.Category, existing.Description, existing.WingetId) = before;
            BuildCategories();
            ApplyFilter();
            TaliaDialog.Show($"Не удалось сохранить каталог: {ex.Message}", "Talia", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteCustomApp_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstalling || sender is not Button { DataContext: AppEntry app } || !app.IsCustom) return;
        if (TaliaDialog.Show($"Удалить «{app.Name}» из личного каталога?\nУстановленная программа на компьютере не удаляется.", "Talia", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var index = _apps.IndexOf(app);
        _apps.Remove(app);
        Untrack(app);
        try
        {
            await SaveCatalogAsync();
            BuildCategories();
            ApplyFilter();
            StatusText.Text = $"Удалена программа: {app.Name}";
        }
        catch (Exception ex)
        {
            _apps.Insert(Math.Clamp(index, 0, _apps.Count), app);
            Track(app);
            BuildCategories();
            ApplyFilter();
            TaliaDialog.Show($"Не удалось сохранить каталог: {ex.Message}", "Talia", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
