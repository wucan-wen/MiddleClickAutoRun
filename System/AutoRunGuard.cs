using System;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace AutoRunKeeper.System;

/// <summary>
///     保持前进的核心。
///     开关就是鼠标中键：按一次开始按住「左键 + 右键」（游戏原生的前进方式），再按一次松开。
///     这个状态不是游戏的自动前进，用技能不会被打断，所以没有任何补回逻辑。
///     用户接管信号（按 WASD、位移技能、走到目标附近）会立刻松开。
/// </summary>
internal sealed unsafe class AutoRunGuard
{
    private const int VkMiddleMouse = 0x04;

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;

    private bool intentOn;
    private bool wasBusy;
    private bool lastMiddleDown;
    private bool lastMoving;
    private int runCount;
    private float lastTargetDist = -1f;
    private bool targetStopArmed = true;
    private float idleFacing;
    private float facingBefore;
    private long lastPosAt;
    private global::System.Numerics.Vector3 lastPos;
    private float peakSpeed;

    /// <summary>是否正在保持前进。</summary>
    internal bool PlayerIntent => intentOn;

    internal bool IsCasting { get; private set; }

    internal float AnimationLock { get; private set; }

    /// <summary>本次会话累计开启次数。</summary>
    internal int RunCount => runCount;

    internal string LastReason { get; private set; } = "还没触发过";

    internal void Update()
    {
        if (!DalamudApi.ClientState.IsLoggedIn || DalamudApi.ObjectTable.LocalPlayer is null)
        {
            ResetInstance();
            return;
        }

        var now = Environment.TickCount64;

        IsCasting = DalamudApi.Condition[ConditionFlag.Casting] || DalamudApi.Condition[ConditionFlag.Casting87];
        AnimationLock = ReadAnimationLock();

        var busy = IsCasting || AnimationLock > 0.05f;

        var rotation = DalamudApi.ObjectTable.LocalPlayer.Rotation;
        if (!busy)
            idleFacing = rotation;
        if (busy && !wasBusy)
            facingBefore = idleFacing;

        var skillJustEnded = !busy && wasBusy;
        wasBusy = busy;

        if (skillJustEnded)
            peakSpeed = 0f;

        // 继续原有面向：技能动作期间压住朝向，避免被技能转过去后走偏
        if (Plugin.Config.KeepFacing && intentOn)
        {
            var drift = MathF.Abs(WrapAngle(rotation - facingBefore));
            if ((busy || skillJustEnded) && drift > 0.02f)
                RestoreFacing(facingBefore);
        }

        // ---- 中键开关 ----
        var middleDown = IsMouseDown(VkMiddleMouse);
        if (middleDown && !lastMiddleDown)
            Toggle("中键");
        lastMiddleDown = middleDown;

        // ---- 用户接管信号 ----
        var moving = ReadMoveInput();
        if (moving && !lastMoving && intentOn)
            CancelByUser("手动移动输入（WASD）");
        lastMoving = moving;

        if (Plugin.Config.StopNearTarget && intentOn)
            CheckStopNearTarget();

        // 位移技能：技能动作中的瞬时移动速度明显超过跑步速度
        var position = DalamudApi.ObjectTable.LocalPlayer.Position;
        var speed = 0f;
        if (lastPosAt == 0)
        {
            lastPos = position;
            lastPosAt = now;
        }
        else if (now - lastPosAt >= 40)
        {
            speed = (position - lastPos).Length() / ((now - lastPosAt) / 1000f);
            lastPos = position;
            lastPosAt = now;
        }

        if (busy && speed > peakSpeed)
            peakSpeed = speed;

        if (Plugin.Config.StopOnDisplacement && intentOn && busy &&
            speed > Plugin.Config.DisplacementSpeed)
        {
            CancelByUser($"位移技能（{speed:0} 米/秒）");
        }
    }

    /// <summary>切换保持前进的开 / 关（中键与窗口按钮共用）。</summary>
    internal void Toggle(string source)
    {
        intentOn = !intentOn;
        LastReason = intentOn ? $"{source}开启" : $"{source}停止";

        if (intentOn)
        {
            runCount++;
            HoldForward();
        }
        else
        {
            ReleaseForward();
        }

        DalamudApi.Log.Information($"[Auto move on] {source}{(intentOn ? "开启" : "停止")}保持前进");
    }

