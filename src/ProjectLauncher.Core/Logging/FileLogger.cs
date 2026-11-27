using System.Text;

namespace ProjectLauncher.Core.Logging;

public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}

public sealed class NullLogger : IAppLogger
{
    public static readonly NullLogger Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? ex = null) { }
}

/// <summary>
/// 日付ごとのファイル (app-yyyyMMdd.log) に追記する簡易ロガー。
/// ローテーション：起動時に retentionDays より古いファイルを削除、1ファイルが maxBytes を超えたら .1 に退避。
/// </summary>
public sealed class FileLogger : IAppLogger
{
    private readonly string _dir;
    private readonly long _maxBytes;
    private readonly object _lock = new();

    public FileLogger(string directory, int retentionDays = 7, long maxBytes = 2 * 1024 * 1024)
    {
        _dir = directory;
        _maxBytes = maxBytes;
        try
        {
            Directory.CreateDirectory(_dir);
            Cleanup(retentionDays);
        }
        catch
        {
            // ログが書けなくてもアプリは動かす
        }
    }

    public string CurrentFilePath => System.IO.Path.Combine(_dir, $"app-{DateTime.Now:yyyyMMdd}.log");

    public void Info(string message) => Write("INFO ", message, null);
    public void Warn(string message) => Write("WARN ", message, null);
    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private void Write(string level, string message, Exception? ex)
    {
        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ')
          .Append(level).Append(" [").Append(Environment.CurrentManagedThreadId).Append("] ")
          .Append(message);
        if (ex != null)
            sb.AppendLine().Append(ex);
        sb.AppendLine();

        lock (_lock)
        {
            try
            {
                var path = CurrentFilePath;
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > _maxBytes)
                {
                    var rotated = path + ".1";
                    File.Move(path, rotated, overwrite: true);
                }
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // ignore
            }
        }
    }

    private void Cleanup(int retentionDays)
    {
        var limit = DateTime.Now.AddDays(-retentionDays);
        foreach (var f in Directory.EnumerateFiles(_dir, "app-*.log*"))
        {
            try
            {
                if (File.GetLastWriteTime(f) < limit)
                    File.Delete(f);
            }
            catch { /* ignore */ }
        }
    }
}
