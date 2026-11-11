// @file PowerModeController.cs
// @author Codex
// @module PowerModeSwitch.Services

using PowerModeSwitch.Models;

namespace PowerModeSwitch.Services;

internal sealed class PowerModeController : IDisposable
{
    private static readonly TimeSpan PowerEventDebounce = TimeSpan.FromMilliseconds(1200);

    private readonly LenovoModeService _modeService = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly object _stateLock = new();

    private ModeRegistryMonitor? _modeMonitor;
    private CancellationTokenSource? _debounceCancellation;
    private CancellationTokenSource? _pendingBatteryCancellation;
    private AppSettings _settings;
    private PowerModeStatus _status;
    private AdapterState _lastStableAdapter;
    private bool _started;
    private bool _disposed;

    public PowerModeController()
    {
        _settings = _settingsService.Load();
        var adapter = NativePowerStatus.GetAdapterState();
        _lastStableAdapter = adapter;
        _status = new PowerModeStatus(
            adapter,
            _modeService.ReadCurrentMode(),
            _settings.AutomationEnabled,
            "程序启动时仅读取状态，不修改当前模式。",
            null);
    }

    public event EventHandler<PowerModeStatus>? StatusChanged;

    public bool AutomationEnabled
    {
        get
        {
            lock (_stateLock)
            {
                return _settings.AutomationEnabled;
            }
        }
    }

    public int BatteryDelaySeconds
    {
        get
        {
            lock (_stateLock)
            {
                return _settings.BatteryDelaySeconds;
            }
        }
    }

    public PowerModeStatus CurrentStatus
    {
        get
        {
            lock (_stateLock)
            {
                return _status;
            }
        }
    }

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _modeMonitor = new ModeRegistryMonitor(RefreshModeOnly);
        _modeMonitor.Start();

