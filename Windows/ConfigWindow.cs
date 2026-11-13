using System;
using System.Numerics;
using AutoRunKeeper.System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AutoRunKeeper.Windows;

/// <summary>设置与状态窗口：主窗口 /arun cfg 打开。</summary>
internal sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    internal ConfigWindow(Plugin plugin)
        : base("Auto move on###AutoRunKeeperConfig")
    {
        this.plugin = plugin;

        // 窗口尺寸固定跟着内容走：不手动改大小、不滚动
        Flags |= ImGuiWindowFlags.NoResize
                 | ImGuiWindowFlags.AlwaysAutoResize
                 | ImGuiWindowFlags.NoScrollbar
                 | ImGuiWindowFlags.NoScrollWithMouse;
    }

    public override void Draw()
    {
        using var fontScope = PluginFont.PushScope();

        var config = Plugin.Config;

        ImGui.TextDisabled("插件启用即生效，停用即完全不干预；不注册任何聊天命令。");

        ImGui.Separator();

        DrawSettings(config);

        ImGui.Spacing();
        ImGui.TextDisabled("插件列表里的设置按钮（齿轮）可再次打开本窗口");
    }

    private static readonly string[] ModeNames =
    {
        "跟随游戏自动前进（补回被技能打断的）",
        "中键开关（按一次开始，再按一次停止）",
    };

    /// <summary>开关状态显示：中键模式下显示开/关并可以直接点，状态跟着中键实时变化。</summary>
    private void DrawSwitchState(Configuration config)
    {
        if (config.Mode != AutoRunMode.MiddleMouse)
        {
            ImGui.TextDisabled("当前模式：由游戏自己的自动前进键控制");
            return;
        }

        var on = plugin.Guard.PlayerIntent;

        ImGui.TextColored(
            on ? new Vector4(0.45f, 0.87f, 0.55f, 1f) : new Vector4(0.68f, 0.68f, 0.68f, 1f),
            on ? "保持前进：开" : "保持前进：关");

        ImGui.SameLine();
        if (ImGui.Button(on ? "停止" : "开始"))
            plugin.Guard.Toggle("窗口");

        ImGui.SameLine();
        ImGui.TextDisabled("（与鼠标中键同一个开关）");
    }

    private void DrawSettings(Configuration config)
    {
        var changed = false;

        ImGui.SetNextItemWidth(220f);
        var mode = (int)config.Mode;
        if (ImGui.Combo("工作模式", ref mode, ModeNames, ModeNames.Length))
        {
            config.Mode = (AutoRunMode)mode;
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("跟随游戏：用游戏自己的自动前进开关键，插件只负责补回被技能打断的；中键开关：插件自己把中键当开关，按一次一直保持前进，再按一次停止。");

        DrawSwitchState(config);

        ImGui.SetNextItemWidth(220f);
        var grace = config.GraceMs;
        if (ImGui.SliderInt("恢复时间窗 (毫秒)", ref grace, 100, 3000, "%d ms", ImGuiSliderFlags.AlwaysClamp))
        {
            config.GraceMs = grace;
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("技能结束后的这段窗口内，若自动前进被关掉，插件会帮你补回；\n超过窗口的关闭视为你自己停的。");

        ImGui.SetNextItemWidth(220f);
        var interval = config.RecoverIntervalMs;
        if (ImGui.SliderInt("补回最小间隔 (毫秒)", ref interval, 100, 1000, "%d ms", ImGuiSliderFlags.AlwaysClamp))
        {
            config.RecoverIntervalMs = interval;
            changed = true;
        }

        var stopNear = config.StopNearTarget;
        if (ImGui.Checkbox("到达当前目标附近就停止自动前进", ref stopNear))
        {
            config.StopNearTarget = stopNear;
            changed = true;
        }

        ImGui.SetNextItemWidth(220f);
        var stopDistance = config.TargetStopDistance;
        if (ImGui.SliderFloat("停止距离 (米)", ref stopDistance, 0.5f, 10f, "%.1f 米", ImGuiSliderFlags.AlwaysClamp))
        {
            config.TargetStopDistance = stopDistance;
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("按水平距离判定：与当前目标中心的距离小于这个值就停。");

        var keepFacing = config.KeepFacing;
        if (ImGui.Checkbox("继续原有面向（技能结束后转回原方向）", ref keepFacing))
        {
            config.KeepFacing = keepFacing;
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("放技能时角色会自动面向目标，勾选后在技能动作结束的瞬间把面向转回技能前的方向，\n自动前进就会沿原来的方向继续走，不会被技能带着拐弯。");

        var stopOnDisplace = config.StopOnDisplacement;
        if (ImGui.Checkbox("使用位移技能时停止自动前进", ref stopOnDisplace))
        {
            config.StopOnDisplacement = stopOnDisplace;
            changed = true;
        }

        ImGui.SetNextItemWidth(220f);
        var displaceSpeed = config.DisplacementSpeed;
        if (ImGui.SliderFloat("位移判定速度 (米/秒)", ref displaceSpeed, 5f, 60f, "%.0f 米/秒", ImGuiSliderFlags.AlwaysClamp))
        {
            config.DisplacementSpeed = displaceSpeed;
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("技能动作中的瞬时移动速度超过这个值就判定位移技能。\n跑步约 4~6，坐骑约 20，后跃射击一类位移技能会明显更高。");

        if (changed)
            config.Save();
    }
}
