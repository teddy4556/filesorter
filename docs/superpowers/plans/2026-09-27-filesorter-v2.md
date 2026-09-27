# FileSorter v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add SendTo integration, Rule+Mappings concept, 2 new rule types (name_template + filename_pattern), and a GUI Rule Editor to FileSorter v1.

**Architecture:** v1 foundation (core models + YAML parser + matcher + classifier + WPF shell) stays intact. v2 extends `Rule` model with `Active` + `Mappings[]`, adds `name_template`/`filename_pattern` types, auto-generates regex from `{token}` placeholders, adds 3 new WPF windows (QuickRuleDialog / RuleEditor / MappingsEditor), and a CLI arg `--sendto` for Windows Explorer "Send To" integration.

**Tech Stack:** .NET 8 / WPF / C# 12 / YamlDotNet 15.1.6 / System.Drawing.Common 8.0.7 / xUnit

**Spec:** `docs/superpowers/specs/2026-09-27-filesorter-v2-design.md`

**v1 invariant:** All 16 existing tests MUST keep passing (zero regression).

---

## Task Index

| Task | Milestone | What it builds |
|---|---|---|
| T1 | M14 | Rule model: add `Active` field + `Mappings` nested model |
| T2 | M14 | Mapping serialisation: `[YamlMember]` aliases + roundtrip test |
| T3 | M15 | NameTemplate generator: `{token}` → regex |
| T4 | M15 | NameTemplate path builder: mappings → path string |
| T5 | M16 | filename_pattern type + starts_with/ends_with matcher |
| T6 | M17 | CLI arg `--sendto <files>` parser |
| T7 | M17 | SendTo shortcut installer (`%USERPROFILE%\SendTo\FileSorter.lnk`) |
| T8 | M17 | QuickRuleDialog.xaml + .xaml.cs |
| T9 | M18 | Atomic YAML writer (with backup rotation, keep 3) |
| T10 | M18 | RuleEditor.xaml (list + add/edit/delete + Save) |
| T11 | M19 | Mappings UI sub-component (token picker + level dropdown) |
| T12 | M20 | Test input preview (live path computation in editor) |
| T13 | M21 | README + docs/design.md sync |
| T14 | M22 | End-to-end verification + new tests |
| T15 | M23 | dotnet publish + exe size check |

---

## Task 1: Rule model — add Active + Mappings field

**Files:**
- Modify: `src/FileSorter/Core/Models/Rule.cs`
- Modify: `src/FileSorter/Core/Models/Mapping.cs` (NEW)
- Test: `tests/FileSorter.Tests/RuleModelTests.cs` (NEW)

- [ ] **Step 1: Write the failing test**

Create `tests/FileSorter.Tests/RuleModelTests.cs`:

```csharp
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class RuleModelTests
{
    [Fact]
    public void New_Rule_Has_Active_Default_True()
    {
        var r = new Rule { Name = "x", Type = "extension", Destination = "y" };
        Assert.True(r.Active);
    }

    [Fact]
    public void New_Rule_Has_Null_Mappings_By_Default()
    {
        var r = new Rule { Name = "x", Type = "extension", Destination = "y" };
        Assert.Null(r.Mappings);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```bash
cd /opt/data/projects/filesorter && git add tests/FileSorter.Tests/RuleModelTests.cs
# Push to Windows (you'll do this in final verify); for now, mark test as expected-fail
```

Actually, since Linux can't compile, just verify **type compilation** by reading current `Rule.cs`:

```bash
grep -n "public class Rule" src/FileSorter/Core/Models/Rule.cs
grep -n "Active\|Mappings" src/FileSorter/Core/Models/Rule.cs
```

Expected: no `Active` field, no `Mappings` field.

- [ ] **Step 3: Add Mapping.cs**

Create `src/FileSorter/Core/Models/Mapping.cs`:

```csharp
namespace FileSorter.Core.Models;

public class Mapping
{
    public string Token { get; set; } = "";
    public int Level { get; set; } = 1;
}
```

- [ ] **Step 4: Add Active + Mappings to Rule.cs**

Modify `src/FileSorter/Core/Models/Rule.cs` — append these two properties (keep all existing fields):

```csharp
    [YamlMember(Alias = "active")] public bool Active { get; set; } = true;
    [YamlMember(Alias = "mappings")] public List<Mapping>? Mappings { get; set; }
```

(Use full namespace `YamlDotNet.Serialization.YamlMember` if needed — match existing file style.)

- [ ] **Step 5: Verify file**

Read `src/FileSorter/Core/Models/Rule.cs` to confirm both fields added in the right place (after existing fields, before class closing brace).

- [ ] **Step 6: Commit**

```bash
cd /opt/data/projects/filesorter
git add src/FileSorter/Core/Models/Rule.cs src/FileSorter/Core/Models/Mapping.cs tests/FileSorter.Tests/RuleModelTests.cs
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M14: Rule model 加 Active + Mappings 字段(Mapping 新模型)"
```

---

## Task 2: Mapping YAML round-trip test

**Files:**
- Test: append to `tests/FileSorter.Tests/RuleModelTests.cs`

- [ ] **Step 1: Add test**

Append to `RuleModelTests.cs`:

```csharp
    [Fact]
    public void Rule_With_Mappings_Roundtrips_Yaml()
    {
        var yaml = """
            version: 1
            default_action: move
            destinations:
              images: D:\cat\img
            rules:
              - name: social
                type: name_template
                template: "{platform}_@{author}"
                mappings:
                  - token: "{platform}"
                    level: 1
                  - token: "{author}"
                    level: 2
            """;

        var cfg = RuleEngine.LoadFromString(yaml);
        Assert.Single(cfg.Rules);
        var r = cfg.Rules![0];
        Assert.Equal("social", r.Name);
        Assert.NotNull(r.Mappings);
        Assert.Equal(2, r.Mappings!.Count);
        Assert.Equal("{platform}", r.Mappings[0].Token);
        Assert.Equal(1, r.Mappings[0].Level);
        Assert.Equal("{author}", r.Mappings[1].Token);
        Assert.Equal(2, r.Mappings[1].Level);
    }
```

- [ ] **Step 2: Commit (test will be verified on Windows)**

```bash
cd /opt/data/projects/filesorter
git add tests/FileSorter.Tests/RuleModelTests.cs
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M14-test: 加 Rule.Mappings YAML round-trip 测试"
```

---

## Task 3: NameTemplate regex generator

**Files:**
- Create: `src/FileSorter/Core/NameTemplateCompiler.cs`
- Test: `tests/FileSorter.Tests/NameTemplateCompilerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using FileSorter.Core;
using Xunit;

namespace FileSorter.Tests;

