// @file MainWindow.xaml.cs
// @author Codex
// @module PowerModeSwitch.Views

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PowerModeSwitch.Models;
using PowerModeSwitch.Services;
using System.Runtime.InteropServices;
using Windows.System;

namespace PowerModeSwitch.Views;

public sealed partial class MainWindow : Window
{
    private readonly PowerModeController _controller;
    private readonly ToastService _toastService;
    private bool _updatingToggle;
    private bool _updatingDelay;
    private bool _allowClose;

    internal MainWindow(PowerModeController controller, ToastService toastService)
    {
        InitializeComponent();
        _controller = controller;
        _toastService = toastService;

        Title = "联想电源模式助手";
        AppWindow.Closing += AppWindow_Closing;
        RootGrid.Loaded += RootGrid_Loaded;
        BatteryDelayNumberBox.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(BatteryDelayNumberBox_KeyDown),
            handledEventsToo: true);
        _controller.StatusChanged += Controller_StatusChanged;
        UpdateStatus(_controller.CurrentStatus);
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(300);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetWindowPos(
                windowHandle,
                IntPtr.Zero,
                0,
                0,
                1000,
                900,
                0x0002 | 0x0004 | 0x0010);
        };
        timer.Start();
    }

    internal void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    internal void AllowClose()
    {
        _allowClose = true;
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        AppWindow.Hide();
    }

    private void Controller_StatusChanged(object? sender, PowerModeStatus status)
    {
        DispatcherQueue.TryEnqueue(() => UpdateStatus(status));
    }

    private void UpdateStatus(PowerModeStatus status)
    {
        AdapterStatusText.Text = status.AdapterState.AdapterText();
        CurrentModeText.Text = $"当前：{status.CurrentMode.ModeText()}";
        DetailText.Text = status.Detail;
        LastTransitionText.Text = status.LastTransition is null
            ? "尚无自动切换"
            : $"最近切换：{status.LastTransition:HH:mm:ss}";

        _updatingToggle = true;
        AutomationToggle.IsOn = status.AutomationEnabled;
        _updatingToggle = false;

        _updatingDelay = true;
        BatteryDelayNumberBox.Value = _controller.BatteryDelaySeconds;
        _updatingDelay = false;
    }

    private void AutomationToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingToggle)
        {
            return;
        }

        _controller.SetAutomationEnabled(AutomationToggle.IsOn);
    }

    private void BatteryDelayNumberBox_ValueChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        if (_updatingDelay || double.IsNaN(args.NewValue))
        {
            return;
        }

        var delaySeconds = (int)Math.Round(args.NewValue);
        if (Math.Abs(args.NewValue - delaySeconds) > 0.001)
        {
            _updatingDelay = true;
            sender.Value = delaySeconds;
            _updatingDelay = false;
        }

        _controller.SetBatteryDelaySeconds(delaySeconds);
    }

    private void BatteryDelayNumberBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || sender is not NumberBox numberBox)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            var delayValue = numberBox.Value;
            if (double.TryParse(numberBox.Text, out var textValue))
            {
                delayValue = textValue;
            }

            if (!double.IsNaN(delayValue))
            {
                var delaySeconds = (int)Math.Round(delayValue);
                _controller.SetBatteryDelaySeconds(delaySeconds);
            }

            RefreshButton.Focus(FocusState.Keyboard);
        });
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await _controller.RefreshAsync();
    }

    private void TestToastButton_Click(object sender, RoutedEventArgs e)
    {
        _toastService.Show("联想电源模式助手", "系统通知工作正常。");
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

}
