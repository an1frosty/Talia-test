using System.Text.Json;
using WinSetup.Models;
using WinSetup.Services;
using WinSetup.UI;

int pass = 0, fail = 0;
void Check(string name, bool ok, string? info = null)
{
    if (ok) { pass++; Console.WriteLine($"PASS  {name}"); }
    else { fail++; Console.WriteLine($"FAIL  {name} {info}"); }
}

var home = Path.Combine(Path.GetTempPath(), "talia-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(home);
// NOTE: stores write under %LOCALAPPDATA%\WinSetup. On Windows set LOCALAPPDATA to a temp dir before running, e.g.
//   $env:LOCALAPPDATA = "$env:TEMP\talia-checks"; dotnet run --project Tests/Talia.LogicChecks
// On Linux set HOME and XDG_DATA_HOME to a temp dir (as done when these checks were first run).
var opts = new JsonSerializerOptions { WriteIndented = true };

// ---- SafeJsonFile ----
var f = Path.Combine(home, "t", "a.json");
await SafeJsonFile.SaveAsync(f, new List<string> { "one" }, opts);
var r1 = await SafeJsonFile.LoadAsync(f, opts, () => new List<string>());
Check("SafeJson roundtrip", r1.Value.SequenceEqual(["one"]) && r1.Warning is null);
await SafeJsonFile.SaveAsync(f, new List<string> { "two" }, opts);
Check("SafeJson keeps .bak of previous version", File.Exists(f + ".bak") && File.ReadAllText(f + ".bak").Contains("one"));
File.WriteAllText(f, "{ not json");
var r2 = await SafeJsonFile.LoadAsync(f, opts, () => new List<string>());
Check("Corrupt file: warning + corrupt copy + restored from .bak", r2.Warning is not null && r2.Value.SequenceEqual(["one"]) && Directory.GetFiles(Path.GetDirectoryName(f)!, "a.json.corrupt-*").Length == 1 && File.ReadAllText(f) == "{ not json", r2.Warning);
File.Delete(f + ".bak");
var r3 = await SafeJsonFile.LoadAsync(f, opts, () => new List<string>());
Check("Corrupt file without .bak: empty value, original NOT deleted, warning", r3.Value.Count == 0 && r3.Warning is not null && File.Exists(f) && !r3.WriteBlocked);
File.WriteAllText(f, "   ");
var r4 = await SafeJsonFile.LoadAsync(f, opts, () => new List<string>());
Check("Empty file treated as corrupt (not silently empty)", r4.Warning is not null);
Check("Missing file: no warning", (await SafeJsonFile.LoadAsync(Path.Combine(home, "nope.json"), opts, () => new List<string>())).Warning is null);

// ---- AppCatalogStore + AppEntry ----
var store = new AppCatalogStore();
Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
File.WriteAllText(store.FilePath, """
[ { "Name":"My App","Category":"Утилиты","Description":"d","WingetId":"Pub.MyApp","GitHubRepo":null,"IsCustom":true,
    "IsSelected":true,"Status":"Установлена","InstalledPath":"C:\\Old","InstalledVersion":"9.9" } ]
""");
var loaded = await store.LoadAsync();
Check("Legacy catalog file loads", loaded.Count == 1 && store.LastWarning is null && loaded[0].Name == "My App");
Check("Stale runtime state from old file is NOT restored", !loaded[0].IsSelected && loaded[0].Status == "Не установлено" && loaded[0].InstalledPath == "");
loaded[0].InstalledPath = @"D:\Programs\My App"; loaded[0].InstalledVersion = "1.2"; loaded[0].IsSelected = true; loaded[0].Status = "Установлена";
loaded[0].Name = "Renamed"; loaded[0].Description = "new"; loaded[0].WingetId = "Pub.Other";
Check("In-place edit preserves runtime state", loaded[0].InstalledPath == @"D:\Programs\My App" && loaded[0].InstalledVersion == "1.2" && loaded[0].IsSelected && loaded[0].Status == "Установлена");
await store.SaveAsync(loaded);
var written = File.ReadAllText(store.FilePath);
Check("Saved catalog has no runtime fields", !written.Contains("InstalledPath") && !written.Contains("Status") && !written.Contains("IsSelected") && written.Contains("Renamed"));
File.WriteAllText(store.FilePath, "garbage");
var store2 = new AppCatalogStore();
var l2 = await store2.LoadAsync();
Check("Corrupt catalog: warning shown, original kept, backup copy created", store2.LastWarning is not null && File.ReadAllText(store2.FilePath) == "garbage" && Directory.GetFiles(Path.GetDirectoryName(store2.FilePath)!, "*.corrupt-*").Length == 1, $"warn={store2.LastWarning}; files={string.Join(",", Directory.GetFiles(Path.GetDirectoryName(store2.FilePath)!).Select(Path.GetFileName))}; content={File.ReadAllText(store2.FilePath)}");
Check("Corrupt catalog: .bak of last good restored", l2.Count == 1 && l2[0].Name == "My App", $"count={l2.Count}");

// ---- Profile import / export ----
var us = new UserSettingsStore();
string P(string name, string text) { var p = Path.Combine(home, name); File.WriteAllText(p, text); return p; }
var good = P("good.json", """{"Format":"talia-profiles","Version":1,"Profiles":[{"Name":"Work","PackageIds":["Mozilla.Firefox","Discord.Discord","Mozilla.Firefox"],"AppNames":["Firefox"]}]}""");
var rg = await us.ReadProfilesFileAsync(good);
Check("Import valid export (dedupes ids)", rg.Profiles.Count == 1 && rg.Profiles[0].PackageIds.Count == 2 && rg.Problems.Count == 0);
var arr = P("arr.json", """[{"name":"A","packageIds":["Valve.Steam"]}]""");
Check("Import bare array, case-insensitive", (await us.ReadProfilesFileAsync(arr)).Profiles[0].PackageIds[0] == "Valve.Steam");
var one = P("one.json", """{"Name":"Solo","PackageIds":["ShareX.ShareX"]}""");
Check("Import single profile object", (await us.ReadProfilesFileAsync(one)).Profiles[0].Name == "Solo");
var bad = P("bad.json", """{"Profiles":[{"Name":"X","PackageIds":["a b; calc","Good.Id","../x"]},{"Name":"","PackageIds":["Good.Id"]},{"Name":"X","PackageIds":["Good.Id"]},{"Name":"Empty","PackageIds":[]}]}""");
var rb = await us.ReadProfilesFileAsync(bad);
Check("Invalid IDs / names / duplicates / empty are rejected with messages", rb.Profiles.Count == 1 && rb.Profiles[0].PackageIds.SequenceEqual(["Good.Id"]) && rb.Problems.Count >= 4, string.Join("|", rb.Problems));
async Task<bool> Throws(string path) { try { await us.ReadProfilesFileAsync(path); return false; } catch (InvalidDataException) { return true; } }
Check("Garbage file -> InvalidDataException", await Throws(P("g.json", "{ nope")));
Check("Wrong format name -> rejected", await Throws(P("w.json", """{"Format":"other","Profiles":[{"Name":"A","PackageIds":["A.B"]}]}""")));
Check("Future version -> rejected", await Throws(P("v.json", """{"Version":5,"Profiles":[{"Name":"A","PackageIds":["A.B"]}]}""")));
Check("Empty list -> rejected", await Throws(P("e.json", """{"Profiles":[]}""")));
Check("Scalar JSON -> rejected", await Throws(P("s.json", "42")));
File.WriteAllBytes(Path.Combine(home, "big.json"), new byte[3 * 1024 * 1024]);
Check("Oversized file -> rejected", await Throws(Path.Combine(home, "big.json")));
var outFile = Path.Combine(home, "export.json");
await us.ExportProfilesAsync(outFile, [new InstallProfile { Name = "RT", PackageIds = ["A.B"], AppNames = ["AB"] }]);
var rt = await us.ReadProfilesFileAsync(outFile);
Check("Export -> import roundtrip", rt.Profiles.Count == 1 && rt.Profiles[0].Name == "RT" && rt.Profiles[0].PackageIds[0] == "A.B");
await us.SaveProfilesAsync(rt.Profiles);
Check("Profiles persist and reload", (await new UserSettingsStore().LoadProfilesAsync()).Count == 1);

// ---- Package id validation ----
Check("IsValidPackageId accepts Publisher.Name", InstallerService.IsValidPackageId("Mozilla.Firefox") && InstallerService.IsValidPackageId("Microsoft.VisualStudioCode"));
Check("IsValidPackageId rejects injection / bad input", !InstallerService.IsValidPackageId("a b") && !InstallerService.IsValidPackageId("x;calc") && !InstallerService.IsValidPackageId("-x.y") && !InstallerService.IsValidPackageId("") && !InstallerService.IsValidPackageId("a\"b.c") && !InstallerService.IsValidPackageId(new string('a', 121)));

// ---- Status classification ----
void K(string s, StatusKind k) => Check($"Status '{s}' -> {k}", StatusClassifier.Classify(s) == k, StatusClassifier.Classify(s).ToString());
K("В очереди", StatusKind.Waiting); K("Подготовка...", StatusKind.Working); K("Установка...", StatusKind.Working); K("Проверка установки...", StatusKind.Working);
K("Установлена", StatusKind.Success); K("Уже установлена", StatusKind.Already); K("Установлена · нужна перезагрузка", StatusKind.Warning);
K("Установлена · другой путь", StatusKind.Warning); K("Установлена · предупреждение установщика", StatusKind.Warning);
K("Найдена в WinGet · путь не подтверждён", StatusKind.Warning); K("Требует отдельной настройки", StatusKind.Setup);
K("Ошибка установки", StatusKind.Error); K("Отменено", StatusKind.Cancelled); K("Не установлена", StatusKind.Neutral); K("Не удалось проверить", StatusKind.Warning);

Console.WriteLine($"\n{pass} passed, {fail} failed");
Directory.Delete(home, true);
return fail == 0 ? 0 : 1;
