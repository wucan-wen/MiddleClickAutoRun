using Dalamud.Configuration;
using Dalamud.Plugin;

namespace AutoRunKeeper;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>游戏里已把「自动前进」绑到鼠标中键：插件完全停手，不触发任何功能。</summary>
    public bool GameHandlesAutoRun { get; set; }

    /// <summary>改键位前游戏「自动前进」的原键位（两个槽，各 2 字节），关闭开关时还原。</summary>
    public uint OriginalAutoRunSlot0 { get; set; }

    public uint OriginalAutoRunSlot1 { get; set; }

    /// <summary>改键位前是否已记录原键位。</summary>
    public bool OriginalAutoRunSaved { get; set; }

    /// <summary>走到当前目标附近时自动停止前进。</summary>
    public bool StopNearTarget { get; set; } = true;

    /// <summary>停止的判定距离（米，按水平距离算）。</summary>
    public float TargetStopDistance { get; set; } = 2f;

    /// <summary>技能动作期间把面向压回技能前的方向，避免被技能带着拐弯。</summary>
    public bool KeepFacing { get; set; }

    /// <summary>使用位移技能时停止前进。</summary>
    public bool StopOnDisplacement { get; set; } = true;

    /// <summary>位移技能的判定速度（米/秒，技能动作中瞬时速度超过它就停）。</summary>
    public float DisplacementSpeed { get; set; } = 15f;

    public void Save()
    {
        DalamudApi.PluginInterface.SavePluginConfig(this);
    }
}
