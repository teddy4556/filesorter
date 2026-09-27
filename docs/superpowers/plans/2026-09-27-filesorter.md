# FileSorter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows file classifier (.NET 8 + WPF) that organizes files into folders by rules, with drag-drop and "Send To" entry points, hot-reloadable YAML rules, install/uninstall registry entry for auto-start, and a minimal path-edit GUI.

**Architecture:** Single .NET 8 WPF app. Core = `RuleEngine` (parses YAML, matches files) + `ClassifierService` (runs file ops). UI = tray icon + small floating disk + a path-edit window. Watchdog uses `FileSystemWatcher`. Shell integration via `SendTo` shortcut.

**Tech Stack:** .NET 8 SDK, WPF, C# 12, YamlDotNet (NuGet), xUnit (tests), `Microsoft.Win32.OpenFolderDialog` (Win10+).

**Spec:** `/opt/data/projects/filesorter/docs/design.md`

**Working dir (NAS):** `/opt/data/projects/filesorter/`
**Working dir (Windows):** `D:\hermes-工作目录\filesorter\`
**Sync:** use `windows-mcp` FileSystem to push code from NAS → Windows. All `dotnet build/publish` runs on Windows.

---

## Conventions

- **TDD order**: write failing test → run → write impl → run → commit.
- **Commit format**: `M<N>: <verb> <thing>` (e.g. `M2: scaffold project + test infra`).
- **File paths**: relative to project root unless absolute.
- **YAML in tests**: small inline strings — no fixtures needed.
- **Windows path quoting**: always use verbatim strings `@"..."`.

---

## M2: Project Scaffold

**Goal:** Create the .NET project structure, add YamlDotNet + xUnit, verify `dotnet build` works.

**Files:**
- Create: `src/FileSorter/FileSorter.csproj`
- Create: `src/FileSorter/Program.cs` (minimal entry)
- Create: `src/FileSorter/Core/Models/RulesFile.cs` (placeholder)
- Create: `tests/FileSorter.Tests/FileSorter.Tests.csproj`
- Create: `tests/FileSorter.Tests/SanityTest.cs`
- Create: `FileSorter.sln`

- [ ] **Step 1: Create the .sln and 2 csproj files**

`src/FileSorter/FileSorter.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <AssemblyName>filesorter</AssemblyName>
    <RootNamespace>FileSorter</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="YamlDotNet" Version="15.1.6" />
  </ItemGroup>
</Project>
```

`tests/FileSorter.Tests/FileSorter.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <ProjectReference Include="..\..\src\FileSorter\FileSorter.csproj" />
  </ItemGroup>
</Project>
```

`FileSorter.sln`:

```
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "FileSorter", "src\FileSorter\FileSorter.csproj", "{11111111-1111-1111-1111-111111111111}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "FileSorter.Tests", "tests\FileSorter.Tests\FileSorter.Tests.csproj", "{22222222-2222-2222-2222-222222222222}"
EndProject
Global
  GlobalSection(SolutionConfigurationPlatforms) = preSolution
    Debug|Any CPU = Debug|Any CPU
    Release|Any CPU = Release|Any CPU
  EndGlobalSection
  GlobalSection(ProjectConfigurationPlatforms) = postSolution
    {11111111-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
    {11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Debug|Any CPU
    {11111111-1111-1111-1111-111111111111}.Release|Any CPU.ActiveCfg = Release|Any CPU
    {11111111-1111-1111-1111-111111111111}.Release|Any CPU.Build.0 = Release|Any CPU
    {22222222-2222-2222-2222-222222222222}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
    {22222222-2222-2222-2222-222222222222}.Debug|Any CPU.Build.0 = Debug|Any CPU
    {22222222-2222-2222-2222-222222222222}.Release|Any CPU.ActiveCfg = Release|Any CPU
    {22222222-2222-2222-2222-222222222222}.Release|Any CPU.Build.0 = Release|Any CPU
  EndGlobalSection
EndGlobal
```

- [ ] **Step 2: Write the minimal `Program.cs`**

`src/FileSorter/Program.cs`:

```csharp
using System;

namespace FileSorter;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Console.WriteLine("filesorter scaffold OK");
        return 0;
    }
}
```

- [ ] **Step 3: Write the placeholder + sanity test**

`src/FileSorter/Core/Models/RulesFile.cs`:

```csharp
namespace FileSorter.Core.Models;

