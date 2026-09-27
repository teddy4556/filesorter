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