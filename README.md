# Talia (formerly WinSetup) — portable Windows post-install tool

> **Talia 1.0.0** — audit, bug fixes, Installed section, profile import/export, improved queue. Engine behavior is preserved; see `CHANGELOG.md`, `VALIDATION_REPORT.md`, `TALIA_UI_NOTES.md`. **This release was not compiled in the authoring environment** — build on Windows with the .NET 10 SDK and run the checklist in `VALIDATION_REPORT.md`.

WPF / C# / .NET 10 project. Open `WinSetup.csproj` in Visual Studio with the .NET 10 SDK installed, restore, then run with F5.

## Publish a single-file portable EXE

Run in PowerShell from this folder:

```powershell
dotnet publish .\WinSetup.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `bin\Release\net10.0-windows\win-x64\publish\WinSetup.exe`.

## Current functionality

- Catalog of the 12 initially requested applications, category navigation and search.
- Multi-select queue with per-application status.
- WinGet installation for entries with configured package IDs.
- Cancel button: stops the current WinGet process tree and prevents the remaining queue from starting.
- Installation log in `%LOCALAPPDATA%\WinSetup\Logs`.
- Temporary working directory cleanup.
- Preset/custom folder path workflow and folder creation; folder selections persist between launches.
- Installation profiles: save current selection, load it later, and delete profiles.
- WinGet readiness/version check directly in the UI.
- First-run WinGet bootstrap: downloads the stable App Installer bundle from Microsoft `aka.ms/getwinget`, checks its Authenticode signature and opens the native MSIX installer for user confirmation.
- Live process output and serialized log writes during installation.
- WinGet executable lookup through PATH and the WindowsApps alias.

## Important limitations — read before testing

- WinGet bootstrap is assisted, not fully unattended: WinSetup downloads the official App Installer bundle, verifies its Microsoft Authenticode signature and opens the native Windows installer. The user must confirm the MSIX installation. Afterward, return to WinSetup and click «Проверить WinGet». Some Windows images may still require Store/App Installer dependencies or policy changes.
- Zapret Discord and tg-ws-proxy are intentionally marked as requiring dedicated setup. Their archives/scripts are not executed blindly. Their project-specific install/configuration workflow is a separate task.
- Happ, ExitLag and GearUP are intentionally disabled in the built-in catalog until their exact package IDs and publishers are verified. Use `winget search <name>` and confirm the publisher/source before adding them as custom apps.
- Silent installer behavior differs by vendor. WinGet may still request elevation or show UAC; user consent is controlled by Windows.
- WinGet can return success while a vendor installer schedules a reboot or requires additional first-run setup. The app reports the WinGet process result, not a guarantee that every application is fully configured.
- This code has not been compiled or run in this environment because the .NET SDK is unavailable here. Compile in Visual Studio and test in a Windows VM before using after a real reinstall.

## Log location

`%LOCALAPPDATA%\WinSetup\Logs\install-*.log`

## v0.4 additions — personal catalog

- `+ Добавить программу` opens a form for a user-defined app.
- Custom entries require a WinGet package ID; the ID is passed as a separate process argument (not through a shell command string).
- Custom entries are stored in `%LOCALAPPDATA%\WinSetup\Catalog\custom-apps.json` and loaded on the next launch.
- User categories are added to the category navigation automatically.
- The form validates required fields and prevents duplicate app names.

To find a package ID, use `winget search <name>` in PowerShell and verify the publisher/source before adding it. Do not add an ID solely because its display name looks familiar.


## v0.5 additions — profiles and persistent settings

- Installation profiles are stored in `%LOCALAPPDATA%\WinSetup\Profiles\profiles.json`.
- Folder selections are stored in `%LOCALAPPDATA%\WinSetup\folders.json`.
- The toolbar includes a WinGet readiness/version check.
- The unverified package IDs for Happ, ExitLag and GearUP are blank by default to prevent accidental installation of a similarly named package.

## Portable build

Use `Publish-Portable.ps1` from the project directory. It publishes a self-contained, single-file Windows x64 executable. The .NET SDK is still required on the build machine, but not on the target PC.


## v0.7 additions — WinGet bootstrap and process diagnostics

- The «Установить WinGet» action uses Microsoft's stable `https://aka.ms/getwinget` endpoint. It enforces a 150 MB download limit, rejects unexpectedly small downloads, validates the Authenticode signature and signer before opening the package, and never executes downloaded scripts.
- The actual MSIX installation remains a native Windows operation requiring user confirmation. WinSetup does not claim WinGet is ready until the user returns and the version check succeeds.
- WinGet stdout and stderr are surfaced while installation runs and are written to the log. Concurrent log writes are serialized.
- Install/list commands explicitly use the `winget` source. Package IDs are restricted to letters, digits, dots, underscores and hyphens before they reach WinGet.