public class RulesFile
{
    public int Version { get; set; }
}
```

`tests/FileSorter.Tests/SanityTest.cs`:

```csharp
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class SanityTest
{
    [Fact]
    public void RulesFile_DefaultVersion_IsZero()
    {
        var r = new RulesFile();
        Assert.Equal(0, r.Version);
    }

    [Fact]
    public void Scaffold_Loads()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 4: Push to Windows + build**

On NAS terminal:

```bash
# sync the project files into Windows (windows-mcp FileSystem)
# see Appendix A for the sync helper script
python3 sync_to_windows.py
```

On Windows (via PowerShell tool call):

```powershell
cd D:\hermes-工作目录\filesorter
dotnet restore
dotnet build -c Debug
dotnet test -c Debug
```

Expected output:

```
Build succeeded.
  FileSorter -> ...\FileSorter.dll
Test Run Successful.
Total tests: 2
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "M2: scaffold project + test infra (dotnet build + dotnet test pass)"
```

---

## M3: Rule Models + YAML Parser

**Goal:** Parse the YAML rules file into strongly-typed models. Support all 5 rule types (extension, filename_keyword, combined, path_template, default).

**Files:**
- Create: `src/FileSorter/Core/Models/Rule.cs`
- Create: `src/FileSorter/Core/Models/RulesConfig.cs`
- Create: `src/FileSorter/Core/RuleEngine.cs`
- Create: `tests/FileSorter.Tests/RuleEngineLoadTests.cs`

- [ ] **Step 1: Write failing tests**

`tests/FileSorter.Tests/RuleEngineLoadTests.cs`:

```csharp
using FileSorter.Core;
using Xunit;

namespace FileSorter.Tests;

public class RuleEngineLoadTests
{
    [Fact]
    public void Load_ValidYaml_ReturnsConfig()
    {
        var yaml = """
        version: 1
        default_action: move
        conflict_strategy: rename
        log_level: info
        destinations:
          images: D:\cat\img
          inbox: D:\cat\inbox
        rules:
          - name: pics
            type: extension
            patterns: [jpg, png]
            destination: images
          - name: catch-all
            type: default
            destination: inbox
        """;
        var cfg = RuleEngine.LoadFromString(yaml);
        Assert.Equal(1, cfg.Version);
        Assert.Equal("move", cfg.DefaultAction);
        Assert.Equal(2, cfg.Destinations.Count);
        Assert.Equal("D:\\cat\\img", cfg.Destinations["images"]);
        Assert.Equal(2, cfg.Rules.Count);
    }

    [Fact]
    public void Load_InvalidYaml_Throws()
    {
        var yaml = "this: is: not: valid: yaml:";
        Assert.ThrowsAny<Exception>(() => RuleEngine.LoadFromString(yaml));
    }

    [Fact]
    public void Load_PathTemplate_PreservesFields()
    {
        var yaml = """
        version: 1
        default_action: move
        conflict_strategy: rename
        log_level: info
        destinations:
          images: D:\cat\img
        rules:
          - name: sm-pic
            type: path_template
            extensions: [jpg, png]
            filename_pattern: '^([a-z]+)_\(@([^)]+)\)_'
            path: '{destinations.images}\{1}\{2}'
        """;
        var cfg = RuleEngine.LoadFromString(yaml);
        var r = cfg.Rules[0];
        Assert.Equal("path_template", r.Type);
        Assert.Equal(new[] { "jpg", "png" }, r.Extensions);
        Assert.Equal(@"^([a-z]+)_\(@([^)]+)\)_", r.FilenamePattern);
        Assert.Equal(@"{destinations.images}\{1}\{2}", r.Path);
    }
}
```

- [ ] **Step 2: Run tests, expect 3 failures**

```powershell
cd D:\hermes-工作目录\filesorter
dotnet test --filter "FullyQualifiedName~RuleEngineLoadTests"
```

Expected: 3 failures (RuleEngine / Rule / RulesConfig types not defined).

- [ ] **Step 3: Implement models + parser**

`src/FileSorter/Core/Models/Rule.cs`:

```csharp
namespace FileSorter.Core.Models;

public class Rule
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // extension | filename_keyword | combined | path_template | default
    public List<string>? Patterns { get; set; }
    public List<string>? Extension { get; set; }
    public List<string>? FilenameKeyword { get; set; }
    public List<string>? Extensions { get; set; }
    public string? FilenamePattern { get; set; }
    public string? Path { get; set; }
    public string? Destination { get; set; }
    public string? DestinationAlias { get; set; }
    public bool? CaseSensitive { get; set; }
}
```

`src/FileSorter/Core/Models/RulesConfig.cs`:

```csharp
namespace FileSorter.Core.Models;

public class RulesConfig
{
    public int Version { get; set; }
    public string DefaultAction { get; set; } = "move";
    public string ConflictStrategy { get; set; } = "rename";
    public string LogLevel { get; set; } = "info";
    public Dictionary<string, string> Destinations { get; set; } = new();
    public List<Rule> Rules { get; set; } = new();
}
```

`src/FileSorter/Core/RuleEngine.cs`:

```csharp
using FileSorter.Core.Models;
using YamlDotNet.Serialization;

namespace FileSorter.Core;

public static class RuleEngine
{
    private static readonly IDeserializer _yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public static RulesConfig LoadFromString(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            throw new ArgumentException("YAML is empty", nameof(yaml));
        var cfg = _yaml.Deserialize<RulesConfig>(yaml)
            ?? throw new InvalidDataException("YAML deserialized to null");
        if (cfg.Version == 0) cfg.Version = 1;
        return cfg;
    }

    public static RulesConfig LoadFromFile(string path)
    {
        return LoadFromString(File.ReadAllText(path));
    }
}
```

- [ ] **Step 4: Run tests, expect 3 passes**

```powershell
dotnet test --filter "FullyQualifiedName~RuleEngineLoadTests"
```

Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "M3: rule models + YAML parser (3 unit tests)"
```

---

## M4: Rule Matching Engine

**Goal:** Given a file path + rules, return the first matching rule + the computed destination path.

**Files:**
- Create: `src/FileSorter/Core/RuleEngine.cs` (extend)
- Create: `src/FileSorter/Core/MatchResult.cs`
- Create: `tests/FileSorter.Tests/RuleMatchTests.cs`

- [ ] **Step 1: Write failing tests**

`tests/FileSorter.Tests/RuleMatchTests.cs`:

```csharp
using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class RuleMatchTests
{
    private static RulesConfig Cfg() => RuleEngine.LoadFromString("""
        version: 1
        default_action: move
        conflict_strategy: rename
        log_level: info
        destinations:
          images: D:\cat\img
          documents: D:\cat\doc
          inbox: D:\cat\inbox
        rules:
          - name: pics
            type: extension
            patterns: [jpg, jpeg, png]
            destination: images
          - name: invoice
            type: filename_keyword
            patterns: ["发票", "invoice"]
            extensions: [pdf, jpg]
            destination: documents
          - name: screenshot
            type: combined
            extension: [png]
            filename_keyword: ["screen"]
            destination: images
          - name: twitter-pic
            type: path_template
            extensions: [jpg, png]
            filename_pattern: '^([a-z]+)_\(@([^)]+)\)_'
            path: '{destinations.images}\{1}\{2}'
          - name: catch-all
            type: default
            destination: inbox
        """);

    [Fact]
    public void Match_ExtensionRule_JpgGoesToImages()
    {
        var r = RuleEngine.Match(Cfg(), @"C:\in\photo.jpg");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\img", r!.DestinationPath);
    }

    [Fact]
    public void Match_FilenameKeyword_InvoicePdfGoesToDocuments()
    {
        var r = RuleEngine.Match(Cfg(), @"C:\in\发票_2025.pdf");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\doc", r!.DestinationPath);
    }

    [Fact]
    public void Match_Combined_PngScreenGoesToImages()
    {
        var r = RuleEngine.Match(Cfg(), @"C:\in\screen.png");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\img", r!.DestinationPath);
    }

    [Fact]
    public void Match_PathTemplate_TwitterPictureGoesToNestedFolder()
    {
        var r = RuleEngine.Match(Cfg(), @"C:\in\twitter_(@hahaoy8)_肉丝儿_20260730.jpg");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\img\twitter\@hahaoy8", r!.DestinationPath);
    }

    [Fact]
    public void Match_NoRuleHits_FallsBackToDefault()
    {
        var r = RuleEngine.Match(Cfg(), @"C:\in\notes.txt");
        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\inbox", r!.DestinationPath);
    }
}
```

- [ ] **Step 2: Run tests, expect 5 failures**

```powershell
dotnet test --filter "FullyQualifiedName~RuleMatchTests"
```

- [ ] **Step 3: Implement Match + MatchResult**

`src/FileSorter/Core/MatchResult.cs`:

```csharp
namespace FileSorter.Core;

public record MatchResult(string RuleName, string DestinationPath);
```

Extend `src/FileSorter/Core/RuleEngine.cs` (add at end of class):

```csharp
    public static MatchResult? Match(RulesConfig cfg, string filePath)
    {
        var name = Path.GetFileName(filePath);
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(filePath);

        foreach (var rule in cfg.Rules)
        {
            switch (rule.Type)
            {
                case "extension":
                    if (rule.Patterns != null && rule.Patterns.Any(p =>
                        string.Equals(p, ext, StringComparison.OrdinalIgnoreCase)))
                        return DestResult(cfg, rule, filePath, rule.Destination!);
                    break;

                case "filename_keyword":
                    if (rule.Extensions != null && !rule.Extensions.Contains(ext)) break;
                    if (rule.Patterns != null && rule.Patterns.Any(p =>
                        name.Contains(p, rule.CaseSensitive == true
                            ? StringComparison.Ordinal
                            : StringComparison.OrdinalIgnoreCase)))
                        return DestResult(cfg, rule, filePath, rule.Destination!);
                    break;

                case "combined":
                    if (rule.Extension != null && !rule.Extension.Contains(ext)) break;
                    if (rule.FilenameKeyword != null && rule.FilenameKeyword.Any(p =>
                        name.Contains(p, rule.CaseSensitive == true
                            ? StringComparison.Ordinal
                            : StringComparison.OrdinalIgnoreCase)))
                        return DestResult(cfg, rule, filePath, rule.Destination!);
                    break;

                case "path_template":
                    if (rule.Extensions != null && !rule.Extensions.Contains(ext)) break;
                    if (rule.FilenamePattern != null && System.Text.RegularExpressions.Regex.IsMatch(name, rule.FilenamePattern))
                        return TemplateResult(cfg, rule, filePath, name);
                    break;

                case "default":
                    return DestResult(cfg, rule, filePath, rule.Destination!);
            }
        }
        return null;
    }

    private static MatchResult DestResult(RulesConfig cfg, Rule rule, string filePath, string alias)
    {
        var basePath = ResolveAlias(cfg, alias);
        return new MatchResult(rule.Name, basePath);
    }

    private static MatchResult TemplateResult(RulesConfig cfg, Rule rule, string filePath, string fileName)
    {
        var tpl = rule.Path ?? ResolveAlias(cfg, rule.DestinationAlias ?? "");
        var m = System.Text.RegularExpressions.Regex.Match(fileName, rule.FilenamePattern!);
        var groups = m.Groups;

        string result = tpl;
        // Replace {N} with capture groups (1-based)
        for (int i = 1; i < groups.Count; i++)
            result = result.Replace("{" + i + "}", groups[i].Value);
        // Replace {destinations.<name>}
        foreach (var kv in cfg.Destinations)
            result = result.Replace("{destinations." + kv.Key + "}", kv.Value);
        // Date tokens
        var lastWrite = File.GetLastWriteTime(filePath);
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\{date:([^}]+)\}", mm =>
        {
            try { return lastWrite.ToString(mm.Groups[1].Value); }
            catch { return ""; }
        });
        return new MatchResult(rule.Name, result);
    }

    private static string ResolveAlias(RulesConfig cfg, string alias)
    {
        if (cfg.Destinations.TryGetValue(alias, out var v)) return v;
        // Alias not found → return as-is (might be absolute path already)
        return alias;
    }
