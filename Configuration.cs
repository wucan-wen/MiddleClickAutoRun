using Dalamud.Configuration;
using Dalamud.Plugin;

namespace AutoRunKeeper;

/// <summary>工作模式：两套逻辑互相独立，用设置里的下拉切换。</summary>
public enum AutoRunMode
{
    /// <summary>跟随游戏的自动前进：用它自己的开关键，插件只负责补回被技能打断的。</summary>
    FollowGame = 0,

    /// <summary>中键开关：按一次一直保持前进，再按一次停止。</summary>
    MiddleMouse = 1,
}

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>工作模式。</summary>
    public AutoRunMode Mode { get; set; } = AutoRunMode.MiddleMouse;

    /// <summary>技能结束后的守护时间窗（毫秒）。窗口外的关闭视为用户手动关闭。</summary>
    public int GraceMs { get; set; } = 500;

    /// <summary>两次补回之间的最小间隔（毫秒）。</summary>
    public int RecoverIntervalMs { get; set; } = 80;

    /// <summary>补回成功后多久内不再重复补（毫秒），避免来回拉扯导致移动生硬。</summary>
    public int SuccessCooldownMs { get; set; } = 800;

    /// <summary>走到当前目标附近时自动停止自动前进。</summary>
    public bool StopNearTarget { get; set; } = true;

    /// <summary>停止的判定距离（米，按水平距离算）。</summary>
    public float TargetStopDistance { get; set; } = 2f;

    /// <summary>技能结束后把面向转回技能前的方向（自动前进才不会被技能带着拐弯）。</summary>
    public bool KeepFacing { get; set; }

    /// <summary>使用位移技能时停止自动前进。</summary>
    public bool StopOnDisplacement { get; set; } = true;

    /// <summary>位移技能的判定速度（米/秒，技能动作中瞬时速度超过它就停）。</summary>
    public float DisplacementSpeed { get; set; } = 15f;

    public void Save()
    {
        DalamudApi.PluginInterface.SavePluginConfig(this);
    }
}
