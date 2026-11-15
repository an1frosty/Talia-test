# Передача проекта Claude

1. Открой `PROMPT_1_REVIEW_AND_PLAN.md` и отправь его Claude вместе с ZIP проекта.
2. Дождись анализа и плана. Проверь предложенную архитектуру.
3. Затем отправь `PROMPT_2_IMPLEMENT_UI.md`.
4. Проси Claude вернуть полный проект ZIP, а не отдельные фрагменты файлов.
5. После UI-этапа отдельным запросом можно делать архитектурную модернизацию сервисов и новые функции.

Файлы:
- `PROJECT_AUDIT.md` — что реально реализовано, ограничения и найденные риски.
- `CLAUDE_DESIGN_BRIEF.md` — подробное ТЗ и технический контракт.
- `PROMPT_1_REVIEW_AND_PLAN.md` — первый запрос Claude, только аудит/план.
- `PROMPT_2_IMPLEMENT_UI.md` — второй запрос, реализация UI после согласования плана.
- `REFERENCES.md` — проекты и официальная документация.
- `VALIDATION_REPORT.md` — что удалось проверить в этой среде.


**Важно для v0.8.3:** перед установкой реализована проверка уже установленного приложения через реестр, известные EXE-пути и `winget list`. Сохрани это поведение. Не запускай `winget install` для уже установленного пакета, не создавай пустые папки назначения и отдельно показывай уже установленные программы в итогах очереди.


## v0.8.5 installation contract — preserve this behavior
- The chosen folder is a parent/root. Each app gets a sanitized child directory named after its catalog entry.
- Pass the per-app directory to WinGet as `--location`; use exact, reviewed vendor-specific overrides for Firefox (`/InstallDirectoryPath`) and VLC (WiX MSI `APPLICATIONFOLDER`).
- Verify the detected install path against that individual child directory. A mismatch or unknown path is an error when a target was selected.
- Existing apps outside the target child directory trigger a reinstall attempt; do not silently skip them. Never automatically uninstall or move the old copy.
- Generic installers may ignore custom paths. Do not claim universal support; report the actual path and add a reviewed package-specific strategy when needed.

## v0.8.5 installation folder behavior
The selected directory is a parent/root. Each application must be installed into its own sanitized child folder named after the catalog entry. Current implementation uses per-app child paths, a Firefox `/InstallDirectoryPath` override, and a VLC WiX MSI `APPLICATIONFOLDER` override. Post-install verification must compare the actual location to that exact child folder. Preserve this behavior during UI refactoring; do not revert to one shared folder or claim generic support for installers that ignore custom paths.
