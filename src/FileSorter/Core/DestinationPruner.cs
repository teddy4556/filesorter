using System.Collections.Generic;
using System.IO;
using FileSorter.Core.Models;

namespace FileSorter.Core;

/// <summary>
/// Cleans up unused destination aliases when rules are deleted, and
/// auto-registers an alias for any rule whose Destination is an absolute
/// path that has no alias in Destinations yet.
///
/// Behaviour (v6):
///   PASS 1 — auto-register alias for absolute-path rules whose path is not
///            yet known. Alias key = last non-empty directory segment of the
///            path (with _2/_3/... suffix on collision). Updates the rule's
///            Destination to point to the new alias key.
///   PASS 2 — collect all referenced alias keys (rule.Destination or
///            rule.DestinationAlias equals key, OR reverse-lookup matches
///            the rule's resolved path).
///   PASS 3 — remove alias keys that are NOT referenced. "删了就是删了"
///            — 包括 inbox 全删,无兜底(2026-09-29 用户决定)。
///   PASS 4 — null any rule whose Destination can no longer be resolved.
///
/// Returns a Result so callers can show the user what changed.
/// </summary>
public static class DestinationPruner
{
    public const string ReservedFallbackKey = "inbox";

    public record Result(
        IReadOnlyList<string> RemovedKeys,
        IReadOnlyList<string> NulledRuleNames,
        IReadOnlyList<string> RegisteredKeys);

    public static Result Prune(RulesConfig cfg)
    {
        var removedKeys = new List<string>();
        var nulledRuleNames = new List<string>();
        var registeredKeys = new List<string>();

        if (cfg.Rules == null) cfg.Rules = new List<Rule>();
        if (cfg.Destinations == null) cfg.Destinations = new Dictionary<string, string>();

        // PASS 1 — auto-register a new alias for any rule whose Destination is
        // an absolute path that has no alias yet.
        foreach (var rule in cfg.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Destination)) continue;
            if (cfg.Destinations.ContainsKey(rule.Destination)) continue;
            if (TryResolveAliasKey(rule.Destination, cfg.Destinations) != null) continue;

            var path = rule.Destination.TrimEnd('\\', '/');
            var lastSep = path.LastIndexOfAny(new[] { '\\', '/' });
            var baseName = lastSep >= 0 ? path.Substring(lastSep + 1) : path;
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "new_destination";

            var aliasKey = MakeUniqueKey(baseName, cfg.Destinations.Keys);
            cfg.Destinations[aliasKey] = rule.Destination;
            rule.Destination = aliasKey;
            registeredKeys.Add(aliasKey);
        }

        // PASS 2 — collect alias keys actually referenced by any rule.
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in cfg.Rules)
        {
            foreach (var field in new[] { rule.Destination, rule.DestinationAlias })
            {
                if (string.IsNullOrWhiteSpace(field)) continue;
                var resolved = TryResolveAliasKey(field, cfg.Destinations);
                if (resolved != null) referenced.Add(resolved);
            }
        }

        // PASS 3 — remove unreferenced keys. "删了就是删了" 无兜底。
        var toRemove = new List<string>();
        foreach (var key in cfg.Destinations.Keys)
        {
            if (!referenced.Contains(key)) toRemove.Add(key);
        }
        foreach (var key in toRemove)
        {
            cfg.Destinations.Remove(key);
            removedKeys.Add(key);
        }

        // PASS 4 — null any rule whose Destination can no longer be resolved
        foreach (var rule in cfg.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Destination)) continue;
            if (TryResolveAliasKey(rule.Destination, cfg.Destinations) == null)
            {
                nulledRuleNames.Add(rule.Name);
                rule.Destination = null;
            }
        }

        return new Result(removedKeys, nulledRuleNames, registeredKeys);
    }

    private static string? TryResolveAliasKey(string field, Dictionary<string, string> destinations)
    {
        if (destinations.ContainsKey(field)) return field;
        foreach (var kv in destinations)
        {
            if (string.Equals(kv.Value, field, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        }
        return null;
    }

    private static string MakeUniqueKey(string baseName, IEnumerable<string> existing)
    {
        var set = new HashSet<string>(existing, StringComparer.Ordinal);
        if (!set.Contains(baseName)) return baseName;
        for (int i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName}_{i}";
            if (!set.Contains(candidate)) return candidate;
        }
        return $"{baseName}_{Guid.NewGuid():N}";
    }
}
