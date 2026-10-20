using System.Text;

namespace FileSorter.Core;

/// <summary>
/// 追加写文件日志。自动归类在后台跑,WinExe 的 Console 没人看得到,
/// 出问题必须能从 %APPDATA%\FileSorter\filesorter.log 查。
/// 单文件上限 2MB,超了轮转一份 .1。
/// </summary>
public sealed class FileLogger : ILogger
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private readonly string _path;
    private readonly object _gate = new();

    public FileLogger(string path)
    {
        _path = path;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        catch { /* 日志目录建不出来也不能让主流程挂 */ }
    }

    public void Info(string msg) => Write("INFO", msg);
    public void Warn(string msg) => Write("WARN", msg);
    public void Error(string msg) => Write("ERROR", msg);

    private void Write(string level, string msg)
    {
        try
        {
            lock (_gate)
            {
                Rotate();
                File.AppendAllText(_path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch { /* 写日志失败静默 —— 绝不能因为日志把归类流程带崩 */ }
    }

    private void Rotate()
    {
        try
        {
            var fi = new FileInfo(_path);
            if (!fi.Exists || fi.Length < MaxBytes) return;
            var old = _path + ".1";
            try { if (File.Exists(old)) File.Delete(old); } catch { }
            File.Move(_path, old);
        }
        catch { }
    }
}
