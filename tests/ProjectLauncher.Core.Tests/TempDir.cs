namespace ProjectLauncher.Core.Tests;

/// <summary>テストごとの一時フォルダ。Dispose で削除。</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Dir(params string[] parts)
    {
        var p = System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
        Directory.CreateDirectory(p);
        return p;
    }

    public string File(string relative, string content = "", DateTime? lastWriteUtc = null)
    {
        var p = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        if (lastWriteUtc != null)
            System.IO.File.SetLastWriteTimeUtc(p, lastWriteUtc.Value);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* ignore */ }
    }
}
