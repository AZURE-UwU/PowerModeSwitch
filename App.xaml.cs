// @file App.xaml.cs
// @author Codex
// @module PowerModeSwitch

using Microsoft.UI.Xaml;
using PowerModeSwitch.Services;
using PowerModeSwitch.Views;

namespace PowerModeSwitch;

public partial class App : Application
{
    private const string MutexName = @"Local\PowerModeSwitch.SingleInstance";

    private readonly Mutex _singleInstanceMutex;
    private readonly bool _ownsMutex;
    private MainWindow? _mainWindow;
    private PowerModeController? _controller;
    private TrayIconService? _trayIcon;
    private ToastService? _toastService;
    private bool _isExiting;

    internal ToastService? ToastService => _toastService;

    public App()
    {
        InitializeComponent();

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            NativeMessageBox.Show("联想电源模式助手已在运行。", "联想电源模式助手");
            Environment.Exit(0);
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLog.Info("应用启动。");
        _toastService = new ToastService();
        _toastService.Initialize();

        _controller = new PowerModeController();
        _trayIcon = new TrayIconService();
        _mainWindow = new MainWindow(_controller, _toastService);
        _toastService.AttachFallback(_trayIcon);

        _trayIcon.Attach(
            _mainWindow,
            _controller,
            showWindow: ShowMainWindowAsync,
            exitApplication: ExitApplicationAsync);

        _mainWindow.Activate();
        _controller.Start();
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowFromTray();
    }

    public async Task ShowMainWindowAsync()
    {
        await Task.CompletedTask;
        ShowMainWindow();
    }

    public async Task ExitApplicationAsync()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        AppLog.Info("用户请求退出。");
        _controller?.Dispose();
        _trayIcon?.Dispose();
        _toastService?.Dispose();

        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose();
            _mainWindow.Close();
        }

        if (_ownsMutex)
        {
            _singleInstanceMutex.ReleaseMutex();
        }

        await Task.CompletedTask;
        Exit();
    }
}
