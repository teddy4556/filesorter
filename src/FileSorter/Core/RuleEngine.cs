using FileSorter.Core.Models;
using YamlDotNet.Serialization;

namespace FileSorter.Core;

public static class RuleEngine
{
    private static readonly IDeserializer _yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    public static RulesConfig LoadFromString(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            throw new ArgumentException("YAML is empty", nameof(yaml));
        var cfg = _yaml.Deserialize<RulesConfig>(yaml)
            ?? throw new InvalidDataException("YAML deserialized to null");
        if (cfg.Version == 0) cfg.Version = 1;
        return cfg;
    }

    public static RulesConfig LoadFromFile(string path)
    {
        return LoadFromString(File.ReadAllText(path));
    }
}