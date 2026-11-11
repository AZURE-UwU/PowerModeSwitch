// @file LenovoModeService.cs
// @author Codex
// @module PowerModeSwitch.Services

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PowerModeSwitch.Models;

namespace PowerModeSwitch.Services;

internal sealed class LenovoModeService
{
    private const string ItsServiceName = "LITSSVC";
    private const string ModeRegistryPath =
        @"SYSTEM\CurrentControlSet\Services\LITSSVC\LNBITS\IC\MMC";

    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceUserDefinedControl = 0x0100;

    public const uint BalancedControlCode = 135;
    public const uint SavingControlCode = 146;
    public const uint PerformanceControlCode = 148;

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenServiceW(IntPtr serviceManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(
        IntPtr service,
        uint control,
        out ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    public LenovoPowerMode ReadCurrentMode()
    {
        using var key = RegistryKey
            .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
            .OpenSubKey(ModeRegistryPath, writable: false);

        if (key is null)
        {
            return LenovoPowerMode.Unknown;
        }

        var automatic = key.GetValue("AutomaticModeSetting") is int automaticValue
            ? automaticValue
            : -1;
        var current = key.GetValue("CurrentSetting") is int currentValue
            ? currentValue
            : -1;

        if (automatic == 2)
        {
            return LenovoPowerMode.Balanced;
        }

        return (automatic, current) switch
        {
            (1, 1) => LenovoPowerMode.Saving,
            (1, 3) => LenovoPowerMode.Performance,
            _ => LenovoPowerMode.Unknown
        };
    }

    public async Task SetModeAsync(LenovoPowerMode mode, CancellationToken cancellationToken = default)
    {
        var controlCode = mode switch
        {
            LenovoPowerMode.Balanced => BalancedControlCode,
            LenovoPowerMode.Saving => SavingControlCode,
            LenovoPowerMode.Performance => PerformanceControlCode,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

        await Task.Run(() =>
        {
            SendControlCode(controlCode);

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ReadCurrentMode() == mode)
                {
                    return;
                }

                Thread.Sleep(100);
            }

            throw new InvalidOperationException(
                $"LITSSVC 未在预期时间内进入{mode.ModeText()}。");
        }, cancellationToken);
    }

    private static void SendControlCode(uint controlCode)
    {
        var manager = OpenSCManagerW(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法连接 Windows 服务管理器。");
        }

        try
        {
            var service = OpenServiceW(manager, ItsServiceName, ServiceUserDefinedControl);
            if (service == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开 LITSSVC。");
            }

            try
            {
                if (!ControlService(service, controlCode, out _))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "LITSSVC 拒绝了控制请求。");
                }
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }
}
