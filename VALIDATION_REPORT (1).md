# Отчёт проверки исходников

- PASS XML: WinSetup.csproj
- PASS XML: App.xaml
- PASS XML: MainWindow.xaml
- PASS XML: AddAppWindow.xaml
- PASS XML: FolderSettingsWindow.xaml
- XAML handlers: 23 referenced; missing: none
- ZIP integrity: PASS
- ZIP entries: 21
- dotnet SDK: NOT AVAILABLE in this environment; no compile/runtime claim


## v0.8.3 update
- ZIP integrity checked for source and handoff archives.
- Static inspection confirms pre-install detection is called before `winget install`, and already-installed results are counted separately.
- .NET SDK is unavailable in the authoring environment; WPF compilation and Windows runtime behavior are not verified here.


## v0.8.4 update
- Static inspection confirms `--location` is passed when a target root is selected.
- Static inspection confirms `--force` is used to avoid WinGet's existing-package no-op path.
- Existing installs outside the selected root no longer short-circuit the install request.
- Firefox/VLC/Chrome known-app checks require the executable file.
- Post-install verification rejects an unverified or mismatched target path.
- No .NET SDK available here; compilation and Windows runtime behavior are not verified.


## v0.8.5 installation folder behavior
The selected directory is a parent/root. Each application must be installed into its own sanitized child folder named after the catalog entry. Current implementation uses per-app child paths, a Firefox `/InstallDirectoryPath` override, and a VLC WiX MSI `APPLICATIONFOLDER` override. Post-install verification must compare the actual location to that exact child folder. Preserve this behavior during UI refactoring; do not revert to one shared folder or claim generic support for installers that ignore custom paths.


## v0.8.5 static checks
- Added parent-root plus per-app child-directory contract.
- Added Firefox and VLC package-specific location switches.
- Added exact per-app path verification and mismatch rejection.
- XML/XAML and ZIP checks are performed for the delivered archive; no .NET compilation/runtime validation is available in this environment.
