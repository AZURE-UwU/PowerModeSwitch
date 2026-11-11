// @file NativeMessageBox.cs
// @author Codex
// @module PowerModeSwitch.Native

using System.Runtime.InteropServices;

namespace PowerModeSwitch;

internal static class NativeMessageBox
{
    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONINFORMATION = 0x00000040;
    private const uint MB_SETFOREGROUND = 0x00010000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static void Show(string text, string caption)
    {
        MessageBoxW(IntPtr.Zero, text, caption, MB_OK | MB_ICONINFORMATION | MB_SETFOREGROUND);
    }
}
