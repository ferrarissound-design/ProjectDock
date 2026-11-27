namespace ProjectLauncher.Core.Models;

public enum SortMode
{
    LastModifiedDesc = 0,
    Name = 1,
    DirtyFirst = 2,
}

public sealed class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+P";
    public bool StartWithWindows { get; set; }
    public int ScanDepth { get; set; } = 2;
    public int MaxParallelism { get; set; } = 4;
    public SortMode SortMode { get; set; } = SortMode.LastModifiedDesc;
    public bool OnlyDirty { get; set; }
    public string? LastScanFolder { get; set; }

    /// <summary>ウィンドウの位置とサイズ（x,y,w,h）。</summary>
    public int[]? WindowBounds { get; set; }

    /// <summary>×ボタンでトレイに格納した旨のバルーンを一度出したか。</summary>
    public bool TrayHintShown { get; set; }
}

/// <summary>projects.json のルート。</summary>
public sealed class LauncherData
{
    public int SchemaVersion { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<ProjectEntry> Projects { get; set; } = new();
}