```

- [ ] **Step 4: Run tests, expect 5 passes**

```powershell
dotnet test --filter "FullyQualifiedName~RuleMatchTests"
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "M4: rule matching engine (5 match tests, supports path_template)"
```

---

## M5: Classifier Service (file ops)

**Goal:** Take matched result + source path, perform `move` / `copy` with conflict handling.

**Files:**
- Create: `src/FileSorter/Core/ClassifierService.cs`
- Create: `tests/FileSorter.Tests/ClassifierServiceTests.cs`

- [ ] **Step 1: Write failing tests**

`tests/FileSorter.Tests/ClassifierServiceTests.cs`:

```csharp
using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class ClassifierServiceTests : IDisposable
{
    private readonly string _tmp;
    public ClassifierServiceTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "filesorter-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tmp);
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch {} }

    private RulesConfig Cfg() => RuleEngine.LoadFromString($"""
        version: 1
        default_action: move
        conflict_strategy: rename
        log_level: info
        destinations:
          images: {_tmp}\img
        rules:
          - name: pics
            type: extension
            patterns: [jpg]
            destination: images
        """);

    [Fact]
    public void Classify_Move_JpgFromSrcToDest()
    {
        var src = Path.Combine(_tmp, "photo.jpg");
        File.WriteAllText(src, "x");
        var svc = new ClassifierService(Cfg(), dryRun: false);
        var result = svc.ClassifyOne(src);
        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_tmp, "img", "photo.jpg")));
        Assert.False(File.Exists(src));
    }

    [Fact]
    public void Classify_DryRun_DoesNotMove()
    {
        var src = Path.Combine(_tmp, "photo.jpg");
        File.WriteAllText(src, "x");
        var svc = new ClassifierService(Cfg(), dryRun: true);
        var result = svc.ClassifyOne(src);
        Assert.True(result.Success);
        Assert.True(File.Exists(src));
        Assert.False(File.Exists(Path.Combine(_tmp, "img", "photo.jpg")));
    }

    [Fact]
    public void Classify_ConflictRename_AddsSuffix()
    {
        var dest = Path.Combine(_tmp, "img");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "photo.jpg"), "old");
        var src = Path.Combine(_tmp, "photo.jpg");
        File.WriteAllText(src, "new");
        var svc = new ClassifierService(Cfg(), dryRun: false);
        var result = svc.ClassifyOne(src);
        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(dest, "photo.jpg"))); // old
        Assert.True(File.Exists(Path.Combine(dest, "photo_1.jpg"))); // new
    }

    [Fact]
    public void Classify_AutoCreatesDestDir()
    {
        var src = Path.Combine(_tmp, "photo.jpg");
        File.WriteAllText(src, "x");
        // dest dir does NOT exist
        var svc = new ClassifierService(Cfg(), dryRun: false);
        svc.ClassifyOne(src);
        Assert.True(Directory.Exists(Path.Combine(_tmp, "img")));
    }
}
```

- [ ] **Step 2: Run tests, expect 4 failures**

- [ ] **Step 3: Implement ClassifierService**

`src/FileSorter/Core/ClassifierService.cs`:

```csharp
using FileSorter.Core.Models;

