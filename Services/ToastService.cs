// @file ToastService.cs
// @author Codex
// @module PowerModeSwitch.Services

using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace PowerModeSwitch.Services;

internal sealed class ToastService : IDisposable
{
    private AppNotificationManager? _manager;
    private TrayIconService? _trayIcon;
    private bool _registered;
    private bool _disposed;
    private string _lastError = string.Empty;

    public void Initialize()
    {
        if (_registered || _disposed)
        {
            return;
        }

        try
        {
            if (!AppNotificationManager.IsSupported())
            {
                _lastError = "当前系统不支持应用通知。";
                AppLog.Info(_lastError);
                return;
            }

            var applicationPath = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "PowerModeSwitch.exe");
            var identityReady = NotificationShortcutService.EnsureCreated(applicationPath);
            _manager = AppNotificationManager.Default;
            _manager.Register();
            _registered = true;
            _lastError = string.Empty;
            AppLog.Info(
                identityReady
                    ? "Windows App SDK Toast 与通知身份已注册。"
                    : "Windows App SDK Toast 已注册，开始菜单通知身份创建失败。");
        }
        catch (Exception ex)
        {
            _registered = false;
            _lastError = ex.Message;
            AppLog.Info($"Windows App SDK Toast 注册失败：{ex}");
        }
    }

    public void AttachFallback(TrayIconService trayIcon)
    {
        _trayIcon = trayIcon;
    }

    public void Show(string title, string message)
    {
        if (_disposed)
        {
            return;
        }

        Initialize();
        if (!_registered || _manager is null)
        {
            ShowFallback(title, message, _lastError);
            return;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();
            _manager.Show(notification);
            AppLog.Info($"已显示 Toast：{title}。");
        }
        catch (Exception ex)
        {
            _registered = false;
            _lastError = ex.Message;
            AppLog.Info($"Toast 显示失败：{ex}");
            ShowFallback(title, message, _lastError);
        }
    }

    private void ShowFallback(string title, string message, string reason)
    {
        AppLog.Info($"Toast 不可用，改用托盘通知：{reason}。");
        _trayIcon?.ShowBalloon(title, message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_registered)
        {
            return;
        }

        try
        {
            _manager?.Unregister();
        }
        catch
        {
        }

        _registered = false;
    }
}
