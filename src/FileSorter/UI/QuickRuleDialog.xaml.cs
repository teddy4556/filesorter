using System.IO;
using System.Windows;
using System.Windows.Controls;
using FileSorter.Core.Models;

namespace FileSorter.UI;

public partial class QuickRuleDialog : Window
{
    /// <summary>Display label + optional Rule reference for the ListBox.</summary>
    public class Choice
    {
        public string Display { get; set; } = "";
        public Rule? Rule { get; set; }
    }

    /// <summary>Chosen rule, or null to use the default ("catch-all") rule set.</summary>
    public Rule? SelectedRule { get; private set; }

    public QuickRuleDialog(IReadOnlyList<string> files, IReadOnlyList<Rule> rules)
    {
        InitializeComponent();
        FileCountText.Text = $"{files.Count} 个文件: " + string.Join(", ", files.Select(Path.GetFileName));
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