namespace FileSorter.Core;

public record ClassifyResult(bool Success, string? Error, string FinalPath);

public class ClassifierService
{
    private readonly RulesConfig _cfg;
    private readonly bool _dryRun;
    private readonly ILogger _log;

    public ClassifierService(RulesConfig cfg, bool dryRun, ILogger? log = null)
    {
        _cfg = cfg;
        _dryRun = dryRun;
        _log = log ?? new ConsoleLogger();
    }

    public ClassifyResult ClassifyOne(string srcPath)
    {
        try
        {
            if (!File.Exists(srcPath))
                return new ClassifyResult(false, "source not found", srcPath);

            var match = RuleEngine.Match(_cfg, srcPath);
            if (match is null)
                return new ClassifyResult(false, "no rule matched", srcPath);

            Directory.CreateDirectory(match.DestinationPath);

            var fileName = Path.GetFileName(srcPath);
            var finalPath = ResolveConflict(Path.Combine(match.DestinationPath, fileName));

            if (_dryRun)
            {
                _log.Info($"[DRY] {srcPath} -> {finalPath}");
                return new ClassifyResult(true, null, finalPath);
            }

            if (string.Equals(_cfg.DefaultAction, "copy", StringComparison.OrdinalIgnoreCase))
                File.Copy(srcPath, finalPath, overwrite: false);
            else
                File.Move(srcPath, finalPath);

            _log.Info($"{srcPath} -> {finalPath}");
            return new ClassifyResult(true, null, finalPath);
        }
        catch (Exception ex)
        {
            _log.Error($"classify failed for {srcPath}: {ex.Message}");
            return new ClassifyResult(false, ex.Message, srcPath);
        }
    }

    private string ResolveConflict(string targetPath)
    {
        if (!File.Exists(targetPath)) return targetPath;
        switch (_cfg.ConflictStrategy)
        {
            case "overwrite":
                return targetPath;
            case "skip":
                // Move/Copy with overwrite:false will throw → caught above
                return targetPath;
            case "rename":
            default:
                var dir = Path.GetDirectoryName(targetPath)!;
                var stem = Path.GetFileNameWithoutExtension(targetPath);
                var ext = Path.GetExtension(targetPath);
                for (int i = 1; i < 10_000; i++)
                {
                    var candidate = Path.Combine(dir, $"{stem}_{i}{ext}");
                    if (!File.Exists(candidate)) return candidate;
                }
                throw new IOException("too many conflicts");
        }
    }
}

public interface ILogger
{
    void Info(string msg);
    void Warn(string msg);
    void Error(string msg);
}

public class ConsoleLogger : ILogger
{
    public void Info(string msg) => Console.WriteLine($"[INFO] {msg}");
    public void Warn(string msg) => Console.WriteLine($"[WARN] {msg}");
    public void Error(string msg) => Console.Error.WriteLine($"[ERROR] {msg}");
}
```

- [ ] **Step 4: Run tests, expect 4 passes**

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "M5: classifier service (move/copy + rename conflict strategy + dry-run)"
```

---

## M6: Rule File Watcher (hot reload)

**Goal:** Watch `rules.yaml` for changes, reload config within 2s, keep old config if reload fails.

**Files:**
- Create: `src/FileSorter/Core/RuleWatcher.cs`
- Create: `tests/FileSorter.Tests/RuleWatcherTests.cs`

- [ ] **Step 1: Write failing test**

`tests/FileSorter.Tests/RuleWatcherTests.cs`:

```csharp
using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class RuleWatcherTests : IDisposable
{
    private readonly string _tmp;
    public RuleWatcherTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "fs-watch-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tmp);
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch {} }

    [Fact]
    public async Task Watch_FileChanges_TriggersReload()
    {
        var path = Path.Combine(_tmp, "rules.yaml");
        File.WriteAllText(path, """
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              inbox: C:\x
            rules:
              - name: catch
                type: default
                destination: inbox
            """);
        int reloads = 0;
        RulesConfig? last = null;
        using var w = new RuleWatcher(path, cfg => { reloads++; last = cfg; });
        w.Start();

        await Task.Delay(300); // let watcher initialize
        File.WriteAllText(path, """
            version: 1
            default_action: copy
            conflict_strategy: overwrite
            log_level: info
            destinations:
              inbox: C:\y
            rules:
              - name: catch
                type: default
                destination: inbox
            """);

        await Task.Delay(2500); // > 2s debounce

        Assert.True(reloads >= 1);
        Assert.NotNull(last);
        Assert.Equal("copy", last!.DefaultAction);
    }
}
```