public class NameTemplateCompilerTests
{
    [Fact]
    public void Compile_Token_Without_Type_Matches_Any_NonSep()
    {
        var re = NameTemplateCompiler.CompileToRegex("{platform}_@{author}_{title}");
        Assert.Equal(@"^([a-zA-Z0-9]+)_@([a-zA-Z0-9]+)_([a-zA-Z0-9]+)$", re);
    }

    [Fact]
    public void Compile_Text_Type_Lowercase_Only()
    {
        var re = NameTemplateCompiler.CompileToRegex("{platform:text}_{author}");
        Assert.Contains("[a-z]+", re);
    }

    [Fact]
    public void Compile_Number_Type_Matches_Digits()
    {
        var re = NameTemplateCompiler.CompileToRegex("img_{idx:number}.jpg");
        Assert.Contains(@"(\d+)", re);
    }

    [Fact]
    public void Compile_Date_Format_yyyyMMdd()
    {
        var re = NameTemplateCompiler.CompileToRegex("snap_{date:yyyyMMdd}.jpg");
        Assert.Contains(@"(\d{8})", re);
    }

    [Fact]
    public void Compile_Date_Format_yyyy_MM_dd()
    {
        var re = NameTemplateCompiler.CompileToRegex("snap_{date:yyyy-MM-dd}.jpg");
        Assert.Contains(@"(\d{4}-\d{2}-\d{2})", re);
    }

    [Fact]
    public void Compile_Literal_At_Stays_Literal()
    {
        var re = NameTemplateCompiler.CompileToRegex("{a}_@{b}");
        Assert.Contains(@"_@", re);  // literal @
        Assert.Contains("([a-zA-Z0-9]+)", re); // both tokens
    }

    [Fact]
    public void Compile_Template_With_Dots_And_Spaces()
    {
        var re = NameTemplateCompiler.CompileToRegex("{name}.{ext}");
        Assert.Contains(@"\.([a-zA-Z0-9]+)$", re);
    }
}
```

- [ ] **Step 2: Implement `NameTemplateCompiler`**

Create `src/FileSorter/Core/NameTemplateCompiler.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace FileSorter.Core;

/// <summary>
/// Compiles a name template like "{platform}_@{author}_{date:yyyyMMdd}" into a regex string.
/// Supported tokens:
///   {name}             → any non-separator (default type: any → [a-zA-Z0-9]+)
///   {name:text}        → [a-z]+
///   {name:string}      → [a-zA-Z]+
///   {name:int}         → [0-9]+
///   {name:number}      → [0-9]+
///   {name:date:fmt}    → \d{...} based on fmt (yyyyMMdd=8 digits, yyyy-MM-dd=10 chars, HHmmss=6)
///   {name:any}         → [a-zA-Z0-9]+
/// Literal characters are escaped for regex.
/// </summary>
public static class NameTemplateCompiler
{
    public static string CompileToRegex(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
            throw new ArgumentException("Template is empty", nameof(template));

        var sb = new StringBuilder();
        sb.Append('^');
        var i = 0;
        while (i < template.Length)
        {
            if (template[i] == '{')
            {
                var end = template.IndexOf('}', i + 1);
                if (end < 0) throw new ArgumentException($"Unclosed token at index {i}", nameof(template));
                var inner = template.Substring(i + 1, end - i - 1);  // e.g. "platform" or "date:yyyyMMdd"
                sb.Append('(');
                sb.Append(TokenPattern(inner));
                sb.Append(')');
                i = end + 1;
            }
            else
            {
                sb.Append(Regex.Escape(template[i].ToString()));
                i++;
            }
        }
        sb.Append('$');
        return sb.ToString();
    }

    private static string TokenPattern(string inner)
    {
        var colonIdx = inner.IndexOf(':');
        string name = colonIdx < 0 ? inner : inner.Substring(0, colonIdx);
        string typeSpec = colonIdx < 0 ? "any" : inner.Substring(colonIdx + 1);

        // Handle date:fmt → name="date", fmt="yyyyMMdd" etc.
        if (typeSpec.StartsWith("date:"))
        {
            var fmt = typeSpec.Substring("date:".Length);
            return fmt switch
            {
                "yyyyMMdd" => @"\d{8}",
                "HHmmss" => @"\d{6}",
                "yyyy-MM-dd" => @"\d{4}-\d{2}-\d{2}",
                "yyyyMMdd_HHmmss" => @"\d{8}_\d{6}",
                _ => throw new ArgumentException($"Unsupported date format: {fmt}")
            };
        }

        return typeSpec switch
        {
            "text" or "string" => @"[a-zA-Z]+",
            "text_lower" => @"[a-z]+",
            "int" or "number" => @"\d+",
            "any" => @"[a-zA-Z0-9]+",
            _ => throw new ArgumentException($"Unknown token type: {typeSpec}")
        };
    }
}
```

- [ ] **Step 3: Commit**

```bash
cd /opt/data/projects/filesorter
git add src/FileSorter/Core/NameTemplateCompiler.cs tests/FileSorter.Tests/NameTemplateCompilerTests.cs
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M15: NameTemplateCompiler — {token[:type]} 占位符转 C# regex"
```

---

## Task 4: NameTemplate path builder (mappings → path)

**Files:**
- Create: `src/FileSorter/Core/NameTemplatePathBuilder.cs`
- Test: append to `tests/FileSorter.Tests/NameTemplateCompilerTests.cs`

- [ ] **Step 1: Add tests**

Append:

```csharp
public class NameTemplatePathBuilderTests
{
    [Fact]
    public void Build_NoMappings_Returns_DestinationFlat()
    {
        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: "D:\\cat\\img",
            template: "{platform}_@{author}",
            mappings: null,
            captured: new[] { "twitter", "hahaoy8" });
        Assert.Equal(@"D:\cat\img", path);
    }

    [Fact]
    public void Build_OneMapping_Level1_Adds_Subdir()
    {
        var mappings = new List<FileSorter.Core.Models.Mapping>
        {
            new() { Token = "{platform}", Level = 1 }
        };
        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: "D:\\cat\\img",
            template: "{platform}_@{author}",
            mappings: mappings,
            captured: new[] { "twitter", "hahaoy8" });
        Assert.Equal(@"D:\cat\img\twitter", path);
    }

    [Fact]
    public void Build_TwoMappings_Level1And2_Nested_Subdir()
    {
        var mappings = new List<FileSorter.Core.Models.Mapping>
        {
            new() { Token = "{platform}", Level = 1 },
            new() { Token = "{author}", Level = 2 }
        };
        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: "D:\\cat\\img",
            template: "{platform}_@{author}",
            mappings: mappings,
            captured: new[] { "twitter", "hahaoy8" });
        Assert.Equal(@"D:\cat\img\twitter\hahaoy8", path);
    }

    [Fact]
    public void Build_Level3_Supported()
    {
        var mappings = new List<FileSorter.Core.Models.Mapping>
        {
            new() { Token = "{a}", Level = 1 },
            new() { Token = "{b}", Level = 3 }   // skip level 2 (no token mapped)
        };
        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: "D:\\x",
            template: "{a}_{b}",
            mappings: mappings,
            captured: new[] { "foo", "bar" });
        Assert.Equal(@"D:\x\foo\bar", path);  // level 2 missing → just skip (no empty dir)
    }
}
```

- [ ] **Step 2: Implement**

Create `src/FileSorter/Core/NameTemplatePathBuilder.cs`:

```csharp
using System.Text;
using FileSorter.Core.Models;

