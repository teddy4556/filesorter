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