- [ ] **Step 2: Run test, expect failure (RuleWatcher not defined)**

- [ ] **Step 3: Implement RuleWatcher**

`src/FileSorter/Core/RuleWatcher.cs`:

```csharp
using FileSorter.Core.Models;

namespace FileSorter.Core;

public class RuleWatcher : IDisposable
{
    private readonly string _path;
    private readonly Action<RulesConfig> _onReload;
    private readonly FileSystemWatcher _fsw;
    private DateTime _lastReloadUtc = DateTime.MinValue;
    private readonly TimeSpan _debounce = TimeSpan.FromSeconds(2);

    public RuleWatcher(string path, Action<RulesConfig> onReload)
    {
        _path = path;
        _onReload = onReload;
        var dir = System.IO.Path.GetDirectoryName(path)!;
        _fsw = new FileSystemWatcher(dir, System.IO.Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = false
        };
        _fsw.Changed += (_, _) => TryReload();
        _fsw.Created += (_, _) => TryReload();
        _fsw.Renamed += (_, _) => TryReload();
    }

    public void Start() => _fsw.EnableRaisingEvents = true;
    public void Stop() => _fsw.EnableRaisingEvents = false;

    private void TryReload()
    {
        var now = DateTime.UtcNow;
        if (now - _lastReloadUtc < _debounce) return;
        _lastReloadUtc = now;
        try
        {
            var cfg = RuleEngine.LoadFromFile(_path);
            _onReload(cfg);
        }
        catch
        {
            // Keep old config silently; orchestrator may surface a toast later.
        }
    }

    public void Dispose() => _fsw.Dispose();
}
```

- [ ] **Step 4: Run test, expect pass**

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "M6: rule file watcher (2s debounce, keep old on parse error)"
```

---

## M7: WPF App Skeleton + Tray Icon

**Goal:** A runnable WPF app with a tray icon. Right-click menu: "Open paths", "Open rules.yaml", "Quit".

**Files:**
- Create: `src/FileSorter/App.xaml`
- Create: `src/FileSorter/App.xaml.cs`
- Modify: `src/FileSorter/Program.cs`
- Create: `src/FileSorter/UI/TrayIcon.cs`

- [ ] **Step 1: `App.xaml`**

`src/FileSorter/App.xaml`:

```xml
<Application x:Class="FileSorter.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown" />
```

- [ ] **Step 2: `App.xaml.cs`**

`src/FileSorter/App.xaml.cs`:

```csharp
using System.Windows;
using FileSorter.UI;

namespace FileSorter;

public partial class App : Application
{
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _tray = new TrayIcon();
        _tray.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 3: `Program.cs`**

`src/FileSorter/Program.cs`:

```csharp
using System;

namespace FileSorter;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Single-instance check (optional): for now, allow multiple.
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
```

- [ ] **Step 4: `TrayIcon.cs`**

`src/FileSorter/UI/TrayIcon.cs`:

```csharp
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;

namespace FileSorter.UI;

public class TrayIcon : IDisposable
{
    private NotifyIcon? _notify;

    public void Initialize()
    {
        _notify = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = "FileSorter"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open paths…", null, (_, _) => EditPathsRequested?.Invoke());
        menu.Items.Add("Open rules.yaml", null, (_, _) =>
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FileSorter", "rules.yaml");
            System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) =>
        {
            System.Windows.Application.Current.Shutdown();
        });
        _notify.ContextMenuStrip = menu;
    }

    public event Action? EditPathsRequested;

    private static Icon LoadIcon()
    {
        // Minimal: use system app icon; replace with embedded icon later.
        return Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location)
            ?? SystemIcons.Application;
    }

    public void Dispose()
    {
        _notify?.Dispose();
    }
}
```

- [ ] **Step 5: Sync + build + run**

On Windows:

```powershell
cd D:\hermes-工作目录\filesorter
dotnet build -c Debug
# Manual run:
.\src\FileSorter\bin\Debug\net8.0-windows\filesorter.exe
```

Expected: tray icon appears. Right-click → menu shows 3 items + Quit.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "M7: WPF app + tray icon (right-click menu: paths / rules / quit)"
```

---

## M8: Floating Disk (drag target)

**Goal:** A small always-on-top floating window. Drag a file/folder onto it → trigger classification (with folder dialog if folder).

**Files:**
- Create: `src/FileSorter/UI/FloatingDisk.xaml`
- Create: `src/FileSorter/UI/FloatingDisk.xaml.cs`
- Modify: `src/FileSorter/App.xaml.cs`

- [ ] **Step 1: `FloatingDisk.xaml`**

`src/FileSorter/UI/FloatingDisk.xaml`:

```xml
<Window x:Class="FileSorter.UI.FloatingDisk"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Width="80" Height="80"
        WindowStartupLocation="Manual"
        AllowsTransparency="True"
        WindowStyle="None"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        ResizeMode="NoResize">
    <Border BorderBrush="#333" BorderThickness="2" CornerRadius="40"
            Background="#CC007ACC" AllowDrop="True"
            DragEnter="OnDragEnter" DragOver="OnDragEnter"
            Drop="OnDrop">
        <TextBlock Text="📁" FontSize="32" HorizontalAlignment="Center"
                   VerticalAlignment="Center" Foreground="White"/>
    </Border>
</Window>
```

- [ ] **Step 2: `FloatingDisk.xaml.cs`**

`src/FileSorter/UI/FloatingDisk.xaml.cs`:

```csharp
using System.Windows;

namespace FileSorter.UI;

public partial class FloatingDisk : Window
{
    public FloatingDisk()
    {
        InitializeComponent();
        PositionBottomRight();
    }

    private void PositionBottomRight()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 20;
        Top = work.Bottom - Height - 20;
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        FilesDropped?.Invoke(paths);
    }

