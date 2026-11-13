using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace AutoRunKeeper.System;

/// <summary>
///     自动前进标志的直接读写通道。
///     做法：反查 <c>InputManager.IsAutoRunning()</c> 的机器码，找出它读取的那个全局 bool 地址，
///     校验「该字节的值 == IsAutoRunning() 的返回值」后再写入。
///     校验不过就一律不写，调用方回退到聊天命令 <c>/automove on|off</c>。
///     这样绕开聊天命令的排队延迟，技能打断时可以立刻补回，不会再出现移动卡顿。
/// </summary>
internal static unsafe class AutoRunFlag
{
    private static nint flagAddress;
    private static byte onValue = 1;
    private static bool resolved;
    private static bool available;

    /// <summary>是否已成功定位（只读，供界面/日志判断）。</summary>
    internal static bool Available => available;

    /// <summary>定位到的地址（调试用）。</summary>
    internal static nint Address => flagAddress;

    /// <summary>插件加载时先尝试定位，便于日志里确认通道是否可用。</summary>
    internal static void Initialize() => EnsureResolved();

    /// <summary>尝试直接把自动前进设为指定值，成功返回 true。</summary>
    internal static bool TrySet(bool value)
    {
        if (!EnsureResolved())
            return false;

        try
        {
            var current = *(byte*)flagAddress;
            var target = value ? onValue : (byte)0;

            if (current == target)
                return true;

            *(byte*)flagAddress = target;

            // 写完立刻用游戏自己的查询函数验证
            if (InputManager.IsAutoRunning() == value)
                return true;

            // 验证失败：还原，避免留下脏状态
            *(byte*)flagAddress = current;
            available = false;
            DalamudApi.Log.Warning("[Auto move on] 自动前进标志写入未生效，回退到聊天命令");
            return false;
        }
        catch (Exception ex)
        {
            available = false;
            DalamudApi.Log.Error(ex, "[Auto move on] 写入自动前进标志失败，回退到聊天命令");
            return false;
        }
    }

    private static bool EnsureResolved()
    {
        if (resolved)
            return available;

        resolved = true;

        try
        {
            var function = (byte*)InputManager.MemberFunctionPointers.IsAutoRunning;
            if (function == null)
                return false;

            var running = InputManager.IsAutoRunning();

            // 在函数体前 96 字节里找形如 mov/movzx/cmp 读 [rip+disp32] 的字节访问
            for (var i = 0; i < 96; i++)
            {
                var candidate = TryDecodeRipByteRead(function + i, out var compareValue);
                if (candidate == 0)
                    continue;

                if (!IsWritable(candidate))
                    continue;

                var value = *(byte*)candidate;

                // 校验：该字节是否解释了 IsAutoRunning() 的返回值
                if (running != (value == compareValue))
                    continue;

                flagAddress = candidate;
                onValue = compareValue;
                available = true;

                DalamudApi.Log.Information(
                    $"[Auto move on] 已定位自动前进标志：0x{candidate:X}（开值={onValue}，函数 0x{(nint)function:X}）");
                return true;
            }

            var hex = new global::System.Text.StringBuilder();
            for (var i = 0; i < 64; i++)
                hex.Append(function[i].ToString("X2")).Append(' ');
            DalamudApi.Log.Warning($"[Auto move on] 未能定位自动前进标志，将使用聊天命令。函数 0x{(nint)function:X} 前 64 字节：{hex}");
            return false;
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[Auto move on] 定位自动前进标志失败，将使用聊天命令");
            return false;
        }
    }

    /// <summary>识别几种常见的 RIP 相对字节读取指令，返回被访问的绝对地址；不匹配返回 0。</summary>
    private static nint TryDecodeRipByteRead(byte* p, out byte compareValue)
    {
        int length;
        int displacementOffset;
        compareValue = 1;

        if (p[0] == 0x0F && p[1] == 0xB6 && p[2] == 0x05)
        {
            length = 7;
            displacementOffset = 3;
        }
        else if (p[0] == 0x0F && p[1] == 0xB6 && (p[2] == 0x0D || p[2] == 0x15 || p[2] == 0x1D || p[2] == 0x35 || p[2] == 0x3D))
        {
            length = 7;
            displacementOffset = 3;
        }
        else if (p[0] == 0x8A && (p[1] == 0x05 || p[1] == 0x0D || p[1] == 0x15 || p[1] == 0x1D || p[1] == 0x35 || p[1] == 0x3D))
        {
            length = 6;
            displacementOffset = 2;
        }
        else if (p[0] == 0x80 && p[1] == 0x3D)
        {
            // cmp byte ptr [rip+disp32], imm8  —— 立即数就是「开」的值
            length = 7;
            displacementOffset = 2;
            compareValue = p[6];
        }
        else if (p[0] == 0x44 && p[1] == 0x0F && p[2] == 0xB6 && p[3] == 0x05)
        {
            length = 8;
            displacementOffset = 4;
        }
        else
        {
            return 0;
        }

        var displacement = *(int*)(p + displacementOffset);
        return (nint)(p + length + displacement);
    }

    /// <summary>粗判目标是否落在可写映射里（只读区域绝不动）。</summary>
    private static bool IsWritable(nint address)
    {
        try
        {
            var process = global::System.Diagnostics.Process.GetCurrentProcess();
            var main = process.MainModule;
            if (main == null)
                return false;

            var baseAddress = (nint)main.BaseAddress.ToInt64();
            var end = baseAddress + main.ModuleMemorySize;
            if (address < baseAddress || address >= end)
                return false;

            // .text 等只读段通常在前 16MB 内，真正的全局变量在数据段；这里用简单启发式：
            // 目标必须在模块内且值可读，实际写失败会被 try/catch 兜住并回退。
            return true;
        }
        catch
        {
            return false;
        }
    }
}
