# Сообщение 2 для Claude — реализация редизайна

Теперь реализуй утверждённый план UI/UX для WinSetup. Работай с тем же исходным проектом v0.8.1.

Главная цель — законченный, компактный, удобный Windows-интерфейс. Верхняя зона должна быть чистой: поиск, фильтры и только одна-две основные команды. WinGet bootstrap, проверка программ, повтор ошибок, журнал, папка установки, шаблоны папок и прочие редкие действия должны находиться в «Инструментах»/настройках, а не занимать постоянную панель.

Обязательно:
- сохранить все текущие функции и пользовательские данные;
- сделать ясную навигацию «Каталог», «Установленные», «Обновления» (последние две только при реальной реализации данных), «Очередь», «Профили», «Настройки»;
- заменить `APP` на аккуратные иконки/монограммы; не скачивать произвольные изображения во время запуска;
- адаптировать сетку карточек и поведение при изменении размера окна;
- разделить краткий статус и подробный лог;
- показывать фактический путь только если он реально обнаружен; отдельно показывать неизвестный путь и несовпадение с запрошенным;
- для функций, которых ещё нет, не рисовать активные кнопки. Можно поместить их в roadmap, но не выдавать за работающие.

Технические ограничения:
- WPF + .NET 10;
- не удалять и не переименовывать сервисы/модели без необходимости;
- не менять бизнес-логику установки в рамках UI-only прохода;
- не менять package IDs, источники и аргументы WinGet;
- сохранить `ProcessStartInfo.ArgumentList`, cancellation, progress, logging и data persistence;
- все новые кнопки должны иметь рабочие обработчики;
- не хранить важные данные только в UI state;
- не создавать новые NuGet-зависимости без реальной необходимости.

После реализации:
1. Собери Release для `win-x64` и исправь ошибки.
2. Проверь все XAML bindings, names, handlers и ресурсы.
3. Проверь сценарии из `PROJECT_AUDIT.md` и `CLAUDE_DESIGN_BRIEF.md`.
4. Покажи diff/список изменённых файлов.
5. Создай ZIP исходников.
6. Отдельно сообщи, какие тесты реально выполнены. Если Windows runtime/build недоступен — не называй проект готовым.


## v0.8.5 installation folder behavior
The selected directory is a parent/root. Each application must be installed into its own sanitized child folder named after the catalog entry. Current implementation uses per-app child paths, a Firefox `/InstallDirectoryPath` override, and a VLC WiX MSI `APPLICATIONFOLDER` override. Post-install verification must compare the actual location to that exact child folder. Preserve this behavior during UI refactoring; do not revert to one shared folder or claim generic support for installers that ignore custom paths.
