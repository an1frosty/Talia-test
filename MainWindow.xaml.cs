using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinSetup.Models;
using WinSetup.Services;
using WinSetup.UI;

namespace WinSetup;

// MainWindow is split by responsibility:
//   MainWindow.xaml.cs          shell: fields, startup/shutdown, navigation, responsive layout
//   MainWindow.Catalog.cs       catalog: search / sort / categories, custom apps
//   MainWindow.Profiles.cs      profiles: save / load / delete / import / export
//   MainWindow.Queue.cs         installation queue, retry, cancel, summary
//   MainWindow.Installed.cs     installed-programs view and catalog verification
//   MainWindow.Settings.cs      WinGet, install root, folders, log
public partial class MainWindow : Window
{
    private readonly ObservableCollection<AppEntry> _apps = new();
    private readonly ObservableCollection<AppEntry> _queue = new();
    private readonly List<AppEntry> _preservedCustom = new(); // custom entries that could not be shown; kept so saving never drops them
    private readonly InstallerService _installer = new();
    private readonly AppCatalogStore _catalogStore = new();
    private readonly UserSettingsStore _settingsStore = new();
    private List<InstallProfile> _profiles = [];
    private readonly List<string> _categories = ["Все программы", "Общение", "Браузеры", "Игры", "Сеть", "Утилиты"];
    private string _activeCategory = "Все программы";
    private int _sortMode; // 0 = catalog order, 1 = name, 2 = category
    private List<AppEntry> _visibleApps = [];
    private readonly List<string> _folders = new();
    private CancellationTokenSource? _cts;
    private bool _isInstalling;
    private bool _isCheckingInstalled;
    private bool _isBootstrappingWinget;
    private bool _closeRequested;
    private bool? _compactLayout;
    private string _installRoot = "";

    private bool IsBusy => _isInstalling || _isCheckingInstalled || _isBootstrappingWinget;

    public MainWindow()
    {
        InitializeComponent();
        TitleBar.Apply(this);
        FitInitialSizeToScreen();
        SizeChanged += OnWindowSizeChanged;
        QueueList.ItemsSource = _queue;
        Navigate("Home");
        LoadBuiltInCatalog();
        BuildCategories();
        ApplyFilter();
        UpdateCount();
        Loaded += async (_, _) => await OnLoadedAsync();
        ContentRendered += (_, _) => UpdateCardWidth();
    }

    private async Task OnLoadedAsync()
    {
        await InitializeUserDataAsync();
        UpdateCardWidth();
        _ = Task.Run(_installer.CleanupStaleTemp);
    }

    private async Task InitializeUserDataAsync()
    {
        _folders.Clear();
        _folders.AddRange(await _settingsStore.LoadFoldersAsync());
        _installRoot = await _settingsStore.LoadInstallRootAsync();
        UpdateInstallRootText();
        await LoadCustomAppsAsync();
        _profiles = await _settingsStore.LoadProfilesAsync();
        RefreshProfiles();

        var warnings = new List<string>(_settingsStore.Warnings);
        if (!string.IsNullOrWhiteSpace(_catalogStore.LastWarning)) warnings.Add(_catalogStore.LastWarning!);
        if (warnings.Count > 0)
            TaliaDialog.Show(string.Join("\n\n", warnings), "Talia — проблема с данными", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ---------- Window sizing, navigation, responsive layout ----------

    private void FitInitialSizeToScreen()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width * 0.94));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height * 0.94));
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = ActualWidth < 960;
        if (_compactLayout == compact) return;
        _compactLayout = compact;
        SidebarColumn.Width = new GridLength(compact ? 68 : 208);
        Resources["NavLabelVisibility"] = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CatalogScroll_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateCardWidth();

    /// <summary>
    /// Card grid: as many columns as fit with a ~260 px minimum card (3 columns at normal sizes, 2 on small windows,
    /// 1 on very narrow ones, up to 5 on wide monitors). Each card occupies width + 12 px right margin, so the
    /// formula reserves that margin plus 12 px for the vertical scrollbar; no horizontal scrolling is possible.
    /// </summary>
    private void UpdateCardWidth()
    {
        var available = CatalogScroll.ActualWidth - 12; // scrollbar (10 px) + safety
        if (available <= 0) return;
        const double gap = 12, minCard = 260;
        var columns = Math.Clamp((int)Math.Floor((available + gap) / (minCard + gap)), 1, 5);
        var width = Math.Max(200.0, Math.Floor((available - columns * gap) / columns));
        if (Resources["CardWidth"] is double current && Math.Abs(current - width) < 0.5) return;
        Resources["CardWidth"] = width;
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Name: { } name }) Navigate(name.Replace("Nav", ""));
    }

    private void GoPrograms_Click(object sender, RoutedEventArgs e) => Navigate("Programs");

    private void Navigate(string view)
    {
        HomeView.Visibility = view == "Home" ? Visibility.Visible : Visibility.Collapsed;
        ProgramsView.Visibility = view == "Programs" ? Visibility.Visible : Visibility.Collapsed;
        InstalledView.Visibility = view == "Installed" ? Visibility.Visible : Visibility.Collapsed;
        QueueView.Visibility = view == "Queue" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesView.Visibility = view == "Profiles" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = view == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        NavHome.IsChecked = view == "Home";
        NavPrograms.IsChecked = view == "Programs";
        NavInstalled.IsChecked = view == "Installed";
        NavQueue.IsChecked = view == "Queue";
        NavProfiles.IsChecked = view == "Profiles";
        NavSettings.IsChecked = view == "Settings";
        if (view == "Programs")
        {
            UpdateCardWidth();
            // Layout of a just-shown view is not measured yet: recalculate once it is.
            Dispatcher.BeginInvoke(new Action(UpdateCardWidth), DispatcherPriority.Loaded);
        }
        if (view == "Installed" && !_installedLoaded) _ = RefreshInstalledAsync();
    }

    private void UpdateInstallRootText()
    {
        var empty = string.IsNullOrWhiteSpace(_installRoot);
        if (InstallRootText != null) InstallRootText.Text = empty ? "Корневая папка программ: не задана" : $"Корень программ: {_installRoot}";
        if (HomeRootText != null) HomeRootText.Text = empty ? "Корневая папка программ: не задана" : _installRoot;
    }

    private void UpdateHomeSummary()
    {
        if (HomeCatalogText != null) HomeCatalogText.Text = $"Программ в каталоге: {_apps.Count}";
        if (HomeSelectedText != null) HomeSelectedText.Text = $"Выбрано: {_apps.Count(a => a.IsSelected)}";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isInstalling && !_closeRequested)
        {
            if (TaliaDialog.Show("Идёт установка. Остановить её и закрыть Talia?", "Talia", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            // Let the running installer process tree be stopped properly; the window closes when the queue unwinds.
            _closeRequested = true;
            e.Cancel = true;
            _cts?.Cancel();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _installer.Cleanup();
        base.OnClosed(e);
    }
}
