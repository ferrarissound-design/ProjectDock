namespace ProjectLauncher.Core.Services;

public sealed record HotkeyDefinition(bool Ctrl, bool Alt, bool Shift, bool Win, string Key)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(Key);
        return string.Join("+", parts);
    }
}

/// <summary>"Ctrl+Alt+P" 形式の文字列を解釈する。キー名→仮想キーコードの変換は UI 層で行う。</summary>
public static class HotkeyParser
{
    public static bool TryParse(string? text, out HotkeyDefinition? hotkey)
    {
        hotkey = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        bool ctrl = false, alt = false, shift = false, win = false;
        string? key = null;

        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0)
                return false;
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win" or "windows": win = true; break;
                default:
                    if (key != null)
                        return false; // 修飾キー以外が2つ
                    key = raw.Length == 1 ? raw.ToUpperInvariant() : raw;
                    break;
            }
        }

        // 修飾キーなしのグローバルホットキーは誤爆するので認めない
        if (key == null || !(ctrl || alt || shift || win))
            return false;

        hotkey = new HotkeyDefinition(ctrl, alt, shift, win, key);
        return true;
    }
}
