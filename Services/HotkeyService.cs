using System.Windows.Input;
using System.Windows.Interop;
using DockNest.Interop;

namespace DockNest.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource source;
    private readonly Action onPressed;
    private int activeId;
    private uint activeModifiers, activeKey;
    private static int nextId = 0x444E;
    private readonly int firstId = Interlocked.Add(ref nextId, 2);
    public bool IsRegistered => activeId != 0;

    public HotkeyService(HwndSource source, Action onPressed)
    {
        this.source = source;
        this.onPressed = onPressed;
        source.AddHook(WindowProc);
    }

    public bool TryRegister(uint modifiers, uint key)
    {
        if (activeId != 0 && activeModifiers == modifiers && activeKey == key) return true;
        var nextId = activeId == firstId ? firstId + 1 : firstId;
        // Keep the old binding alive if the new combination is unavailable.
        if (!NativeMethods.RegisterHotKey(source.Handle, nextId, modifiers | 0x4000, key)) return false;
        if (activeId != 0) NativeMethods.UnregisterHotKey(source.Handle, activeId);
        activeId = nextId;
        activeModifiers = modifiers;
        activeKey = key;
        return true;
    }

    public static string Format(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0) parts.Add("Ctrl");
        if ((modifiers & 1) != 0) parts.Add("Alt");
        if ((modifiers & 4) != 0) parts.Add("Shift");
        if ((modifiers & 8) != 0) parts.Add("Win");
        parts.Add(key == 0x20 ? "空格" : KeyInterop.KeyFromVirtualKey((int)key).ToString());
        return string.Join(" + ", parts);
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == activeId) { onPressed(); handled = true; }
        return 0;
    }

    public void Dispose()
    {
        if (activeId != 0) NativeMethods.UnregisterHotKey(source.Handle, activeId);
        source.RemoveHook(WindowProc);
        activeId = 0;
    }
}
