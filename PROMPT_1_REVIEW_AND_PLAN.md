# Сообщение 1 для Claude — анализ и план

Я прикрепляю исходный проект WinSetup v0.8.3. Это C# / WPF / .NET 10 приложение для установки программ после переустановки Windows.

Сначала полностью изучи проект и документы `PROJECT_AUDIT.md` и `CLAUDE_DESIGN_BRIEF.md`. Не начинай править файлы сразу.

Твоя задача на этом этапе:
1. Просмотреть все файлы, зависимости, XAML, code-behind, сервисы, модели, сохранение данных и publish script.
2. Сверить фактические обработчики и привязки XAML с code-behind.
3. Найти потенциальные ошибки и архитектурные риски, особенно в очереди, отмене, обработке ненулевых кодов WinGet, определении установленного состояния и путей. Отдельно проверь новый pre-install detection: уже установленные приложения должны пропускаться, а не отправляться в `winget install`; очередь отдельно считает «установлено сейчас» и «уже было установлено».
4. Предложить законченную информационную архитектуру и UI для WinSetup. Главные проблемы: слишком много действий в верхней зоне, карточки с заглушкой APP, неясная навигация, смешанная нижняя панель статуса/очереди.
5. Взять UX-идеи из UniGetUI (пакетные сведения, bulk actions, разделы installed/updates, export/import) и Ninite (короткий массовый сценарий), но не копировать их внешний вид или код.
6. Разделить план на два этапа: A — UI/UX без изменения бизнес-логики; B — отдельная архитектурная/функциональная модернизация.

Верни:
- краткую оценку текущего состояния;
- предложенную структуру окон/разделов;
- схему главного экрана и очереди;
- список файлов, которые планируешь менять;
- список файлов/сервисов, которые нельзя трогать на UI-этапе;
- регрессионный чек-лист.

Не создавай фальшивые функции. Не меняй package IDs, параметры WinGet, безопасность bootstrap, формат LocalAppData или логику установки. Не обещай, что `--location` работает для каждого приложения. Не утверждай, что программа собрана или протестирована, пока реально не выполнишь build/runtime проверки.

На этом этапе только анализ и план. Жди моего следующего сообщения перед изменением файлов.


## v0.8.5 installation folder behavior
The selected directory is a parent/root. Each application must be installed into its own sanitized child folder named after the catalog entry. Current implementation uses per-app child paths, a Firefox `/InstallDirectoryPath` override, and a VLC WiX MSI `APPLICATIONFOLDER` override. Post-install verification must compare the actual location to that exact child folder. Preserve this behavior during UI refactoring; do not revert to one shared folder or claim generic support for installers that ignore custom paths.
