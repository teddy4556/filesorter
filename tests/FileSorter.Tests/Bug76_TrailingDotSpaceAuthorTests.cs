using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

// v2.8.1 · 2026-11-13 — Bug #76 回归钉子。
//
// #76  Twitter 作者名末尾是「空格 + 句点」→ 归档目录建不出来
//      真实文件名: Twitter#𝑮𝒊𝒔𝒕𝒂𝒓𝒂 .#6997340593#20261113#071231#01.jpg
//
//      链路:
//        1. name_template 正则**匹配成功**（unicode-dash 类里 \s 和 . 都在）
//        2. NameTemplatePathBuilder 把作者段拼成目录名 → "...\𝑮𝒊𝒔𝒕𝒂𝒓𝒂 ."
//        3. Windows 建目录时**自动剥掉末尾的空格和点** → 盘上实际是 "...\𝑮𝒊𝒔𝒕𝒂𝒓𝒂"
//        4. 拼路径用的仍是 "...\𝑮𝒊𝒔𝒕𝒂𝒓𝒂 ." → File.Move 找不到该目录
//           → "Could not find a part of the path." → 归档失败，文件永远躺在待整理
//
//      判据:作者目录名必须与 Windows 实际落盘的目录名一致（末尾空格/点被归一）。
public class Bug76_TrailingDotSpaceAuthorTests
{
    // 用户真实文件名（2026-11-13 16:48 落盘，3 张同名不同序号）
    private const string RealFileName =
        "Twitter#𝑮𝒊𝒔𝒕𝒂𝒓𝒂 .#6997340593#20261113#071231#01.jpg";

    private const string TwitterYaml = """
        version: 1
        default_action: move
        conflict_strategy: rename
        log_level: info
        destinations:
          twitter: D:\tw
        rules:
          - name: Twitter 图片
            type: name_template
            active: true
            mappings:
              - token: '{user_name}'
                level: 1
            extensions: [jpg, jpeg, png, mp4, webp]
            template: Twitter#{user_name:unicode-dash}#{post_id:number}#{date:yyyyMMdd}#{date:HHmmss}#{index:number}.{ext:any}
            destination: twitter
        """;

    // ── 根因 #1：作者段末尾的空格/点会让目录名和盘上真实目录名对不上 ──────────

    [Fact]
    public void BuildPath_AuthorEndingWithSpaceAndDot_IsNormalized()
    {
        var mappings = new List<Mapping> { new() { Token = "{user_name}", Level = 1 } };

        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: @"D:\tw",
            template: "Twitter#{user_name:unicode-dash}#{post_id:number}",
            mappings: mappings,
            captured: new[] { "𝑮𝒊𝒔𝒕𝒂𝒓𝒂 .", "6997340593" });

