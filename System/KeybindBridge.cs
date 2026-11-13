using System;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace AutoRunKeeper.System;

/// <summary>
///     直接读写游戏的「自动前进」键位（InputId.AUTORUN_KEY）。
///     用来实现「把自动前进绑到鼠标中键 / 解绑」这个一键切换。
/// </summary>
internal static unsafe class KeybindBridge
{
    /// <summary>读当前自动前进键位（两个键位槽，各 2 字节：Key + Modifier）。</summary>
    internal static bool TryReadAutoRun(out uint slot0, out uint slot1)
    {
        slot0 = 0;
        slot1 = 0;

        try
        {
            var uiInput = UIInputData.Instance();
            if (uiInput == null)
                return false;

            var inputData = (InputData*)uiInput;
            var keybind = inputData->GetKeybind(InputId.AUTORUN_KEY);
            if (keybind == null)
                return false;

            slot0 = ReadSlot(keybind, 0);
            slot1 = ReadSlot(keybind, 1);
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[Auto move on] 读取自动前进键位失败");
            return false;
        }
    }

    /// <summary>把自动前进绑到鼠标中键。</summary>
    internal static bool TrySetAutoRunToMiddleMouse()
    {
        try
        {
            var uiInput = UIInputData.Instance();
            if (uiInput == null)
            {
                DalamudApi.Log.Warning("[Auto move on] 取不到 UIInputData，无法改键位");
                return false;
            }

            var inputData = (InputData*)uiInput;
            var keybind = inputData->GetKeybind(InputId.AUTORUN_KEY);
            if (keybind == null)
                return false;

            WriteSlot(keybind, 0, (byte)SeVirtualKey.MBUTTON, 0);
            WriteSlot(keybind, 1, 0, 0);

            uiInput->SetKeybind(InputId.AUTORUN_KEY, keybind);
            DalamudApi.Log.Information("[Auto move on] 已把游戏「自动前进」设为鼠标中键");
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[Auto move on] 设置自动前进键位失败");
            return false;
        }
    }

    /// <summary>还原成指定的键位（两个槽）。</summary>
    internal static bool TryRestoreAutoRun(uint slot0, uint slot1)
    {
        try
        {
            var uiInput = UIInputData.Instance();
            if (uiInput == null)
                return false;

            var inputData = (InputData*)uiInput;
            var keybind = inputData->GetKeybind(InputId.AUTORUN_KEY);
            if (keybind == null)
                return false;

            WriteRawSlot(keybind, 0, slot0);
            WriteRawSlot(keybind, 1, slot1);

            uiInput->SetKeybind(InputId.AUTORUN_KEY, keybind);
            DalamudApi.Log.Information("[Auto move on] 已还原游戏「自动前进」键位");
            return true;
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[Auto move on] 还原自动前进键位失败");
            return false;
        }
    }

    /// <summary>当前自动前进是否就是鼠标中键。</summary>
    internal static bool IsAutoRunMiddleMouse()
    {
        if (!TryReadAutoRun(out var slot0, out _))
            return false;

        return (byte)(slot0 & 0xFF) == (byte)SeVirtualKey.MBUTTON;
    }

    private static uint ReadSlot(Keybind* keybind, int index)
    {
        var settings = keybind->KeySettings;
        var setting = settings[index];
        return (uint)((byte)setting.Key | ((byte)setting.KeyModifier << 8));
    }

    private static void WriteSlot(Keybind* keybind, int index, byte key, byte modifier)
    {
        var settings = keybind->KeySettings;
        settings[index] = new KeySetting
        {
            Key = (SeVirtualKey)key,
            KeyModifier = (KeyModifierFlag)modifier,
        };
    }

    private static void WriteRawSlot(Keybind* keybind, int index, uint raw)
    {
        WriteSlot(keybind, index, (byte)(raw & 0xFF), (byte)((raw >> 8) & 0xFF));
    }
}
