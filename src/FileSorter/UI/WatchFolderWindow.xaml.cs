using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FileSorter.Core;
using FileSorter.Core.Models;

// ⚠️ 本项目 csproj 同时开 UseWPF + UseWindowsForms,裸写 Brush / Color / Button / Panel
// 会在 System.Windows.Media 和 System.Drawing 之间报 CS0104 歧义。
// 这里显式起别名锁定 WPF 版本。FolderBrowserDialog 用全限定名 System.Windows.Forms.* 避开。
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Panel = System.Windows.Controls.Panel;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Orientation = System.Windows.Controls.Orientation;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using FontFamily = System.Windows.Media.FontFamily;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Application = System.Windows.Application;

namespace FileSorter.UI;

/// <summary>
/// 自动归类管理窗口(毛玻璃)。
///
/// 数据全部来自真实的 rules.yaml + WatchFolderService 运行时统计 —— 不是示例值。
/// 界面按秒刷新统计数字,配置改动立即写回 rules.yaml 并热重载监听。
/// </summary>
public partial class WatchFolderWindow : Window
{
    private readonly string _rulesPath;
    private readonly WatchFolderService? _service;
    private readonly DispatcherTimer _ticker;
    private RulesConfig _cfg;

    public WatchFolderWindow(string rulesPath, WatchFolderService? service)
    {
        _rulesPath = rulesPath;
        _service = service;
        InitializeComponent();

        try { _cfg = RuleEngine.LoadFromFile(rulesPath); }
        catch { _cfg = new RulesConfig { Destinations = new(), Rules = new() }; }

        // 毛玻璃必须在窗口句柄创建后、显示前设置
        Loaded += (_, _) => ApplyGlass();
        SourceInitialized += (_, _) => ApplyGlass();

        BindTitleBarDrag();

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) => RefreshLive();
        _ticker.Start();

