# Talia security model

## Trust boundaries

- WinGet package manifests and their referenced installers are external inputs. A successful WinGet exit code is not proof that the vendor application is trustworthy or fully configured.
- Custom catalog entries are user-controlled data. Package IDs are validated before they are passed to WinGet.
- Process arguments are passed with `ProcessStartInfo.ArgumentList`; WinSetup does not build a shell command from package IDs or folder paths.
- GitHub project archives and scripts are not executed automatically. Zapret and tg-ws-proxy remain explicitly unsupported until a dedicated, reviewed installation recipe exists.

## WinGet bootstrap

- The bootstrap download uses Microsoft's stable `https://aka.ms/getwinget` endpoint over HTTPS.
- The download is capped at 150 MB and rejected if unexpectedly small.
- Before opening the package, WinSetup asks Windows PowerShell to validate its Authenticode signature and checks that the signer certificate subject starts with `CN=Microsoft Corporation` (a mere substring match is not accepted); no ExecutionPolicy override is used.
- WinSetup then opens the native MSIX installer. Windows displays the package installation UI and the user must confirm it. WinSetup does not silently provision packages or bypass Windows policy.

## Before a public release

1. Build and run on supported Windows 10/11 x64 images, including a clean image without WinGet.
2. Test valid and invalid Authenticode signatures using controlled test files.
3. Confirm the official bootstrap endpoint redirects to a Microsoft-published, Microsoft-signed App Installer bundle.
4. Verify every built-in package ID and publisher against the live WinGet source before enabling it.
5. Review installer behavior, elevation prompts, reboot codes and cancellation on each supported package.
6. Scan the published executable and test it in a disposable VM.

## Known limitations

The current bootstrap flow is user-assisted. Some Windows images may lack required App Installer dependencies, Store components, or policies needed for MSIX installation. Those cases must produce a clear failure and must not fall back to executing an unverified script.


## Installation path handling

A configured install root is a user-selected preference, not a guarantee. WinSetup does not blindly pass `--location` to generic EXE/MSI installers and does not create per-app placeholder directories. The actual install location is reported only when it can be verified from Windows uninstall metadata or a known executable path. WinSetup never silently moves an installed application after setup.


## Custom install locations (v0.8.4; superseded by the per-app directory contract below)

When a user chooses an install root, WinSetup passes it to WinGet through `--location` and uses `--force` to keep the install command in the install flow when package metadata reports an existing package. WinGet documents `--location` as conditional on installer support. WinSetup verifies the resulting executable/install path and reports failure when the installer does not honor the requested location. It does not silently move application files or automatically uninstall an existing copy.


## Installation directory contract (v0.8.5)

The user-selected directory is a parent folder. The application derives a sanitized child directory from the catalog display name and validates that it remains inside the selected parent. Known vendor-specific installer switches are used only for exact, reviewed package IDs (Mozilla.Firefox and VideoLAN.VLC). All other packages receive the generic WinGet location hint. The actual path is checked after installation; a mismatch is an error. WinSetup does not automatically uninstall an existing application or move its files.

## 1.0 additions

- Every external process (winget, PowerShell) runs with a timeout (2 min for queries and signature checks, 60 min for an installation); on timeout or cancellation the whole process tree is killed.
- "Installed" is reported only when an executable is evidenced on disk (DisplayIcon .exe, a .exe in the install folder, or the exact vendor executable for Firefox/VLC/Chrome). A registry entry or an existing folder alone is not enough; WinGet-only knowledge is shown as "Найдена в WinGet · путь не подтверждён".
- If a vendor installer ignores the requested folder, the real path is shown with a warning. Files are never moved and existing installations are never deleted.
- User data files are never overwritten blindly: corrupted JSON is copied to `*.corrupt-<time>`, the last good `*.bak` is tried, writes are atomic (`File.Replace`).
- Imported profile files are size-limited (2 MB), schema-validated, and every Package ID is validated before use. Import never installs anything by itself.
