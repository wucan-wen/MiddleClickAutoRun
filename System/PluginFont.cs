using System;
using System.IO;
using Dalamud;
using Dalamud.Interface.ManagedFontAtlas;

namespace AutoRunKeeper.System;

/// <summary>
///     中文字形支持。ImGui 默认图集对简体字形覆盖不全，这里挂一个 delegate font handle：
///     优先用 Dalamud 自带思源黑体（TTC 第 2 个 face = 简体），失败回退系统字体，全失败静默降级。
/// </summary>
internal static class PluginFont
{
    private const float FontSizePx = 17f;

    private static readonly string[] FallbackFonts =
    {
        @"C:\Windows\Fonts\msyh.ttc",
        @"C:\Windows\Fonts\msyh.ttf",
        @"C:\Windows\Fonts\simhei.ttf",
        @"C:\Windows\Fonts\simsun.ttc",
    };

    private static IFontHandle? handle;
    private static bool initialized;

    internal static void Initialize()
    {
        if (initialized)
            return;

        initialized = true;

        try
        {
            var atlas = DalamudApi.PluginInterface.UiBuilder.FontAtlas;
            handle = atlas.NewDelegateFontHandle(step => step.OnPreBuild(toolkit =>
            {
                try
                {
                    toolkit.AddDalamudAssetFont(
                        DalamudAsset.NotoSansCjkRegular,
                        new SafeFontConfig { SizePx = FontSizePx, FontNo = 2 });
                }
                catch (Exception ex)
                {
                    DalamudApi.Log.Warning(ex, "[自动前进保持] 内置中文字体不可用，尝试系统字体");

                    foreach (var path in FallbackFonts)
                    {
                        if (!File.Exists(path))
                            continue;

                        try
                        {
                            toolkit.AddFontFromFile(path, new SafeFontConfig { SizePx = FontSizePx });
                            return;
                        }
                        catch (Exception inner)
                        {
                            DalamudApi.Log.Warning(inner, $"[自动前进保持] 加载系统字体失败：{path}");
                        }
                    }

                    DalamudApi.Log.Error("[自动前进保持] 没有可用的中文字体，界面中文可能显示为方块");
                }
            }));

            if (handle.LoadException != null)
                DalamudApi.Log.Error(handle.LoadException, "[自动前进保持] 中文字体句柄构建失败");
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[自动前进保持] 中文字体初始化失败");
        }
    }

    /// <summary>压入中文字体，Dispose 时弹栈；字体未就绪时返回空作用域。</summary>
    internal static IDisposable PushScope()
    {
        if (handle == null)
            return EmptyScope.Instance;

        try
        {
            return handle.Push();
        }
        catch
        {
            return EmptyScope.Instance;
        }
    }

    internal static void Dispose()
    {
        try
        {
            handle?.Dispose();
        }
        catch
        {
            // 忽略
        }

        handle = null;
    }

    private sealed class EmptyScope : IDisposable
    {
        internal static readonly EmptyScope Instance = new();

        public void Dispose()
        {
        }
    }
}