## v0.6 additions — catalog management and install verification

- Custom catalog entries can now be edited and deleted directly from their cards.
- Added a bulk installed-state check using `winget list --id <id> --exact`. Results are shown per app as `Установлена`, `Не установлена` or `Не удалось проверить`.
- Added a retry action that reselects entries whose previous install status is `Ошибка установки` and starts a new queue.
- Added a one-click action to open the current WinSetup installation log.
- Successful WinGet process completion is labeled explicitly as `Установка завершена (WinGet)`; use the installed-state check for a separate presence check.
- Updated version metadata to 0.6.0.

## v0.7 smoke-test checklist (Windows)

1. On a Windows test machine without WinGet, click «Установить WinGet». Confirm that the downloaded package is rejected if its signature is invalid; with a valid package, confirm the native App Installer window opens. Complete installation and then verify the version with «Проверить WinGet».
2. Add a custom app with a verified WinGet ID, close WinSetup, reopen it and confirm the entry persists.
3. Edit its name/category/description/ID and confirm the changes persist after restart.
4. Delete the custom app and confirm it is absent after restart.
5. Click `Проверить программы`; confirm each supported entry receives an installed/not-installed state, or a clear check error if WinGet is unavailable.
6. Force one test installation to fail (use a deliberately invalid package ID only in a disposable test custom entry), then use `Повторить ошибки` and confirm it retries only failed entries.
7. Click `Журнал` and confirm the log opens in the default text editor.
8. Re-test profile save/load/delete, folder save/create, queue cancellation and app selection.

The v0.7 source was structurally checked (XAML XML parsing, handler presence and archive integrity). It was not compiled or launched in this environment because the .NET SDK and Windows desktop runtime are unavailable here. Run the checklist on Windows before relying on the build for a clean reinstall.


## v0.8.0 — installation location verification

- Added a separate configurable root folder for application installs (for example `D:\Programs`). The selected root is persisted in `%LOCALAPPDATA%\WinSetup\install-root.txt`.
- A configured program folder is stored as a preference only and is not forced on generic WinGet EXE/MSI installers. WinSetup does not pass `--location` blindly to generic EXE/MSI packages and does not create per-app placeholder folders. The actual path is determined after installation from Windows uninstall metadata or known executable locations.
- After an install, WinSetup checks standard Windows uninstall registry entries in 64-bit, 32-bit and current-user registry views and displays the detected install directory and version when available.
- Installed-app checks now prefer the registry path and fall back to `winget list`; if WinGet detects a package but Windows does not expose its install directory, the UI reports that the path is unknown instead of inventing one.
- The actual path is shown on each application card.

### v0.8 verification notes

