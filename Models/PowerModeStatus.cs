// @file PowerModeStatus.cs
// @author Codex
// @module PowerModeSwitch.Models

namespace PowerModeSwitch.Models;

public enum AdapterState
{
    Unknown,
    Battery,
    Ac
}

public enum LenovoPowerMode
{
    Unknown,
    Balanced,
    Saving,
    Performance
}

public sealed record PowerModeStatus(
    AdapterState AdapterState,
    LenovoPowerMode CurrentMode,
    bool AutomationEnabled,
    string Detail,
    DateTimeOffset? LastTransition);

public static class PowerModeDisplay
{
    public static string AdapterText(this AdapterState state) => state switch
    {
        AdapterState.Ac => "已连接电源",
        AdapterState.Battery => "正在使用电池",
        _ => "电源状态未知"
    };

    public static string ModeText(this LenovoPowerMode mode) => mode switch
    {
        LenovoPowerMode.Balanced => "平衡模式",
        LenovoPowerMode.Saving => "省电模式",
        LenovoPowerMode.Performance => "性能模式",
        _ => "模式未知"
    };
}
