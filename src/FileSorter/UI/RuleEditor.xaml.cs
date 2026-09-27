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
            EditPanel.Children.Add(new TextBlock
            {
                Text = "选择左侧规则",
                Foreground = System.Windows.Media.Brushes.Gray
            });
            return;
        }

        var stack = new StackPanel();
        // Always-present fields
        stack.Children.Add(MakeRow("Name", _selectedRule.Name, v => _selectedRule.Name = v));
        stack.Children.Add(MakeRow("Type", _selectedRule.Type, v => _selectedRule.Type = v));
        stack.Children.Add(MakeRow("Destination", _selectedRule.Destination ?? "",
            v => _selectedRule.Destination = string.IsNullOrEmpty(v) ? null : v));

        // Optional fields — only show if they exist on this rule (helps the UI stay type-aware)
        if (_selectedRule.Patterns != null)
            stack.Children.Add(MakeRow("Patterns (逗号分隔)",
                string.Join(",", _selectedRule.Patterns),
                v => _selectedRule.Patterns = SplitCsv(v)));

        if (_selectedRule.Extensions != null)
            stack.Children.Add(MakeRow("Extensions (逗号分隔)",
                string.Join(",", _selectedRule.Extensions),
                v => _selectedRule.Extensions = SplitCsv(v)));

        if (_selectedRule.FilenameKeyword != null)
            stack.Children.Add(MakeRow("FilenameKeyword (逗号分隔)",
                string.Join(",", _selectedRule.FilenameKeyword),
                v => _selectedRule.FilenameKeyword = SplitCsv(v).Select(s => s.Trim()).ToList()));

        if (_selectedRule.FilenamePattern != null)
            stack.Children.Add(MakeRow("FilenamePattern (verbatim regex)",
                _selectedRule.FilenamePattern,
                v => _selectedRule.FilenamePattern = v));

        if (_selectedRule.Path != null)
            stack.Children.Add(MakeRow("Path", _selectedRule.Path, v => _selectedRule.Path = v));

        if (_selectedRule.Template != null)
            stack.Children.Add(MakeRow("Template", _selectedRule.Template, v => _selectedRule.Template = v));

        if (_selectedRule.StartsWith != null)
            stack.Children.Add(MakeRow("StartsWith (逗号)",
                string.Join(",", _selectedRule.StartsWith),
                v => _selectedRule.StartsWith = SplitCsv(v).Select(s => s.Trim()).ToList()));

        if (_selectedRule.EndsWith != null)
            stack.Children.Add(MakeRow("EndsWith (逗号)",
                string.Join(",", _selectedRule.EndsWith),
                v => _selectedRule.EndsWith = SplitCsv(v).Select(s => s.Trim()).ToList()));

        if (_selectedRule.Mappings != null && _selectedRule.Mappings.Count > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Mappings:",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 8, 0, 4)
            });
            for (int i = 0; i < _selectedRule.Mappings.Count; i++)
            {
                var m = _selectedRule.Mappings[i];
                stack.Children.Add(MakeRow($"  [{i}] Token", m.Token, v => m.Token = v));
                stack.Children.Add(MakeRow($"  [{i}] Level", m.Level.ToString(),
                    v => { if (int.TryParse(v, out var n)) m.Level = n; }));
            }
        }

        EditPanel.Children.Add(stack);
    }

    private static List<string> SplitCsv(string v) =>
        v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

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
        var newRule = new Rule
        {
            Name = "新规则",
            Type = "extension",
            Destination = "inbox"
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
        // CheckBox binding handles Active via TwoWay; this handler exists so XAML's Click= doesn't fail.
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        try
        {
            _cfg = RuleEngine.LoadFromFile(_rulesPath);
            _selectedRule = null;
            RefreshList();
            RebuildEditPanel();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"重新加载失败: {ex.Message}", "FileSorter", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            RulesFileWriter.WriteWithBackup(_rulesPath, _cfg);
            MessageBox.Show($"已保存到 {_rulesPath}\n(同时保留了最近 3 份 backup)",
                "FileSorter", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败: {ex.Message}", "FileSorter", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}