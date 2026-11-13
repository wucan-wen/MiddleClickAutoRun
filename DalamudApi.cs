using Dalamud.Interface;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace AutoRunKeeper;

/// <summary>Dalamud 服务集中入口，构造期由 PluginInterface.Create 注入。</summary>
internal sealed class DalamudApi
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    /// <summary>UI 构建器不是可注入服务（API 15），统一从 PluginInterface 取。</summary>
    internal static IUiBuilder UiBuilder => PluginInterface.UiBuilder;

    internal static void Initialize(IDalamudPluginInterface pluginInterface) => pluginInterface.Create<DalamudApi>();
}
