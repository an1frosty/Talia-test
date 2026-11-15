using System.Windows;
using System.Windows.Media;
using WinSetup.Models;
using WinSetup.UI;

namespace WinSetup;

public partial class MainWindow
{
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        await RunQueueAsync(_apps.Where(a => a.IsSelected).ToList(), isRetry: false);
    }

    /// <summary>Re-runs ONLY the items that failed in the last queue (selection of other apps is not touched).</summary>
    private async void RetryFailed_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        var failed = _queue.Where(a => StatusClassifier.Classify(a.Status) == StatusKind.Error).ToList();
        if (failed.Count == 0) failed = _apps.Where(a => StatusClassifier.Classify(a.Status) == StatusKind.Error).ToList();
        if (failed.Count == 0) { TaliaDialog.Show("Нет программ с ошибками установки.", "Talia"); return; }
        await RunQueueAsync(failed, isRetry: true);
    }

    /// <summary>Sequential installation queue. One installer at a time; cancellation stops the running process tree.</summary>
    private async Task RunQueueAsync(List<AppEntry> targets, bool isRetry)
    {
        if (targets.Count == 0) { TaliaDialog.Show("Сначала отметь программы галочками.", "Talia"); return; }

        var unsupported = targets.Where(a => string.IsNullOrWhiteSpace(a.WingetId)).ToList();
        var message = isRetry
            ? $"Повторить установку программ с ошибками ({targets.Count})?"
            : $"Начать установку выбранных приложений ({targets.Count})?";
        if (unsupported.Count > 0)
            message += $"\n\nАвтоматическая установка отключена для: {string.Join(", ", unsupported.Select(a => a.Name))}. Для них нет проверенного Package ID WinGet, поэтому они будут отмечены как требующие настройки.";
        if (TaliaDialog.Show(message, "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _queue.Clear();
        foreach (var queued in targets) { queued.Detail = ""; queued.Status = "В очереди"; _queue.Add(queued); }
        QueueEmptyText.Visibility = Visibility.Collapsed;
        QueueSummaryCard.Visibility = Visibility.Visible;
        QueueSummaryText.Visibility = Visibility.Collapsed;
        QueueProgress.Maximum = targets.Count;
        QueueProgress.Value = 0;
        QueueProgressText.Text = $"Установка: 0 из {targets.Count}";
        QueueCurrentText.Text = "Подготовка очереди…";
        Navigate("Queue");

        _isInstalling = true;
        _cts = new CancellationTokenSource();
        InstallButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        QueueCancelButton.IsEnabled = true;
        SearchBox.IsEnabled = false;
        SortButton.IsEnabled = false;
        BuildCategories();
        InstallProgress.Maximum = targets.Count;
        InstallProgress.Value = 0;

        int installed = 0, already = 0, warnings = 0, failed = 0, skipped = 0, cancelled = 0, current = 0;
        try
        {
            foreach (var app in targets)
            {
                _cts.Token.ThrowIfCancellationRequested();
                current++;
                StatusText.Text = $"{current} из {targets.Count}: {app.Name}";
                QueueProgressText.Text = $"Установка: {current} из {targets.Count}";
                QueueCurrentText.Text = $"Сейчас: {app.Name}";

                if (string.IsNullOrWhiteSpace(app.WingetId))
                {
                    app.Status = "Требует отдельной настройки";
                    app.Detail = "Автоматическая установка отключена: для программы нет проверенного Package ID WinGet.";
                    skipped++;
                    InstallProgress.Value = QueueProgress.Value = current;
                    continue;
                }

                app.Status = "Подготовка...";
                var progress = new Progress<string>(s => { DetailStatusText.Text = s; });
                try
                {
                    await _installer.InstallAsync(app, progress, _cts.Token, _installRoot);
                    switch (StatusClassifier.Classify(app.Status))
                    {
                        case StatusKind.Already: already++; break;
                        case StatusKind.Warning: installed++; warnings++; break;
                        default: installed++; break;
                    }
                }
                catch (OperationCanceledException) { app.Status = "Отменено"; throw; }
                catch (Exception ex)
                {
                    app.Status = "Ошибка установки";
                    app.Detail = ex.Message;
                    failed++;
                    DetailStatusText.Text = ex.Message;
                }
                InstallProgress.Value = QueueProgress.Value = current;
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var waiting in targets.Where(a => StatusClassifier.Classify(a.Status) is StatusKind.Waiting or StatusKind.Working))
                waiting.Status = "Отменено";
            cancelled = targets.Count(a => a.Status == "Отменено");
        }
        finally
        {
            _installer.Cleanup();
            _cts?.Dispose();
            _cts = null;
            _isInstalling = false;
            InstallButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            QueueCancelButton.IsEnabled = false;
            SearchBox.IsEnabled = true;
            SortButton.IsEnabled = true;
            BuildCategories();
        }

        ShowQueueSummary(targets.Count, installed, already, warnings, failed, skipped, cancelled);
        if (_closeRequested) Close();
    }

    private void ShowQueueSummary(int total, int installed, int already, int warnings, int failed, int skipped, int cancelled)
    {
        var parts = new List<string>();
        if (installed > 0) parts.Add($"установлено: {installed}" + (warnings > 0 ? $" (из них с предупреждением: {warnings})" : ""));
        if (already > 0) parts.Add($"уже были установлены: {already}");
        if (failed > 0) parts.Add($"ошибки: {failed}");
        if (skipped > 0) parts.Add($"требуют настройки: {skipped}");
        if (cancelled > 0) parts.Add($"отменено: {cancelled}");
        var headline = cancelled > 0 ? "Очередь отменена" : failed > 0 ? "Очередь завершена с ошибками" : "Очередь завершена";
        var details = parts.Count == 0 ? "ничего не выполнено" : string.Join(" · ", parts);

        QueueProgressText.Text = headline;
        QueueCurrentText.Text = $"Всего в очереди: {total}";
        QueueSummaryText.Text = $"Итог: {details}.\nЖурнал: {_installer.LogPath}" + (failed > 0 ? "\nПодробности каждой ошибки указаны у программы в списке ниже; «Повторить ошибки» запустит только их." : "");
        QueueSummaryText.Foreground = (Brush)FindResource(failed > 0 ? "BrushRedText" : "BrushText");
        QueueSummaryText.Visibility = Visibility.Visible;

        StatusText.Text = headline;
        DetailStatusText.Text = char.ToUpper(details[0]) + details[1..];
    }

    private void CancelInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is null) return;
        QueueCurrentText.Text = "Останавливаю текущий процесс…";
        _cts.Cancel();
    }
}
