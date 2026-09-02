# StimGen 题库版本说明（更新于 2026-09-02）

> 当前运行规则请查看 `Docs/VR_Pairing_Similarity_Transition_Protocol_20260830.md`。题库和 session 已按 2×2 Pairing 协议重新生成。

这是一批已经由生成器冻结、可以在 PC 版 Unity 中运行的实验刺激与 session 文件：

- `stimulus_bank.json`：24 个正式家族、97 个正式物体、15 个练习物体，以及只含 High/Low 的结构配对矩阵。
- `session_P001.json` 到 `session_P024.json`：24 名受试者的固定 session。
- 每份 session 有 4 个 block；每个 block 30 个完整 Pairing trial；每份共 120 个 scored trials。
- 每份 session 的 High、Low 各 60 个，X轴 RotationDelta 0°、180°各 60 个，四个交叉条件各 30 个，Target 40 个、Non-target 80 个。

## 旋转协议

当前协议版本为 `Pair_XDelta_0_180_YSpin_v1`：

- 每个 trial 独立比较 Reference A 与 Comparison B；B 相对 A 的 X 轴角度差为 0°或180°；
- A 使用基准 X 角度，B 使用 `A + RotationDeltaX`，不依赖前一个 trial，也没有初始化呈现；
- 外层 Y 轴完整自转是所有 trial 共用的观看动画，不参与 RotationDeltaX 计算；
- Same / Different 只由 A、B 的 ObjectID 是否相同决定。

运行时层级为 `PresentationAnimationY → ConditionRotationX → Object`。CSV 分开保存
X轴条件角度与Y轴动画参数，不能再把实验角度解释为 Yaw。

## 重要的实验状态

High / Low 在本项目中仍然是结构条件：分别保留 2、0 条空间关系。原 Medium（1/3）被排除，不并入 Low。自动视觉检查使用的是题库构建阶段的 Y轴 0°/45°/90° 观察视图，它独立于正式任务的 X轴 RotationDelta。视觉检查只淘汰明显遮挡或观察角度不一致的物体，不把未经真人 pilot 验证的轮廓 IoU 区间冒充成难度证明。正式招募前仍应按计划完成 pilot，并根据反应时、错误率和可理解性删除不合格配对。

因此，这批文件可以称为“正式实验的生成版/试运行版题库”：程序结构、顺序和记录字段已经冻结；High 与 Low 是否产生预期的行为差异仍需 pilot 验收。

## 在 PC 版运行

通过 Unity 菜单 `Tools > StimGen > Experiment Launcher` 打开独立的实验员窗口。这个窗口和 Builder 一样属于 Unity 编辑器，不会覆盖电脑 Game 画面，也不会显示在 VR 头显里。

在 Launcher 中：

- 选择 `practice_session` 时，只显示 `运行 Pairing 练习（12 个 trial）`；12 次均为完整 Pairing trial；
- 从下拉框选择 `session_Pxxx`，受试者编号会自动随 Session 更新；
- `测试首个 Block`：只测试第一个 block；
- `运行完整 Session`：运行全部 4 个 block；
- `当前呈现结束后安全暂停`：不会在一个物体呈现到一半时切断 trial；
- `继续当前实验`：从安全暂停处继续；
- `从头重新开始当前 Session`：关闭当前日志并建立一个全新的日志，旧的部分记录不会被覆盖；
- `停止当前实验`：停止并保留已经写入的日志；
- 每个正式 block 结束后，受试者在头显内休息，实验员在 Launcher 中输入 1–7 分心理努力评分和可选备注，再点击 `保存评分并继续`。

不需要事先点击 Unity 的 Play：在编辑模式点击任一启动按钮后，Launcher 会自动进入 Play，等待场景初始化完成后再启动。实验期间两个启动按钮会被禁用。受试者头显在空闲时显示 `Waiting for experimenter`。session 内已经嵌入实际使用的物体定义，因此运行时不会现场重新生成题目；日志会写入 participant ID、区组、转移条件、物体/家族此前曝光次数、旋转轴和题库版本。

### 作答反馈

`ExperimentRunner` 面板中的 `Feedback Mode` 可以选择：

- `None`：不显示反馈。
- `RecordedOnly`：正式实验建议值，只显示中性的 `Recorded`。
- `CorrectnessPracticeOnly`：只有同时勾选 `Practice Mode` 时才显示 `Correct` / `Incorrect`；正式模式会自动降级为 `Recorded`。

实际启动时不需要手动配置这两个选项：Launcher 会根据所选 Session 自动设置反馈策略。头显和电脑 Game 窗口中的实验提示全部使用英文：练习模式在按键后立即显示绿色 `CORRECT` 或红色 `INCORRECT`；正式模式只显示蓝色 `RESPONSE RECORDED`，防止逐题正确性反馈改变正式任务本身；两种模式在超时后都会显示橙色 `NO RESPONSE`。反馈字体默认 120，电脑与头显内均带深色背景。Launcher 操作窗口仍保留中文，方便实验员操作。

`Feedback Duration` 控制显示时间。有效回答的反馈会在按键后立即出现；未作答反馈在刺激结束后出现，并被限制在 ITI 内，不会推迟下一题。CSV 会保存反馈模式、是否显示、文本和反馈开始/结束时间戳。

### 运行前检查与日志

每次点击启动按钮时都会先自动检查当前 Session。检查未通过时不会进入实验；也可以点击 `检查当前 Session 并保存报告` 手动检查。报告保存在项目根目录的 `PreflightReports` 文件夹中，包含题量、旋转协议、物体呈现时长和题库完整性结果。当前允许的物体呈现时长为 10–15 秒，场景默认值为 10 秒。

每次运行会在 `Application.persistentDataPath/StimGenLogs/{ParticipantID}/{ParticipantID}_{SessionID}` 下创建一个独立 Session 文件夹，并把本轮所有文件放进去。例如：`StimGenLogs/P001/P001_20260826_143012_245/P001_20260826_143012_245_*.csv`。同一受试者可以进行多轮实验，每轮使用新的 Session 文件夹，不会覆盖旧轮次：

- `{ParticipantID}_{SessionID}.csv`：每次呈现、答案、正确性、反应时、物体、结构条件和旋转参数；
- `{ParticipantID}_{SessionID}_events.csv`：阶段级事件时间表，同时保存 UTC ISO 时间、Unix 毫秒和 Unity 相对时间，可供 EEG/ECG 后续人工对齐；
- `{ParticipantID}_{SessionID}_blocks.csv`：正式 block 的 1–7 分心理努力评分、休息时长和备注；
- `{ParticipantID}_{SessionID}_session.json`：本次实际使用的完整 Session 副本。

例如 P001 的第二轮实验可能位于 `StimGenLogs/P001/P001_20260826_150430_812/`。受试者编号中的非法文件名字符会被替换为下划线；原始 ParticipantID 仍保存在 CSV 内容中。

事件时间表包含 Session、模式提示、Block 准备/开始/结束、segment 边界、注视、刺激、回答、反馈、ITI、手动暂停、block 休息与评分等事件。所有 CSV 都是边运行边写入并刷新，因此中途停止后，已经完成的数据仍会保留。

## 重新生成

只更新会话而不改变题库时，使用 Unity 菜单：`Tools > StimGen > Regenerate 24 Sessions From Existing Bank`。

需要同时重建题库时，使用：`Tools > StimGen > Generate Final Bank + 24 Sessions`。修改 Builder 中的起始编号、受试者数量或 ID 前缀后，程序会按固定的 6 组顺序生成对应 session；同一个受试者编号重新生成时会得到可复现的顺序和随机结果。
