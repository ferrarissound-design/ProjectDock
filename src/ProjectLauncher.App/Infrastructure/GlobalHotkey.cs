using System.Runtime.InteropServices;
using ProjectLauncher.Core.Services;

namespace ProjectLauncher.App.Infrastructure;

/// <summary>RegisterHotKey によるグローバルホットキー。隠しウィンドウで WM_HOTKEY を受ける。</summary>
internal sealed class GlobalHotkey : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000;
    private const int HotkeyId = 0x504C; // "PL"

    private bool _registered;

    public event EventHandler? Pressed;

    public GlobalHotkey()
    {
        CreateHandle(new CreateParams { Caption = "ProjectLauncher.Hotkey" });
    }

    /// <summary>登録し直す。失敗（他アプリが使用中など）なら false と理由を返す。</summary>
    public bool TryRegister(string text, out string? error)
    {
        Unregister();
        error = null;

        if (!HotkeyParser.TryParse(text, out var def) || def == null)
        {
            error = $"ホットキーの形式が不正です: {text}（例: Ctrl+Alt+P）";
            return false;
        }
        if (!TryGetKey(def.Key, out var key))
        {
            error = $"キー名を解釈できません: {def.Key}";
            return false;
        }

        uint mods = MOD_NOREPEAT;
        if (def.Ctrl) mods |= MOD_CONTROL;
        if (def.Alt) mods |= MOD_ALT;
        if (def.Shift) mods |= MOD_SHIFT;
        if (def.Win) mods |= MOD_WIN;

        if (!RegisterHotKey(Handle, HotkeyId, mods, (uint)key))
        {
            error = $"{def} は他のアプリが使用中のため登録できませんでした（Win32 エラー {Marshal.GetLastWin32Error()}）";
            return false;
        }
        _registered = true;
        return true;
    }

    public static bool TryGetKey(string name, out Keys key)
    {
        // "1" → D1 のように数字は D 付きの名前にする
        if (name.Length == 1 && char.IsDigit(name[0]))
            name = "D" + name;
        return Enum.TryParse(name, ignoreCase: true, out key) && key != Keys.None
               && (key & Keys.Modifiers) == 0;
    }

    /// <summary>設定画面でキー入力から "Ctrl+Alt+P" を作る。</summary>
    public static string? Describe(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None)
            return null;
        var name = key is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + (key - Keys.D0))).ToString() : key.ToString();
        var def = new HotkeyDefinition(
            keyData.HasFlag(Keys.Control), keyData.HasFlag(Keys.Alt), keyData.HasFlag(Keys.Shift), false, name);
        return def.ToString();
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            Pressed?.Invoke(this, EventArgs.Empty);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
