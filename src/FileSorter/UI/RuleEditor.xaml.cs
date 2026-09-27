using System.Windows;
using System.Windows.Controls;
using FileSorter.Core;
using FileSorter.Core.Models;
using Dock = System.Windows.Controls.Dock;

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
        UpdatePreview();
    }

    // --- Dispatch based on rule type ---
    private void RebuildEditPanel()
    {
        EditPanel.Children.Clear();
        if (_selectedRule == null)
        {
            EditPanel.Children.Add(new TextBlock
            {
                Text = "选择左侧规则",
                Foreground = System.Windows.Media.Brushes.Gray
            });
            return;
        }

        var stack = new StackPanel();

        // Always-present: Name
        stack.Children.Add(MakeRow("Name", _selectedRule.Name,
            v => _selectedRule.Name = v));
        // Always-present: Type (read-only hint; user uses Add to create new rules)
        stack.Children.Add(MakeRow("Type", _selectedRule.Type,
            v => { _selectedRule.Type = v; RebuildEditPanel(); UpdatePreview(); }));

        // Type-specific dispatch
        switch (_selectedRule.Type)
        {
            case "extension":
                BuildExtensionPanel(stack);
                break;
            case "filename_keyword":
                BuildFilenameKeywordPanel(stack);
                break;
            case "combined":
                BuildCombinedPanel(stack);
                break;
            case "path_template":
                BuildPathTemplatePanel(stack);
                break;
            case "filename_pattern":
                BuildFilenamePatternPanel(stack);
                break;
            case "name_template":
                BuildNameTemplatePanel(stack);
                break;
            case "default":
                BuildDefaultPanel(stack);
                break;
            default:
                stack.Children.Add(new TextBlock
                {
                    Text = $"(未知 type '{_selectedRule.Type}' — 显示通用字段)",
                    Foreground = System.Windows.Media.Brushes.OrangeRed
                });
                BuildGenericPanel(stack);
                break;
        }

        EditPanel.Children.Add(stack);
    }

    // --- Type-specific builders ---

    private void BuildExtensionPanel(StackPanel stack)
    {
        stack.Children.Add(MakeListBoxRow("扩展名 (extension)",
            _selectedRule.Patterns ?? new(),
            v => _selectedRule.Patterns = v,
            allowEmpty: true));
        stack.Children.Add(MakeDestinationRow());
    }

    private void BuildFilenameKeywordPanel(StackPanel stack)
    {
        stack.Children.Add(MakeListBoxRow("关键字 (patterns)",
            _selectedRule.Patterns ?? new(),
            v => _selectedRule.Patterns = v,
            allowEmpty: true));
        stack.Children.Add(MakeListBoxRow("扩展名 (extensions)",
            _selectedRule.Extensions ?? new(),
            v => _selectedRule.Extensions = v,
            allowEmpty: true));
        stack.Children.Add(MakeBoolRow("大小写敏感 (case_sensitive)",
            _selectedRule.CaseSensitive == true,
            v => _selectedRule.CaseSensitive = v));
        stack.Children.Add(MakeDestinationRow());
    }

    private void BuildCombinedPanel(StackPanel stack)
    {
        stack.Children.Add(MakeListBoxRow("扩展名 (extension)",
            _selectedRule.Extension ?? new(),
            v => _selectedRule.Extension = v,
            allowEmpty: true));
        stack.Children.Add(MakeListBoxRow("关键字 (filename_keyword)",
            _selectedRule.FilenameKeyword ?? new(),
            v => _selectedRule.FilenameKeyword = v,
            allowEmpty: true));
        stack.Children.Add(MakeBoolRow("大小写敏感 (case_sensitive)",
            _selectedRule.CaseSensitive == true,
            v => _selectedRule.CaseSensitive = v));
        stack.Children.Add(MakeDestinationRow());
    }

    private void BuildPathTemplatePanel(StackPanel stack)
    {
        stack.Children.Add(MakeListBoxRow("扩展名 (extensions)",
            _selectedRule.Extensions ?? new(),
            v => _selectedRule.Extensions = v,
            allowEmpty: true));
        stack.Children.Add(MakeRow("filename_pattern (regex)",
            _selectedRule.FilenamePattern ?? "",
            v => _selectedRule.FilenamePattern = v));
        stack.Children.Add(MakeRow("path 模板",
            _selectedRule.Path ?? "",
            v => _selectedRule.Path = v));
        stack.Children.Add(MakeHelp("支持 {1} {2} ... 捕获组,{destinations.x},{filename},{stem},{ext},{date:yyyyMMdd}"));
    }

    private void BuildFilenamePatternPanel(StackPanel stack)
    {
        stack.Children.Add(MakeListBoxRow("扩展名 (extensions)",
            _selectedRule.Extensions ?? new(),
            v => _selectedRule.Extensions = v,
            allowEmpty: true));
        stack.Children.Add(MakeListBoxRow("starts_with (前缀)",
            _selectedRule.StartsWith ?? new(),
            v => _selectedRule.StartsWith = v,
            allowEmpty: true));
        stack.Children.Add(MakeListBoxRow("ends_with (后缀)",
            _selectedRule.EndsWith ?? new(),
            v => _selectedRule.EndsWith = v,
            allowEmpty: true));
        stack.Children.Add(MakeBoolRow("大小写敏感 (case_sensitive)",
            _selectedRule.CaseSensitive == true,
            v => _selectedRule.CaseSensitive = v));
        stack.Children.Add(MakeDestinationRow());
    }

    private void BuildNameTemplatePanel(StackPanel stack)
    {
        stack.Children.Add(MakeListBoxRow("扩展名 (extensions)",
            _selectedRule.Extensions ?? new(),
            v => _selectedRule.Extensions = v,
            allowEmpty: true));
        stack.Children.Add(MakeRow("template (含 {token[:type]})",
            _selectedRule.Template ?? "",
            v =>
            {
                _selectedRule.Template = v;
                // Token list may change → rebuild mappings editor
                RebuildEditPanel();
                UpdatePreview();
            }));
        stack.Children.Add(MakeHelp("例:{platform}_@{author}_{date:yyyyMMdd}.{ext}"));
        stack.Children.Add(MakeDestinationRow());

        // Mappings editor
        stack.Children.Add(new TextBlock
        {
            Text = "Mappings (token → 目录级别)",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 12, 0, 4)
        });
        stack.Children.Add(BuildMappingsEditor());
    }

    private void BuildDefaultPanel(StackPanel stack)
    {
        stack.Children.Add(MakeDestinationRow());
        stack.Children.Add(MakeHelp("default 规则:前面所有规则都不命中时触发,只能配 destination"));
    }

    private void BuildGenericPanel(StackPanel stack)
    {
        stack.Children.Add(MakeRow("Destination", _selectedRule.Destination ?? "",
            v => _selectedRule.Destination = string.IsNullOrEmpty(v) ? null : v));
    }

    // --- Mappings editor (M19 / spec §6.4) ---

    private UIElement BuildMappingsEditor()
    {
        var container = new StackPanel();

        if (_selectedRule!.Mappings == null) _selectedRule.Mappings = new();
        var tokenOptions = NameTemplateCompiler.ExtractTokenNames(_selectedRule.Template ?? "");
        if (tokenOptions.Count == 0) tokenOptions.Add("(模板无 token)");

        for (int i = 0; i < _selectedRule.Mappings.Count; i++)
        {
            var mapping = _selectedRule.Mappings[i];
            int idx = i; // capture
            container.Children.Add(BuildMappingRow(mapping, idx, tokenOptions));
        }

        // [+ Add mapping] button
        var addBtn = new System.Windows.Controls.Button
        {
            Content = "+ Add mapping",
            Width = 120,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0)
        };
        addBtn.Click += (_, _) =>
        {
            var m = new Mapping { Token = tokenOptions[0], Level = _selectedRule!.Mappings.Count + 1 };
            _selectedRule.Mappings.Add(m);
            RebuildEditPanel();
            UpdatePreview();
        };
        container.Children.Add(addBtn);

        return container;
    }

    private UIElement BuildMappingRow(Mapping m, int idx, List<string> tokenOptions)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Label
        var lbl = new TextBlock { Text = $"[{idx}]", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        // Token ComboBox
        var tokenCb = new System.Windows.Controls.ComboBox
        {
            IsEditable = false,
            ItemsSource = tokenOptions,
            SelectedItem = tokenOptions.Contains(m.Token) ? m.Token : tokenOptions[0]
        };
        tokenCb.SelectionChanged += (_, _) =>
        {
            if (tokenCb.SelectedItem is string s) m.Token = s;
            UpdatePreview();
        };
        Grid.SetColumn(tokenCb, 1);
        grid.Children.Add(tokenCb);

        // Level ComboBox
        var levels = new List<int> { 1, 2, 3, 4, 5 };
        var levelCb = new System.Windows.Controls.ComboBox
        {
            ItemsSource = levels,
            SelectedItem = m.Level
        };
        levelCb.SelectionChanged += (_, _) =>
        {
            if (levelCb.SelectedItem is int n) m.Level = n;
            UpdatePreview();
        };
        levelCb.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(levelCb, 2);
        grid.Children.Add(levelCb);

        // Delete button
        var delBtn = new System.Windows.Controls.Button { Content = "×", Width = 24, Margin = new Thickness(4, 0, 0, 0) };
        delBtn.Click += (_, _) =>
        {
            _selectedRule!.Mappings.RemoveAt(idx);
            RebuildEditPanel();
            UpdatePreview();
        };
        Grid.SetColumn(delBtn, 3);
        grid.Children.Add(delBtn);

        return grid;
    }

    // --- Reusable UI primitives ---

    private static List<string> SplitCsv(string v) =>
        v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

    private UIElement MakeRow(string label, string value, Action<string> onChange)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tb, 0);
        var box = new System.Windows.Controls.TextBox { Text = value };
        box.TextChanged += (_, _) => onChange(box.Text);
        Grid.SetColumn(box, 1);
        grid.Children.Add(tb);
        grid.Children.Add(box);
        return grid;
    }

    /// <summary>
    /// A "label + ListBox-with-rows + Add/Delete buttons" row.
    /// Each row = TextBox + [×] delete. Top = [+ Add] row.
    /// On any change, the parent List<string> is replaced (handler invoked).
    /// </summary>
    private UIElement MakeListBoxRow(string label, List<string> items, Action<List<string>> onChange, bool allowEmpty)
    {
        var container = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        container.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });

        var listPanel = new StackPanel();
        for (int i = 0; i < items.Count; i++)
        {
            int idx = i;
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var delBtn = new System.Windows.Controls.Button { Content = "×", Width = 24 };
            delBtn.Click += (_, _) =>
            {
                items.RemoveAt(idx);
                RefreshListPanel();
                onChange(items);
                UpdatePreview();
            };
            var tb = new System.Windows.Controls.TextBox { Text = items[i] };
            tb.TextChanged += (_, _) => { items[idx] = tb.Text; onChange(items); UpdatePreview(); };
            DockPanel.SetDock(delBtn, Dock.Right);
            row.Children.Add(delBtn);
            row.Children.Add(tb);
            listPanel.Children.Add(row);
        }
        container.Children.Add(listPanel);

        var addBtn = new System.Windows.Controls.Button { Content = "+ Add", Width = 60, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        addBtn.Click += (_, _) =>
        {
            items.Add("");
            RebuildEditPanel();
            UpdatePreview();
        };
        container.Children.Add(addBtn);

        return container;

        void RefreshListPanel()
        {
            listPanel.Children.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var delBtn = new System.Windows.Controls.Button { Content = "×", Width = 24 };
                delBtn.Click += (_, _) =>
                {
                    items.RemoveAt(idx);
                    RefreshListPanel();
                    onChange(items);
                    UpdatePreview();
                };
                var tb = new System.Windows.Controls.TextBox { Text = items[i] };
                tb.TextChanged += (_, _) => { items[idx] = tb.Text; onChange(items); UpdatePreview(); };
                DockPanel.SetDock(delBtn, Dock.Right);
                row.Children.Add(delBtn);
                row.Children.Add(tb);
                listPanel.Children.Add(row);
            }
        }
    }

    private UIElement MakeBoolRow(string label, bool value, Action<bool> onChange)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tb, 0);
        var cb = new System.Windows.Controls.CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        cb.Checked += (_, _) => { onChange(true); UpdatePreview(); };
        cb.Unchecked += (_, _) => { onChange(false); UpdatePreview(); };
        Grid.SetColumn(cb, 1);
        grid.Children.Add(tb);
        grid.Children.Add(cb);
        return grid;
    }

    private UIElement MakeDestinationRow()
    {
        // Build dropdown from _cfg.Destinations keys + free-text fallback.
        var aliases = _cfg.Destinations.Keys.ToList();
        var container = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        container.Children.Add(new TextBlock { Text = "destination (alias 或 绝对路径)", FontWeight = FontWeights.SemiBold });
        var cb = new System.Windows.Controls.ComboBox
        {
            IsEditable = true,
            ItemsSource = aliases,
            Text = _selectedRule!.Destination ?? ""
        };
        cb.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new System.Windows.Controls.TextChangedEventHandler((_, _) =>
        {
            _selectedRule.Destination = string.IsNullOrEmpty(cb.Text) ? null : cb.Text;
            UpdatePreview();
        }));
        container.Children.Add(cb);
        return container;
    }

    private UIElement MakeHelp(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = System.Windows.Media.Brushes.Gray,
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(0, 4, 0, 4),
            TextWrapping = TextWrapping.Wrap
        };
    }

    // --- Buttons ---

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var newRule = new Rule
        {
            Name = "新规则",
            Type = "extension",
            Destination = "inbox",
            Active = true
        };
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
        UpdatePreview();
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

    private void OnActiveToggle(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        try
        {
            _cfg = RuleEngine.LoadFromFile(_rulesPath);
            _selectedRule = null;
            RefreshList();
            RebuildEditPanel();
            UpdatePreview();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"重新加载失败: {ex.Message}", "FileSorter", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            RulesFileWriter.WriteWithBackup(_rulesPath, _cfg);
            System.Windows.MessageBox.Show($"已保存到 {_rulesPath}\n(同时保留了最近 3 份 backup)",
                "FileSorter", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "FileSorter", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // --- Test input preview (M20 / spec §6.5) ---

    private void OnPreviewInputChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (PreviewInputBox == null || PreviewResultBox == null) return;
        var input = PreviewInputBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(input))
        {
            PreviewResultBox.Text = "(输入文件名预览分类结果)";
            return;
        }
        // Prepend a fake path if user only typed a bare filename
        var fakePath = input.Contains('\\') || input.Contains('/') ? input : @"C:\preview\" + input;
        try
        {
            var mr = RuleEngine.Match(_cfg, fakePath);
            if (mr == null)
                PreviewResultBox.Text = "(no match — 没有规则命中)";
            else
                PreviewResultBox.Text = $"→ {mr.DestinationPath}    (via rule '{mr.RuleName}')";
        }
        catch (Exception ex)
        {
            PreviewResultBox.Text = $"(error: {ex.Message})";
        }
    }
}