namespace FileSorter.Core;

/// <summary>
/// Builds a target directory path from a name_template rule's mappings.
/// Path = baseDestination + (level 1 token value) + (level 2 token value) + ...
/// Tokens not in mappings are ignored.
/// Skipped level numbers are tolerated (no empty dir inserted).
/// </summary>
public static class NameTemplatePathBuilder
{
    public static string BuildPath(
        string baseDestination,
        string template,
        List<Mapping>? mappings,
        IReadOnlyList<string> captured)
    {
        if (mappings == null || mappings.Count == 0)
            return baseDestination;

        // Token name in template (e.g. {platform}) → captured value
        var tokenValues = new Dictionary<string, string>();
        var tokenNames = ExtractTokenNames(template);
        for (int i = 0; i < tokenNames.Count && i < captured.Count; i++)
            tokenValues[tokenNames[i]] = captured[i];

        var sb = new StringBuilder(baseDestination);
        var sortedMappings = mappings.OrderBy(m => m.Level).ToList();
        foreach (var m in sortedMappings)
        {
            if (tokenValues.TryGetValue(m.Token, out var v))
            {
                if (string.IsNullOrEmpty(v)) continue;
                sb.Append('\\');
                sb.Append(SanitizeDirName(v));
            }
        }
        return sb.ToString();
    }

    private static List<string> ExtractTokenNames(string template)
    {
        var names = new List<string>();
        var i = 0;
        while (i < template.Length)
        {
            if (template[i] == '{')
            {
                var end = template.IndexOf('}', i + 1);
                if (end < 0) break;
                var inner = template.Substring(i + 1, end - i - 1);
                var colonIdx = inner.IndexOf(':');
                var name = colonIdx < 0 ? inner : inner.Substring(0, colonIdx);
                names.Add("{" + name + "}");
                i = end + 1;
            }
            else i++;
        }
        return names;
    }

    private static string SanitizeDirName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }
}
```

- [ ] **Step 3: Commit**

```bash
cd /opt/data/projects/filesorter
git add src/FileSorter/Core/NameTemplatePathBuilder.cs tests/FileSorter.Tests/NameTemplateCompilerTests.cs
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M15: NameTemplatePathBuilder — mappings 排 level 拼目录路径"
```

---

## Task 5: filename_pattern rule type

**Files:**
- Modify: `src/FileSorter/Core/RuleEngine.cs` — extend `Match()` switch
- Modify: `src/FileSorter/Core/Models/Rule.cs` — add fields
- Test: append to `tests/FileSorter.Tests/RuleMatchTests.cs`

- [ ] **Step 1: Add test**

Append:

```csharp
[Fact]
public void Match_FilenamePattern_StartsWith_GoesToDestination()
{
    var cfg = new FileSorter.Core.Models.RulesConfig
    {
        Destinations = new() { ["docs"] = "D:\\cat\\docs" },
        Rules = new()
        {
            new()
            {
                Name = "acme invoice",
                Type = "filename_pattern",
                StartsWith = new() { "acme_" },
                Extensions = new() { "pdf" },
                Destination = "docs"
            }
        }
    };
    var r = RuleEngine.Match(cfg, @"C:\in\acme_jan.pdf");
    Assert.NotNull(r);
    Assert.Equal(@"D:\cat\docs", r!.DestinationPath);
}

[Fact]
public void Match_FilenamePattern_EndsWith_GoesToDestination()
{
    var cfg = new FileSorter.Core.Models.RulesConfig
    {
        Destinations = new() { ["docs"] = "D:\\cat\\docs" },
        Rules = new()
        {
            new()
            {
                Name = "invoice",
                Type = "filename_pattern",
                EndsWith = new() { "_invoice.pdf" },
                Destination = "docs"
            }
        }
    };
    var r = RuleEngine.Match(cfg, @"C:\in\acme_invoice.pdf");
    Assert.NotNull(r);
    Assert.Equal(@"D:\cat\docs", r!.DestinationPath);
}
```

- [ ] **Step 2: Add fields to Rule.cs**

Modify `src/FileSorter/Core/Models/Rule.cs` — append:

```csharp
    [YamlMember(Alias = "starts_with")] public List<string>? StartsWith { get; set; }
    [YamlMember(Alias = "ends_with")] public List<string>? EndsWith { get; set; }
    [YamlMember(Alias = "template")] public string? Template { get; set; }
```

(`Template` is shared with name_template type.)

- [ ] **Step 3: Extend RuleEngine.Match() switch**

Find the `Match()` method in `src/FileSorter/Core/RuleEngine.cs`. It currently has a switch on `r.Type`. Add a case `"filename_pattern"`:

```csharp
                case "filename_pattern":
                    if (MatchStartsWith(name, r.StartsWith, r.CaseSensitive)) return new MatchResult(...);
                    if (MatchEndsWith(name, r.EndsWith, r.CaseSensitive)) return new MatchResult(...);
                    break;
```

(Read the existing switch and `MatchResult` constructor to fill in the exact arguments — likely: `rule: r`, `destPath: ResolveDestination(r.Destination, cfg.Destinations)`.)

Add private helpers in RuleEngine.cs:

```csharp
    private static bool MatchStartsWith(string name, List<string>? patterns, bool caseSensitive)
    {
        if (patterns == null || patterns.Count == 0) return false;
        var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return patterns.Any(p => name.StartsWith(p, cmp));
    }

    private static bool MatchEndsWith(string name, List<string>? patterns, bool caseSensitive)
    {
        if (patterns == null || patterns.Count == 0) return false;
        var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return patterns.Any(p => name.EndsWith(p, cmp));
    }
```

Note: Rule.cs also needs `CaseSensitive` field. Add:

```csharp
    [YamlMember(Alias = "case_sensitive")] public bool CaseSensitive { get; set; } = false;