        AppLog.Info("已通过隐藏窗口订阅 WM_POWERBROADCAST。");
        AppLog.Info(
            $"初始状态：{CurrentStatus.AdapterState.AdapterText()}，{CurrentStatus.CurrentMode.ModeText()}，自动切换={AutomationEnabled}。");
        Publish(CurrentStatus);
    }

    internal void NotifyWindowsPowerEvent()
    {
        SchedulePowerTransition();
    }

    public void SetAutomationEnabled(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        PowerModeStatus updated;
        lock (_stateLock)
        {
            if (!enabled)
            {
                CancelPendingBatteryTransitionLocked();
            }

            _settings = _settings with { AutomationEnabled = enabled };
            _settingsService.Save(_settings);
            AppLog.Info($"自动切换状态已改为 {enabled}。");
            updated = _status with
            {
                AutomationEnabled = enabled,
                CurrentMode = _modeService.ReadCurrentMode(),
                Detail = enabled
                    ? "自动切换已启用；将在下次电源状态变化时执行。"
                    : "自动切换已暂停，电源插拔不会修改模式。"
            };
            _status = updated;
        }

        Publish(updated);
    }

    public void SetBatteryDelaySeconds(int seconds)
    {
        if (_disposed)
        {
            return;
        }

        var delaySeconds = Math.Clamp(seconds, 0, AppSettings.MaxBatteryDelaySeconds);
        PowerModeStatus updated;
        lock (_stateLock)
        {
            _settings = _settings with { BatteryDelaySeconds = delaySeconds };
            _settingsService.Save(_settings);
            var pendingBatteryTransition = _pendingBatteryCancellation is not null;
            updated = _status with
            {
                Detail = pendingBatteryTransition
                    ? $"断电延时已保存为 {delaySeconds} 秒；当前倒计时仍按原设置执行。"
                    : delaySeconds == 0
                        ? "断电后将立即切换为省电模式。"
                        : $"断电并稳定识别后等待 {delaySeconds} 秒，再切换为省电模式。"
            };
            _status = updated;
        }

        AppLog.Info($"断电延时已改为 {delaySeconds} 秒。");
        Publish(updated);
    }

    public async Task RefreshAsync()
    {
        if (_disposed)
        {
            return;
        }

        var adapter = NativePowerStatus.GetAdapterState();
        var mode = await Task.Run(_modeService.ReadCurrentMode);
        PowerModeStatus updated;

        lock (_stateLock)
        {
            updated = _status with
            {
                AdapterState = adapter,
                CurrentMode = mode,
                Detail = "状态已刷新。"
            };
            _status = updated;
        }

        Publish(updated);
    }

    private void RefreshModeOnly()
    {
        if (_disposed)
        {
            return;
        }

        PowerModeStatus updated;
        lock (_stateLock)
        {
            updated = _status with
            {
                CurrentMode = _modeService.ReadCurrentMode()
            };
            _status = updated;
        }

        Publish(updated);
    }

    private void SchedulePowerTransition()
    {
        if (_disposed)
        {
            return;
        }

        CancellationTokenSource next;
        lock (_stateLock)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            next = new CancellationTokenSource();
            _debounceCancellation = next;
        }

        _ = DebounceAndApplyAsync(next.Token);
    }

    private async Task DebounceAndApplyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(PowerEventDebounce, cancellationToken);
            await ApplyStablePowerTransitionAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ApplyStablePowerTransitionAsync(CancellationToken cancellationToken)
    {
        await _transitionGate.WaitAsync(cancellationToken);
        try
        {
            var adapter = NativePowerStatus.GetAdapterState();
            if (adapter == AdapterState.Unknown)
            {
                return;
            }

            AdapterState previous;
            bool automationEnabled;
            int batteryDelaySeconds;
            lock (_stateLock)
            {
                previous = _lastStableAdapter;
                if (adapter == previous)
                {
                    return;
                }

                _lastStableAdapter = adapter;
                automationEnabled = _settings.AutomationEnabled;
                batteryDelaySeconds = _settings.BatteryDelaySeconds;
            }
            AppLog.Info($"电源回调：当前 {adapter.AdapterText()}，上次 {previous.AdapterText()}。");

            if (!automationEnabled)
            {
                lock (_stateLock)
                {
                    CancelPendingBatteryTransitionLocked();
                }

                Publish(new PowerModeStatus(
                    adapter,
                    _modeService.ReadCurrentMode(),
                    false,
                    $"电源状态已变为“{adapter.AdapterText()}”，自动切换已暂停。",
                    DateTimeOffset.Now));
                return;
            }

            var desiredMode = adapter == AdapterState.Ac
                ? LenovoPowerMode.Balanced
                : LenovoPowerMode.Saving;

            if (adapter == AdapterState.Ac)
            {
                lock (_stateLock)
                {
                    CancelPendingBatteryTransitionLocked();
                }

                await SwitchModeAsync(adapter, desiredMode, cancellationToken);
                return;
            }

            if (batteryDelaySeconds == 0)
            {
                await SwitchModeAsync(adapter, desiredMode, cancellationToken);
                return;
            }

            ScheduleBatteryTransition(batteryDelaySeconds);
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private void ScheduleBatteryTransition(int delaySeconds)
    {
        CancellationTokenSource next;
        PowerModeStatus pendingStatus;
        lock (_stateLock)
        {
            CancelPendingBatteryTransitionLocked();
            next = new CancellationTokenSource();
            _pendingBatteryCancellation = next;
            pendingStatus = _status with
            {
                AdapterState = AdapterState.Battery,
                CurrentMode = _modeService.ReadCurrentMode(),
                AutomationEnabled = true,
                Detail = $"适配器已断开，将在 {delaySeconds} 秒后切换为省电模式。重新接入适配器会取消本次切换。"
            };
            _status = pendingStatus;
        }

        AppLog.Info($"已开始断电延时：{delaySeconds} 秒。");
        Publish(pendingStatus);
        _ = ApplyDelayedBatteryTransitionAsync(delaySeconds, next);
    }

    private async Task ApplyDelayedBatteryTransitionAsync(
        int delaySeconds,
        CancellationTokenSource cancellationSource)
    {
        var cancellationToken = cancellationSource.Token;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            await _transitionGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var adapter = NativePowerStatus.GetAdapterState();

                lock (_stateLock)
                {
                    if (!ReferenceEquals(_pendingBatteryCancellation, cancellationSource))
                    {
                        return;
                    }

                    if (adapter != AdapterState.Battery || !_settings.AutomationEnabled)
                    {
                        CancelPendingBatteryTransitionLocked();
                        return;
                    }
                }

                await SwitchModeAsync(AdapterState.Battery, LenovoPowerMode.Saving, cancellationToken);
                lock (_stateLock)
                {
                    if (ReferenceEquals(_pendingBatteryCancellation, cancellationSource))
                    {
                        _pendingBatteryCancellation = null;
                    }
                }
            }
            finally
            {
                _transitionGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellationSource.Dispose();
        }
    }

    private async Task SwitchModeAsync(
        AdapterState adapter,
        LenovoPowerMode desiredMode,
        CancellationToken cancellationToken)
    {
        try
        {
            await _modeService.SetModeAsync(desiredMode, cancellationToken);
            AppLog.Info($"已切换为{desiredMode.ModeText()}。");
            Publish(new PowerModeStatus(
                adapter,
                desiredMode,
                true,
                adapter == AdapterState.Ac
                    ? "检测到适配器接入，已切换为平衡模式。"
                    : "检测到适配器断开，已切换为省电模式。",
                DateTimeOffset.Now));

            if (adapter == AdapterState.Ac)
            {
                GetToastService()?.Show("电源已连接", "已切换为平衡模式");
            }
            else
            {
                GetToastService()?.Show("电源已断开", "已切换为省电模式");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Info($"模式切换失败：{ex}");
            Publish(new PowerModeStatus(
                adapter,
                _modeService.ReadCurrentMode(),
                true,
                $"电源状态已变化，但模式切换失败：{ex.Message}",
                DateTimeOffset.Now));
            GetToastService()?.Show("模式切换失败", ex.Message);
        }
    }

    private void CancelPendingBatteryTransitionLocked()
    {
        var pending = _pendingBatteryCancellation;
        _pendingBatteryCancellation = null;
        if (pending is null)
        {
            return;
        }

        pending.Cancel();
        AppLog.Info("已取消未完成的断电延时切换。");
    }

    private ToastService? GetToastService()
    {
        return App.Current is App app ? app.ToastService : null;
    }

    private void Publish(PowerModeStatus status)
    {
        lock (_stateLock)
        {
            _status = status;
        }

        StatusChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_stateLock)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            _debounceCancellation = null;
            CancelPendingBatteryTransitionLocked();
        }

        _modeMonitor?.Dispose();
        _transitionGate.Dispose();
    }
}
