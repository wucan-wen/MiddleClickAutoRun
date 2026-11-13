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

    private static void DrawSettings(Configuration config)
    {
        var changed = false;

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
