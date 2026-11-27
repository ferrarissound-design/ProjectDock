using ProjectLauncher.Core.Detection;
using ProjectLauncher.Core.FileSystem;
using ProjectLauncher.Core.Logging;
using ProjectLauncher.Core.Models;
using ProjectLauncher.Core.Storage;

namespace ProjectLauncher.App;

/// <summary>UI スレッドからのみ触るデータの持ち主。変更したら Save() を呼ぶ。</summary>
internal sealed class ProjectCatalog
{
    private readonly ProjectStore _store;
    private readonly IAppLogger _log;

    public ProjectCatalog(ProjectStore store, IAppLogger log)
    {
        _store = store;
        _log = log;
        var r = store.Load();
        Data = r.Data;
        CorruptBackupPath = r.CorruptBackupPath;
        _log.Info($"projects.json 読み込み: {Data.Projects.Count} 件");
    }

    public LauncherData Data { get; }
    public AppSettings Settings => Data.Settings;
    public List<ProjectEntry> Projects => Data.Projects;

    /// <summary>起動時に JSON が壊れていた場合の退避先。</summary>
    public string? CorruptBackupPath { get; }

    public event EventHandler? Changed;

    public void Save()
    {
        try
        {
            _store.Save(Data);
        }
        catch (Exception ex)
        {
            _log.Error("projects.json の保存に失敗", ex);
        }
    }

    public bool Contains(string path) => Projects.Any(p => PathUtil.SamePath(p.Path, path));

    /// <summary>フォルダを登録する。登録済みなら null。</summary>
    public ProjectEntry? Add(string path, string? name = null, ProjectKind? kind = null)
    {
        var norm = PathUtil.Normalize(path);
        if (norm.Length == 0 || Contains(norm))
            return null;
        var entry = new ProjectEntry
        {
            Name = string.IsNullOrWhiteSpace(name) ? PathUtil.FolderName(norm) : name.Trim(),
            Path = norm,
            Kind = kind ?? ProjectDetector.DetectKind(norm),
        };
        Projects.Add(entry);
        _log.Info($"登録: {entry.Name} ({entry.Kind}) {entry.Path}");
        return entry;
    }

    public void Remove(ProjectEntry entry)
    {
        Projects.Remove(entry);
        _log.Info($"削除: {entry.Name} {entry.Path}");
    }

    public void NotifyChanged()
    {
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