    public event Action<string[]>? FilesDropped;
}
```

- [ ] **Step 3: Wire up in App.xaml.cs**

Extend `src/FileSorter/App.xaml.cs`:

```csharp
using System.IO;
using System.Windows;
using FileSorter.Core;
using FileSorter.Core.Models;
using FileSorter.UI;

namespace FileSorter;

public partial class App : Application
{
    private TrayIcon? _tray;
    private FloatingDisk? _disk;
    private RulesConfig _cfg = new();
    private string? _rulesPath;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _rulesPath = EnsureRulesFile();
        _cfg = RuleEngine.LoadFromFile(_rulesPath);

        _tray = new TrayIcon();
        _tray.Initialize();
        _tray.EditPathsRequested += () => OpenEditPaths();

        _disk = new FloatingDisk();
        _disk.FilesDropped += OnFilesDropped;
        _disk.Show();
    }

    private void OnFilesDropped(string[] paths)
    {
        // Folder dialog logic lives in M12 (ClassifierService path).
        // For now: walk and classify each file; folder triggers recursion without prompt (will fix in M12).
        var svc = new ClassifierService(_cfg, dryRun: false);
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                    svc.ClassifyOne(f);
            else
                svc.ClassifyOne(p);
        }
    }

    private void OpenEditPaths()
    {
        // implemented in M11
    }

    private static string EnsureRulesFile()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileSorter");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "rules.yaml");
        if (!File.Exists(dest))
        {
            // Copy template from exe directory
            var src = Path.Combine(AppContext.BaseDirectory, "rules.yaml");
            if (File.Exists(src))
                File.Copy(src, dest);
            else
                File.WriteAllText(dest, DefaultYaml());
        }
        return dest;
    }

    private static string DefaultYaml() => """
        version: 1
        default_action: move
        conflict_strategy: rename
        log_level: info
        destinations:
          inbox: <USERPROFILE>\Documents\FileSorter-Inbox
        rules:
          - name: catch-all
            type: default
            destination: inbox
        """;

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 4: Add bundled rules.yaml**

Create `src/FileSorter/rules.yaml` (the template copied to APPDATA on first run):

```yaml
version: 1
default_action: move
conflict_strategy: rename
log_level: info

destinations:
  images: D:\分类\图片
  documents: D:\分类\文档
  archives: D:\分类\压缩包
  inbox: D:\分类\未分类

rules:
  - name: 社交媒体图片
    type: path_template
    extensions: [jpg, jpeg, png, gif, webp]
    filename_pattern: '^([a-z]+)_\(@([^)]+)\)_'
    path: '{destinations.images}\{1}\{2}'

  - name: 截图
    type: combined
    extension: [png, jpg]
    filename_keyword: ["screenshot", "截图", "screen"]
    destination: images

  - name: 发票
    type: filename_keyword
    patterns: ["发票", "invoice", "receipt"]
    case_sensitive: false
    extensions: [pdf, jpg, png]
    destination: documents

  - name: 压缩包
    type: extension
    patterns: [zip, rar, 7z, tar, gz]
    destination: archives

  - name: 图片
    type: extension
    patterns: [jpg, jpeg, png, gif, bmp, webp, svg]
    destination: images

  - name: 其他
    type: default
    destination: inbox
```

Also update `.csproj` to copy it to output:

Append to `src/FileSorter/FileSorter.csproj` inside `<ItemGroup>`:

```xml
    <None Include="rules.yaml">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
```

- [ ] **Step 5: Build + manual test**

On Windows:

```powershell
dotnet build -c Debug
# Run: drag a jpg to the floating disk → moves to D:\分类\图片\
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "M8: floating disk (drag target) + first-run rules.yaml template"
```

---

## M9: Install / Uninstall Auto-Start (registry)

**Goal:** CLI flags `--install` and `--uninstall` write/remove `HKCU\...\Run\FileSorter`.

**Files:**
- Modify: `src/FileSorter/Program.cs`
- Create: `src/FileSorter/Core/AutoStart.cs`

- [ ] **Step 1: `AutoStart.cs`**

`src/FileSorter/Core/AutoStart.cs`:

```csharp
using Microsoft.Win32;

namespace FileSorter.Core;

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FileSorter";

    public static void Install()
    {
        var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{exe}\"");
    }

    public static void Uninstall()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) != null;
    }
}
```

- [ ] **Step 2: Hook into `Program.cs`**

`src/FileSorter/Program.cs`:

```csharp
using System;
using FileSorter.Core;

namespace FileSorter;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--install":
                    AutoStart.Install();
                    Console.WriteLine("installed.");
                    return 0;
                case "--uninstall":
                    AutoStart.Uninstall();
                    Console.WriteLine("uninstalled.");
                    return 0;
            }
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
```

- [ ] **Step 3: Manual test**

On Windows:

```powershell
cd D:\hermes-工作目录\filesorter
dotnet build -c Debug
.\src\FileSorter\bin\Debug\net8.0-windows\filesorter.exe --install
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v FileSorter
# Expect: FileSorter    REG_SZ    "...\filesorter.exe"
.\src\FileSorter\bin\Debug\net8.0-windows\filesorter.exe --uninstall
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v FileSorter
# Expect: ERROR: The system was unable to find the specified registry key or value.
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "M9: --install / --uninstall via HKCU\\...\\Run"
```

---

## M10: Path-Template Rule Type (integration test)

**Goal:** Wire M4's path_template through end-to-end classifier on the twitter example.

This was already implemented in M4's `TemplateResult`. M10 adds one **integration test** that proves the whole pipeline.

**Files:**
- Create: `tests/FileSorter.Tests/IntegrationTests.cs`

- [ ] **Step 1: Write integration test**

