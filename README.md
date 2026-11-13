# Autowalk

Dalamud（卫月）插件，API 15。开着自动前进放技能时，游戏会取消自动前进；这个插件把被技能打断的自动前进自动补回，走位不被打断。

本仓库同时是**源码**与**插件仓库（pluginmaster）**。

## 安装（在线仓库，推荐）

1. `/xlplugins` → 设置 → 自定义插件仓库（Custom Plugin Repositories）→ 添加：

```
https://raw.githubusercontent.com/wucan-wen/Autowalk/main/pluginmaster.json
```

2. 刷新仓库 → 在插件列表里找到「Autowalk」→ 安装并启用
3. 如果之前用开发者模式（Dev Tools）装过同一个插件，**先停用或移除那份**，同一个 InternalName 两份会冲突拒载

## 安装（开发者模式）

`/xlplugins` → 设置 → 开发者设置（Dev Tools）→ 添加 `dist` 解压后的 `AutoRunKeeper.dll` 路径，或直接添加构建产物的 dll。

## 使用

- 在插件列表启用 → 功能生效；停用 → 完全不干预。
- 不注册任何聊天命令，也不往聊天框打任何提示。
- 设置窗口：插件列表里本插件的设置按钮（齿轮）打开。

## 判定规则

- 自动前进被打开 → 记为「用户意图 = 开」，开始守护。
- 被关掉时看上下文：
  - 正在放技能（施法 / 动画锁）或技能动作刚结束不到「补回时间窗」→ 判定技能打断，持续发 `/automove on` 补回。
  - 其他情况 → 判定是你自己停的，立即停手，不再干预。
- 用户主动接管的三条信号（都会发 `/automove off` 并停止守护）：
  1. 按下移动键（WASD / 方向键）
  2. 走向当前目标并进入判定距离（只有「正在靠近」才停，远离不拦）
  3. 技能动作中出现位移技能级别的高速位移

## 参数

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| 补回时间窗 | 500 ms | 技能动作结束后的守护窗口，窗口外的关闭视为用户手动关闭 |
| 补回最小间隔 | 180 ms | 两次补回的节流间隔 |
| 到达目标附近就停止 | 开 | 靠近当前目标到判定距离即停 |
| 停止距离 | 2.0 米 | 按水平距离算 |
| 继续原有面向 | 关 | 技能动作期间把面向压回技能前的方向，避免被技能带着拐弯 |
| 使用位移技能时停止 | 开 | 技能动作中瞬时速度超过判定速度即停 |
| 位移判定速度 | 15 米/秒 | 跑步约 4~6，坐骑约 20 |

## 实现方式（规范相关）

- 不用 Harmony 钩子、不写游戏内存、不注入按键、不注册聊天命令。
- 只读：`InputManager.IsAutoRunning()`（是否自动前进）、`ConditionFlag.Casting`（是否施法）、`ActionManager.AnimationLock`（动画锁）、`InputManager.GetInputStatus(MOVE_*)`（移动输入）、玩家位置与朝向。
- 命令通过游戏自己的聊天通道执行：`UIModule.ProcessChatBoxEntry`（`/automove on|off`）。
- 注意：`ICommandManager.ProcessCommand` 只查插件注册的命令，不会转发给游戏，不能用它发游戏命令。
- `Dispose` 里解绑所有事件与字体句柄。

## 构建

```
build.cmd
```

构建产物在 `AutoRunKeeper\bin\x64\Debug\`，脚本会自动复制到 `..\..\devPlugins\AutoRunKeeper\`。

## 目录

```
AutoRunKeeper.csproj / AutoRunKeeper.yaml   工程与插件清单源
Plugin.cs / Configuration.cs / DalamudApi.cs
System/AutoRunGuard.cs                      守护核心（自动前进补回、接管判定）
System/PluginFont.cs                        中文字体
Windows/ConfigWindow.cs                     设置窗口
pluginmaster.json                           在线仓库清单
dist/Autowalk-<版本>.zip                    分发包（dll + json + deps.json）
```
