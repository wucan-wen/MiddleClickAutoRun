using System.Numerics;
using AutoRunKeeper.System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AutoRunKeeper.Windows;

/// <summary>设置窗口：开关状态 + 参数。窗口尺寸跟着内容走。</summary>
internal sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    internal ConfigWindow(Plugin plugin)
        : base("Auto move on###AutoRunKeeperConfig")
    {
        this.plugin = plugin;

        Flags |= ImGuiWindowFlags.NoResize
                 | ImGuiWindowFlags.AlwaysAutoResize
                 | ImGuiWindowFlags.NoScrollbar
                 | ImGuiWindowFlags.NoScrollWithMouse;
    }

    public override void Draw()
    {
        using var fontScope = PluginFont.PushScope();

        var config = Plugin.Config;

        DrawSwitchState(config);

        ImGui.Separator();

        DrawSettings(config);

        ImGui.Spacing();
        ImGui.TextDisabled("中键 = 开关；WASD / 位移技能 / 走到目标附近会自动停止。");
    }

    /// <summary>开关状态显示，跟着中键实时变化，也可以直接在这里点。</summary>
    private void DrawSwitchState(Configuration config)
    {
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

        var stopNear = config.StopNearTarget;
        if (ImGui.Checkbox("到达当前目标附近就停止", ref stopNear))
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
            ImGui.SetTooltip("按水平距离判定：靠近当前目标到小于这个值就停；远离不会拦。");

        var stopOnDisplace = config.StopOnDisplacement;
        if (ImGui.Checkbox("使用位移技能时停止", ref stopOnDisplace))
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
            ImGui.SetTooltip("技能动作中的瞬时移动速度超过这个值就判定位移技能。\n跑步约 4~6，坐骑约 20，后跃射击一类会明显更高。");

        var keepFacing = config.KeepFacing;
        if (ImGui.Checkbox("继续原有面向", ref keepFacing))
        {
            config.KeepFacing = keepFacing;
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("技能动作期间把面向压回技能前的方向，不会被技能带着拐弯。");

        if (changed)
            config.Save();
    }
}
