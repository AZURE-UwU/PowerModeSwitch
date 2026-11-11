// @file TrayIconService.cs
// @author Codex
// @module PowerModeSwitch.Services

using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using PowerModeSwitch.Models;
using PowerModeSwitch.Views;

namespace PowerModeSwitch.Services;

internal sealed class TrayIconService : IDisposable
{
    private const int WmApp = 0x8000;
    private const int TrayCallbackMessage = WmApp + 1;
    private const int WmCommand = 0x0111;
    private const int WmPowerBroadcast = 0x0218;
    private const int WmLeftButtonDoubleClick = 0x0203;
    private const int WmRightButtonUp = 0x0205;
    private const uint MfString = 0x00000000;
    private const uint MfChecked = 0x00000008;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmReturnCmd = 0x0100;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint NifShowTip = 0x00000080;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NiifInfo = 0x00000001;
    private const uint IdiApplication = 32512;
    private const uint MenuShow = 1001;
    private const uint MenuToggleAutomation = 1002;
    private const uint MenuExit = 1003;

    private readonly Guid _windowClassId = Guid.NewGuid();
    private readonly DispatcherQueue _dispatcherQueue;

    private PowerModeController? _controller;
    private WindowProc? _windowProc;
    private IntPtr _windowHandle;
    private IntPtr _iconHandle;
    private uint _taskbarCreatedMessage;
    private bool _installed;
    private bool _disposed;
    private PowerModeStatus? _lastStatus;

    public TrayIconService()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public void Attach(
        MainWindow mainWindow,
        PowerModeController controller,
        Func<Task> showWindow,
        Func<Task> exitApplication)
    {
        _controller = controller;
        _ = mainWindow;

        CreateHiddenWindow(showWindow, exitApplication);
        InstallIcon();
        UpdateStatus(controller.CurrentStatus);
        controller.StatusChanged += (_, status) =>
        {
            if (_dispatcherQueue.HasThreadAccess)
            {
                UpdateStatus(status);
            }
            else
            {
                _dispatcherQueue.TryEnqueue(() => UpdateStatus(status));
            }
        };
    }

    public void UpdateStatus(PowerModeStatus status)
    {
        _lastStatus = status;
        if (!_installed || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => UpdateStatus(status));
            return;
        }

        var data = CreateIconData();
        data.uFlags = NifTip;
        data.szTip = BuildTooltip(status);
        Shell_NotifyIconW(NimModify, ref data);
    }

    public void ShowBalloon(string title, string message)
    {
        if (!_installed || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => ShowBalloon(title, message));
            return;
        }

        var data = CreateIconData();
        data.uFlags = NifInfo;
        data.szInfoTitle = Limit(title, 63);
        data.szInfo = Limit(message, 255);
        data.dwInfoFlags = NiifInfo;
        data.uTimeout = 3500;
        Shell_NotifyIconW(NimModify, ref data);
    }

    private void CreateHiddenWindow(Func<Task> showWindow, Func<Task> exitApplication)
    {
        var className = "PowerModeSwitchTray_" + _windowClassId.ToString("N");
        _windowProc = (hwnd, message, wParam, lParam) =>
        {
            if (message == TrayCallbackMessage)
            {
                var mouseMessage = unchecked((int)lParam.ToInt64()) & 0xFFFF;
                if (mouseMessage == WmLeftButtonDoubleClick)
                {
                    _ = showWindow();
                }
                else if (mouseMessage == WmRightButtonUp)
                {
                    ShowContextMenu(showWindow, exitApplication);
                }

                return IntPtr.Zero;
            }

            if (message == WmCommand)
            {
                ExecuteMenuCommand(
                    (uint)(wParam.ToInt64() & 0xFFFF),
                    showWindow,
                    exitApplication);
                return IntPtr.Zero;
            }

            if (message == WmPowerBroadcast)
            {
                AppLog.Info($"收到 WM_POWERBROADCAST：0x{wParam.ToInt64():X}。");
                _controller?.NotifyWindowsPowerEvent();
                return IntPtr.Zero;
            }

            if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
            {
                _installed = false;
                InstallIcon();
                if (_lastStatus is not null)
                {
                    UpdateStatus(_lastStatus);
                }

                return IntPtr.Zero;
            }

            return DefWindowProcW(hwnd, message, wParam, lParam);
        };

        var moduleHandle = GetModuleHandleW(null);
        var windowClass = new WindowClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            hInstance = moduleHandle,
            lpszClassName = className
        };

        RegisterClassExW(ref windowClass);
        _windowHandle = CreateWindowExW(
            0x00000080,
            className,
            "PowerModeSwitchTray",
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            moduleHandle,
            IntPtr.Zero);
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
    }

    private void ShowContextMenu(Func<Task> showWindow, Func<Task> exitApplication)
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenuW(menu, MfString, MenuShow, "显示主界面");
            var enabled = _controller?.AutomationEnabled == true;
            AppendMenuW(
                menu,
                MfString | (enabled ? MfChecked : 0),
                MenuToggleAutomation,
                enabled ? "暂停自动切换" : "启用自动切换");
            AppendMenuW(menu, MfSeparator, 0, null);
            AppendMenuW(menu, MfString, MenuExit, "退出");

            GetCursorPos(out var point);
            SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenu(
                menu,
                TpmRightButton | TpmBottomAlign | TpmReturnCmd,
                point.X,
                point.Y,
                0,
                _windowHandle,
                IntPtr.Zero);

            ExecuteMenuCommand(command, showWindow, exitApplication);

            PostMessageW(_windowHandle, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void ExecuteMenuCommand(
        uint command,
        Func<Task> showWindow,
        Func<Task> exitApplication)
    {
        switch (command)
        {
            case MenuShow:
                AppLog.Info("托盘菜单：显示主界面。");
                _ = showWindow();
                break;
            case MenuToggleAutomation:
                AppLog.Info("托盘菜单：切换自动切换状态。");
                _controller?.SetAutomationEnabled(!_controller.AutomationEnabled);
                break;
            case MenuExit:
                AppLog.Info("托盘菜单：退出。");
                _ = exitApplication();
                break;
        }
    }

    private void InstallIcon()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        var moduleHandle = GetModuleHandleW(null);
        _iconHandle = LoadIconW(moduleHandle, (IntPtr)IdiApplication);
        if (_iconHandle == IntPtr.Zero)
        {
            _iconHandle = LoadIconW(IntPtr.Zero, (IntPtr)IdiApplication);
        }
        var data = CreateIconData();
        data.uFlags = NifMessage | NifIcon | NifTip | NifShowTip;
        data.szTip = "联想电源模式助手";
        _installed = Shell_NotifyIconW(NimAdd, ref data);
        AppLog.Info(_installed ? "托盘图标已创建。" : "托盘图标创建失败。");
    }

    private NotifyIconData CreateIconData()
    {
        return new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _windowHandle,
            uID = 1,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _iconHandle,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private static string BuildTooltip(PowerModeStatus status)
    {
        return Limit(
            $"联想电源模式助手\r\n{status.AdapterState.AdapterText()} · {status.CurrentMode.ModeText()}",
            127);
    }

    private static string Limit(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_windowHandle != IntPtr.Zero)
        {
            var data = CreateIconData();
            Shell_NotifyIconW(NimDelete, ref data);
            DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }

        _installed = false;
        GC.KeepAlive(_windowProc);
    }

    private delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeout;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr instance, IntPtr iconName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, uint itemId, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        int reserved,
        IntPtr hwnd,
        IntPtr rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
