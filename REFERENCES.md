# Референсы и источники

Использовать как источники UX-идей и проверки возможностей. Не копировать код/бренд/визуальный стиль.

- UniGetUI (официальный GitHub): https://github.com/Devolutions/UniGetUI — единый GUI над пакетными менеджерами, поиск, установка, обновления, удаление, сведения о пакетах, bulk actions, экспорт/импорт.
- UniGetUI portable docs: https://github.com/Devolutions/UniGetUI — портативный режим и переносимость настроек описаны в документации репозитория.
- Ninite: https://ninite.com/ — минимальный массовый сценарий установки популярных программ.
- WinGet install: https://learn.microsoft.com/windows/package-manager/winget/install — install, exact ID, source, location (если поддерживается), agreements, logs.
- WinGet upgrade: https://learn.microsoft.com/windows/package-manager/winget/upgrade — проверка/обновление пакетов и ограничения unknown versions/pins.
- WinGet overview: https://learn.microsoft.com/windows/package-manager/winget/ — list, upgrade, uninstall, export/import, configure, repair и другие команды.
- WinGet settings: https://learn.microsoft.com/windows/package-manager/winget/settings — install roots и portable package roots; не путать с универсальным путём для всех установщиков.

Важно: Microsoft прямо указывает, что `--location` применим только если установщик поддерживает пользовательский путь. Нельзя показывать это как гарантию.
