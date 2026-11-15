# Talia UI notes (v1.0.0)

## Design system (`Themes/Talia.xaml`)
- Palette: Obsidian #0B0B10 base; Talia Pink #E99BC5 primary accent; Chrome Silver #C8CDD8 borders/neutral; Deep Red #A83250 errors only; Icy Blue #9BAEDC and Warm Skin #D9A99B as small secondary accents. Neutrals: #131319 / #191920 / #202027, border #303039, text #F2EFF3 / #A5A1AA / #77737E.
- One derived tint: `#E57A93` (lightened Deep Red) is used for error *text* only, because #A83250 is too dark to read on the dark surfaces. Deep Red itself is used for borders and dots.
- Radii: 6 (small) / 8 (controls) / 12 (cards). Type scale: 24 page, 15 section, 14 card, 13 body, 12 small, 11 caption (Segoe UI Variable Text, falls back to Segoe UI).
- Styles: `PrimaryButton`, `SecondaryButton` (implicit Button), `DangerButton`, `GhostButton`, `ChipButton(Active)`, `NavItem`, TextBox (with hint via `Tag`), CheckBox, ListBox, ScrollBar, ProgressBar, ToolTip, `Card`, text styles.

## Structure
- `MainWindow.xaml(.cs)`: sidebar + five views (Home, Programs, Queue, Profiles, Settings) + persistent footer (status, progress, Cancel, Install). All original handler and control names are preserved.
- `UI/UiHelpers.cs`: status classifier/converters (display only), dark title bar, work-area clamping.
- `TaliaDialog.xaml(.cs)`: themed message/prompt dialog with the same call shape as `MessageBox.Show`.
- Services: not touched.

## Responsive behavior
- Window minimum 880x560 (reduced automatically on small screens); initial size is capped to 94 % of the work area. Catalog cards: 260 px minimum, 1-5 columns.
- Sidebar is 208 px and collapses to a 68 px icon rail below 960 px width.
- Catalog columns are computed from available width; page content is capped at 1560 px and centered on large/ultrawide monitors.
- No global Viewbox, no horizontal scrolling; vertical ScrollViewers only on the catalog list and on form-like pages.
- Per-Monitor V2 DPI manifest added.

## Navigation scope
Only sections the project actually supports are present. The Installed page (registry scan) was added in 1.0. An Updates page was not added: WinGet offers no reliable machine-readable upgrade list.

## Known limitations
- Not compiled or run here. Expect to fix small XAML/C# issues on first build.
- The engine reports one "installing" phase; a separate "downloading" state is not exposed by the existing logic, so it is shown as Installing (live WinGet output appears in the footer).
- UI language stays Russian to match the existing status texts; brand name and tagline are English.
- Title bar colouring uses DWM attributes (full effect on Windows 11; dark mode only on late Windows 10).
- Hover transitions are 120 ms opacity fades; no other animations.

## Windows test checklist
1. Build and launch; check Home, Programs, Queue, Profiles, Settings.
2. Resize from 880x560 to maximized on 100/125/150/200 % scaling; confirm no clipped text or overlap, sidebar collapses, catalog columns change.
3. Select apps (checkbox and card click), search, switch categories, add/edit/delete a custom app.
4. Run a real install (Firefox, VLC): queue states, error detail text, cancel, retry, log.
5. Profiles save/load/delete; folder settings window; WinGet check/bootstrap; installed check.
