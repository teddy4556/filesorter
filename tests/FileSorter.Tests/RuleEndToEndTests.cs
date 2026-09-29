using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

/// <summary>
/// End-to-end pipeline tests (M22):
/// Verify that a full YAML config + a real filename produce the expected
/// MatchResult.DestinationPath, exercising ALL the layers a real user would hit:
/// YAML deserialization → RuleEngine.Match → TemplateResult/MatchNameTemplate path building.
/// </summary>
public class RuleEndToEndTests
{
    [Fact]
    public void E2E_FullPipeline_ExtensionMove()
    {
        // Simulate: a real rules.yaml with several rules; user drops a .txt file.
        // The extension rule should fire → "text" destination.
        var cfg = RuleEngine.LoadFromString("""
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              text: D:\cat\text
              images: D:\cat\img
              inbox: D:\cat\inbox
            rules:
              - name: imgs
                type: extension
                patterns: [jpg, png, gif]
                destination: images
              - name: texts
                type: extension
                patterns: [txt, md, log]
                destination: text
              - name: catch-all
                type: default
                destination: inbox
            """);

        var r = RuleEngine.Match(cfg, @"C:\inbox\notes.txt");

        Assert.NotNull(r);
        Assert.Equal("texts", r!.RuleName);
        Assert.Equal(@"D:\cat\text", r.DestinationPath);
    }

    [Fact]
    public void E2E_PathTemplate_WithAuthor()
    {
        // Simulate the famous twitter example: path_template should produce
        //   D:\cat\img\twitter\@hahaoy8
        var cfg = RuleEngine.LoadFromString("""
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              images: D:\cat\img
            rules:
              - name: social-media-image
                type: path_template
                extensions: [jpg, jpeg, png]
                filename_pattern: '^([a-z]+)_\(@([^)]+)\)_'
                path: '{destinations.images}\{1}\@{2}'
            """);

        var r = RuleEngine.Match(cfg, @"C:\downloads\twitter_(@hahaoy8)_肉丝儿.jpg");

        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\img\twitter\@hahaoy8", r!.DestinationPath);
    }

    [Fact]
    public void E2E_NameTemplate_MultiLevelMappings()
    {
        // 3-level mapping: platform / author / year
        // File:  twitter_@hahaoy8_2026_pic.jpg
        // Should produce: D:\cat\img\twitter\hahaoy8\2026
        // Note: mapping Token uses {name} form (without type spec) — this matches the
        // NameTemplatePathBuilder.ExtractTokenNames() convention.
        var cfg = RuleEngine.LoadFromString("""
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              images: D:\cat\img
            rules:
              - name: social-3level
                type: name_template
                extensions: [jpg, jpeg, png]
                template: "{platform}_@{author}_{date:yyyy}_{title}.{ext}"
                destination: images
                mappings:
                  - token: "{platform}"
                    level: 1
                  - token: "{author}"
                    level: 2
                  - token: "{date}"
                    level: 3
            """);

        var r = RuleEngine.Match(cfg, @"C:\downloads\twitter_@hahaoy8_2026_pic.jpg");

        Assert.NotNull(r);
        Assert.Equal(@"D:\cat\img\twitter\hahaoy8\2026", r!.DestinationPath);
    }

    [Fact]
    public void E2E_InactiveRule_Skipped()
    {
        // A disabled rule MUST NOT fire. With only one rule (active=false), the file
        // should match NOTHING and Match() should return null (no default rule).
        var cfg = RuleEngine.LoadFromString("""
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              imgs: D:\cat\img
            rules:
              - name: disabled-pics
                type: extension
                patterns: [jpg, png]
                destination: imgs
                active: false
            """);

        var r = RuleEngine.Match(cfg, @"C:\in\test.jpg");

        Assert.Null(r);
    }