```csharp
using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class IntegrationTests : IDisposable
{
    private readonly string _tmp;
    public IntegrationTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "fs-int-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tmp);
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch {} }

    [Fact]
    public void EndToEnd_TwitterFile_GoesToNestedFolder()
    {
        var cfg = RuleEngine.LoadFromString($"""
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              images: {_tmp}\img
            rules:
              - name: sm
                type: path_template
                extensions: [jpg, png]
                filename_pattern: '^([a-z]+)_\(@([^)]+)\)_'
                path: '{{destinations.images}}\{{1}}\{{2}}'
            """);

        var src = Path.Combine(_tmp, "twitter_(@hahaoy8)_肉丝儿_20260730-085445_2082751743373496825.jpg");
        File.WriteAllText(src, "x");

        var svc = new ClassifierService(cfg, dryRun: false);
        var r = svc.ClassifyOne(src);

        Assert.True(r.Success);
        var expectedDir = Path.Combine(_tmp, "img", "twitter", "@hahaoy8");
        Assert.True(Directory.Exists(expectedDir), $"missing {expectedDir}");
        var expectedFile = Path.Combine(expectedDir, "twitter_(@hahaoy8)_肉丝儿_20260730-085445_2082751743373496825.jpg");
        Assert.True(File.Exists(expectedFile), $"missing {expectedFile}");
    }
}
```

- [ ] **Step 2: Run test, expect pass (since M4 already implements this)**

```powershell
dotnet test --filter "FullyQualifiedName~IntegrationTests"
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "M10: integration test for path_template (twitter example)"
```

---

## M11: EditPathsWindow (minimal GUI for paths)

**Goal:** Tray menu "Open paths…" opens a window listing every `destinations.<name>` with a "Browse…" button.

**Files:**
- Create: `src/FileSorter/UI/EditPathsWindow.xaml`
- Create: `src/FileSorter/UI/EditPathsWindow.xaml.cs`
- Create: `src/FileSorter/Core/PathsEditor.cs`
- Modify: `src/FileSorter/App.xaml.cs`

- [ ] **Step 1: `PathsEditor.cs` (atomic save)**

`src/FileSorter/Core/PathsEditor.cs`:

```csharp
using System.IO;
using FileSorter.Core.Models;
using YamlDotNet.Serialization;

namespace FileSorter.Core;

public static class PathsEditor
{
    public static void SavePaths(string rulesPath, Dictionary<string, string> newPaths)
    {
        var current = RuleEngine.LoadFromFile(rulesPath);
        current.Destinations = newPaths;
        var yaml = new SerializerBuilder().Build().Serialize(current);
        var tmp = rulesPath + ".tmp";
        File.WriteAllText(tmp, yaml);
        File.Replace(tmp, rulesPath, null);
    }
}
```

- [ ] **Step 2: `EditPathsWindow.xaml`**

`src/FileSorter/UI/EditPathsWindow.xaml`:

```xml
<Window x:Class="FileSorter.UI.EditPathsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="FileSorter - 编辑路径" Width="560" SizeToContent="Height"
        WindowStartupLocation="CenterScreen">
    <StackPanel Margin="12">
        <TextBlock Text="选择每个目的地的路径,然后点保存。" Margin="0,0,0,8" />
        <ItemsControl x:Name="Rows">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Grid Margin="0,4">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="120"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="{Binding Name}" VerticalAlignment="Center"/>
                        <TextBox Grid.Column="1" Text="{Binding Path, UpdateSourceTrigger=PropertyChanged}"
                                 Margin="4,0" x:Name="PathBox"/>
                        <Button Grid.Column="2" Content="浏览…" Click="OnBrowse" Margin="4,0"/>
                        <Button Grid.Column="3" Content="清除" Click="OnClear"/>
                    </Grid>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button Content="打开 rules.yaml" Click="OnOpenYaml" Margin="0,0,8,0"/>
            <Button Content="取消" Click="OnCancel" Margin="0,0,8,0"/>
            <Button Content="保存" Click="OnSave" IsDefault="True"/>
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 3: `EditPathsWindow.xaml.cs`**

```csharp
using System.Collections.ObjectModel;
using System.Windows;
using FileSorter.Core;
using Microsoft.Win32;

namespace FileSorter.UI;

public partial class EditPathsWindow : Window
{
    public class PathRow
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
    }

    private readonly string _rulesPath;
    public ObservableCollection<PathRow> Rows { get; } = new();

    public EditPathsWindow(string rulesPath)
    {
        InitializeComponent();
        _rulesPath = rulesPath;
        var cfg = RuleEngine.LoadFromFile(rulesPath);
        foreach (var kv in cfg.Destinations)
            Rows.Add(new PathRow { Name = kv.Key, Path = kv.Value });
        Rows.CollectionChanged += (_, _) => { };
        ((ItemsControl)FindName("Rows"))!.ItemsSource = Rows;
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PathRow row) return;
        var dlg = new OpenFolderDialog
        {
            Title = $"选择 {row.Name} 路径",
            InitialDirectory = System.IO.Directory.Exists(row.Path) ? row.Path : ""
        };
        if (dlg.ShowDialog(this) == true)
            row.Path = dlg.FolderName;
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PathRow row)
            row.Path = "";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var dict = Rows.ToDictionary(r => r.Name, r => r.Path);
        PathsEditor.SavePaths(_rulesPath, dict);
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnOpenYaml(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start("explorer.exe", $"\"{_rulesPath}\"");
    }
}
```

- [ ] **Step 4: Wire in `App.xaml.cs`** (replace `OpenEditPaths` placeholder)

```csharp
    private void OpenEditPaths()
    {
        if (_rulesPath is null) return;
        var w = new EditPathsWindow(_rulesPath);
        w.ShowDialog();
        // After save, reload config from disk (RuleWatcher would also catch it within 2s).
        try { _cfg = RuleEngine.LoadFromFile(_rulesPath); }
        catch { /* keep old */ }
    }
