# Changelog

## 1.0.0 — Talia 1.0 (audit, fixes, new features)
Fixed
- Editing a custom app no longer replaces the object: selection, status, installed path and version are preserved (`AppEntry` is edited in place).
- Runtime state (status, installed path/version, selection) is no longer written to `custom-apps.json`, so stale "installed" states are not restored on next start. Old files remain readable.
- Custom entries that could not be displayed (blank/duplicate name) were silently dropped on the next save; they are now kept and written back.
- Corrupted or unreadable JSON no longer yields a silent empty catalog/profile list: file is copied to `*.corrupt-<time>`, last good `*.bak` is tried, the user gets a warning, unreadable files are never overwritten. Writes are atomic with flush and `.bak` rotation. Install root is saved atomically too.
- "Retry failed" used to re-run every selected app; it now runs only the failed ones.
- Registry/folder scanning ran on the UI thread; it now runs in the background.
- Post-install detection accepted any existing folder; it now requires an evidenced executable (stale uninstall entries are logged and ignored; all matching registry entries are examined, not just the first).
- A failed installer run is no longer reported as "installed" when an old copy exists elsewhere.
- Installer ignoring the requested folder is now "Установлена · другой путь" with actual path and warning (previously an error although the app was installed).
- WinGet-only knowledge is no longer shown as "Установлена"; it is "Найдена в WinGet · путь не подтверждён".
- Authenticode check now requires signer `CN=Microsoft Corporation` (was substring match) and no longer passes `-ExecutionPolicy Bypass`.
- Processes get timeouts and are killed as a tree; closing the window during an installation asks for confirmation and stops the installer cleanly.
- Stale temp folders from crashed runs are cleaned; leftover log header says Talia.
- Catalog card grid: ~260 px minimum card, 3 columns at normal sizes, 2 on small windows, 1 on very narrow ones, up to 5 on wide monitors; accounts for margins and scrollbar and recalculates after first layout.
New
- Installed section (registry scan, version, path, source, verification status, open folder, search, "only verified" filter).
- Profile export/import (JSON, validated, duplicate handling, size limit).
- Queue: overall progress, current app, per-app states (queued / preparing / installing / verifying / installed / already / warning / restart / manual / failed / cancelled), full error text, cancel, retry, final summary.
- Search by name, description and category (multi-word), sorting (catalog / name / category), selection kept while filtering, hidden-selection counter.
- Duplicate Package ID protection when adding/editing apps.
- Code split into partial classes by responsibility; registry logic moved to `InstalledAppsScanner`; `SafeJsonFile` for persistence.
Not added (on purpose)
- Updates section: no reliable machine-readable WinGet upgrade list is available (output is localized text). No Update/Uninstall/Repair buttons.
Build status
- Not compiled or run in the authoring environment. See VALIDATION_REPORT.md.

## 0.9.0 — Talia UI/UX redesign (UI only)
- Visible rebrand WinSetup -> **Talia** ("Install. Organize. Enjoy."); internal namespace, exe name and `%LOCALAPPDATA%\WinSetup` data folder unchanged so existing settings, profiles and custom apps keep working.
- New shared design system: `Themes/Talia.xaml` (palette, brushes, type scale, radii, buttons, inputs, checkbox, list, scrollbar, progress, tooltip).
- New shell: collapsible sidebar (icon rail below 1100 px) with Home / Programs / Queue / Profiles / Settings views; persistent status + install footer.
- Programs: category chips, responsive card grid (2-5 columns from available width), monogram icons, clickable cards, status dots, empty-search state.
- Queue view: per-app state (waiting / installing / installed / already installed / warning / failed / cancelled) with full error text kept per item.
- Themed `TaliaDialog` replaces native MessageBox / InputBox; Add-app and Folder windows restyled and clamped to the work area.
- Dark title bar on Windows 10/11, Per-Monitor V2 DPI manifest, app icon.
- Engine untouched: `Services/*` was not modified. `AppEntry` gained a non-persisted `Detail` property used only for display.
- Not compiled or run in the authoring environment (no .NET SDK / Windows). Build and test on Windows.

## 0.9.0 — Talia UI/UX redesign (UI only)
- Visible rebrand WinSetup -> **Talia** ("Install. Organize. Enjoy."); internal namespace, exe name and `%LOCALAPPDATA%\WinSetup` data folder unchanged so existing settings, profiles and custom apps keep working.
- New shared design system: `Themes/Talia.xaml` (palette, brushes, type scale, radii, buttons, inputs, checkbox, list, scrollbar, progress, tooltip).
- New shell: collapsible sidebar (icon rail below 1100 px) with Home / Programs / Queue / Profiles / Settings views; persistent status + install footer.
- Programs: category chips, responsive card grid (2-5 columns from available width), monogram icons, clickable cards, status dots, empty-search state.
- Queue view: per-app state (waiting / installing / installed / already installed / warning / failed / cancelled) with full error text kept per item.
- Themed `TaliaDialog` replaces native MessageBox / InputBox; Add-app and Folder windows restyled and clamped to the work area.
- Dark title bar on Windows 10/11, Per-Monitor V2 DPI manifest, app icon.
- Engine untouched: `Services/*` was not modified. `AppEntry` gained a non-persisted `Detail` property used only for display.
- Not compiled or run in the authoring environment (no .NET SDK / Windows). Build and test on Windows.