    /// <summary>统计清零。</summary>
    internal void ResetStats()
    {
        runCount = 0;
        LastReason = "还没触发过";
    }

    /// <summary>靠近当前目标到阈值内就停；远离时不做任何事。</summary>
    private void CheckStopNearTarget()
    {
        var target = DalamudApi.TargetManager.Target;
        if (target == null)
        {
            lastTargetDist = -1f;
            targetStopArmed = true;
            return;
        }

        var delta = target.Position - DalamudApi.ObjectTable.LocalPlayer.Position;
        var flat = new global::System.Numerics.Vector2(delta.X, delta.Z).Length();

        if (flat > Plugin.Config.TargetStopDistance + 1.5f)
            targetStopArmed = true;

        var approaching = lastTargetDist >= 0f && flat < lastTargetDist - 0.005f;

        if (targetStopArmed && approaching && flat <= Plugin.Config.TargetStopDistance)
        {
            targetStopArmed = false;
            CancelByUser($"接近目标至 {flat:0.0} 米");
        }

        lastTargetDist = flat;
    }

    /// <summary>用户接管：立刻松开前进键。</summary>
    private void CancelByUser(string reason)
    {
        intentOn = false;
        LastReason = $"用户接管：{reason}";

        DalamudApi.Log.Information($"[Auto move on] 用户接管（{reason}），停止前进");
        ReleaseForward();
    }

    /// <summary>模拟按住「左键 + 右键」（游戏原生前进）。</summary>
    private static void HoldForward()
    {
        try
        {
            mouse_event(MouseEventLeftDown, 0, 0, 0, nint.Zero);
            mouse_event(MouseEventRightDown, 0, 0, 0, nint.Zero);
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[Auto move on] 按下左右键失败");
        }
    }

    /// <summary>松开左右键。</summary>
    private static void ReleaseForward()
    {
        try
        {
            mouse_event(MouseEventLeftUp, 0, 0, 0, nint.Zero);
            mouse_event(MouseEventRightUp, 0, 0, 0, nint.Zero);
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[Auto move on] 松开左右键失败");
        }
    }

    /// <summary>读游戏的移动输入（前进/后退/左右/平移）。</summary>
    private static unsafe bool ReadMoveInput()
    {
        try
        {
            var inputManager = InputManager.Instance();
            if (inputManager == null)
                return false;

            return inputManager->GetInputStatus(InputCode.MOVE_FORE)
                   || inputManager->GetInputStatus(InputCode.MOVE_BACK)
                   || inputManager->GetInputStatus(InputCode.MOVE_LEFT)
                   || inputManager->GetInputStatus(InputCode.MOVE_RIGHT)
                   || inputManager->GetInputStatus(InputCode.MOVE_STRIFE_L)
                   || inputManager->GetInputStatus(InputCode.MOVE_STRIFE_R);
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[Auto move on] 读取移动输入失败");
            return false;
        }
    }

    /// <summary>把角色朝向转回指定角度。</summary>
    private static unsafe void RestoreFacing(float rotation)
    {
        try
        {
            var player = DalamudApi.ObjectTable.LocalPlayer;
            if (player == null)
                return;

            var gameObject = (global::FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)player.Address;
            gameObject->SetRotation(rotation);
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[Auto move on] 恢复朝向失败");
        }
    }

    private static float WrapAngle(float angle)
    {
        const float tau = MathF.PI * 2f;
        while (angle > MathF.PI)
            angle -= tau;
        while (angle < -MathF.PI)
            angle += tau;
        return angle;
    }

    private static bool IsMouseDown(int virtualKey)
    {
        try
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nint extraInfo);

    /// <summary>插件卸载/登出前松开按键，别把状态留给游戏。</summary>
    internal void Shutdown()
    {
        if (intentOn)
            ReleaseForward();
        intentOn = false;
    }

    private void ResetInstance()
    {
        if (intentOn)
            ReleaseForward();
        intentOn = false;
        wasBusy = false;
        lastMoving = false;
        lastTargetDist = -1f;
    }

    private static float ReadAnimationLock()
    {
        try
        {
            var actionManager = ActionManager.Instance();
            return actionManager == null ? 0f : actionManager->AnimationLock;
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[Auto move on] 读取动画锁失败");
            return 0f;
        }
    }
}