        // 末尾不得留空格/点 —— Windows 建目录时会剥掉，导致拼接路径找不到目录
        Assert.Equal(@"D:\tw\𝑮𝒊𝒔𝒕𝒂𝒓𝒂", path);
    }

    [Fact]
    public void BuildPath_AuthorEndingWithDotOnly_IsNormalized()
    {
        var mappings = new List<Mapping> { new() { Token = "{user_name}", Level = 1 } };

        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: @"D:\tw",
            template: "Twitter#{user_name:unicode-dash}",
            mappings: mappings,
            captured: new[] { "路昕-." });

        Assert.Equal(@"D:\tw\路昕-", path);
    }

    [Fact]
    public void BuildPath_AuthorWithInnerSpaceAndDot_IsPreserved()
    {
        // 只有**末尾**的空格/点要归一，中间的必须原样保留
        var mappings = new List<Mapping> { new() { Token = "{user_name}", Level = 1 } };

        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: @"D:\tw",
            template: "Twitter#{user_name:unicode-dash}",
            mappings: mappings,
            captured: new[] { "a.b c" });

        Assert.Equal(@"D:\tw\a.b c", path);
    }

    [Fact]
    public void BuildPath_AuthorOnlySpaceAndDot_DoesNotBecomeEmptySegment()
    {
        // 全是空格/点 → 归一后为空 → 必须兜底成一个非空名，
        // 否则拼出的路径会退化成 "...\tw\"（等于没建作者目录）
        var mappings = new List<Mapping> { new() { Token = "{user_name}", Level = 1 } };

        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: @"D:\tw",
            template: "Twitter#{user_name:unicode-dash}",
            mappings: mappings,
            captured: new[] { " . " });

        var lastSegment = path[(path.LastIndexOf('\\') + 1)..];
        Assert.NotEmpty(lastSegment);
    }

    // ── 根因 #2：段名整体是 "." / ".." —— 归档会越位到上级目录 ──────────────
    //   2026-11-13 实测:Twitter#..#5908773273#20261112#100159#01.jpg
    //   作者段 ".." → 路径 DCIM\Twitter\.. → 文件落进 DCIM\(连平台文件夹都跳出去了)

    [Fact]
    public void BuildPath_AuthorIsDotDot_DoesNotEscapeToParent()
    {
        var mappings = new List<Mapping> { new() { Token = "{user_name}", Level = 1 } };

        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: @"D:\dcim\twitter",
            template: "Twitter#{user_name:unicode-dash}",
            mappings: mappings,
            captured: new[] { ".." });

        // 绝不能拼出 D:\dcim\twitter\.. —— 那会让文件落到 D:\dcim\
        Assert.Equal(@"D:\dcim\twitter\_", path);
        Assert.DoesNotContain(@"..", path);
    }

    [Fact]
    public void BuildPath_AuthorIsDot_DoesNotCollapseToPlatformRoot()
    {
        var mappings = new List<Mapping> { new() { Token = "{user_name}", Level = 1 } };

        var path = NameTemplatePathBuilder.BuildPath(
            baseDestination: @"D:\dcim\twitter",
            template: "Twitter#{user_name:unicode-dash}",
            mappings: mappings,
            captured: new[] { "." });

        // 绝不能拼出 D:\dcim\twitter\. —— 那等于没建作者目录
        Assert.Equal(@"D:\dcim\twitter\_", path);
    }

    // ── 根因 #3：真实文件名必须能匹配上（确保正则侧不是问题） ────────────────

    [Fact]
    public void Compile_TwitterTemplate_MatchesRealUserFileName()
    {
        var re = NameTemplateCompiler.CompileToRegex(
            "Twitter#{user_name:unicode-dash}#{post_id:number}#{date:yyyyMMdd}#{date:HHmmss}#{index:number}.{ext:any}");

        var m = System.Text.RegularExpressions.Regex.Match(RealFileName, re);
        Assert.True(m.Success, "真实文件名应当匹配 —— 匹配不上说明是正则侧的问题，不是路径侧");
        Assert.Equal("6997340593", m.Groups[2].Value);
        Assert.Equal("20261113", m.Groups[3].Value);
        Assert.Equal("071231", m.Groups[4].Value);
    }

    [Fact]
    public void Compile_TwitterTemplate_MatchesDotDotAuthorFileName()
    {
        // Twitter#..#5908773273#20261112#100159#01.jpg 的作者段就是 ".."
        var re = NameTemplateCompiler.CompileToRegex(
            "Twitter#{user_name:unicode-dash}#{post_id:number}#{date:yyyyMMdd}#{date:HHmmss}#{index:number}.{ext:any}");

        var m = System.Text.RegularExpressions.Regex.Match(
            "Twitter#..#5908773273#20261112#100159#01.jpg", re);
        Assert.True(m.Success);
        Assert.Equal("..", m.Groups[1].Value);
    }

    // ── 端到端：归档必须真成功（这是用户看到的症状） ────────────────────────

    [Fact]
    public void Classify_RealTwitterFileName_MovesSuccessfully()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "fs-bug76-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            // yaml 里 destinations.twitter = D:\tw → 替换成 tmp，落点就是 tmp 本身
            var cfg = RuleEngine.LoadFromString(TwitterYaml.Replace(@"D:\tw", tmp.Replace("\\", "\\\\")));
            var src = Path.Combine(tmp, RealFileName);
            File.WriteAllText(src, "x");

            var svc = new ClassifierService(cfg, dryRun: false);
            var result = svc.ClassifyOne(src);

            Assert.True(result.Success, $"归档失败: {result.Error}");
            Assert.False(File.Exists(src));

            // 文件必须落在「作者目录」里，且该目录名要与 Windows 实际落盘的一致
            var authorDirs = Directory.GetDirectories(tmp);
            Assert.Single(authorDirs);
            var files = Directory.GetFiles(authorDirs[0]);
            Assert.Single(files);
            Assert.Equal(RealFileName, Path.GetFileName(files[0]));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    [Fact]
    public void Classify_DotDotAuthorFileName_StaysInsideDestination()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "fs-bug76b-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            var cfg = RuleEngine.LoadFromString(TwitterYaml.Replace(@"D:\tw", tmp.Replace("\\", "\\\\")));
            const string dotDotName = "Twitter#..#5908773273#20261112#100159#01.jpg";
            var src = Path.Combine(tmp, dotDotName);
            File.WriteAllText(src, "x");

            var svc = new ClassifierService(cfg, dryRun: false);
            var result = svc.ClassifyOne(src);

            Assert.True(result.Success, $"归档失败: {result.Error}");
            Assert.False(File.Exists(src));

            // 关键断言：文件必须还在 tmp 下面，不能跳到 tmp 的上级
            var authorDirs = Directory.GetDirectories(tmp);
            Assert.Single(authorDirs);
            var files = Directory.GetFiles(authorDirs[0]);
            Assert.Single(files);
            Assert.Equal(dotDotName, Path.GetFileName(files[0]));

            // tmp 的上级目录不能出现这个文件（越位检查）
            // ⚠ 不用 Directory.GetFiles(parent, dotDotName) —— searchPattern 里的 ".."
            //   会被 .NET 当成路径片段,匹配到 Temp\Twitter#.. 这类无关残留,产生假失败。
            //   越位检查必须用精确路径 + File.Exists。
            var parent = Directory.GetParent(tmp)!.FullName;
            Assert.False(File.Exists(Path.Combine(parent, dotDotName)),
                "文件越位到上级目录了 —— '..' 作者段被当成路径导航");
            Assert.False(File.Exists(Path.Combine(parent, "Twitter#..#5908773273#20261112#100159#01.jpg")));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}
