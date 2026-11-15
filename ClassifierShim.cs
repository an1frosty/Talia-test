namespace WinSetup.UI;

/// <summary>Presentation-only classification of the human-readable status strings produced by the install engine.</summary>
public enum StatusKind { Neutral, Waiting, Working, Success, Already, Warning, Error, Cancelled, Setup }

public static class StatusClassifier
{
    public static StatusKind Classify(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return StatusKind.Neutral;
        var t = status.Trim();
        bool Starts(string prefix) => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        bool Has(string part) => t.Contains(part, StringComparison.OrdinalIgnoreCase);

        if (Starts("Ошибка")) return StatusKind.Error;
        if (Starts("Не удалось")) return StatusKind.Warning;
        if (Starts("Отменено")) return StatusKind.Cancelled;
        if (Starts("В очереди")) return StatusKind.Waiting;
        if (Starts("Установка...") || Starts("Подготовка...") || Starts("Проверка установки...")) return StatusKind.Working;
        if (Starts("Найдена в WinGet")) return StatusKind.Warning;
        if (Starts("Уже установлена")) return StatusKind.Already;
        if (Starts("Требует")) return StatusKind.Setup;
        if (Starts("Установлена") || Starts("Установка завершена"))
            return Has("предупреждение") || Has("перезагрузка") || Has("другой путь") || Has("путь не определён") || Has("путь не подтверждён")
                ? StatusKind.Warning
                : StatusKind.Success;
        return StatusKind.Neutral;
    }
}