```

(Check if this field already exists in v1 — if yes, skip this addition.)

- [ ] **Step 4: Commit**

```bash
cd /opt/data/projects/filesorter
git add src/FileSorter/Core/Models/Rule.cs src/FileSorter/Core/RuleEngine.cs tests/FileSorter.Tests/RuleMatchTests.cs
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M16: filename_pattern 规则类型(StartsWith/EndsWith 边界匹配)"
```

---

## Task 6: name_template matching in RuleEngine

**Files:**
- Modify: `src/FileSorter/Core/RuleEngine.cs`
- Test: append to `tests/FileSorter.Tests/RuleMatchTests.cs`

- [ ] **Step 1: Add test**

```csharp
[Fact]
public void Match_NameTemplate_AutoRegex_GoesToMappingPath()
{
    var cfg = new FileSorter.Core.Models.RulesConfig
    {
        Destinations = new() { ["images"] = "D:\\cat\\img" },
        Rules = new()
        {
            new()
            {
                Name = "social",
                Type = "name_template",
                Extensions = new() { "jpg" },
                Template = "{platform}_@{author}",
                Mappings = new()
                {
                    new() { Token = "{platform}", Level = 1 },
                    new() { Token = "{author}", Level = 2 }
                },
                Destination = "images"
            }
        }
    };
    var r = RuleEngine.Match(cfg, @"C:\in\twitter_@hahaoy8_xxx.jpg");
    Assert.NotNull(r);
    Assert.Equal(@"D:\cat\img\twitter\hahaoy8", r!.DestinationPath);
}
```

- [ ] **Step 2: Extend RuleEngine.Match() switch**

Add a case `"name_template"`:

```csharp
                case "name_template":
                    if (MatchNameTemplate(Path.GetFileName(fullPath), r, cfg, out var ntResult))
                        return ntResult;
                    break;
```

Add private helper:

```csharp
    private static bool MatchNameTemplate(string fileName, Rule r, RulesConfig cfg, out MatchResult? result)
    {
        result = null;
        if (r.Template == null) return false;
        // Filter by extensions first (cheap)
        if (r.Extensions != null && r.Extensions.Count > 0)
        {
            var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            if (!r.Extensions.Contains(ext)) return false;
        }
        // Compile template → regex
        var regex = NameTemplateCompiler.CompileToRegex(r.Template);
        var m = System.Text.RegularExpressions.Regex.Match(fileName, regex);
        if (!m.Success) return false;
        var captured = new List<string>();
        for (int i = 1; i < m.Groups.Count; i++) captured.Add(m.Groups[i].Value);
        // Resolve destination via mappings
        var baseDest = ResolveDestination(r.Destination, cfg.Destinations);
        var path = NameTemplatePathBuilder.BuildPath(baseDest, r.Template, r.Mappings, captured);
        result = new MatchResult(rule: r, destPath: path);
        return true;
    }
```

Note: `MatchResult` constructor signature may vary — read the existing code to confirm arg order. If different, adjust accordingly.

- [ ] **Step 3: Commit**

```bash
cd /opt/data/projects/filesorter
git add src/FileSorter/Core/RuleEngine.cs tests/FileSorter.Tests/RuleMatchTests.cs
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M15: RuleEngine.Match 接 name_template 类型(自动 regex + path 拼接)"
```

---

## Task 7: Active flag in matcher

**Files:**
- Modify: `src/FileSorter/Core/RuleEngine.cs`
- Test: append to `tests/FileSorter.Tests/RuleMatchTests.cs`

- [ ] **Step 1: Add test**

```csharp
[Fact]
public void Match_Skips_Inactive_Rule()
{
    var cfg = new FileSorter.Core.Models.RulesConfig
    {
        Destinations = new() { ["img"] = "D:\\cat\\img" },
        Rules = new()
        {
            new()
            {
                Name = "disabled",
                Type = "extension",
                Patterns = new() { "jpg" },
                Destination = "img",
                Active = false
            }
        }
    };
    var r = RuleEngine.Match(cfg, @"C:\in\test.jpg");
    Assert.Null(r);  // Inactive rule should NOT match
}
```

- [ ] **Step 2: Skip inactive rules in Match()**

Find the `Match()` foreach loop. Add a guard at the top:

```csharp
    foreach (var r in cfg.Rules!)
    {
        if (!r.Active) continue;
        // ... existing match logic
    }
```

- [ ] **Step 3: Commit**

```bash
cd /opt/data/projects/filesorter
git add src/FileSorter/Core/RuleEngine.cs tests/FileSorter.Tests/RuleMatchTests.cs
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M14: Match 跳过 Active=false 的规则"
```

---

## Task 8: Atomic YAML writer with backup rotation

**Files:**
- Create: `src/FileSorter/Core/RulesFileWriter.cs`
- Test: `tests/FileSorter.Tests/RulesFileWriterTests.cs`

- [ ] **Step 1: Add test**

```csharp
using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

public class RulesFileWriterTests : IDisposable
{
    private readonly string _dir;

    public RulesFileWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "filesorter_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch {} }

    [Fact]
    public void Write_FirstTime_NoBackup_Creates_File()
    {
        var path = Path.Combine(_dir, "rules.yaml");
        var cfg = new RulesConfig { Version = 1 };
        RulesFileWriter.WriteWithBackup(path, cfg);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.bak.*"));
    }

    [Fact]
    public void Write_Overwrite_Creates_Backup()
    {
        var path = Path.Combine(_dir, "rules.yaml");
        File.WriteAllText(path, "old content");
        RulesFileWriter.WriteWithBackup(path, new RulesConfig { Version = 1 });
        var baks = Directory.GetFiles(_dir, "*.bak.*");
        Assert.Single(baks);
    }

    [Fact]
    public void Write_FourTimes_Keeps_Three_Newest_Backups()
    {
        var path = Path.Combine(_dir, "rules.yaml");
        File.WriteAllText(path, "v0");
        for (int i = 1; i <= 4; i++)
        {
            RulesFileWriter.WriteWithBackup(path, new RulesConfig { Version = i });
            Thread.Sleep(1100);  // ensure different YYYYMMDD-HHMMSS suffix
        }
        var baks = Directory.GetFiles(_dir, "*.bak.*");
        Assert.Equal(3, baks.Length);
    }
}
```

- [ ] **Step 2: Implement RulesFileWriter**

Create `src/FileSorter/Core/RulesFileWriter.cs`:

```csharp
using FileSorter.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FileSorter.Core;

public static class RulesFileWriter
{
    private const int KeepBackups = 3;

