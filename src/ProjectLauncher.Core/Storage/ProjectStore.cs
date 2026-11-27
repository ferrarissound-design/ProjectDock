using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectLauncher.Core.Logging;
using ProjectLauncher.Core.Models;

namespace ProjectLauncher.Core.Storage;

public sealed record LoadResult(LauncherData Data, string? CorruptBackupPath);

/// <summary>projects.json の読み書き。書き込みは一時ファイル経由で原子的に置き換える。</summary>
public sealed class ProjectStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IAppLogger _log;
    private readonly object _writeLock = new();

    public ProjectStore(string filePath, IAppLogger? log = null)
    {
        FilePath = filePath;
        _log = log ?? NullLogger.Instance;
    }

    public string FilePath { get; }

    /// <summary>
    /// 読み込む。ファイルがなければ空データ。
    /// JSON が壊れていたら projects.json.broken-yyyyMMdd-HHmmss.bak に退避して空データで返す。
    /// </summary>
    public LoadResult Load()
    {
        if (!File.Exists(FilePath))
            return new LoadResult(new LauncherData(), null);

        try
        {
            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<LauncherData>(json, JsonOptions)
                       ?? throw new JsonException("root is null");
            Sanitize(data);
            return new LoadResult(data, null);
        }
        catch (JsonException ex)
        {
            var backup = BackupCorrupt();
            _log.Error($"projects.json が壊れていたため退避しました: {backup}", ex);
            return new LoadResult(new LauncherData(), backup);
        }
        catch (NotSupportedException ex)
        {
            var backup = BackupCorrupt();
            _log.Error($"projects.json を解釈できないため退避しました: {backup}", ex);
            return new LoadResult(new LauncherData(), backup);
        }
    }

    public void Save(LauncherData data)
    {
        lock (_writeLock)
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tmp = FilePath + ".tmp";
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
    }

    private string BackupCorrupt()
    {
        var backup = $"{FilePath}.broken-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        var i = 1;
        while (File.Exists(backup))
            backup = $"{FilePath}.broken-{DateTime.Now:yyyyMMdd-HHmmss}-{i++}.bak";
        File.Move(FilePath, backup);
        return backup;
    }

    private static void Sanitize(LauncherData data)
    {
        data.Settings ??= new AppSettings();
        data.Projects ??= new List<ProjectEntry>();
        data.Projects.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Path));
        foreach (var p in data.Projects)
        {
            p.Tags ??= new List<string>();
            if (string.IsNullOrWhiteSpace(p.Id))
                p.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(p.Name))
                p.Name = FileSystem.PathUtil.FolderName(p.Path);
        }
        if (data.Settings.MaxParallelism < 1)
            data.Settings.MaxParallelism = 4;
        data.Settings.ScanDepth = Math.Clamp(data.Settings.ScanDepth, 1, 2);
        if (string.IsNullOrWhiteSpace(data.Settings.Hotkey))
            data.Settings.Hotkey = "Ctrl+Alt+P";
    }
}