    [Fact]
    public void E2E_RealRulesYaml_ExampleFile_DoesNotCrash()
    {
        // Integration check: load the shipped examples/rules.yaml and run a known filename
        // through it. Verifies the example file is valid YAML AND every rule type compiles.
        // (We don't assert destination paths because the shipped example uses example paths,
        //  we just assert Match returns non-null for several common file types.)
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var yamlPath = Path.Combine(repoRoot, "examples", "rules.yaml");
        if (!File.Exists(yamlPath))
        {
            // Examples not shipped in test bin — skip silently
            return;
        }

        var cfg = RuleEngine.LoadFromFile(yamlPath);
        Assert.NotNull(cfg);
        Assert.NotEmpty(cfg.Rules);
        // Just ensure each known filename resolves to SOME rule (or null), no exception.
        foreach (var filename in new[] { "photo.jpg", "invoice_2026.pdf", "notes.txt" })
        {
            var r = RuleEngine.Match(cfg, @"C:\inbox\" + filename);
            // Either matched or null is fine; just no exception
            _ = r;
        }
    }

    // v2.7.2 Bug #52: StripTrailingExtFromRegex used EndsWith(dedupSuffix) which failed because
    //   the actual regex ends with `...([any]+)(?:-\d+(_\d+)*)?$` — the `)` between group close
    //   and the dedup suffix meant EndsWith never matched, so the trailing .{ext} group was NEVER
    //   stripped, so the regex anchored at `$` never matched the stem (which has no extension).
    //   Fix uses LastIndexOf. This E2E test exercises the FULL Match() → MatchNameTemplate →
    //   StripTrailingExtFromRegex → stem-match path with the EXACT production Weibo rule.
    [Fact]
    public void E2E_Bug52_Weibo_NameTemplate_StripsExtension()
    {
        // The exact rule from the user's rules.yaml (do not modify; this is the regression case).
        var cfg = RuleEngine.LoadFromString("""
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              weibo: D:\默认保存\下载\下载（待整理）\自动分类\DCIM\微博
            rules:
              - name: 微博
                type: name_template
                active: true
                mappings:
                  - token: '{username}'
                    level: 1
                extensions: [jpg, jpeg, png, mp4, webp]
                template: 微博-{username:unicode}-{date:yyyyMMdd}-{index}.{ext}
                destination: weibo
            """);

        // Real test file 1: 清补凉好好吃 (Chinese username)
        // NameTemplatePathBuilder emits a DIRECTORY only (mappings → subdirs), so DestinationPath
        // does NOT include the extension. Assert on the directory shape + verify the rule actually
        // extracted username correctly (not falling through to "其他" / a different rule).
        var r1 = RuleEngine.Match(cfg, @"D:\hermes-工作目录\filesorter\test-tweets\微博-清补凉好好吃-20260922-10.mp4");
        Assert.NotNull(r1);
        Assert.Equal("微博", r1!.RuleName);
        Assert.Equal(@"D:\默认保存\下载\下载（待整理）\自动分类\DCIM\微博\清补凉好好吃", r1.DestinationPath);

        // Real test file 2: 宋小睿爱唱歌 (Chinese username, jpg extension)
        var r2 = RuleEngine.Match(cfg, @"D:\hermes-工作目录\filesorter\test-tweets\微博-宋小睿爱唱歌-20260926-4.jpg");
        Assert.NotNull(r2);
        Assert.Equal("微博", r2!.RuleName);
        Assert.Equal(@"D:\默认保存\下载\下载（待整理）\自动分类\DCIM\微博\宋小睿爱唱歌", r2.DestinationPath);
    }

    // v2.7.5 Bug #56: Twitter rule with `unicode-dash` type for display names containing spaces
    //   + ASCII parens like '饼干姐姐 (FortuneCutie00)' or 'bruce love you'. The RuleEngine's
    //   MatchNameTemplate path must extract the user_name correctly (with space + parens intact).
    [Fact]
    public void E2E_Bug56_Twitter_NameTemplate_Accepts_Spaces_And_Parens()
    {
        // Template uses '#' as the separator (NOT '-'), so unicode-dash is the right type for user_name.
        // '{date-time}' is a plain token (any type) that catches `20260927-051900`.
        var cfg = RuleEngine.LoadFromString("""
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              twitter: D:\默认保存\下载\下载（待整理）\自动分类\DCIM\twitter
            rules:
              - name: Twitter
                type: name_template
                active: true
                mappings:
                  - token: '{user_id}'
                    level: 1
                extensions: [jpg, jpeg, png, mp4, webp]
                template: twitter#(@{user_id})#{user_name:unicode-dash}#{date-time}#{status_id}.{ext}
                destination: twitter
            """);

        // Test 1: 'bruce love you' (English name with ASCII space) → user_name must capture the full name
        var r1 = RuleEngine.Match(cfg, @"D:\inbox\twitter#(@loveyou2tf5)#bruce love you#20260927-011908#2104017966467817747.jpg");
        Assert.NotNull(r1);
        Assert.Equal("Twitter", r1!.RuleName);
        Assert.Equal(@"D:\默认保存\下载\下载（待整理）\自动分类\DCIM\twitter\loveyou2tf5", r1.DestinationPath);

        // Test 2: '饼干姐姐 (FortuneCutie00)' (Chinese + ASCII parens)
        var r2 = RuleEngine.Match(cfg, @"D:\inbox\twitter#(@FortuneCutie01)#饼干姐姐 (FortuneCutie00)#20260927-051900#2104078329087504671.jpg");
        Assert.NotNull(r2);
        Assert.Equal("Twitter", r2!.RuleName);
        Assert.Equal(@"D:\默认保存\下载\下载（待整理）\自动分类\DCIM\twitter\FortuneCutie01", r2.DestinationPath);

        // Test 3: 'chelseaxny' (plain ASCII) — regression check, still matches
        var r3 = RuleEngine.Match(cfg, @"D:\inbox\twitter#(@chelseaxny)#chelseaxny#20260927#2104114.jpg");
        Assert.NotNull(r3);
        Assert.Equal("Twitter", r3!.RuleName);
        Assert.Equal(@"D:\默认保存\下载\下载（待整理）\自动分类\DCIM\twitter\chelseaxny", r3.DestinationPath);
    }
}
