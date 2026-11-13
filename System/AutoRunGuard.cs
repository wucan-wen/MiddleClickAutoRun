using System;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace AutoRunKeeper.System;

/// <summary>
///     自动前进守护核心。
///     不绑定任何键位（每个人的自动前进键都可能不同），只看两件事：
///     1. 游戏状态：是否正在自动前进、是否正在放技能（施法 / 动画锁）。
///     2. 技能上下文：游戏在放技能时会取消自动前进，所以只在「技能动作中」以及「动作刚结束的
///        一小段时间内」补回；离开这个上下文之后再被关掉，就判定是用户自己关的，立即停止守护。
///     用户主动接管还有两条明确信号：按下移动键（WASD），或走到当前目标附近。
/// </summary>
internal sealed unsafe class AutoRunGuard
{
    private bool lastAutoRun;
    private bool intentOn;
    private bool wasBusy;
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

    /// <summary>用户意图：他想不想自动前进。</summary>
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
            facingBefore = idleFacing; // 技能开始前的朝向

        if (busy)
            lastBusyAt = now;

        var skillJustEnded = !busy && wasBusy;
        wasBusy = busy;

        if (skillJustEnded)
        {
            if (peakSpeed > 8f)
                DalamudApi.Log.Information($"[Autowalk] 本次技能动作峰值速度 {peakSpeed:0} 米/秒");
            peakSpeed = 0f;
        }

        // 可选的「继续原有面向」：
        // 技能动作期间每帧把面向压回技能前的方向（否则会被技能转过去，自动前进就先往回走一小段），
        // 动作结束再确认一次。
        if (Plugin.Config.KeepFacing && intentOn)
        {
            var drift = MathF.Abs(WrapAngle(rotation - facingBefore));
            if ((busy || skillJustEnded) && drift > 0.02f)
                RestoreFacing(facingBefore);
        }

        // 位移技能：技能动作中的瞬时移动速度明显超过跑步速度
        // 用 40ms 采样窗口算速度，避免帧率抖动把速度放大好几倍
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

        // 移动键（WASD / 方向键）按下瞬间 = 想自己走
        var moving = ReadMoveInput();
        if (moving && !lastMoving && intentOn)
            CancelByUser("手动移动输入（WASD）");
        lastMoving = moving;

        // 走到当前目标附近（默认 2 米，水平距离）→ 停；只有「正在靠近」才停，远离时不动
        if (Plugin.Config.StopNearTarget && intentOn)
            CheckStopNearTarget();

        // 玩家开启自动前进 → 记录意图，开始守护
        if (GameAutoRun && !lastAutoRun)
            intentOn = true;

        var inSkillWindow = busy || now - lastBusyAt <= Plugin.Config.GraceMs;

        // 被关掉：在技能上下文内是游戏干的（继续守），之外是用户干的（不再管）
        if (!GameAutoRun && lastAutoRun && !inSkillWindow)
        {
            intentOn = false;
            LastReason = "用户手动关闭（不干预）";
        }

        // 技能上下文内持续补回
        if (intentOn && !GameAutoRun && inSkillWindow &&
            now - lastRecoverAt >= Plugin.Config.RecoverIntervalMs)
        {
            Recover(now, busy ? "技能动作中补回" : "技能结束后补回");
        }

        if (GameAutoRun != lastAutoRun)
            DalamudApi.Log.Debug(
                $"[Autowalk] 自动前进变化 {lastAutoRun} -> {GameAutoRun}（施法={IsCasting} 动画锁={AnimationLock:0.00} 意图={intentOn}）");

        lastAutoRun = ReadAutoRun();
    }

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

        // 离开到阈值外 1.5 米以上才重新武装，避免边界抖动
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

    /// <summary>用户接管：立即取消自动前进并停止守护。</summary>
    private void CancelByUser(string reason)
    {
        intentOn = false;
        LastReason = $"用户接管：{reason}";

        DalamudApi.Log.Information($"[Autowalk] 用户接管（{reason}），取消自动前进");

        try
        {
            SendGameCommand("/automove off");
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[Autowalk] 发送 /automove off 失败");
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
            if (gameObject == null)
                return;

            gameObject->SetRotation(rotation);
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[Autowalk] 恢复朝向失败");
        }
    }

    /// <summary>把角度差规整到 -π..π，方便比较。</summary>
    private static float WrapAngle(float angle)
    {
        const float tau = MathF.PI * 2f;
        while (angle > MathF.PI)
            angle -= tau;
        while (angle < -MathF.PI)
            angle += tau;
        return angle;
    }

    private void Recover(long now, string reason)
    {
        lastRecoverAt = now;
        recoverCount++;
        LastReason = reason;

        try
        {
            SendGameCommand("/automove on");
            DalamudApi.Log.Debug($"[Autowalk] 补回自动前进（{reason}，累计 {recoverCount}）");
        }
        catch (Exception ex)
        {
            DalamudApi.Log.Error(ex, "[Autowalk] 发送 /automove on 失败");
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
        {
            DalamudApi.Log.Warning("[Autowalk] UIModule 不可用，无法发送命令");
            return;
        }

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

    /// <summary>读游戏的移动输入（键位映射后的逻辑输入：前进/后退/左右/平移）。</summary>
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
            DalamudApi.Log.Error(ex, "[Autowalk] 读取移动输入失败");
            return false;
        }
    }

    private void ResetInstance()
    {
        lastAutoRun = false;
        intentOn = false;
        wasBusy = false;
        lastMoving = false;
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
            DalamudApi.Log.Error(ex, "[Autowalk] 读取自动前进状态失败");
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
            DalamudApi.Log.Error(ex, "[Autowalk] 读取动画锁失败");
            return 0f;
        }
    }
}