```

- [ ] **Step 5: Manual test**

On Windows:

```powershell
dotnet build -c Debug
.\src\FileSorter\bin\Debug\net8.0-windows\filesorter.exe
# Right-click tray → "Open paths…" → window opens, change one, save, verify rules.yaml updated
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "M11: EditPathsWindow + atomic YAML save (destinations only)"
```

---

## M12: Folder Drop Dialog (recursive vs top-level)

**Goal:** When user drops a folder, ask "Recursive / Top-level / Cancel" before classifying.

**Files:**
- Create: `src/FileSorter/UI/FolderModeDialog.xaml`
- Create: `src/FileSorter/UI/FolderModeDialog.xaml.cs`
- Modify: `src/FileSorter/App.xaml.cs` (`OnFilesDropped`)

- [ ] **Step 1: `FolderModeDialog.xaml`**

```xml
<Window x:Class="FileSorter.UI.FolderModeDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="FileSorter - 拖入了文件夹" Width="480" SizeToContent="Height"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">
    <StackPanel Margin="16">
        <TextBlock x:Name="PathLabel" FontWeight="Bold" TextWrapping="Wrap"/>
        <TextBlock x:Name="StatsLabel" Margin="0,4,0,12" Opacity="0.7"/>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="递归处理所有文件" Click="OnRecursive" Width="140" Margin="0,0,8,0" IsDefault="True"/>
            <Button Content="只处理顶层文件" Click="OnTopLevel" Width="140" Margin="0,0,8,0"/>
            <Button Content="取消" Click="OnCancel" Width="80" IsCancel="True"/>
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 2: `FolderModeDialog.xaml.cs`**

```csharp
using System.IO;
using System.Windows;

namespace FileSorter.UI;

public partial class FolderModeDialog : Window
{
    public enum Mode { Recursive, TopLevel, Cancel }
    public Mode Result { get; private set; } = Mode.Cancel;

    public FolderModeDialog(string folderPath)
    {
        InitializeComponent();
        PathLabel.Text = folderPath;
        var files = Directory.GetFiles(folderPath).Length;
        var dirs = Directory.GetDirectories(folderPath).Length;
        StatsLabel.Text = $"{files} 个顶层文件,{dirs} 个子文件夹";
    }

    private void OnRecursive(object sender, RoutedEventArgs e) { Result = Mode.Recursive; Close(); }
    private void OnTopLevel(object sender, RoutedEventArgs e) { Result = Mode.TopLevel; Close(); }
    private void OnCancel(object sender, RoutedEventArgs e) { Result = Mode.Cancel; Close(); }
}
```

- [ ] **Step 3: Wire in `App.xaml.cs`** — replace `OnFilesDropped`:

```csharp
    private void OnFilesDropped(string[] paths)
    {
        var svc = new ClassifierService(_cfg, dryRun: false);
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                var dlg = new FolderModeDialog(p) { Owner = _disk };
                if (dlg.ShowDialog() != true || dlg.Result == FolderModeDialog.Mode.Cancel) continue;

                var opt = dlg.Result == FolderModeDialog.Mode.Recursive
                    ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var f in Directory.EnumerateFiles(p, "*", opt))
                    svc.ClassifyOne(f);
            }
            else
            {
                svc.ClassifyOne(p);
            }
        }
    }
```

- [ ] **Step 4: Manual test**

```powershell
dotnet build -c Debug
# Create test folder: C:\test-fs\ with 3 files in root + 2 in sub
.\src\FileSorter\bin\Debug\net8.0-windows\filesorter.exe
# Drag the folder onto floating disk → dialog appears → click "Recursive" → all 5 files classified
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "M12: folder drop dialog (recursive / top-level / cancel)"
```

---

## M13 (final): End-to-End Manual Verification

**Goal:** Run the full UX once and confirm everything works.

**Manual checklist:**

- [ ] **Step 1: Build release**

```powershell
cd D:\hermes-工作目录\filesorter
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o D:\hermes-工作目录\filesorter\dist\
```

Expected: `D:\hermes-工作目录\filesorter\dist\filesorter.exe` exists, size < 15 MB.

- [ ] **Step 2: Run the exe, verify tray icon**

```powershell
D:\hermes-工作目录\filesorter\dist\filesorter.exe
```

- [ ] **Step 3: Drag a `.jpg` to floating disk → moves to `D:\分类\图片\`

- [ ] **Step 4: Drag a twitter-named `.jpg` → moves to `D:\分类\图片\twitter\@<author>\`

- [ ] **Step 5: Drag a folder → dialog appears → click "Recursive" → all files classified

- [ ] **Step 6: Edit rules.yaml by hand → 2s later, new rule applies to next drop

- [ ] **Step 7: Right-click tray → "Open paths…" → change one → save → verify rules.yaml diff

- [ ] **Step 8: Right-click SendTo on a file → "FileSorter" appears → click → classified

- [ ] **Step 9: `filesorter.exe --install` → check registry → `filesorter.exe --uninstall` → check gone

- [ ] **Step 10: User signs off**

If any step fails: file a bug, fix, re-run from that step.

- [ ] **Step 11: Final commit**

```bash
git tag v0.1.0
git log --oneline
```

---

## Appendix A: Sync Helper (NAS → Windows)

When code lives in NAS at `/opt/data/projects/filesorter/`, sync to Windows via windows-mcp:

```python
# sync_to_windows.py — run from NAS side
import os, urllib.request, json

# (use the mcp_call.py helpers from session — see skill windows-mcp-manual-start)
# Walk all files under src/ and tests/, mirror to D:\hermes-工作目录\filesorter\
```

For manual one-off sync, copy individual files via:

```
mcp__windows-mcp__FileSystem {mode:"write", path:"D:\\hermes-工作目录\\filesorter\\<relative>", content:"<file content>"}
```

For initial bulk push, do it once after M2 completes (small enough to fit in one script).

---

## Appendix B: Acceptance Criteria Mapping

| Spec criterion | Tested by |
|---|---|
| 启动时间 < 1s | manual M13 step 2 |
| 单文件分类 < 100ms | implicit in unit tests |
| 规则热加载 < 2s | M6 test |
| 内存 < 50MB | manual observation M13 |
| exe < 15MB | M13 step 1 file size |
| 多级目录 twitter 例 | M10 integration test |
| 文件夹拖入弹对话框 | M13 step 5 |
| 重名默认 rename | M5 conflict test |
| 自启动 HKCU\Run | M9 manual |
| EditPathsWindow | M13 step 7 |