## 0.6.0 — 2026-11-14
- Added edit and delete actions for custom catalog entries.
- Added installed-state check through WinGet list.
- Added retry for failed installation queue items.
- Added open-current-log action.
- Clarified install-success status semantics.
- Updated README and Claude design handoff.

## 0.5.0
- Added saved installation profiles.
- Added persistent folder settings.
- Added WinGet readiness/version check.
- Disabled unverified package IDs for Happ, ExitLag and GearUP Booster.


## v0.7.0 — WinGet bootstrap and live process output

- Added a first-run WinGet setup action. It downloads the stable App Installer bundle from Microsoft's `https://aka.ms/getwinget` endpoint, limits the download size, checks the Authenticode signature with Windows PowerShell, and only then opens the native MSIX installer. The user must confirm the installation in Windows.
- Added live stdout/stderr reporting during WinGet operations instead of waiting until the process exits. Output is also written to the application log.
- Serialized concurrent log writes to avoid collisions between stdout and stderr readers.
- Pinned installs and installed-state checks to the `winget` source to avoid ambiguous matches from other configured sources.
- Validated package IDs against a conservative allowlist both when adding custom entries and immediately before invoking WinGet.
- Clarified that successful WinGet exit is not the same as independent installed-state verification.
- Prevented the install queue and WinGet setup/check actions from running concurrently with the bulk installed-state scan.

## 0.7.2
- Исправлена ошибка CS0201 при запуске установщика App Installer: результат Process.Start теперь сохраняется в переменную и проверяется отдельным if.


## v0.8.0 — real install path tracking

- Added a configurable application install root and persisted it separately from folder templates.
- Passes WinGet `--location` per application when a root is configured, while explicitly treating the argument as best-effort because package installers may not support it.
- Detects actual install locations and versions from Windows uninstall registry entries (HKLM 64/32-bit and HKCU).
- Displays the detected path on application cards and distinguishes unknown paths from verified locations.
- Improved post-install verification and logging.

## 0.8.1
- Убрана перегруженная постоянно видимая панель кнопок: профили и дополнительные инструменты свёрнуты в две группы.
- Проверка наличия приложения выполняется даже после ненулевого кода завершения WinGet. Если приложение фактически найдено, очередь больше не помечает его как неудачную установку.
- Добавлены проверки стандартных путей исполняемых файлов Firefox и Chrome как резервный способ обнаружения.
- Фактический путь и предупреждение о несовпадении с выбранной папкой сохраняются отдельно.


## 0.8.2 — установка и проверка результата
- Убрано безусловное создание папок приложений и слепая передача `--location` для всех пакетов.
- Успех установки теперь требует подтверждения через реальную запись удаления/известный EXE-путь или `winget list`, даже если WinGet вернул код 0.
- Проверка `winget list` больше не привязана к источнику `winget`.
- Добавлено определение VLC по записям Windows и известным путям `vlc.exe`.
- Текст интерфейса уточняет, что выбранная папка не гарантирует фактическое место установки.

## v0.8.3
- Added a pre-install detection pass using Windows uninstall metadata, known executable locations, and `winget list`.
- Already-installed applications are now reported as `Уже установлена` and are not passed to `winget install`; this prevents WinGet from attempting an unintended upgrade and avoids treating `0x8A15002B` as a fresh-install result.
- The queue summary now separates newly installed apps from apps that were already present.
- When WinGet detects a package but Windows does not expose its path, the app is reported as already installed with an unknown path; no destination folder is created and no relocation is implied.


## v0.8.4 — selected install root is an actual install request
- Pass `--location` to WinGet whenever the user selected an install root.
- Add `--force` to avoid WinGet converting a requested install into a no-op upgrade when package registration is stale/mismatched.
- Do not short-circuit when an existing install is outside the requested root; attempt the requested install/reinstall.
- Verify executable presence for Firefox, VLC and Chrome instead of trusting a leftover registry record or empty directory.
- Fail the queue item when the actual install path is outside the selected root or cannot be verified.


## 0.8.5
- Treat selected install path as a parent directory and create a separate sanitized subfolder for each application.
- Add vendor-specific custom directory arguments for Firefox and VLC.
- Verify the final install path against the individual application's directory.
- Reject location mismatches even when WinGet returns the reboot-required exit code.