        BuildHead();
        BuildContent();
        Closed += (_, _) => _ticker.Stop();
    }

    private void ApplyGlass()
    {
        try
        {
            if (Dwm.TryEnableLegacyBlur(this))
            {
                // 半透明窗口配深色标题栏图标才看得清
                Dwm.SetDarkTitleBar(this, true);
            }
            Dwm.TrySetRoundedCorners(this);
        }
        catch { /* 材质失败不影响功能 */ }
    }

    // ================= 标题栏交互 =================

    /// <summary>
    /// 标题栏拖动。用 code-behind 挂事件而不是 XAML 的 InputBindings ——
    /// XAML 里写 MouseButton="Left" 会报 MC3072。
    /// 挂在 TitleBar Border 上而不是整窗,这样点右侧的最小化/最大化/关闭按钮
    /// 不会触发 DragMove(DragMove 会吃掉点击)。
    /// </summary>
    private void BindTitleBarDrag()
    {
        if (TitleBar is null) return;
        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                OnMaximize(null!, new RoutedEventArgs());
                return;
            }
            if (e.ButtonState == MouseButtonState.Pressed) OnTitleDrag(null!, e);
        };
    }

    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch { /* 正在最大化/恢复时会抛,忽略 */ }
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>当前挂在 Overlay 上的对话框。
    /// ⚠️ 必须持有引用并在关闭时移除 —— ShowEditor 每次都会 RootGrid.Children.Add(dialog),
    /// 不移除的话旧 dialog 会一直叠在最顶层,下次打开的新 dialog 跟它完全重叠,
    /// 表现为「点调整没反应」和「保存后不关闭」。</summary>
    private Border? _dlg;

    private void CloseEditor()
    {
        Overlay.Visibility = Visibility.Collapsed;
        if (_dlg is not null)
        {
            RootGrid.Children.Remove(_dlg);
            _dlg = null;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnOverlayClick(object sender, MouseButtonEventArgs e) => CloseEditor();

    // ================= 顶部操作区 =================

    private void BuildHead()
    {
        HeadPanel.Children.Clear();

        HeadPanel.Children.Add(MakeText("自动归类", 19, FontWeights.SemiBold, TextPrimary()));
        HeadPanel.Children.Add(MakeText(
            "指定文件夹后,文件放进去就自动归类 —— 不用再右键发送到 FileSorter",
            12.5, FontWeights.Normal, TextTertiary(), wrap: true, margin: new Thickness(0, 5, 0, 14)));

        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(MakeButton("+  添加监听目录", PrimaryButtonStyle(), OnAddFolder));

        var pauseBtn = MakeButton(_service?.Enabled == false ? "▶  恢复运行" : "⏸  全部暂停",
                                  _service?.Enabled == false ? BaseButtonStyle() : WarnButtonStyle(),
                                  OnTogglePause);
        pauseBtn.Name = "PauseButton";
        bar.Children.Add(pauseBtn);

        bar.Children.Add(MakeButton("打开 rules.yaml", BaseButtonStyle(), (_, _) =>
        {
            try { Process.Start("explorer.exe", $"\"{_rulesPath}\""); } catch { }
        }));
        bar.Children.Add(MakeButton("查看日志", BaseButtonStyle(), (_, _) =>
        {
            var log = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FileSorter", "filesorter.log");
            try { Process.Start("notepad.exe", $"\"{log}\""); } catch { }
        }));

        HeadPanel.Children.Add(bar);
    }

    private void OnTogglePause(object sender, RoutedEventArgs e)
    {
        if (_service is null)
        {
            MessageBox.Show(this, "自动归类服务未初始化。", "FileSorter",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _service.Enabled = !_service.Enabled;
        BuildHead();          // 按钮文案跟着变
        BuildContent();
    }

    // ================= 内容区 =================

    private void BuildContent()
    {
        ContentPanel.Children.Clear();

        // 概览三卡
        ContentPanel.Children.Add(BuildOverviewCards());

        // 列表标题
        var listHead = new Grid { Margin = new Thickness(0, 18, 0, 10) };
        listHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        listHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(MakeText("监听目录", 13, FontWeights.SemiBold, TextPrimary()));
        var countBadge = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(8, 1, 8, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = _cfg.WatchFolders.Count.ToString(),
                FontSize = 11,
                Foreground = TextTertiary()
            }
        };
        left.Children.Add(countBadge);
        listHead.Children.Add(left);
        var hint = new TextBlock
        {
            Text = "点「调整」改参数 · 点「移除」删配置",
            FontSize = 11.5,
            Foreground = TextDisabled(),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(hint, 1);
        listHead.Children.Add(hint);
        ContentPanel.Children.Add(listHead);

        // 目录行
        var folders = _cfg.WatchFolders;
        if (folders.Count == 0)
        {
            ContentPanel.Children.Add(BuildEmptyState());
        }
        else
        {
            var perFolder = _service?.PerFolderCount ?? new Dictionary<string, int>();
            for (var i = 0; i < folders.Count; i++)
            {
                perFolder.TryGetValue(folders[i].Path, out var n);
                ContentPanel.Children.Add(BuildFolderRow(folders[i], i, n));
            }
        }

        // 说明
        ContentPanel.Children.Add(BuildFootnote());
    }

    private UIElement BuildOverviewCards()
    {
        var wrap = new StackPanel { Orientation = Orientation.Horizontal };
        var svc = _service;

        string lastText;
        if (svc is null) lastText = "—";
        else if (svc.LastProcessedUtc == DateTime.MinValue) lastText = "尚未处理";
        else
        {
            var ago = DateTime.UtcNow - svc.LastProcessedUtc;
            lastText = ago.TotalSeconds < 60
                ? $"{(int)ago.TotalSeconds} 秒前"
                : ago.TotalMinutes < 60
                    ? $"{(int)ago.TotalMinutes} 分钟前"
                    : $"{(int)ago.TotalHours} 小时前";
        }

        var uptime = svc?.Uptime ?? TimeSpan.Zero;
        var upText = uptime.TotalHours >= 1
            ? $"{(int)uptime.TotalHours} 小时 {uptime.Minutes} 分"
            : $"{uptime.Minutes} 分 {uptime.Seconds} 秒";

        // ⚠️ 接住返回值存字段 —— 不存的话每秒刷新碰不到这几张卡(见 MakeStatCard 注释)
        _cardProcessed = MakeStatCard("本次会话已归类",
            svc?.ProcessedCount.ToString() ?? "—", "个文件", Color.FromRgb(0x07, 0xC1, 0x60));
        _cardUnmatched = MakeStatCard("未命中(原地不动)",
            svc?.UnmatchedCount.ToString() ?? "—", "个文件", Color.FromRgb(0xAA, 0xAA, 0xAA));
        _cardLast = MakeStatCard("最近一次处理", lastText, "", Color.FromRgb(0x0F, 0x8F, 0xD4));
        _cardUptime = MakeStatCard("运行时长", upText, "", Color.FromRgb(0x55, 0x55, 0x55));
        _cardActive = MakeStatCard("正在监听",
            svc?.ActiveCount.ToString() ?? "—", "个目录", Color.FromRgb(0x07, 0xC1, 0x60));

        // WrapPanel 只能放 Panel 的子元素,裸 TextBlock 加不进去 ——
        // 用 Border 包一层,顺手把卡片的圆角/边框/内边距搬到这里(值本身留在内层)
        foreach (var (t, color) in new[]
                 {
                     (_cardProcessed, Color.FromRgb(0x07, 0xC1, 0x60)),
                     (_cardUnmatched, Color.FromRgb(0xAA, 0xAA, 0xAA)),
                     (_cardLast, Color.FromRgb(0x0F, 0x8F, 0xD4)),
                     (_cardUptime, Color.FromRgb(0x55, 0x55, 0x55)),
                     (_cardActive, Color.FromRgb(0x07, 0xC1, 0x60)),
                 })
            wrap.Children.Add(WrapCard(t, color));

        return wrap;
    }

    private UIElement BuildFolderRow(WatchFolder w, int index, int processedToday)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(
                w.Enabled && Directory.Exists(w.Path)
                    ? Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0xF8, 0xFA, 0xFA, 0xFA)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 13, 14, 13),
            Margin = new Thickness(0, 0, 0, 9)
        };

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();

        // 路径行:开关 + 路径 + 模式徽章
        var pathRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        // 注意:Checked/Unchecked 事件不能在对象初始化器里 +=(C# 报 CS0747,
        // 因为初始化器里还没有实例)。必须 new 完之后再挂。
        var sw = new CheckBox
        {
            IsChecked = w.Enabled,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = index,
            ToolTip = "启用 / 停用这个监听目录"
        };
        sw.Checked += OnToggleEnabled;
        sw.Unchecked += OnToggleEnabled;
        pathRow.Children.Add(sw);

        pathRow.Children.Add(new TextBlock
        {
            Text = w.Path,
            FontSize = 13.5,
            FontWeight = FontWeights.Medium,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = w.Enabled ? TextPrimary() : TextDisabled(),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 520,
            ToolTip = w.Path
        });

        if (!Directory.Exists(w.Path))
            pathRow.Children.Add(MakePill("目录不存在", Color.FromRgb(0xD9, 0x39, 0x39), true));

        var (modeText, modeColor) = w.Mode switch
        {
            WatchMode.Every5Minutes => ("每 5 分钟", Color.FromRgb(0x88, 0x88, 0x88)),
            WatchMode.Every30Minutes => ("每 30 分钟", Color.FromRgb(0x88, 0x88, 0x88)),
            _ => ("实时", Color.FromRgb(0x07, 0xA8, 0x54))
        };
        pathRow.Children.Add(MakePill(modeText, modeColor, w.Mode == WatchMode.Realtime));
        left.Children.Add(pathRow);

        // 元信息
        var meta = new StackPanel
        { Orientation = Orientation.Horizontal, Margin = new Thickness(30, 7, 0, 0) };
        meta.Children.Add(MakePill($"稳定期 {w.EffectiveSettleSeconds} 秒", Color.FromRgb(0x66, 0x66, 0x66), false));
        meta.Children.Add(MakePill(w.Recursive ? "递归子目录" : "不递归", Color.FromRgb(0x66, 0x66, 0x66), false));

        if (w.Mode != WatchMode.Realtime)
        {
            var nextIn = w.EffectiveIntervalSeconds -
                (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond % Math.Max(1, w.EffectiveIntervalSeconds));
            meta.Children.Add(new TextBlock
            {
                Text = $"下次扫描约 {nextIn} 秒后",
                FontSize = 11.5,
                Foreground = TextTertiary(),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });
        }
        else if (processedToday > 0)
        {
            meta.Children.Add(new TextBlock
            {
                Text = $"本次会话处理 {processedToday} 个",
                FontSize = 11.5,
                Foreground = TextTertiary(),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });
        }
        left.Children.Add(meta);

        Grid.SetColumn(left, 0);
        g.Children.Add(left);

        // 操作按钮
        var actions = new StackPanel
        { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var adjust = MakeButton("调整", RowButtonStyle(), (_, _) => OnEditFolder(index));
        adjust.Tag = index;
        actions.Children.Add(adjust);

        var remove = MakeButton("移除", RowButtonStyle(), (_, _) => OnRemoveFolder(index));
        remove.Tag = index;
        remove.Style = DangerRowButtonStyle();
        actions.Children.Add(remove);

        Grid.SetColumn(actions, 1);
        g.Children.Add(actions);

        card.Child = g;
        return card;
    }

    private UIElement BuildEmptyState()
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(40, 36, 40, 36),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(new TextBlock
        {
            Text = "还没有配置监听目录",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextTertiary(),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        sp.Children.Add(new TextBlock
        {
            Text = "点上方「+ 添加监听目录」,或者直接手写 rules.yaml 的 watch_folders 段",
            FontSize = 12.5,
            Foreground = TextDisabled(),
            Margin = new Thickness(0, 8, 0, 18),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var btn = MakeButton("+  添加第一个目录", PrimaryButtonStyle(), OnAddFolder);
        btn.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(btn);
        card.Child = sp;
        return card;
    }

    private UIElement BuildFootnote()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0xE8, 0xF7, 0xEF)),
            BorderBrush = PrimaryBrushColor(),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 18, 0, 8),
            CornerRadius = new CornerRadius(0, 6, 6, 0)
        };
        var sp = new StackPanel();
        sp.Children.Add(MakeText(
            "工作方式 — 文件放进监听目录后,先等它写完(大小连续「稳定期」秒不变),再按 rules.yaml 里的规则归类。",
            12, FontWeights.Normal, new SolidColorBrush(Color.FromRgb(0x4A, 0x6B, 0x58)), wrap: true));
        sp.Children.Add(MakeText(
            "未命中规则的文件原地不动,不会被移动或删除。监听目录和目标目录重合也不会死循环。",
            12, FontWeights.Normal, new SolidColorBrush(Color.FromRgb(0x4A, 0x6B, 0x58)), wrap: true,
            margin: new Thickness(0, 4, 0, 0)));
        sp.Children.Add(MakeText(
            "紧急停止 — 点「全部暂停」或托盘图标右键取消勾选「自动归类」,所有监听立即停,配置保留。",
            12, FontWeights.Normal, new SolidColorBrush(Color.FromRgb(0x4A, 0x6B, 0x58)), wrap: true,
            margin: new Thickness(0, 4, 0, 0)));
        border.Child = sp;
        return border;
    }

    // ================= 左侧实时状态栏 =================

    private void RefreshLive()
    {
        // 状态栏数字每秒变,重建代价太大 —— 只更新文本
        if (SidePanel.Children.Count == 0) BuildSidebar();
        UpdateSidebarStats();
        UpdateMainCards();
    }

    /// <summary>主区五张概览卡的每秒刷新。
    /// ⚠️ 这段以前根本不存在 —— 侧栏每秒变、主区卡永远停在打开窗口那一秒,
    /// 同一屏出现两个版本的事实(实测:侧栏 9 / 主区 0)。
    /// 它们和侧栏读的是同一个 service,所以必须一起更新,不能只顾一边。</summary>
    private void UpdateMainCards()
    {
        var svc = _service;
        if (svc is null) return;

        if (_cardProcessed is not null)
            _cardProcessed.Text = svc.ProcessedCount.ToString();
        if (_cardUnmatched is not null)
            _cardUnmatched.Text = svc.UnmatchedCount.ToString();
        if (_cardActive is not null)
            _cardActive.Text = svc.ActiveCount.ToString();

        if (_cardLast is not null)
        {
            // 和侧栏用同一个判定逻辑:任何一次做出判定(归类/未命中/失败)都算「处理过」。
            // 旧代码只在 res.Success 时更新时间,导致全部未命中时永远显示「尚未处理」——
            // 而那 12 个文件明明被扫过了,界面却在制造假警报。
            _cardLast.Text = svc.LastProcessedUtc == DateTime.MinValue
                ? "尚未处理"
                : FormatAgo(DateTime.UtcNow - svc.LastProcessedUtc);
        }

        if (_cardUptime is not null)
        {
            var up = svc.Uptime;
            _cardUptime.Text = up.TotalHours >= 1
                ? $"{(int)up.TotalHours} 小时 {up.Minutes} 分"
                : $"{up.Minutes} 分 {up.Seconds} 秒";
        }
    }

    private TextBlock? _liveBig, _liveProcessed, _liveUnmatched, _liveFailed, _liveLast, _liveUptime;

    /// <summary>主区五张概览卡里「值」那个 TextBlock 的引用。
    /// 跟侧栏的 _live* 是同一批数据的两个视图,必须一起刷新,否则两处数字打架
    /// (实测踩过:侧栏 9 / 主区 0,同一屏显示两个版本的事实)。</summary>
    private TextBlock? _cardProcessed, _cardUnmatched, _cardLast, _cardUptime, _cardActive;
    private System.Windows.Shapes.Ellipse? _liveDot;

    private void BuildSidebar()
    {
        SidePanel.Children.Clear();
        _statLineHosts.Clear();

        SidePanel.Children.Add(MakeText("实时状态", 11, FontWeights.Bold, TextTertiary(),
            margin: new Thickness(2, 0, 0, 10)));

        // 状态卡
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var cardSp = new StackPanel();

        var statusRow = new StackPanel
        { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        _liveDot = MakeDot(Color.FromRgb(0x07, 0xC1, 0x60));
        statusRow.Children.Add(_liveDot);
        var statusText = new TextBlock
        {
            Text = _service?.Enabled == false ? "已暂停" : "监听运行中",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary(),
            VerticalAlignment = VerticalAlignment.Center
        };
        statusText.Name = "StatusText";
        statusRow.Children.Add(statusText);
        cardSp.Children.Add(statusRow);

        _liveBig = new TextBlock
        {
            Text = _service?.ActiveCount.ToString() ?? "0",
            FontSize = 34,
            FontWeight = FontWeights.Bold,
            Foreground = PrimaryBrushColor(),
            Margin = new Thickness(0, 6, 0, 0)
        };
        cardSp.Children.Add(_liveBig);
        cardSp.Children.Add(new TextBlock
        {
            Text = "个目录正在监听",
            FontSize = 11.5,
            Foreground = TextTertiary(),
            Margin = new Thickness(0, 0, 0, 12)
        });

        cardSp.Children.Add(new Separator
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1F, 0, 0, 0)),
            Margin = new Thickness(0, 0, 0, 10)
        });

        _liveProcessed = MakeStatLine("本次会话已归类", "0", PrimaryBrushColor());
        _liveUnmatched = MakeStatLine("未命中(原地不动)", "0", TextDisabled());
        // ⚠️ 失败以前只存在 service 里,UI 一次都没露过脸 ——
        // 权限不足 / 目标目录被锁这类问题完全静默,用户根本不知道有文件没归进去。
        // 失败数 > 0 时变红,0 时保持灰,平时不抢眼但出事了第一眼就能看到。
        _liveFailed = MakeStatLine("处理失败", "0", TextDisabled());
        _liveLast = MakeStatLine("最近一次", "—", InfoColor());
        _liveUptime = MakeStatLine("运行时长", "—", TextSecondary());
        foreach (var host in _statLineHosts) cardSp.Children.Add(host);
        _statLineHosts.Clear();

        card.Child = cardSp;
        SidePanel.Children.Add(card);

        // 监听明细
        SidePanel.Children.Add(MakeText("监听明细", 11, FontWeights.Bold,
            new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)), margin: new Thickness(2, 0, 0, 6)));

        var perFolder = _service?.PerFolderCount ?? new Dictionary<string, int>();
        var perFailed = _service?.PerFolderFailedCount ?? new Dictionary<string, int>();
        var running = new HashSet<string>(
            _foldersToRunningPaths(), StringComparer.OrdinalIgnoreCase);

        var folders = _cfg.WatchFolders;
        if (folders.Count == 0)
        {
            SidePanel.Children.Add(MakeText("(未配置)", 11.5, FontWeights.Normal, TextDisabled(),
                margin: new Thickness(8, 4, 0, 0)));
        }
        foreach (var w in folders)
        {
            perFolder.TryGetValue(w.Path, out var n);
            perFailed.TryGetValue(w.Path, out var f);
            SidePanel.Children.Add(MakeSideFolder(w, running.Contains(w.Path), n, f));
        }
    }

    private IEnumerable<string> _foldersToRunningPaths()
    {
        // 运行中的目录 = 配置里 enabled 且目录存在
        return _cfg.WatchFolders
            .Where(w => w.Enabled && Directory.Exists(w.Path))
            .Select(w => w.Path);
    }

    private void UpdateSidebarStats()
    {
        var svc = _service;
        if (svc is null) return;

        if (_liveBig is not null)
            _liveBig.Text = svc.ActiveCount.ToString();
        if (_liveProcessed is not null)
            _liveProcessed.Text = svc.ProcessedCount.ToString();
        if (_liveUnmatched is not null)
            _liveUnmatched.Text = svc.UnmatchedCount.ToString();
        if (_liveFailed is not null)
        {
            _liveFailed.Text = svc.FailedCount.ToString();
            // 有失败就变红,平时保持灰色不抢眼
            _liveFailed.Foreground = svc.FailedCount > 0
                ? new SolidColorBrush(Color.FromRgb(0xD9, 0x3A, 0x3A))
                : TextDisabled();
        }
        if (_liveLast is not null)
        {
            _liveLast.Text = svc.LastProcessedUtc == DateTime.MinValue
                ? "尚未处理"
                : FormatAgo(DateTime.UtcNow - svc.LastProcessedUtc);
        }
        if (_liveUptime is not null)
        {
            var up = svc.Uptime;
            _liveUptime.Text = up.TotalHours >= 1
                ? $"{(int)up.TotalHours} 小时 {up.Minutes} 分"
                : $"{up.Minutes} 分 {up.Seconds} 秒";
        }

        if (_liveDot is not null)
            _liveDot.Fill = new SolidColorBrush(
                svc.Enabled ? Color.FromRgb(0x07, 0xC1, 0x60) : Color.FromRgb(0xB0, 0xB0, 0xB0));
    }

    private static string FormatAgo(TimeSpan ago) =>
        ago.TotalSeconds < 60 ? $"{(int)ago.TotalSeconds} 秒前"
        : ago.TotalMinutes < 60 ? $"{(int)ago.TotalMinutes} 分钟前"
        : ago.TotalHours < 24 ? $"{(int)ago.TotalHours} 小时前"
        : $"{(int)ago.TotalDays} 天前";

    /// <summary>一行「标签 …… 值」。返回的是值的那个 TextBlock,调用方存起来就能后续改 .Text。</summary>
    private TextBlock MakeStatLine(string k, string v, Brush color)
    {
        var tb = new TextBlock
        {
            Text = v,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = color,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var g = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock
        {
            Text = k,
            FontSize = 11.5,
            Foreground = TextTertiary(),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(tb, 1);
        g.Children.Add(tb);

        _statLineHosts.Add(g);      // 把整行 Grid 存起来,BuildSidebar 里再挂进卡片
        return tb;
    }

    private readonly List<UIElement> _statLineHosts = new();

    /// <summary>
    /// 「监听明细」里的一行 —— 两行式布局(2026-10-20 改)。
    ///
    /// 旧版是一行「● 目录名  374」,信息量太低:374 在主区概览卡里已经能看到,
    /// 侧栏再列一遍没意义;而且模式/稳定期/失败数全靠点「调整」才看得到。
    ///
    /// 现在第一行放状态点 + 路径(悬停出完整路径),第二行放灰色小字:
    ///     实时 · 稳定期 15 秒 · 本次 374 · 失败 2
    /// 失败数 > 0 时转红,这是整个 UI 里唯一能暴露「文件没归进去」的地方。
    /// </summary>
    private UIElement MakeSideFolder(WatchFolder w, bool isRunning, int processed, int failed)
    {
        var panel = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 2)
        };

        var sp = new StackPanel();

        // ---- 第一行:状态点 + 目录名 + 成功数 ----
        var row1 = new Grid();
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row1.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            // 停用 = 灰点;运行中且有失败 = 琥珀色点(有东西没归进去,值得提醒)
            Fill = !isRunning ? new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8))
                 : failed > 0 ? new SolidColorBrush(Color.FromRgb(0xE6, 0xA2, 0x3C))
                 : PrimaryBrushColor(),
            VerticalAlignment = VerticalAlignment.Center
        });

        var pathText = new TextBlock
        {
            Text = Path.GetFileName(w.Path.TrimEnd('\\')) is { Length: > 0 } n ? n : w.Path,
            FontSize = 11,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = isRunning ? TextSecondary() : TextDisabled(),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = w.Path
        };
        Grid.SetColumn(pathText, 1);
        row1.Children.Add(pathText);

        var cnt = new TextBlock
        {
            Text = isRunning ? processed.ToString() : "停用",
            FontSize = 10.5,
            Foreground = TextDisabled(),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(cnt, 2);
        row1.Children.Add(cnt);

        sp.Children.Add(row1);

        // ---- 第二行:模式 · 稳定期 · 失败 ----
        var chips = new List<string>();
        chips.Add(ModeLabel(w.Mode));
        chips.Add(w.EffectiveSettleSeconds > 0 ? $"稳定期 {w.EffectiveSettleSeconds} 秒" : "默认稳定期");
        if (w.Recursive) chips.Add("递归");

        var chipRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(14, 3, 0, 0)
        };
        foreach (var c in chips)
        {
            chipRow.Children.Add(new TextBlock
            {
                Text = c,
                FontSize = 10,
                Foreground = TextTertiary(),
                Margin = new Thickness(0, 0, 6, 0)
            });
        }
        if (failed > 0)
        {
            chipRow.Children.Add(new TextBlock
            {
                Text = $"失败 {failed}",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0x3A, 0x3A)),
                ToolTip = $"这个目录有 {failed} 个文件处理失败,点「查看日志」看原因"
            });
        }
        sp.Children.Add(chipRow);

        panel.Child = sp;
        return panel;
    }

    /// <summary>WatchMode -> 中文标签。定时模式优先显示真实间隔而不是"每 5 分钟"这种死说法。</summary>
    private static string ModeLabel(WatchMode m) => m switch
    {
        WatchMode.Realtime => "实时",
        WatchMode.Every5Minutes => "5 分钟",
        WatchMode.Every30Minutes => "30 分钟",
        _ => m.ToString()
    };

    // ================= 配置编辑 =================

    private void OnAddFolder(object sender, RoutedEventArgs e) => ShowEditor(-1);
    private void OnEditFolder(int index) => ShowEditor(index);

    private void ShowEditor(int index)
    {
        var isNew = index < 0;
        var existing = isNew ? new WatchFolder() : _cfg.WatchFolders[index];

        var pathBox = new TextBox
        {
            Text = existing.Path,
            Style = TryFindResource("GlassTextBox") as Style,
            Margin = new Thickness(0, 0, 0, 10)
        };
        if (pathBox.Style is null)
            pathBox.Margin = new Thickness(0, 0, 0, 10);

        var modeCombo = new ComboBox
        {
            Height = 32,
            FontSize = 13,
            ItemsSource = new[]
            {
                new ComboItem("实时(文件落地即处理)", WatchMode.Realtime),
                new ComboItem("每 5 分钟", WatchMode.Every5Minutes),
                new ComboItem("每 30 分钟", WatchMode.Every30Minutes)
            },
            SelectedIndex = (int)existing.Mode
        };
        modeCombo.DisplayMemberPath = "Label";

        var settleBox = new TextBox
        {
            Text = existing.SettleSeconds > 0 ? existing.SettleSeconds.ToString() : "",
            Style = TryFindResource("GlassTextBox") as Style
        };

        var recursiveCheck = new CheckBox { IsChecked = existing.Recursive, Margin = new Thickness(0, 12, 0, 0) };
        var enabledCheck = new CheckBox { IsChecked = isNew ? true : existing.Enabled, Margin = new Thickness(0, 6, 0, 0) };

        var stack = new StackPanel { Margin = new Thickness(22, 20, 22, 20), Width = 520 };

        stack.Children.Add(MakeText(isNew ? "添加监听目录" : "调整监听目录",
            16, FontWeights.SemiBold, TextPrimary(), margin: new Thickness(0, 0, 0, 16)));

        stack.Children.Add(MakeField("监听路径 *", pathBox,
            "填要监听的文件夹绝对路径。目录不存在会被跳过。"));
        stack.Children.Add(MakeField("扫描间隔", modeCombo,
            "实时 = 文件落地就处理(推荐);定时 = 按间隔轮询目录。"));
        stack.Children.Add(MakeField("稳定期(秒)", settleBox,
            "文件大小连续几秒不变才处理 —— 防半拷贝。留空 = 默认 2 秒,大视频建议 10~30。"));
        stack.Children.Add(recursiveCheck);
        stack.Children.Add(MakeText("包含子目录(递归监听)", 12.5, FontWeights.Normal, TextSecondary(),
            margin: new Thickness(22, 0, 0, 0)));
        stack.Children.Add(enabledCheck);
        stack.Children.Add(MakeText("启用这条配置", 12.5, FontWeights.Normal, TextSecondary(),
            margin: new Thickness(22, 0, 0, 0)));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };

        var browse = MakeButton("浏览…", BaseButtonStyle(), (_, _) =>
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择要监听的文件夹",
                UseDescriptionForTitle = true
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                pathBox.Text = dlg.SelectedPath;
        });
        buttons.Children.Add(browse);
        buttons.Children.Add(MakeButton("取消", BaseButtonStyle(), (_, _) => CloseEditor()));
        buttons.Children.Add(MakeButton("保存", PrimaryButtonStyle(), (_, _) =>
        {
            var p = pathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(p))
            {
                MessageBox.Show(this, "监听路径不能为空。", "FileSorter",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(settleBox.Text.Trim(), out var settle) || settle < 0) settle = 0;

            if (modeCombo.SelectedItem is not ComboItem target)
            {
                MessageBox.Show(this, "请先选择扫描间隔。", "FileSorter",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var updated = new WatchFolder
            {
                Path = p,
                Enabled = enabledCheck.IsChecked == true,
                Recursive = recursiveCheck.IsChecked == true,
                Mode = target.Value,
                SettleSeconds = settle,
                IntervalSeconds = existing.IntervalSeconds
            };

            MutateWatchFolders(_cfg, list =>
            {
                if (isNew) list.Add(updated);
                else list[index] = updated;
            });

            SaveAndReload();
            CloseEditor();
            BuildHead();
            BuildContent();
            BuildSidebar();
        }));
        stack.Children.Add(buttons);

        var dialog = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFA, 0xF5, 0xF7, 0xF8)),
            CornerRadius = new CornerRadius(10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = stack,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 32,
                ShadowDepth = 6,
                Opacity = 0.35,
                Color = Colors.Black
            }
        };
        // 先清掉可能残留的旧 dialog,再挂新的
        CloseEditor();
        _dlg = dialog;
        RootGrid.Children.Add(dialog);
        Overlay.Visibility = Visibility.Visible;
    }

    private UIElement MakeField(string label, UIElement editor, string hint)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        sp.Children.Add(MakeText(label, 12.5, FontWeights.Medium, TextSecondary()));
        sp.Children.Add(editor);
        if (!string.IsNullOrEmpty(hint))
            sp.Children.Add(MakeText(hint, 11, FontWeights.Normal, TextDisabled(),
                wrap: true, margin: new Thickness(0, 5, 0, 0)));
        return sp;
    }

    private void OnToggleEnabled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.Tag is not int idx) return;
        if (idx < 0 || idx >= _cfg.WatchFolders.Count) return;
        var want = cb.IsChecked == true;
        MutateWatchFolders(_cfg, list => list[idx].Enabled = want);
        SaveAndReload();
        BuildContent();
    }

    private void OnRemoveFolder(int index)
    {
        if (index < 0 || index >= _cfg.WatchFolders.Count) return;
        var w = _cfg.WatchFolders[index];   // 只读拷贝,用来拿 Path 提示,不改

        var ok = MessageBox.Show(this,
            $"确定移除这个监听目录?\n\n{w.Path}\n\n配置会从 rules.yaml 删除(文件本身不受影响)。",
            "FileSorter", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;

        MutateWatchFolders(_cfg, list => list.RemoveAt(index));
        SaveAndReload();
        BuildContent();
        BuildSidebar();
    }

    /// <summary>
    /// 把 cfg 写盘,再读回来对账,返回读回来的新配置。
    ///
    /// ⚠️ 抽成静态公共方法是有原因的(2026-10-20):这段逻辑原来埋在 private
    /// SaveAndReload() 里,UI 测试够不到,于是「写盘 → 读回」这条链路上出的三个
    /// bug(YAML 重复 key / getter 返回副本 / 别名丢失)全都没被 118 个测试覆盖到,
    /// 测试全绿而功能是坏的。抽出来后 UiSaveRoundTripTests 能直接跑真实代码路径。
    ///
    /// 对账失败返回 null(不抛异常):UI 层决定怎么提示,测试层只要判 null。
    /// </summary>
    public static RulesConfig? SaveAndVerify(string path, RulesConfig cfg)
    {
        var expected = cfg.WatchFolders.Count;

        RulesFileWriter.WriteWithBackup(path, cfg);

        // 读回来对账:数量对不上说明写盘链路有 bug(重复 key / 字段名不匹配 / 别名丢失)。
        // 静默失败过一次,不能再静默。
        var fresh = RuleEngine.LoadFromFile(path);
        var actual = fresh.WatchFolders.Count;
        return actual == expected ? fresh : null;
    }

    /// <summary>就地改写 cfg 的监听目录列表并写回。
    ///
    /// ⚠️ 必须走这个方法,不能直接 <c>cfg.WatchFolders.Add(...)</c>:
    /// RulesConfig.WatchFolders 的 getter 每次 .ToList() 返回**新对象**
    /// (它是从 Snake + Camel 两个别名属性合并出来的只读视图),
    /// 直接 Add/RemoveAt 改的是临时副本,setter 从未被调用,写盘还是 0 个目录。
    /// 这就是「保存未生效」三个叠坑里最隐蔽的一个。
    /// </summary>
    public static void MutateWatchFolders(RulesConfig cfg, Action<List<WatchFolder>> mutate)
    {
        var list = cfg.WatchFolders;      // getter 返回副本
        mutate(list);
        cfg.WatchFolders = list;          // setter 写回 WatchFoldersSnake —— 这步是关键
    }

    private void SaveAndReload()
    {
        try
        {
            var fresh = SaveAndVerify(_rulesPath, _cfg);
            if (fresh is null)
            {
                var actual = RuleEngine.LoadFromFile(_rulesPath).WatchFolders.Count;
                MessageBox.Show(this,
                    $"配置写盘后读回不一致。\n期望 {_cfg.WatchFolders.Count} 个监听目录,实际读到 {actual} 个。\n\n" +
                    $"文件:{_rulesPath}\n请检查该文件里 watchFolders 段是否出现重复 key。",
                    "FileSorter 保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _cfg = fresh;
            _service?.Reload(fresh);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败:{ex.Message}", "FileSorter",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ================= 视觉辅助 =================

    private static Brush TextPrimary() => new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    private static Brush TextSecondary() => new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    private static Brush TextTertiary() => new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
    private static Brush TextDisabled() => new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
    private static Brush PrimaryBrushColor() => new SolidColorBrush(Color.FromRgb(0x07, 0xC1, 0x60));
    private static Brush InfoColor() => new SolidColorBrush(Color.FromRgb(0x0F, 0x8F, 0xD4));

    private static TextBlock MakeText(string s, double size, FontWeight weight, Brush color,
        bool wrap = false, Thickness margin = default)
    {
        return new TextBlock
        {
            Text = s,
            FontSize = size,
            FontWeight = weight,
            Foreground = color,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Margin = margin
        };
    }

    private static Button MakeButton(string content, Style? style, RoutedEventHandler handler)
    {
        var b = new Button { Content = content, Margin = new Thickness(0, 0, 8, 0) };
        if (style is not null) b.Style = style;
        b.Click += handler;
        return b;
    }

    private static Style? BaseButtonStyle() =>
        Application.Current.TryFindResource("BaseButton") as Style;

    private static Style? PrimaryButtonStyle() =>
        Application.Current.TryFindResource("PrimaryButton") as Style;

    private static Style? WarnButtonStyle() =>
        Application.Current.TryFindResource("WarnButton") as Style;

    private static Style? RowButtonStyle() =>
        Application.Current.TryFindResource("RowButton") as Style;

    private static Style? DangerRowButtonStyle() =>
        Application.Current.TryFindResource("RowButton") as Style;

    private static Border MakePill(string text, Color fg, bool filled) => new()
    {
        CornerRadius = new CornerRadius(4),
        Background = new SolidColorBrush(
            filled ? Color.FromArgb(0x33, fg.R, fg.G, fg.B) : Color.FromRgb(0xF0, 0xF0, 0xF0)),
        Padding = new Thickness(8, 2, 8, 2),
        Margin = new Thickness(8, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11.5,
            Foreground = new SolidColorBrush(fg)
        }
    };

    private static System.Windows.Shapes.Ellipse MakeDot(Color c)
    {
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 9, Height = 9,
            Fill = new SolidColorBrush(c),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        // 呼吸动画:运行中才有存在感
        dot.BeginAnimation(System.Windows.UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 1.0, To = 0.5,
            Duration = new Duration(TimeSpan.FromSeconds(1.2)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        });
        return dot;
    }

    /// <summary>主区概览卡。
    /// ⚠️ 返回的是**值那个 TextBlock**,不是 Border —— 调用方必须接住引用存到字段,
    /// 否则每秒刷新时只能重建整个卡片(太贵)或者干脆不更新(旧 bug)。
    /// 2026-10-20 实测:侧栏每秒变、主区五张卡永远停在打开窗口那一秒的数字,
    /// 根因就是这里返回 UIElement,引用被丢进了 Children.Add() 里没人拿。</summary>
    private TextBlock MakeStatCard(string label, string value, string sub, Color valueColor)
    {
        var valueText = MakeText(value, 24, FontWeights.SemiBold, new SolidColorBrush(valueColor),
            margin: new Thickness(0, 5, 0, 0));
        // 标签和单位文案挂到 valueText 的 Tag 上,WrapCard 取出来拼进卡片里 ——
        // 这样卡片重建(比如宽度变化)时不用重新查 label,也不会跟值脱节。
        valueText.Tag = new StatCardMeta(label, sub);
        return valueText;
    }

    private sealed record StatCardMeta(string Label, string Sub);

    /// <summary>把「值」TextBlock 包成一张可视的卡片。Border 是 Panel 的子类,可以进 WrapPanel。</summary>
    private static Border WrapCard(TextBlock valueText, Color accent)
    {
        var meta = valueText.Tag as StatCardMeta;
        var sp = new StackPanel();
        sp.Children.Add(MakeText(meta?.Label ?? "", 12, FontWeights.Normal, TextTertiary()));
        sp.Children.Add(valueText);
        if (!string.IsNullOrEmpty(meta?.Sub))
            sp.Children.Add(MakeText(meta.Sub, 11, FontWeights.Normal, TextDisabled(),
                margin: new Thickness(0, 5, 0, 0)));

        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 10, 0),
            MinWidth = 152,
            Child = sp
        };
    }

    /// <summary>ComboBox 的显示项 + 实际值。</summary>
    private sealed record ComboItem(string Label, WatchMode Value)
    {
        public override string ToString() => Label;
    }
}