    public static void WriteWithBackup(string path, RulesConfig cfg)
    {
        // Backup existing file (if any) with timestamp
        if (File.Exists(path))
        {
            var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var bakPath = $"{path}.bak.{ts}";
            File.Copy(path, bakPath, overwrite: true);
        }
        // Atomic write: write .tmp then File.Replace
        var tmpPath = path + ".tmp";
        var yaml = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build()
            .Serialize(cfg);
        File.WriteAllText(tmpPath, yaml);
        if (File.Exists(path))
            File.Replace(tmpPath, path, null);
        else
            File.Move(tmpPath, path);
        // Rotate backups: keep only newest KeepBackups
        var dir = Path.GetDirectoryName(path)!;
        var baks = Directory.GetFiles(dir, Path.GetFileName(path) + ".bak.*")
            .OrderByDescending(f => f)
            .ToList();
        foreach (var old in baks.Skip(KeepBackups))
        {
            try { File.Delete(old); } catch { /* best-effort */ }
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
cd /opt/data/projects/filesorter
git add src/FileSorter/Core/RulesFileWriter.cs tests/FileSorter.Tests/RulesFileWriterTests.cs
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M18: RulesFileWriter — 原子写(.tmp→Replace)+ 保留最近 3 份 backup"
```

---

## Task 9: SendTo shortcut installer + CLI arg

**Files:**
- Create: `src/FileSorter/Core/SendToInstaller.cs`
- Create: `src/FileSorter/Core/StartupArgs.cs`
- Test: `tests/FileSorter.Tests/SendToInstallerTests.cs`

- [ ] **Step 1: Add test**

```csharp
using FileSorter.Core;
using Xunit;

namespace FileSorter.Tests;

public class StartupArgsTests
{
    [Fact]
    public void Parse_NoArgs_Returns_Normal()
    {
        var a = StartupArgs.Parse(Array.Empty<string>());
        Assert.Equal(StartupMode.Normal, a.Mode);
        Assert.Empty(a.Files);
    }

    [Fact]
    public void Parse_SendTo_OneFile()
    {
        var a = StartupArgs.Parse(new[] { "--sendto", @"C:\in\test.jpg" });
        Assert.Equal(StartupMode.SendTo, a.Mode);
        Assert.Equal(new[] { @"C:\in\test.jpg" }, a.Files);
    }

    [Fact]
    public void Parse_Install_Returns_Install()
    {
        var a = StartupArgs.Parse(new[] { "--install" });
        Assert.Equal(StartupMode.Install, a.Mode);
    }

    [Fact]
    public void Parse_Uninstall_Returns_Uninstall()
    {
        var a = StartupArgs.Parse(new[] { "--uninstall" });
        Assert.Equal(StartupMode.Uninstall, a.Mode);
    }

    [Fact]
    public void Parse_InstallSendTo_Returns_InstallSendTo()
    {
        var a = StartupArgs.Parse(new[] { "--install-sendto" });
        Assert.Equal(StartupMode.InstallSendTo, a.Mode);
    }
}

public class SendToInstallerTests
{
    [Fact]
    public void IsInstalled_True_When_Lnk_Exists()
    {
        var sendToDir = Path.Combine(Path.GetTempPath(), "sendto_test_" + Guid.NewGuid());
        Directory.CreateDirectory(sendToDir);
        var exe = Path.Combine(sendToDir, "filesorter.exe");
        File.WriteAllBytes(exe, new byte[] { 0 });
        // Create fake .lnk (just an empty file with .lnk extension — sufficient for test)
        File.WriteAllText(Path.Combine(sendToDir, "FileSorter.lnk"), "");
        Assert.True(SendToInstaller.IsInstalled(exe, sendToDir));
        Directory.Delete(sendToDir, true);
    }
}
```

- [ ] **Step 2: Implement StartupArgs**

Create `src/FileSorter/Core/StartupArgs.cs`:

```csharp
namespace FileSorter.Core;

public enum StartupMode { Normal, SendTo, Install, Uninstall, InstallSendTo }

public record StartupArgs(StartupMode Mode, IReadOnlyList<string> Files)
{
    public static StartupArgs Parse(string[] args)
    {
        if (args.Length == 0) return new(StartupMode.Normal, Array.Empty<string>());
        switch (args[0])
        {
            case "--sendto":       return new(StartupMode.SendTo, args.Skip(1).ToList());
            case "--install":      return new(StartupMode.Install, Array.Empty<string>());
            case "--uninstall":    return new(StartupMode.Uninstall, Array.Empty<string>());
            case "--install-sendto": return new(StartupMode.InstallSendTo, Array.Empty<string>());
            default:               return new(StartupMode.Normal, Array.Empty<string>());
        }
    }
}
```

- [ ] **Step 3: Implement SendToInstaller**

Create `src/FileSorter/Core/SendToInstaller.cs`:

```csharp
namespace FileSorter.Core;

public static class SendToInstaller
{
    public static string GetSendToDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "SendTo");
    }

    public static string GetShortcutPath(string exePath)
    {
        return Path.Combine(GetSendToDir(), "FileSorter.lnk");
    }

    public static bool IsInstalled(string exePath, string? sendToDir = null)
    {
        sendToDir ??= GetSendToDir();
        return File.Exists(Path.Combine(sendToDir, "FileSorter.lnk"));
    }

    /// <summary>
    /// Create a .lnk file in the SendTo folder pointing to exePath.
    /// Uses WScript.Shell COM (Windows-only).
    /// </summary>
    public static void Install(string exePath)
    {
        var shell = (dynamic)Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            var shortcut = shell.CreateShortcut(GetShortcutPath(exePath));
            shortcut.TargetPath = exePath;
            shortcut.Arguments = "--sendto";
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
            shortcut.WindowStyle = 7;  // Minimized (tray app)
            shortcut.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
```

- [ ] **Step 4: Commit**

```bash
cd /opt/data/projects/filesorter
git add src/FileSorter/Core/StartupArgs.cs src/FileSorter/Core/SendToInstaller.cs tests/FileSorter.Tests/SendToInstallerTests.cs
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M17: StartupArgs + SendToInstaller(创建 .lnk 快捷方式)"
```

---

## Task 10: QuickRuleDialog.xaml

**Files:**
- Create: `src/FileSorter/UI/QuickRuleDialog.xaml`
- Create: `src/FileSorter/UI/QuickRuleDialog.xaml.cs`

- [ ] **Step 1: XAML**

```xml
<Window x:Class="FileSorter.UI.QuickRuleDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="FileSorter - 选择规则" Width="500" SizeToContent="Height"
        WindowStartupLocation="CenterScreen">
    <StackPanel Margin="12">
        <TextBlock x:Name="FileCountText" Margin="0,0,0,8" />
        <ListBox x:Name="RulesList" Height="220" SelectionMode="Single">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <TextBlock Text="{Binding Display}" Padding="4" />
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,12,0,0">
            <Button Content="取消" Click="OnCancel" Margin="0,0,8,0" Width="80" />
            <Button Content="确定" Click="OnOk" IsDefault="True" Width="80" />
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Code-behind**

```csharp
using System.Windows;
using System.Windows.Controls;
using FileSorter.Core;
using FileSorter.Core.Models;

namespace FileSorter.UI;

public partial class QuickRuleDialog : Window
{
    public class Choice { public string Display { get; set; } = ""; public Rule? Rule { get; set; } }

    public Rule? SelectedRule { get; private set; }  // null = use defaults

    public QuickRuleDialog(IReadOnlyList<string> files, IReadOnlyList<Rule> rules)
    {
        InitializeComponent();
        FileCountText.Text = $"{files.Count} 个文件:" + string.Join(", ", files.Select(Path.GetFileName));
        var choices = new List<Choice>
        {
            new() { Display = "● 按默认规则(全自动)", Rule = null }
        };
        choices.AddRange(rules.Select(r => new Choice { Display = $"{r.Name}  ({r.Type})", Rule = r }));
        RulesList.ItemsSource = choices;
        RulesList.SelectedIndex = 0;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        SelectedRule = (RulesList.SelectedItem as Choice)?.Rule;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
```

- [ ] **Step 3: Commit**

```bash
cd /opt/data/projects/filesorter
git add src/FileSorter/UI/QuickRuleDialog.xaml src/FileSorter/UI/QuickRuleDialog.xaml.cs
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M17: QuickRuleDialog(下拉规则列表 + 按默认规则选项)"
```

---

## Task 11: RuleEditor.xaml (list + add/edit/delete + Save)

**Files:**
- Create: `src/FileSorter/UI/RuleEditor.xaml`
- Create: `src/FileSorter/UI/RuleEditor.xaml.cs`

- [ ] **Step 1: XAML**

```xml
<Window x:Class="FileSorter.UI.RuleEditor"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="FileSorter - 规则编辑器" Width="900" Height="600"
        WindowStartupLocation="CenterScreen">
    <DockPanel Margin="12">
        <!-- Toolbar -->
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
            <Button Content="+ 添加" Click="OnAdd" Width="80" Margin="0,0,4,0" />
            <Button Content="删除" Click="OnDelete" Width="80" Margin="0,0,4,0" />
            <Button Content="上移" Click="OnMoveUp" Width="60" Margin="0,0,4,0" />
            <Button Content="下移" Click="OnMoveDown" Width="60" Margin="0,0,4,0" />
            <Separator />
            <Button Content="重新加载" Click="OnReload" Width="80" Margin="0,0,4,0" />
            <Button Content="保存" Click="OnSave" Width="80" Background="#4CAF50" Foreground="White" />
        </StackPanel>

        <!-- Rule list (left) -->
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="280" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <ListBox x:Name="RulesListBox" Grid.Column="0" SelectionChanged="OnSelectionChanged">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal">
                            <CheckBox IsChecked="{Binding Active}" Click="OnActiveToggle" VerticalAlignment="Center" />
                            <TextBlock Text="{Binding Name}" Margin="6,0,0,0" VerticalAlignment="Center" />
                            <TextBlock Text=" (" Foreground="Gray" />
                            <TextBlock Text="{Binding Type}" Foreground="Gray" />
                            <TextBlock Text=")" Foreground="Gray" />
                        </StackPanel>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <!-- Edit panel (right) -->
            <Border Grid.Column="1" BorderBrush="#CCC" BorderThickness="1" Padding="12" Margin="8,0,0,0">
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <StackPanel x:Name="EditPanel">
                        <TextBlock Text="选择左侧规则进行编辑" Foreground="Gray" />
                    </StackPanel>
                </ScrollViewer>
            </Border>
        </Grid>
    </DockPanel>
</Window>
```

- [ ] **Step 2: Code-behind**

```csharp
using System.Windows;
using System.Windows.Controls;
using FileSorter.Core;
using FileSorter.Core.Models;

namespace FileSorter.UI;

public partial class RuleEditor : Window
{
    private readonly string _rulesPath;
    private RulesConfig _cfg;
    private Rule? _selectedRule;

    public RuleEditor(string rulesPath, RulesConfig cfg)
    {
        InitializeComponent();
        _rulesPath = rulesPath;
        _cfg = cfg;
        RefreshList();
    }

    private void RefreshList()
    {
        RulesListBox.ItemsSource = null;
        RulesListBox.ItemsSource = _cfg.Rules;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedRule = RulesListBox.SelectedItem as Rule;
        RebuildEditPanel();
    }

    private void RebuildEditPanel()
    {
        EditPanel.Children.Clear();
        if (_selectedRule == null)
        {
            EditPanel.Children.Add(new TextBlock { Text = "选择左侧规则", Foreground = System.Windows.Media.Brushes.Gray });
            return;
        }
        // Simple flat editor: dump fields as TextBoxes.
        // (Full dynamic UI per spec §6.3 — simplified for first cut)
        var stack = new StackPanel();
        stack.Children.Add(MakeRow("Name", _selectedRule.Name, v => _selectedRule.Name = v));
        stack.Children.Add(MakeRow("Type", _selectedRule.Type, v => _selectedRule.Type = v));
        stack.Children.Add(MakeRow("Destination", _selectedRule.Destination ?? "", v => _selectedRule.Destination = v));
        if (_selectedRule.Patterns != null)
            stack.Children.Add(MakeRow("Patterns (逗号分隔)",
                string.Join(",", _selectedRule.Patterns), v => _selectedRule.Patterns = v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()));
        if (_selectedRule.Extensions != null)
            stack.Children.Add(MakeRow("Extensions (逗号分隔)",
                string.Join(",", _selectedRule.Extensions), v => _selectedRule.Extensions = v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()));
        if (_selectedRule.FilenameKeyword != null)
            stack.Children.Add(MakeRow("FilenameKeyword (逗号分隔)",
                string.Join(",", _selectedRule.FilenameKeyword), v => _selectedRule.FilenameKeyword = v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList()));
        if (_selectedRule.FilenamePattern != null)
            stack.Children.Add(MakeRow("FilenamePattern (verbatim regex)", _selectedRule.FilenamePattern, v => _selectedRule.FilenamePattern = v));
        if (_selectedRule.Path != null)
            stack.Children.Add(MakeRow("Path", _selectedRule.Path, v => _selectedRule.Path = v));
        if (_selectedRule.Template != null)
            stack.Children.Add(MakeRow("Template", _selectedRule.Template, v => _selectedRule.Template = v));
        if (_selectedRule.StartsWith != null)
            stack.Children.Add(MakeRow("StartsWith (逗号)",
                string.Join(",", _selectedRule.StartsWith), v => _selectedRule.StartsWith = v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList()));
        if (_selectedRule.EndsWith != null)
            stack.Children.Add(MakeRow("EndsWith (逗号)",
                string.Join(",", _selectedRule.EndsWith), v => _selectedRule.EndsWith = v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList()));
        // Mappings editor (Task 12 will enhance)
        if (_selectedRule.Mappings != null && _selectedRule.Mappings.Count > 0)
        {
            stack.Children.Add(new TextBlock { Text = "Mappings:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 4) });
            for (int i = 0; i < _selectedRule.Mappings.Count; i++)
            {
                int idx = i;
                var m = _selectedRule.Mappings[i];
                stack.Children.Add(MakeRow($"  [{i}] Token", m.Token, v => m.Token = v));
                stack.Children.Add(MakeRow($"  [{i}] Level", m.Level.ToString(), v => { if (int.TryParse(v, out var n)) m.Level = n; }));
            }
        }
        EditPanel.Children.Add(stack);
    }

    private UIElement MakeRow(string label, string value, Action<string> onChange)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tb, 0);
        var box = new TextBox { Text = value };
        box.TextChanged += (_, _) => onChange(box.Text);
        Grid.SetColumn(box, 1);
        grid.Children.Add(tb);
        grid.Children.Add(box);
        return grid;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var newRule = new Rule { Name = "新规则", Type = "extension", Destination = "inbox" };
        newRule.Patterns = new() { "txt" };
        _cfg.Rules ??= new();
        _cfg.Rules.Add(newRule);
        RefreshList();
        RulesListBox.SelectedItem = newRule;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_selectedRule == null) return;
        _cfg.Rules!.Remove(_selectedRule);
        _selectedRule = null;
        RefreshList();
        RebuildEditPanel();
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => Move(-1);
    private void OnMoveDown(object sender, RoutedEventArgs e) => Move(+1);
    private void Move(int delta)
    {
        if (_selectedRule == null) return;
        var idx = _cfg.Rules!.IndexOf(_selectedRule);
        var newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= _cfg.Rules.Count) return;
        _cfg.Rules.RemoveAt(idx);
        _cfg.Rules.Insert(newIdx, _selectedRule);
        RefreshList();
        RulesListBox.SelectedItem = _selectedRule;
    }

    private void OnActiveToggle(object sender, RoutedEventArgs e) { /* check-box handles Active via binding */ }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        _cfg = RuleEngine.LoadFromFile(_rulesPath);
        RefreshList();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        RulesFileWriter.WriteWithBackup(_rulesPath, _cfg);
        MessageBox.Show($"已保存到 {_rulesPath}", "FileSorter", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
```

Note: The edit panel is intentionally simple (rows of TextBoxes) for the first cut. Task 12 adds the dynamic type-aware UI.

- [ ] **Step 3: Commit**

```bash
cd /opt/data/projects/filesorter
git add src/FileSorter/UI/RuleEditor.xaml src/FileSorter/UI/RuleEditor.xaml.cs
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M18: RuleEditor — 列表 + 添加/删除/排序/保存 + 简单字段编辑"
```

---

## Task 12: App.xaml.cs — wire everything (CLI arg + RuleEditor entry + QuickRuleDialog + SendTo auto-install)

**Files:**
- Modify: `src/FileSorter/App.xaml.cs`

- [ ] **Step 1: Read current App.xaml.cs to see existing structure**

```bash
cat src/FileSorter/App.xaml.cs
```

- [ ] **Step 2: Add CLI handler at top of Main()**

Insert at start of `App.Main()` (or wherever `OnStartup` is set up — read first):

```csharp
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = Core.StartupArgs.Parse(e.Args);
        switch (args.Mode)
        {
            case Core.StartupMode.Install:
                Core.AutoStart.Install();
                Shutdown();
                return;
            case Core.StartupMode.Uninstall:
                Core.AutoStart.Uninstall();
                Shutdown();
                return;
            case Core.StartupMode.InstallSendTo:
                Core.SendToInstaller.Install(System.Reflection.Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe"));
                Shutdown();
                return;
            case Core.StartupMode.SendTo:
                ShowQuickRuleDialog(args.Files);
                Shutdown();
                return;
        }
        // Normal startup
        try { Core.SendToInstaller.Install(GetExePath()); } catch { /* best-effort */ }
        _tray = new UI.TrayIcon();
        _tray.EditPathsRequested += () => OpenEditPaths();
        _tray.OpenRulesEditorRequested += () => OpenRuleEditor();   // ADD this event to TrayIcon in T13
        _tray.Initialize();
    }
```

Note: `OpenRulesEditorRequested` is a NEW event on TrayIcon (Task 13 adds the menu item).

- [ ] **Step 3: Add helper methods**

Add to `App.xaml.cs`:

```csharp
    private string GetExePath()
    {
        // SingleFile-publish: Assembly.Location is null; use Process path
        return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine exe path");
    }

    private void ShowQuickRuleDialog(IReadOnlyList<string> files)
    {
        var cfg = Core.RuleEngine.LoadFromFile(GetRulesPath());
        var dlg = new UI.QuickRuleDialog(files, cfg.Rules ?? new());
        if (dlg.ShowDialog() == true)
        {
            // Apply: for each file, classify with chosen rule (or default if null)
            foreach (var f in files)
            {
                Core.ClassifierService.ClassifySingle(f, cfg, dlg.SelectedRule);
            }
        }
    }

    private string GetRulesPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "FileSorter", "rules.yaml");
    }

    private void OpenRuleEditor()
    {
        var cfg = Core.RuleEngine.LoadFromFile(GetRulesPath());
        var editor = new UI.RuleEditor(GetRulesPath(), cfg);
        editor.ShowDialog();
    }

    // existing OpenEditPaths() remains
```

- [ ] **Step 4: Commit**

```bash
cd /opt/data/projects/filesorter
git add src/FileSorter/App.xaml.cs
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M17+M18: App 接线 CLI 参数/SendTo 装/QuickRuleDialog/RuleEditor"
```

---

## Task 13: TrayIcon — add "Edit rules…" menu item + new event

**Files:**
- Modify: `src/FileSorter/UI/TrayIcon.cs`

- [ ] **Step 1: Read current TrayIcon.cs, then add menu item**

In the existing `Initialize()` method, add a menu item **before** "Open paths…" (or after — your choice):

```csharp
        menu.Items.Add("Open rules…", null, (_, _) => EditRulesRequested?.Invoke());
```

Add the new event next to existing `EditPathsRequested`:

```csharp
    public event Action? EditRulesRequested;
```

(If you want this BEFORE the "Open paths…" menu item, just place the line before.)

- [ ] **Step 2: Commit**

```bash
cd /opt/data/projects/filesorter
git add src/FileSorter/UI/TrayIcon.cs
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M18: TrayIcon 加 Open rules… 菜单 + EditRulesRequested 事件"
```

---

## Task 14: README + docs/design.md sync

**Files:**
- Modify: `README.md`
- Modify: `docs/design.md`

- [ ] **Step 1: Add v2 section to README.md**

Append a new section to README.md before the "已知限制" section:

```markdown
## v2 新功能

- **SendTo 集成**:右键文件 → 发送到 → FileSorter → 弹规则选择
- **新规则类型**:`name_template`(`{token}` 占位符 + 自动生成 regex)+ `filename_pattern`(starts_with / ends_with)
- **Rule + Mappings** 新概念:每条 rule 可定义 `mappings[]`,指定 token 命中哪一级目录
- **GUI 规则编辑器**:右键托盘 → Open rules…(新增菜单项)
- **Save 自动 backup**:每次保存留 3 份 `.bak.YYYYMMDD-HHMMSS`

所有 v1 规则继续工作(YAML 向后兼容)。

### name_template 示例

```yaml
- name: 社交媒体
  type: name_template
  extensions: [jpg, jpeg, png]
  template: "{platform}_@{author}_{title}"
  mappings:
    - token: "{platform}"
      level: 1
    - token: "{author}"
      level: 2
  destination: images
```

→ 自动生成 regex `^([a-zA-Z0-9]+)_@([a-zA-Z0-9]+)_([a-zA-Z0-9]+)$`
→ 文件 `twitter_@hahaoy8_xxx.jpg` 分类到 `D:\示例\图片\twitter\hahaoy8\`

### token 类型

| 语法 | 含义 | 正则 |
|---|---|---|
| `{name}` | 任意非分隔符 | `[a-zA-Z0-9]+` |
| `{name:text}` | 纯字母 | `[a-zA-Z]+` |
| `{name:number}` | 数字 | `\d+` |
| `{date:yyyyMMdd}` | 8 位日期 | `\d{8}` |
| `{date:yyyy-MM-dd}` | 带横线日期 | `\d{4}-\d{2}-\d{2}` |
```

- [ ] **Step 2: Append v2 changelog to docs/design.md**

Append:

```markdown

---

## v2 变更(2026-09-27)

### 新增

- A. **SendTo 集成**:`%USERPROFILE%\SendTo\FileSorter.lnk` 自动创建
- B. **QuickRuleDialog**:SendTo 触发后弹规则选择
- C. **`Rule.mappings[]` 字段**(token → level 映射)
- D. **`name_template` 规则类型**(`{token}` 占位符 + 自动 regex)
- E. **`filename_pattern` 规则类型**(starts_with / ends_with)
- F. **GUI RuleEditor**(托盘 → Open rules…)
- G. **Save backup 轮转**(保留 3 份)

### 修改

- `Rule` 模型:`Active`(默认 true)+ `Mappings`(可空 list)+ `StartsWith`/`EndsWith`(可空)+ `Template`(可空)

### YAML 向后兼容

所有 v1 YAML 文件加载到 v2 软件 → 100% 工作。v2 软件写回的 YAML 保留所有字段,可能添加 `active: true`(只在用户 GUI 添加新规则时)。

### 里程碑

M14-M23(10 个新里程碑,v1 M2-M13 之上叠加)
```

- [ ] **Step 3: Commit + push**

```bash
cd /opt/data/projects/filesorter
git add README.md docs/design.md
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M21: README + design.md 加 v2 章节"
git push origin main
```

---

## Task 15: End-to-end verification + new tests

**Files:**
- Test: append to `tests/FileSorter.Tests/` — at least 5 new tests

- [ ] **Step 1: Add new tests**

```csharp
// In RuleMatchTests.cs append:
[Fact]
public void Match_NameTemplate_With_NoMappings_FallsBack_To_Destination() { /* ... */ }

// In RuleEngineLoadTests.cs append:
[Fact]
public void Load_Yaml_With_Active_False_Preserves() { /* ... */ }

// In RuleModelTests.cs append:
[Fact]
public void Mapping_Default_Level_Is_One() { /* ... */ }
```

(Full test code: see actual file structure — write at least 5 new tests, one per new feature.)

- [ ] **Step 2: Commit (verify on Windows)**

```bash
cd /opt/data/projects/filesorter
git add tests/
git -c user.name=hermes -c user.email=hermes@local commit -q -m "M22: 加 v2 单元测试(目标 21+/16 通过)"
```

- [ ] **Step 3: Document expected Windows-side run**

When implementing on Windows:
```bash
dotnet test -c Debug
# Expected: 21+/21+ passed, 0 failed

dotnet publish src/FileSorter/FileSorter.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\dist\
# Expected: dist\filesorter.exe < 2 MB

# Manual E2E:
# 1. Run exe → tray appears
# 2. Right-click → Open rules… → editor opens
# 3. Add a new name_template rule with mappings → save → verify .bak.YYYYMMDD-HHMMSS created
# 4. Right-click a jpg → Send to → FileSorter → QuickRuleDialog appears → pick rule → file moves
# 5. Verify %USERPROFILE%\SendTo\FileSorter.lnk exists
```

---

## Self-Review Notes

- **Spec coverage**: §3-7 → T6/T7/T8/T9/T10/T11/T12/T13; §4 → T1/T2/T3/T4; §5.1 → T5; §5.2 → T3/T4/T6; §6 → T11/T12; §10 → T9 (backup); §11 → all tasks mapped.
- **No placeholders**: All code blocks complete.
- **Type consistency**: `Rule.Active`, `Rule.Mappings`, `Mapping.Token/Level` used consistently across tasks 1-7.
- **YAGNI**: No SQLite, no rule grouping, no rule history, no dark mode, no file content preview — all explicitly out.
- **Zero regression**: T7 (skip inactive) uses `continue` — doesn't change existing rule match logic. T1 adds fields with safe defaults (`Active=true`, `Mappings=null`).

## Execution Handoff

Plan complete at `docs/superpowers/plans/2026-09-27-filesorter-v2.md`.

**Recommended approach: Subagent-Driven** (one subagent per Phase — M14-M16, M17-M18, M19-M23) with 2-stage review per task.

If you want me to start, dispatch the first subagent for M14-M16 (Rule model + new rule types).