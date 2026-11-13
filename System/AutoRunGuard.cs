using System;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace AutoRunKeeper.System;

/// <summary>
///     自动前进守护核心。
///     开关就是鼠标中键：按一次开始「一直保持前进」，再按一次取消。
///     保持的方式是直接读写游戏的自动前进标志（<see cref="AutoRunFlag"/>），不走聊天命令，
///     所以技能一打断就能当帧补回来，不会一顿一顿。
///     另外这些情况视为「我要自己控制」，会立刻取消：按 WASD、走到目标附近、用位移技能。
/// </summary>
internal sealed unsafe class AutoRunGuard
{
    private const int VkLeftMouse = 0x01;
    private const int VkMiddleMouse = 0x04;

    private bool intentOn;
    private bool wasBusy;
    private bool lastMiddleDown;
    private bool lastMoving;
    private long lastBusyAt = long.MinValue / 2;
    private long lastRecoverAt = long.MinValue / 2;
    private int recoverCount;
    private float lastTargetDist = -1f;
    private bool targetStopArmed = true;
    private float idleFacing;
    private float facingBefore;
    private long lastPosAt;
    private global::System.Numerics.Vector3 lastPos;
    private float peakSpeed;

    /// <summary>用户意图：是否要保持前进（中键开的状态）。</summary>
    internal bool PlayerIntent => intentOn;

    /// <summary>游戏当前的真实自动前进状态。</summary>
    internal bool GameAutoRun { get; private set; }

    internal bool IsCasting { get; private set; }

    internal float AnimationLock { get; private set; }

    /// <summary>累计补回次数。</summary>
    internal int RecoverCount => recoverCount;

    /// <summary>最近一次动作描述。</summary>
    internal string LastReason { get; private set; } = "还没触发过";

    internal long LastRecoverAt => lastRecoverAt;

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
        GameAutoRun = ReadAutoRun();

        var busy = IsCasting || AnimationLock > 0.05f;

        var rotation = DalamudApi.ObjectTable.LocalPlayer.Rotation;
        if (!busy)
            idleFacing = rotation;
        if (busy && !wasBusy)
            facingBefore = idleFacing;

        if (busy)
            lastBusyAt = now;

        var skillJustEnded = !busy && wasBusy;
        wasBusy = busy;

        if (skillJustEnded)
        {
            if (peakSpeed > 8f)
                DalamudApi.Log.Debug($"[Auto move on] 本次技能动作峰值速度 {peakSpeed:0} 米/秒");
            peakSpeed = 0f;
        }

        // 「继续原有面向」：技能动作期间压住朝向，避免被技能转过去后先往回走一小段
        if (Plugin.Config.KeepFacing && intentOn)
        {
            var drift = MathF.Abs(WrapAngle(rotation - facingBefore));
            if ((busy || skillJustEnded) && drift > 0.02f)
                RestoreFacing(facingBefore);
        }

        // ---- 中键开关：按一次保持前进，再按一次取消 ----
        var middleDown = IsMouseDown(VkMiddleMouse);
        if (middleDown && !lastMiddleDown)
        {
            intentOn = !intentOn;
            LastReason = intentOn ? "中键开启" : "中键取消";

            if (intentOn)
                WriteForward(true);
            else
                WriteForward(false);

            DalamudApi.Log.Information($"[Auto move on] 中键{(intentOn ? "开启" : "取消")}保持前进");
        }

        lastMiddleDown = middleDown;

        // 兼容：你自己用游戏自己的自动前进键开起来，也算开启
        if (!intentOn && GameAutoRun)
            intentOn = true;

        // ---- 以下都是「我要自己控制」的信号 ----
        var moving = ReadMoveInput();
        if (moving && !lastMoving && intentOn && !GameAutoRun)
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

        // ---- 保持前进 ----
        if (intentOn && !GameAutoRun)
            Recover(now, busy ? "技能打断后保持" : "补回前进");

        if (GameAutoRun != lastAutoRunLogged)
        {
            DalamudApi.Log.Debug(
                $"[Auto move on] 自动前进变化 {lastAutoRunLogged} -> {GameAutoRun}（施法={IsCasting} 动画锁={AnimationLock:0.00} 意图={intentOn}）");
            lastAutoRunLogged = GameAutoRun;
        }
    }

    private bool lastAutoRunLogged;

    /// <summary>统计清零。</summary>
    internal void ResetStats()
    {
        recoverCount = 0;
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

    /// <summary>用户接管：立即取消并停止守护。</summary>
    private void CancelByUser(string reason)
    {
        intentOn = false;
        LastReason = $"用户接管：{reason}";

        DalamudApi.Log.Information($"[Auto move on] 用户接管（{reason}），取消自动前进");
        WriteForward(false);
    }

    private void Recover(long now, string reason)
    {
        if (now - lastRecoverAt < Plugin.Config.RecoverIntervalMs)
            return;

        lastRecoverAt = now;
        recoverCount++;
        LastReason = reason;
        WriteForward(true);
    }

    /// <summary>把自动前进设为指定值：优先直接写标志，失败回退聊天命令。</summary>
    private void WriteForward(bool value)
    {
        try
        {
            if (AutoRunFlag.TrySet(value))
                return;

            SendGameCommand(value ? "/automove on" : "/automove off");
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, $"[Auto move on] 设置自动前进（{value}）失败");
        }
    }

    /// <summary>
    ///     走游戏自己的聊天命令通道（等同在聊天框手打）。
    ///     注意：不能用 ICommandManager.ProcessCommand —— 它只查插件注册的命令，不会转发给游戏。
    /// </summary>
    private static unsafe void SendGameCommand(string command)
    {
        var uiModule = global::FFXIVClientStructs.FFXIV.Client.UI.UIModule.Instance();
        if (uiModule == null)
            return;

        var utf8 = new global::FFXIVClientStructs.FFXIV.Client.System.String.Utf8String();
        utf8.Ctor();
        try
        {
            utf8.SetString(command);
            uiModule->ProcessChatBoxEntry(&utf8);
        }
        finally
        {
            utf8.Dtor();
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

    /// <summary>读物理鼠标键状态。</summary>
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

    private void ResetInstance()
    {
        intentOn = false;
        wasBusy = false;
        lastMoving = false;
        lastMiddleDown = false;
        lastTargetDist = -1f;
    }

    private static bool ReadAutoRun()
    {
        try
        {
            return InputManager.IsAutoRunning();
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[Auto move on] 读取自动前进状态失败");
            return false;
        }
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
