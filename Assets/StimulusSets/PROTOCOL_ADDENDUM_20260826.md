# StimGen 正式实验协议补充（2026-08-26）

> **历史版本声明（2026-08-30）：** 以下 2-back、t-2 和 initialization 参数已不再对应当前 Unity 运行配置。
> 当前 Pairing 版协议见 `Docs/VR_Pairing_Similarity_Transition_Protocol_20260830.md`。

本文件记录已经确认、并覆盖早期计划草稿中相应旧参数的最终运行决定。

## 已冻结的任务参数

- 任务：视觉 2-back，当前物体与 `t−2` 的物体比较；
- 回答：Meta Quest Pro `B = Same`、`A = Different`，PC 键盘 `J = Same`、`F = Different`；
- 每个 block 前 2 次呈现只用于建立 2-back 序列，不计分且不接受回答；
- 正式角度条件：当前物体相对 `t−2` 物体绕 X 轴变化 `0° / 90° / 180°`；
- 每个 block 的前两次初始化呈现均从 X 轴 `0°` 开始，不再使用随机起始角度；
- 每个物体呈现时绕外层 Y 轴匀速自转 1 圈，以便观察完整空间结构；Y 轴动画不是实验角度条件；
- 物体呈现时长：允许 10–15 秒；当前场景使用 10 秒；
- 正式 Session：6 blocks，每个 block 30 个计分题加 2 次初始化呈现，共 180 个计分题；
- 练习 Session：12 个计分题加 2 次初始化呈现，建议正确率达到 80% 后进入正式实验。

## 反馈规则

- 练习：回答后立即显示 `CORRECT` 或 `INCORRECT`；超时显示 `NO RESPONSE`；
- 正式：回答后立即显示中性 `RESPONSE RECORDED`，不显示正确答案；超时显示 `NO RESPONSE`；
- 反馈不改变原本 10 秒刺激时长；超时提示不推迟下一题。

## 实验员操作

- 统一通过 `Tools > StimGen > Experiment Launcher` 选择和启动 Session；
- 启动前自动进行 preflight 检查，失败时阻止运行；
- 暂停采用“当前呈现结束后安全暂停”，避免切断 trial；
- 正式实验每个 block 后在 Launcher 外部记录 1–7 分心理努力评分，再继续；
- 重启会建立新日志，旧的部分日志保留。

## 时间同步

Unity 目前不直接控制 EEG/ECG。`_events.csv` 为各阶段保存 UTC ISO、Unix 毫秒和 Unity 相对时间，供实验结束后与外部设备的时钟或事件记录人工对齐。
