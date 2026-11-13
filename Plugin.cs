using System;
using AutoRunKeeper.System;
using AutoRunKeeper.Windows;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AutoRunKeeper;

public sealed class Plugin : IDalamudPlugin
{
    private readonly WindowSystem windowSystem = new("Auto move on");

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        DalamudApi.Initialize(pluginInterface);

        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        PluginFont.Initialize();
        AutoRunFlag.Initialize();

        Guard = new AutoRunGuard();
        Window = new ConfigWindow(this);

        windowSystem.AddWindow(Window);

        // 插件列表里的设置按钮（齿轮）打开本窗口；不注册任何聊天命令
        DalamudApi.UiBuilder.OpenConfigUi += OnOpenConfigUi;
        DalamudApi.UiBuilder.OpenMainUi += OnOpenConfigUi;

        DalamudApi.Framework.Update += OnUpdate;
        DalamudApi.UiBuilder.Draw += windowSystem.Draw;

        DalamudApi.Log.Information("[Auto move on] 已加载");
    }

    public string Name => "Auto move on";

    internal static Configuration Config { get; private set; } = null!;

    internal AutoRunGuard Guard { get; }

    internal ConfigWindow Window { get; }

    public void Dispose()
    {
        DalamudApi.Framework.Update -= OnUpdate;
        DalamudApi.UiBuilder.Draw -= windowSystem.Draw;

        DalamudApi.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        DalamudApi.UiBuilder.OpenMainUi -= OnOpenConfigUi;

        windowSystem.RemoveAllWindows();
        PluginFont.Dispose();

        DalamudApi.Log.Information("[Auto move on] 已卸载");
    }

    private void OnOpenConfigUi() => Window.IsOpen = true;

    private void OnUpdate(IFramework framework)
    {
        try
        {
            Guard.Update();
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[Auto move on] 每帧逻辑异常");
        }
    }
}