The source archive is structurally checked (ZIP integrity, XAML XML parsing, event-handler references, and basic C# source checks). This environment does not contain the .NET SDK or Windows, so a real WPF compilation and runtime test are not claimed. Install Firefox and VLC with a configured program folder. Confirm that no empty per-app folders are created, the apps install through WinGet, and the app cards show the actual detected paths. The configured folder must not be reported as the actual install location unless verified.


## v0.8.1 — cleaner toolbar and more reliable install result checks

- Replaced the always-visible row of many action buttons with two collapsed groups: `Профили установки` and `Инструменты и настройки`. The main screen now keeps search, add-app and install actions visually primary.
- Fixed install-result classification: after WinGet returns a non-zero exit code, WinSetup now checks the Windows uninstall registry and known executable locations before marking the operation as failed. This handles vendor installers that install the application but report a non-zero result back to WinGet.
- If the package is present but WinGet returned a warning/error code, the app is reported as installed with a warning; if it is present outside the requested folder, the actual path is shown and the status says `Установлена · другой путь`.
- Added Firefox and Chrome default-location fallbacks, including executable file-version lookup.
- If neither registry nor WinGet can confirm the package after a non-zero exit code, the operation remains a failure; no false success is reported.

### v0.8.1 test checklist

1. Open `Профили установки` and confirm save/load/delete controls appear only when expanded.
2. Open `Инструменты и настройки` and confirm maintenance actions are grouped there.
3. Install Firefox with a configured `D:\Programs` root. If Firefox chooses its default C: location, the card must show `Установлена · другой путь` and the actual path.
4. If WinGet returns a non-zero code after Firefox is installed, the queue must not classify it as a failed installation when the executable/registry confirms presence.
5. Use `Проверить программы` to confirm the detected state remains consistent after restarting WinSetup.

This source update was structurally checked only; compile and runtime testing still need to be done on Windows with the .NET 10 SDK.


## v0.8.2 — installation verification and folder handling fix

- Removed unconditional per-app directory creation and blind `winget --location` arguments. Ordinary EXE/MSI installers choose their own supported location; WinSetup now reports the actual path when it can verify it.
- A successful WinGet exit code is no longer sufficient to mark an app installed. WinSetup checks uninstall registry / known executable locations and then `winget list`; if neither confirms the package, the queue item is an error.
- Post-install `winget list` no longer forces the package source to `winget`, so installed package detection can work when the registered source differs.
- Added VLC display-name aliases and known executable locations to improve path detection, alongside Firefox and Chrome.
- Clarified in the UI that the selected folder is not guaranteed to be used by every installer.

### v0.8.2 verification notes

ZIP integrity, XAML XML parsing, handler references, and targeted source invariants were checked. This environment does not contain the .NET SDK or Windows, so this release has **not** been compiled or runtime-tested here. Run `Publish-Portable.ps1` on Windows with the .NET 10 SDK and complete the regression checklist before relying on the EXE.

### v0.8.3 — already-installed package handling
- Before invoking `winget install`, WinSetup now checks uninstall registry entries, known executable locations, and `winget list`.
- If the package already exists, WinSetup skips installation/upgrade and reports `Уже установлена`; the queue summary counts it separately from newly installed apps.
- WinGet's `0x8A15002B` no-upgrade response is no longer reached for packages detected before installation. An existing app is never claimed to have been moved to the selected root.


## v0.8.4 — honor the selected installation folder

- The selected install root is now passed to WinGet using its documented `--location` option. WinSetup does not create per-application placeholder folders.
- `--force` keeps WinGet in the install flow when its package registration says the app is already installed, including stale/mismatched registrations. This does not bypass installer hash/security checks.
- If a verified existing installation is already inside the selected root, it is reported as already installed. If it is outside the selected root, WinSetup attempts the requested install/reinstall instead of silently skipping it.
- Firefox, VLC and Chrome presence checks require their actual executable files; a leftover uninstall registry entry or empty directory is not sufficient.
- After installation, WinSetup verifies the actual path. If the vendor installer ignores `--location` or the path cannot be verified, the queue reports an error instead of claiming that the selected folder was used.
- Important limitation: Microsoft documents `--location` as supported only when the selected package installer supports custom paths. Some installers may ignore it; WinSetup cannot safely force every vendor installer to relocate an existing app.

### v0.8.4 test checklist

1. Select an empty target folder and install Firefox and VLC. Check the log for `--location` and `--force` behavior.
2. If either app is already installed outside the target folder, confirm WinSetup does not stop at `Уже установлена`; it attempts installation into the requested root.
3. Confirm the app card reports the actual executable/install path. If the installer ignores the target, the queue must report an error and show the actual path.
4. Remove an app and leave only an empty folder/old uninstall registration in a disposable test environment; WinSetup must not call it installed unless the expected executable exists.

This source update was structurally checked only. It has not been compiled or runtime-tested on Windows in this environment because the .NET SDK is unavailable.


## v0.8.5 — separate install directory per application

- The folder selected by the user is now the **parent/root** folder. WinSetup creates one sanitized child folder per app using its catalog name (for example `D:\Programs\Mozilla Firefox` and `D:\Programs\VLC`).
- The per-app folder is passed to WinGet as `--location`; existing applications outside that exact app folder trigger a reinstall attempt rather than being treated as already installed.
- Firefox uses Mozilla Full Installer's documented `/InstallDirectoryPath` argument.
- VLC uses the WiX MSI `APPLICATIONFOLDER` directory property because the generic WinGet `--location` argument was ignored by the MSI in the user's test.
- Post-install verification compares the detected executable/install directory against the **individual app folder**, not merely the selected parent folder. A mismatch is an error even when the installer requests a reboot.
- This is not a universal guarantee for every WinGet package: installers that do not expose a custom directory option may still ignore it. WinSetup reports that mismatch instead of silently claiming success.

### v0.8.5 test checklist

1. Select an empty root such as `D:\Programs`.
2. Install Firefox and VLC. Expected destinations: `D:\Programs\Mozilla Firefox` and `D:\Programs\VLC`.
3. Confirm each app's actual executable and uninstall registry path are inside its own folder.
4. Confirm a package that ignores custom locations is reported as an error with both requested and actual paths in the log.
5. Test an existing copy outside the selected root; it must not be reported as already installed in the target folder.

This release has not been compiled or runtime-tested in this environment because the .NET SDK and Windows are unavailable.
