using Dalamud.Configuration;
using Dalamud.Plugin;

namespace AutoRunKeeper;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

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
