namespace ProjectLauncher.App.Infrastructure;

internal static class AppIcon
{
    private static Icon? _icon;

    public static Icon Get()
    {
        if (_icon != null)
            return _icon;
        using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("ProjectLauncher.app.ico");
        _icon = s != null ? new Icon(s) : SystemIcons.Application;
        return _icon;
    }

    /// <summary>トレイ用に小さいサイズを選ぶ（DPI に合わせて 16/20/24/32）。</summary>
    public static Icon GetSmall()
    {
        var size = SystemInformation.SmallIconSize;
        return new Icon(Get(), size);
    }
}
