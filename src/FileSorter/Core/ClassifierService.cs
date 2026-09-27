using FileSorter.Core.Models;

namespace FileSorter.Core;

public record ClassifyResult(bool Success, string? Error, string FinalPath);

public class ClassifierService
{
    private readonly RulesConfig _cfg;
    private readonly bool _dryRun;
    private readonly ILogger _log;

    public ClassifierService(RulesConfig cfg, bool dryRun, ILogger? log = null)
    {
        _cfg = cfg;
        _dryRun = dryRun;
        _log = log ?? new ConsoleLogger();
    }

    public ClassifyResult ClassifyOne(string srcPath)
    {
        try
        {
            if (!File.Exists(srcPath))
                return new ClassifyResult(false, "source not found", srcPath);

            var match = RuleEngine.Match(_cfg, srcPath);
            if (match is null)
                return new ClassifyResult(false, "no rule matched", srcPath);

            Directory.CreateDirectory(match.DestinationPath);

            var fileName = Path.GetFileName(srcPath);
            var finalPath = ResolveConflict(Path.Combine(match.DestinationPath, fileName));

            if (_dryRun)
            {
                _log.Info($"[DRY] {srcPath} -> {finalPath}");
                return new ClassifyResult(true, null, finalPath);
            }

            if (string.Equals(_cfg.DefaultAction, "copy", StringComparison.OrdinalIgnoreCase))
                File.Copy(srcPath, finalPath, overwrite: false);
            else
                File.Move(srcPath, finalPath);

            _log.Info($"{srcPath} -> {finalPath}");
            return new ClassifyResult(true, null, finalPath);
        }
        catch (Exception ex)
        {
            _log.Error($"classify failed for {srcPath}: {ex.Message}");
            return new ClassifyResult(false, ex.Message, srcPath);
        }
    }

    private string ResolveConflict(string targetPath)
    {
        if (!File.Exists(targetPath)) return targetPath;
        switch (_cfg.ConflictStrategy)
        {
            case "overwrite":
                return targetPath;
            case "skip":
                // Move/Copy with overwrite:false will throw → caught above
                return targetPath;
            case "rename":
            default:
                var dir = Path.GetDirectoryName(targetPath)!;
                var stem = Path.GetFileNameWithoutExtension(targetPath);
                var ext = Path.GetExtension(targetPath);
                for (int i = 1; i < 10_000; i++)
                {
                    var candidate = Path.Combine(dir, $"{stem}_{i}{ext}");
                    if (!File.Exists(candidate)) return candidate;
                }
                throw new IOException("too many conflicts");
        }
    }
}

public interface ILogger
{
    void Info(string msg);
    void Warn(string msg);
    void Error(string msg);
}

public class ConsoleLogger : ILogger
{
    public void Info(string msg) => Console.WriteLine($"[INFO] {msg}");
    public void Warn(string msg) => Console.WriteLine($"[WARN] {msg}");
    public void Error(string msg) => Console.Error.WriteLine($"[ERROR] {msg}");
}