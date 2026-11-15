# WinSetup — Claude UI/UX redesign brief

## Product purpose
WinSetup is a portable Windows post-install application. A user downloads one EXE after reinstalling Windows, opens it without running a setup wizard, selects several applications, chooses folder locations, and lets WinSetup download/install the selected software. It is a practical bulk installer, not an AI-app directory and not a Windows optimizer.

## Product principles
- Make the primary workflow obvious: browse apps → select apps → configure paths if needed → install queue.
- Prioritize clarity, speed, density and legibility over decorative effects.
- The app must feel like a polished native Windows desktop utility.
- Avoid huge empty areas, cramped navigation, generic repeated placeholder icons, excessive blur/glass, oversized cards and unnecessary animations.
- Keep a dark neutral palette with a restrained red accent. Do not make every element red.
- Use real app logos/icons where licensing and packaging allow; provide consistent monogram fallbacks.
- Support 1120×720 minimum window size and larger desktop sizes; layouts must resize gracefully.

## Core information architecture
1. **Программы** — searchable catalog, category navigation, app cards, selection states and install queue.
2. **Пути и папки** — choose target paths, use suggested folders, add/remove custom folders and create nested directories.
3. **Установка / очередь** — progress, current task, per-app state, errors, cancellation and completion summary. This can be a dedicated view or a clear panel/drawer, but must not hide the catalog workflow.
4. **Настройки / журнал** — basic preferences and access to the installation log. Do not invent settings that the code does not support.

## Initial app catalog
- Общение: Discord, Telegram
- Браузеры: Google Chrome, Mozilla Firefox
- Игры: Steam, Epic Games Launcher
- Сеть: Zapret Discord, tg-ws-proxy, Happ, ExitLag, GearUP Booster
- Утилиты: ShareX

Some package identifiers and custom installation flows are still being validated. The UI must not imply that every entry is fully installable if its status says it needs a dedicated setup.

## Main screen requirements
- Clear page title and short explanatory subtitle.
- Search field with visible focus and clear affordance.
- Category navigation with selected state and optional item counts only when backed by actual data.
- App cards with icon, name, category, short description, selection control and current status.
- Distinguish selected, hovered, installing, completed, failed and setup-required states.
- Sticky bottom install area showing selected count, progress/status, install action and cancel action while a queue is active.
- Avoid card heights that create excessive blank space. Use responsive grid sizing rather than hard-coded assumptions where practical.

## Folder screen requirements
- Clearly distinguish preset folder paths from user-created paths.
- Each path must show the full destination and allow editing/removal before creation.
- Explain that folder creation creates directories only; it does not move existing files.
- Provide clear validation for invalid paths and duplicate entries.

## Visual direction
- Dark graphite surfaces, subtle borders, high-contrast text, restrained red accent.
- Strong typographic hierarchy; compact section labels; consistent spacing scale.
- Rounded corners should be subtle and consistent, not applied to every nested element.
- Use native-feeling hover/focus/disabled states and keyboard navigation.
- Use animations sparingly and respect reduced-motion preferences where possible.
- No fake charts, fake ratings, fake download sizes, or invented metadata.

## Technical contract — do not break
- Keep the project on C# + WPF + .NET 10.
- Keep `MainWindow.xaml` and `MainWindow.xaml.cs` class names and event handler names unless you update all references consistently.
- Preserve these existing handlers: `SearchBox_TextChanged`, `Install_Click`, `CancelInstall_Click`, `Folders_Click`, `CreateFolders_Click`.
- Preserve named controls or update code-behind references consistently: `AppsList`, `CategoryPanel`, `SearchBox`, `SelectedCount`, `StatusText`, `DetailStatusText`, `InstallProgress`, `InstallButton`, `CancelButton`.
- Preserve `AppEntry` bindings: `Name`, `Category`, `Description`, `IsSelected`, `Status`.
- Do not move installation, download, filesystem or process logic into XAML/code-behind as part of a visual redesign. Keep service behavior intact.
- Do not replace working event handlers with mock interactions.
- Do not remove cancellation, status reporting, progress, logging or folder creation.
- Do not change Winget IDs, package sources, install arguments or security behavior as a design-only task.
- Keep accessibility basics: keyboard focus, readable contrast, tooltips for icon-only actions, and sensible tab order.

## Expected handoff from Claude
1. Inspect the full solution before editing.
2. Produce a short plan and list of files to change.
3. Redesign the UI without changing business logic.
4. Build the project and fix all XAML/C# errors introduced by the redesign.
5. Report exactly which files changed and confirm which workflows were preserved.
6. Do not claim runtime validation unless the app was actually launched and tested on Windows.

## New functionality added after the initial brief

### User-defined catalog entries
- Main catalog now has a `+ Добавить программу` action.
- `AddAppWindow` collects display name, category, description and exact WinGet Package ID.
- `AppCatalogStore` persists user entries to `%LOCALAPPDATA%\\WinSetup\\Catalog\\custom-apps.json`.
- Custom categories appear automatically in the sidebar.
- Preserve this workflow and its persistence when redesigning. Do not replace the add form with a visual-only mock.
- The package ID is passed to WinGet via `ProcessStartInfo.ArgumentList`; do not change this to shell string concatenation.


### v0.6 functionality — preserve during visual redesign

- Custom app cards expose `Изменить` and `Удалить`; these controls are visible only for user-defined entries.
- `AddAppWindow` supports both create and edit modes and preserves selection/status while editing.
- `Проверить программы` checks catalog entries with a package ID through `winget list --id ... --exact` and updates per-card status.
- `Повторить ошибки` reselects only entries currently marked `Ошибка установки` and starts the normal install queue.
- `Журнал` opens the active install log with the Windows default text editor.
- Preserve the exact meanings of statuses: `Установлена`/`Не установлена` come from a separate WinGet list check; `Установка завершена (WinGet)` means the install process returned a successful code, not that first-run setup or every app feature was validated.

### Installation behavior and folder contract (v0.8.5)
- The selected folder is a **parent/root** folder. Each app must get its own child directory based on a sanitized catalog name, e.g. `D:\Programs\Mozilla Firefox` and `D:\Programs\VLC`.
- Do not put all app files directly in the shared root. Do not create arbitrary extra nesting beyond the app-specific directory unless the vendor installer itself requires it.
- Pass the app-specific directory to WinGet as `--location`. Exact reviewed vendor strategies currently exist for `Mozilla.Firefox` (`/InstallDirectoryPath`) and `VideoLAN.VLC` (WiX MSI `APPLICATIONFOLDER`).
- If an app is already installed outside its requested app-specific directory, attempt installation/reinstallation into the requested directory; do not silently mark it already installed. Do not automatically uninstall or move the old copy.
- Verify the actual executable/install directory against the app-specific directory. If the installer ignores the path or the path cannot be verified, show a clear error and the actual/unknown path. Never claim success in the requested location without proof.
- Generic WinGet installers may not support custom locations. Do not claim universal support; add a reviewed per-package strategy when needed.
- Preserve queue totals for newly installed, already installed, failed and unsupported/skipped apps.
