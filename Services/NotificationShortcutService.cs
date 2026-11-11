// @file NotificationShortcutService.cs
// @author Codex
// @module PowerModeSwitch.Services

using System.Runtime.InteropServices;

namespace PowerModeSwitch.Services;

internal static class NotificationShortcutService
{
    public static bool EnsureCreated(string applicationPath)
    {
        try
        {
            var shortcutDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "Windows",
                "Start Menu",
                "Programs");
            Directory.CreateDirectory(shortcutDirectory);
            var shortcutPath = Path.Combine(shortcutDirectory, "PowerModeSwitch.lnk");

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            object? shellObject = null;
            object? shortcutObject = null;
            try
            {
                dynamic shell = Activator.CreateInstance(shellType)
                    ?? throw new InvalidOperationException("无法创建 WScript.Shell。");
                shellObject = shell;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcutObject = shortcut;

                if (File.Exists(shortcutPath) &&
                    string.Equals(
                        Path.GetFullPath((string)shortcut.TargetPath),
                        Path.GetFullPath(applicationPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                shortcut.TargetPath = applicationPath;
                shortcut.Arguments = string.Empty;
                shortcut.WorkingDirectory =
                    Path.GetDirectoryName(applicationPath) ?? string.Empty;
                shortcut.IconLocation = $"{applicationPath},0";
                shortcut.Description = "联想电源模式助手";
                shortcut.Save();
                return File.Exists(shortcutPath);
            }
            finally
            {
                if (shortcutObject is not null)
                {
                    Marshal.FinalReleaseComObject(shortcutObject);
                }

                if (shellObject is not null)
                {
                    Marshal.FinalReleaseComObject(shellObject);
                }
            }
        }
        catch
        {
            return false;
        }
    }
}
