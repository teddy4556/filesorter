using FileSorter.Core;
using FileSorter.Core.Models;
using Xunit;

namespace FileSorter.Tests;

// v2.8 · 2026-10-14 — 三个 bug 的回归钉子，全部用用户真实文件名。
//
// #73  `_N` 重名后缀 —— instagram#…#20260930_1.jpg
//      正则锚点只认 " (N)" 和 "-N_N_N"，不认 "_N"（第三方下载器/同步盘产物），
//      结果 {date:yyyyMMdd} 后面多出的 _1 直接把整条规则打断。
//
// #74  双扩展名 —— …#2026-10-13.zh.srt
//      Path.GetFileNameWithoutExtension 只剥一层，stem 尾部残留 ".zh"；
//      而 StripTrailingExtFromRegex 已把 .{ext} 整段剥掉，一边剥一边不剥，永远匹配不上。
//
// #75  default 规则短路
//      "default"(UI 里叫「默认(兜底)」)在 foreach 里立即 return，列表位置变成硬闸门。
public class Bug73_74_75_RegressionTests
{
    private const string IgYaml = """
        version: 1
        default_action: move
        conflict_strategy: rename
        log_level: info
        destinations:
          instagram: D:\ig
        rules:
          - name: Instagram 图片
            type: name_template
            active: true
            mappings:
              - token: '{username}'
                level: 1
            extensions: [jpg, jpeg, png, mp4, webp]
            template: instagram#{username}#{uid:number}#{shortcode}#{first_name:unicode-dash}#{date:yyyyMMdd}
            destination: instagram
        """;

    private const string XhsYaml = """
        version: 1
        default_action: move
        conflict_strategy: rename
        log_level: info
        destinations:
          xhs: D:\xhs
        rules:
          - name: 小红书
            type: name_template
            active: true
            mappings:
              - token: '{author}'
                level: 1
            extensions: [jpg, jpeg, png, mp4, webp, mov, srt]
            template: 小红书#{author:unicode-dash}#{title:unicode-dash}#{id6}#{seq:number}#{date:yyyy-MM-dd}.{ext}
            destination: xhs
        """;

    // ── #73 ────────────────────────────────────────────────────────────────

    [Fact]
    public void Compile_Anchor_Accepts_Underscore_Dedup_Suffix()
    {
        var re = NameTemplateCompiler.CompileToRegex("img-{n:number}");

        // 新能力：_1 / _1_2
        Assert.True(System.Text.RegularExpressions.Regex.Match("img-42_1", re).Success);
        Assert.True(System.Text.RegularExpressions.Regex.Match("img-42_1_2", re).Success);

        // 回归：原有三种后缀 + 无后缀必须继续成立
        Assert.True(System.Text.RegularExpressions.Regex.Match("img-42", re).Success);
        Assert.True(System.Text.RegularExpressions.Regex.Match("img-42 (1)", re).Success);
        Assert.True(System.Text.RegularExpressions.Regex.Match("img-42-1_2", re).Success);
    }

    [Fact]
    public void Compile_Anchor_Still_Rejects_NonDedup_Tail()
    {
        // 修 _N 不能把锚点放飞成"什么都收尾"
        var re = NameTemplateCompiler.CompileToRegex("img-{n:number}");
        Assert.False(System.Text.RegularExpressions.Regex.Match("img-42-copy", re).Success);
        Assert.False(System.Text.RegularExpressions.Regex.Match("img-42_v2", re).Success);
        Assert.False(System.Text.RegularExpressions.Regex.Match("img-42_", re).Success);
    }

    [Fact]
    public void E2E_Instagram_Real_File_With_Underscore_Dedup_Suffix()
    {
        // 真实文件（2026-10-14 抓自用户 instagram 目录，同名重复图）
        var r = RuleEngine.Match(RuleEngine.LoadFromString(IgYaml),
            @"C:\inbox\instagram#2.jug_#4287884466#Dd6aPWokt3M#828333208#20260930_1.jpg");

        Assert.NotNull(r);
        Assert.Equal("Instagram 图片", r!.RuleName);
        Assert.Equal(@"D:\ig\2.jug_", r.DestinationPath);
    }

    [Fact]
    public void E2E_Instagram_No_Suffix_Still_Matches()
    {
        // 同一条规则，无 _N 后缀时行为不能被这次修改带歪
        var r = RuleEngine.Match(RuleEngine.LoadFromString(IgYaml),
            @"C:\inbox\instagram#2.jug_#4287884466#Dd6aPWokt3M#828333208#20260930.jpg");

        Assert.NotNull(r);
        Assert.Equal(@"D:\ig\2.jug_", r!.DestinationPath);
    }

    // ── #74 ────────────────────────────────────────────────────────────────

    [Fact]
    public void E2E_Xiaohongshu_DoubleExtension_Subtitle_Matches()
    {
        // 真实文件：小红书下载器产出的双语字幕（.zh.srt 是双扩展名）
        var r = RuleEngine.Match(RuleEngine.LoadFromString(XhsYaml),
            @"C:\inbox\小红书#罗雅熙#“敬自由与爱 做远山般坚韧的女性”#010d7e#001#2026-10-13.zh.srt");

        Assert.NotNull(r);
        Assert.Equal("小红书", r!.RuleName);
        Assert.Equal(@"D:\xhs\罗雅熙", r.DestinationPath);
    }

    [Fact]
    public void E2E_Xiaohongshu_SingleExtension_Unaffected()
    {
        // 回归：单扩展名文件的 author 捕获不能因多层候选而错位
        var r = RuleEngine.Match(RuleEngine.LoadFromString(XhsYaml),
            @"C:\inbox\小红书#179的月大宝#🖤🍂🎧#032200#001#2026-10-13.webp");

        Assert.NotNull(r);
        Assert.Equal(@"D:\xhs\179的月大宝", r!.DestinationPath);
    }

    // ── #75 ────────────────────────────────────────────────────────────────

    [Fact]
    public void Default_Rule_First_Does_Not_Swallow_Later_Specific_Rules()
    {
        var cfg = RuleEngine.LoadFromString("""
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              images: D:\img
              ig: D:\ig
            rules:
              - name: 其他
                type: default
                destination: images
              - name: Instagram 图片
                type: name_template
                active: true
                mappings:
                  - token: '{username}'
                    level: 1
                extensions: [jpg]
                template: instagram#{username}#{date:yyyyMMdd}
                destination: ig
            """);

        // 兜底排第 1 位也必须让位给具体规则
        var r = RuleEngine.Match(cfg, @"C:\inbox\instagram#someone#20260930.jpg");
        Assert.NotNull(r);
        Assert.Equal("Instagram 图片", r!.RuleName);
        Assert.Equal(@"D:\ig\someone", r.DestinationPath);

        // 真正没有规则命中时才轮到兜底
        var fb = RuleEngine.Match(cfg, @"C:\inbox\readme.txt");
        Assert.NotNull(fb);
        Assert.Equal("其他", fb!.RuleName);
        Assert.Equal(@"D:\img", fb.DestinationPath);
    }

    [Fact]
    public void Inactive_Default_Rule_Is_Not_Used_As_Fallback()
    {
        var cfg = RuleEngine.LoadFromString("""
            version: 1
            default_action: move
            conflict_strategy: rename
            log_level: info
            destinations:
              images: D:\img
            rules:
              - name: 其他
                type: default
                active: false
                destination: images
            """);

        Assert.Null(RuleEngine.Match(cfg, @"C:\inbox\readme.txt"));
    }
}
