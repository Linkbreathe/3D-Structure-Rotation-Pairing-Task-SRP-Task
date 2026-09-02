# StimGen 四零件三维结构视觉 2-back

> **历史版本声明（2026-08-30）：** 本文件记录的是已废止的 2-back 方案。当前 Unity 实现已改为独立 Pairing（Reference A -> Comparison B -> Same / Different）；请以 `VR_Pairing_Similarity_Transition_Protocol_20260830.md` 为准。

## 当前实现、正式实验协议与未来数据采集总说明

**文档版本：** 2026-08-26（与当前 Unity 工程和已生成文件对齐）  
**项目路径：** `C:\Users\linki\Application\My project (2)`  
**Unity 版本：** 6000.3.10f1  
**当前目标平台：** Windows PC / Unity Editor + Meta Quest Pro Link  
**当前不要求：** 独立 Quest Build、在头显上完全脱离 PC 运行、Unity 内直接采集 EEG/ECG

> 这份文档取代早期计划中仍然存在的旧参数。当前实施以本文件、C# 源码和已经生成的 JSON 为准；如果旧计划与当前文件冲突，以本文件为准。

---

## 0. 一页速览

| 项目 | 当前冻结设置 |
| --- | --- |
| 任务 | 四零件三维组合物体视觉 2-back |
| 参与者回答 | 当前物体是否与 `t−2` 物体相同 |
| 正式有效样本目标 | 24 人 |
| 建议招募 | 28–30 人，为设备失败和退出预留余量 |
| 正式 Session | 6 个 Block |
| 每个 Block | 2 个初始化呈现 + 30 个 scored trials |
| 每个 Block 的 Segment | 8、7、8、7 个 scored trials |
| 每位参与者 | 180 个 scored trials，192 次正式呈现 |
| Similarity | Low / Medium / High，在 Segment 层面变化 |
| Rotation 条件 | 当前物体相对 `t−2` 绕 X 轴变化 0° / 90° / 180° |
| 起始角度 | 每个 Block 前两次呈现固定为 X 轴 0°，不再随机使用 15°等角度 |
| 观看动画 | 每个物体绕外层 Y 轴自转 1 圈，帮助看到完整空间结构 |
| 当前物体呈现时长 | 10 秒；程序允许配置 10–15 秒 |
| 注视点 | 0.5 秒 |
| 空白间隔 | 0.4 秒 |
| 练习 | 12 个 scored trials + 2 个初始化呈现 |
| PC 键盘 | 默认 J = Same，F = Different；正式 Session 会做左右键平衡 |
| Quest Pro 右手柄 | B = Same，A = Different，当前固定不随键盘平衡改变 |
| 正式反馈 | `RESPONSE RECORDED` 或 `NO RESPONSE`，不在头显中显示对错 |
| 练习反馈 | `CORRECT`、`INCORRECT`、`NO RESPONSE` |
| Unity 与 EEG/ECG | 当前不直接合并；先保存可对齐的时间戳，之后人工或外部程序对齐 |

当前已经生成并更新：

- `Assets/StimulusSets/stimulus_bank.json`
- `Assets/StimulusSets/session_P001.json` 到 `session_P024.json`
- `Assets/StimulusSets/practice_session.json`

---

## 1. 这个项目研究什么

参与者在 PCVR 中观看由四种零件组成的三维物体：

1. Cube；
2. Cylinder；
3. Capsule；
4. Ellipsoid。

每个物体都使用这四种零件各一个。不同物体的主要区别不是颜色或零件数量，而是：

- 哪两个零件相连；
- 它们之间的空间关系是什么；
- 一个零件位于另一个零件的上、下、左、右、前或后。

参与者每次看到一个物体，并把它与前面第二个物体（`t−2`）比较：

```text
当前第 8 次呈现 → 与第 6 次呈现比较
当前第 9 次呈现 → 与第 7 次呈现比较
```

参与者按键回答：

```text
Same      当前物体与 t−2 是同一个结构
Different 当前物体与 t−2 不是同一个结构
```

方向不同不一定代表结构不同。一个物体即使绕 X 轴改变 90°，仍然可能是同一个物体。因此任务要求参与者记住零件之间的三维关系，而不是只记住某一张二维正面图。

### 1.1 主要研究问题

本项目可以支持以下主要问题：

1. 同一个三维结构在不同 X 轴方向差下，识别正确率和反应时间如何变化；
2. 两个不同物体的结构越相似，是否越容易被误认为 Same；
3. Similarity 从一种条件转移到另一种条件后，行为是否出现短时适应过程；
4. 如果以后接入 EEG/ECG，边界前后是否出现与行为变化相对应的生理变化；
5. 在正式任务中重复看到某个物体或物体家族后，结构记忆是否逐渐熟悉。

这些是需要数据检验的假设。`Low`、`Medium`、`High` 首先是结构标签，不能在没有 pilot 的情况下直接写成“低负荷”“中负荷”“高负荷”。

---

## 2. 核心术语

### 2.1 Part、Object、Family

- **Part：** 一个基本零件，例如 Cube 或 Capsule。
- **Combination Object：** 四个零件连接后形成的完整物体，有唯一的 `ObjectID`。
- **Object Family：** 从一个基础结构出发生成的一组相关物体，有 `FamilyID`。
- **Stimulus Bank：** 已经通过结构检查和自动视觉检查的物体、家族和配对矩阵。

### 2.2 Target 和 Non-target

- **Target：** 当前 `ObjectID` 与 `t−2 ObjectID` 相同，正确答案是 Same。
- **Non-target：** 当前 `ObjectID` 与 `t−2 ObjectID` 不同，正确答案是 Different。

一个 Target 可以有不同的绝对呈现方向，但仍然是同一个结构。High、Medium、Low 都属于 Non-target。

### 2.3 Structural Similarity

四个零件的连通结构通常可以用三条完整空间关系描述。每条关系同时包含：

1. 哪两个零件相连；
2. 相对方向是什么。

| 配对类型 | 保留的空间关系 | ObjectID | 正确答案 |
| --- | ---: | --- | --- |
| Target | 3/3 | 相同 | Same |
| High Non-target | 2/3 | 不同 | Different |
| Medium Non-target | 1/3 | 不同 | Different |
| Low Non-target | 0/3 | 不同 | Different |

因此：

> High 不是 Same。High 是最容易让人误按 Same 的 Different。

Similarity 属于一个物体对，而不是某个物体永久拥有的属性。同一个 Object 可能与不同 Reference 形成不同 Similarity。

### 2.4 Segment、Boundary、Transition

- **Segment：** Similarity 保持稳定的一段连续任务。
- **Boundary：** 两个 Segment 之间的切换位置。
- **Transition：** Similarity 从一个等级改变到另一个等级，例如 `Low_to_High`。
- **No-op：** Similarity 没有改变，例如 `Low_to_Low`，用于区分真正切换和单纯时间经过。

参与者看不到 Segment 边界，也不会获得切换提示。Segment 之间不休息；正式休息只在 Block 结束后进行。

### 2.5 Presentation 和 scored trial

- **Presentation：** Unity 显示一次物体。每个 Block 的前两次是初始化呈现。
- **scored trial：** 从第三次呈现开始，存在可比较的 `t−2`，因此需要回答并计分。

每个 Block 的前两次初始化呈现：

- 不计分；
- 不接受 Same/Different 回答；
- 仅用于填入两条 2-back 记忆链。

---

## 3. 当前刺激库

当前 `stimulus_bank.json` 的冻结内容为：

| 内容 | 当前数量 |
| --- | ---: |
| 正式 Object Family | 24 |
| 正式 Objects | 131 |
| 独立练习 Objects | 20 |
| 每个物体的零件数 | 4 |
| 零件集合 | Cube + Cylinder + Capsule + Ellipsoid |
| Master seed | 20260823 |

题库构建时会：

1. 生成四零件基础物体；
2. 生成 High、Medium、Low 变体；
3. 计算正式物体之间的结构配对；
4. 淘汰关系不符合 3/2/1/0 规则的配对；
5. 运行遮挡和多视角一致性检查；
6. 补充候选不足的物体；
7. 把物体定义、关系签名、FamilyID、Seed 和配对矩阵写入 bank。

自动视觉检查只是质量筛选，不是人类难度的证明。High、Medium、Low 是否真的产生可理解的行为梯度，必须通过真人 pilot 验证。

正式招募开始后，不应在没有记录版本的情况下修改刺激库。若刺激库改变，必须重新生成相关 Session，并把新题库版本作为新的研究版本管理。

---

## 4. 当前正式实验设计

### 4.1 Within-subject design

每位参与者都体验全部主要条件：

- Low、Medium、High structural similarity；
- X 轴 RotationDelta 0°、90°、180°；
- Target 和 Non-target；
- 上升、下降和 No-op transition。

这样比较的是同一个人自己在不同条件下的表现，能够减少人与人之间的基础能力差异。

代价是会有练习、疲劳和顺序效应，因此参与者之间使用六种 Block 顺序轮换。

### 4.2 六种 Block sequence

`L`、`M`、`H` 分别代表 Low、Medium、High structural similarity。

| Sequence | Segment 1 | Segment 2 | Segment 3 | Segment 4 | 边界 |
| --- | --- | --- | --- | --- | --- |
| A | L | L | M | H | L→L、L→M、M→H |
| B | L | H | M | M | L→H、H→M、M→M |
| C | M | L | H | H | M→L、L→H、H→H |
| D | M | M | H | L | M→M、M→H、H→L |
| E | H | M | L | L | H→M、M→L、L→L |
| F | H | H | L | M | H→H、H→L、L→M |

每位参与者完成 A–F 全部六个 Sequence，但顺序不同。

### 4.3 六组参与者顺序

| 顺序组 | Block 顺序 |
| --- | --- |
| 1 | A → B → F → C → E → D |
| 2 | B → C → A → D → F → E |
| 3 | C → D → B → E → A → F |
| 4 | D → E → C → F → B → A |
| 5 | E → F → D → A → C → B |
| 6 | F → A → E → B → D → C |

程序按 Participant number 循环分配顺序组。P001、P007、P013、P019 使用第 1 组；之后每六名参与者循环一次。24 名参与者时每组各 4 人。

### 4.4 Transition 覆盖

三个 Similarity 等级共有 9 种有向边界：

```text
L→L、L→M、L→H
M→L、M→M、M→H
H→L、H→M、H→H
```

每位参与者的六个 Block 共 18 个边界，每种边界出现 2 次。真实变化有 6 种，No-op 有 3 种。

这意味着每位参与者都经历所有 transition 类型，但每种类型只有两个边界，仍然不足以训练可靠的个人自适应模型。

### 4.5 每个 Block 的数量

```text
2 次初始化呈现
8 个 scored trials（Segment 1）
7 个 scored trials（Segment 2）
8 个 scored trials（Segment 3）
7 个 scored trials（Segment 4）
--------------------------------
32 次呈现，其中 30 次计分
```

每个 Block：

- Target：10 个；
- Non-target：20 个；
- High、Medium、Low 分布在 Segment 层面；
- 每个 Segment 的前两个 scored trial 固定为 Non-target，用于切换两条交错 2-back 记忆链。

每个正式 Session：

- 6 个 Block；
- 180 个 scored trials；
- 192 次总呈现；
- Similarity 每级 60 个 scored trials；
- RotationDelta 每级 60 个 scored trials；
- Target 60 个，Non-target 120 个。

### 4.6 Rotation 与物体显示

当前旋转协议是：

```text
RotationProtocolVersion = XDelta_0_90_180_YSpin_v1
ConditionRotationAxis = X
PresentationAnimationAxis = Y
```

正式条件是当前物体相对 `t−2` 物体的 X 轴角度差：

```text
0°   当前物体与 t−2 X 轴方向相同
90°  当前物体相对 t−2 绕 X 轴变化 90°
180° 当前物体相对 t−2 绕 X 轴变化 180°
```

当前已取消随机起始角度：

- 每个 Block 的前两个初始化物体 `currentRotationX = 0°`；
- 不再生成 15°、30°等随机起始姿态；
- 后续 trial 按两条 2-back 链计算：`current = t−2 + RotationDeltaX`，再对 360°取模。

因此日志中的绝对 X 角度可能出现 0°、90°、180°、270°。270°只是角度取模后的绝对姿态，等价于 −90°，不是第四个实验条件。真正的条件仍然只有 0°、90°、180°。

此外，物体会在保持 X 轴实验姿态的同时，绕外层 Y 轴匀速自转 1 圈。Y 轴自转的目的只是减少遮挡、帮助参与者看到完整空间结构，不参与 RotationDelta 条件计算。

运行时层级为：

```text
PresentationAnimationY
└── ConditionRotationX
    └── 四零件 Object
```

当前不再使用固定向前倾斜 15° 的设置。

---

## 5. 单个呈现的时序

当前 SampleScene 默认设置：

| 阶段 | 默认时长 | 说明 |
| --- | ---: | --- |
| Fixation | 0.5 秒 | 显示注视十字 |
| Stimulus | 10 秒 | 物体持续显示，期间可以回答 |
| Inter-trial interval | 0.4 秒 | 物体消失后的空白 |
| Feedback | 1.5 秒上限 | 正确回答时在剩余呈现时间中显示；超时提示受 ITI 限制 |

单个 scored trial 的顺序：

1. 显示注视十字；
2. 记录 `FixationOnset`；
3. 显示物体并记录 `StimulusOnset`；
4. 物体保持显示 10 秒；
5. 在显示期间读取回答；
6. 第一次有效回答后立即显示反馈；
7. 即使提前回答，物体仍保持完整 10 秒；
8. 如果没有回答，记录 `Timeout/NoResponse`；
9. 记录 `StimulusOffset`；
10. 进入 0.4 秒空白间隔；
11. 进入下一次呈现。

一旦记录到第一次有效回答，后续重复按键不会改变该 trial 的答案。初始化呈现不会读取或接受 Same/Different 回答。

---

## 6. 回答方式和反馈

### 6.1 键盘和 Quest Pro

Quest Pro 右手柄当前固定为：

```text
B = Same
A = Different
```

PC 键盘默认设置为：

```text
J = Same
F = Different
```

正式 Session 为了平衡左右键顺序，部分 Participant number 会启用 `swapResponseKeys`：

- `swapResponseKeys = false`：J = Same，F = Different；
- `swapResponseKeys = true`：J = Different，F = Same。

当前 Quest 的 B/A 语义不受键盘左右平衡影响。实验员开始前应根据 Launcher 和 Session 文件确认当次键盘映射，并向参与者明确说明。

Windows PC 下程序可以在 Unity Game 窗口失去焦点时继续捕获键盘，前提是 `captureWindowsKeyboardWithoutFocus` 保持开启。

### 6.2 练习模式

练习 Session 有 12 个 scored trials。回答后显示：

- `CORRECT`；
- `INCORRECT`；
- `NO RESPONSE`。

建议练习正确率达到 80% 后进入正式实验。若没有达到，实验员可以重新运行练习 Session。

### 6.3 正式模式

正式实验不把正确答案显示给参与者：

- 有效回答后显示 `RESPONSE RECORDED`；
- 超时显示 `NO RESPONSE`；
- 数据文件仍会记录正确答案和正确性，供事后分析。

反馈不会缩短 10 秒刺激时长，也不会因反馈而临时改变下一题的开始时间。

头显中的提示统一使用英文，避免字体缺少中文字符导致方框或乱码。实验员专用 Launcher 仍然可以显示中文。

---

## 7. 项目文件和功能分工

### 7.1 运行时代码

| 文件 | 作用 |
| --- | --- |
| `Assets/Scripts/StimGen/Runtime/ExperimentRunner.cs` | 播放 Session、注视点、刺激、输入、反馈、暂停和 Block 流程 |
| `Assets/Scripts/StimGen/Runtime/TrialGenerator.cs` | 根据题库和 Participant number 生成完整 Session |
| `Assets/Scripts/StimGen/Runtime/ExperimentDesign.cs` | 固定 Segment、Block 顺序、transition 和 Rotation 条件 |
| `Assets/Scripts/StimGen/Runtime/RotationController.cs` | 内层 X 条件旋转和外层 Y 观看自转 |
| `Assets/Scripts/StimGen/Runtime/PracticeSessionFactory.cs` | 生成独立练习 Session |
| `Assets/Scripts/StimGen/Runtime/ExperimentLogger.cs` | 写入逐呈现 CSV、事件 CSV、Block 评分和 Session 副本 |
| `Assets/Scripts/StimGen/Runtime/TrialTypes.cs` | Session、Block、Presentation 和反应数据结构 |

### 7.2 Unity Editor 工具

| 文件 | 作用 |
| --- | --- |
| `Assets/Scripts/StimGen/Editor/StimulusSetBuilder.cs` | 生成题库和参与者 Session |
| `Assets/Scripts/StimGen/Editor/ExperimentLauncher.cs` | 选择 Session、启动、暂停、评分、停止和重启 |

### 7.3 场景

```text
Assets/Scenes/SampleScene.unity
```

场景中的核心组件是 `ExperimentRunner`、`RotationController` 和用于 PCVR/头显显示的 Camera Rig。当前项目以 PC 运行和 Quest Link 测试为优先，不把独立 Quest Build 作为当前正式上线前提。

---

## 8. Builder：生成题库和 Session

打开：

```text
Tools > StimGen > Builder
```

Builder 的两个主要阶段必须按顺序理解。

### 8.1 什么时候点击“① 建刺激库”

只有在以下内容改变时才需要重建刺激库：

- `copiesPerShape`；
- 零件集合；
- 正式家族数或练习家族数；
- 视觉检查规则；
- bank master seed；
- 需要改变物体本身的结构生成规则。

输出：

```text
Assets/StimulusSets/stimulus_bank.json
```

### 8.2 什么时候点击“② 排会话 + 运行前检查”

以下情况只需要重新排 Session，不需要重建刺激库：

- 修改参与者人数；
- 修改起始 Participant number；
- 修改 Participant ID 前缀；
- 修改 Block 顺序或 trial 排程；
- 修改 Rotation 协议；
- 修改 Target/Non-target 排程规则。

### 8.3 Builder 中最重要的字段

| 字段 | 含义 |
| --- | --- |
| `firstParticipantNumber` | 第一个生成的数字编号，例如 1 |
| `participantCount` | 要生成多少名参与者的 Session |
| `participantIdPrefix` | ID 前缀，默认 `P` |
| `copiesPerShape` | 每种零件复制数量，当前默认 1 |
| `masterSeed` | 生成可复现题库和排程的种子 |
| `maxConsecutiveTargets` | 防止 Target 连续出现过多 |
| `noRepeatWindow` | 具体模型的重复间隔限制 |
| `familyCooldown` | 同一家族的冷却间隔 |

例如生成 P001–P012：

```text
firstParticipantNumber = 1
participantCount = 12
participantIdPrefix = P
点击：② 排会话 + 运行前检查
```

### 8.4 重要注意：减少人数不会自动删除旧文件

当前 Builder 会生成 P001–P012，但不会自动删除以前存在的：

```text
session_P013.json ... session_P024.json
```

因此如果从 24 人改成 12 人，必须在确认新文件和备份都没问题后，人工归档旧文件，避免 Launcher 仍然显示多余 Session。旧 Session 不应在没有确认的情况下直接删除。

### 8.5 固定菜单和自定义人数

菜单中的：

```text
Tools > StimGen > Generate Final Bank + 24 Sessions
Tools > StimGen > Regenerate 24 Sessions From Existing Bank
```

是写死生成 24 人的快捷命令。需要自定义人数时，应使用 Builder 窗口中的 `participantCount`，不要使用这两个固定 24 的快捷命令。

---

## 9. Launcher：实际运行实验

打开：

```text
Tools > StimGen > Experiment Launcher
```

### 9.1 运行前

1. 打开 `Assets/Scenes/SampleScene.unity`；
2. 打开 Experiment Launcher；
3. 点击刷新 Session 列表；
4. 从下拉菜单选择 `PRACTICE` 或正式 `session_Pxxx`；
5. 确认受试者编号和当前 Session；
6. 点击“检查当前 Session 并保存报告”；
7. 只有报告通过后才开始运行。

### 9.2 练习

选择 `PRACTICE — 12 scored trials` 后，点击：

```text
运行练习 Session（12 个计分题）
```

练习结束后 Launcher 显示正确率。低于 80% 时，建议重新练习，而不是直接开始正式 Session。

### 9.3 正式实验

正式 Session 有两个启动方式：

- `测试首个 Block`：只运行当前 Session 的第一个 Block，用于端到端检查；
- `运行完整 Session`：运行六个 Block。

正式流程中，每个 Block 结束后：

1. 头显显示 Block 完成和等待提示；
2. 受试者休息；
3. 实验员在 Launcher 中填写 1–7 的心理努力评分；
4. 可填写备注；
5. 点击“保存评分并继续”；
6. 程序进入下一个 Block。

### 9.4 暂停、停止和重启

- “当前呈现结束后安全暂停”：当前物体完成显示后再暂停，不打断该 trial；
- “继续当前实验”：从暂停位置继续；
- “停止当前实验”：停止，但已经写入的日志保留；
- “从头重新开始当前 Session”：创建新的时间戳日志目录，从 Block 1 重新开始；旧日志保留；
- “退出 Play 模式”：退出 Unity Play。

如果同一 Participant 进行多轮实验，每一轮都会在同一个 Participant 文件夹下面创建新的时间戳 Session 文件夹，不会覆盖上一轮。

---

## 10. 未来数据如何保存

### 10.1 目录结构

程序使用：

```text
Application.persistentDataPath/StimGenLogs/
```

当前 Windows 设置下通常对应：

```text
C:\Users\linki\AppData\LocalLow\DefaultCompany\My project (2)\StimGenLogs\
```

每名参与者一个目录，每次 Session 一个带时间戳的子目录：

```text
StimGenLogs
└── P001
    ├── P001_20260826_143012_245
    │   ├── P001_20260826_143012_245.csv
    │   ├── P001_20260826_143012_245_events.csv
    │   ├── P001_20260826_143012_245_blocks.csv
    │   └── P001_20260826_143012_245_session.json
    └── P001_20260827_101512_018
        ├── P001_20260827_101512_018.csv
        ├── P001_20260827_101512_018_events.csv
        └── P001_20260827_101512_018_session.json
```

如果同一毫秒内创建了重复 Session，程序会追加 `_01`、`_02` 等后缀，防止覆盖。

### 10.2 主数据 CSV

主 CSV 一行代表一次 Presentation，包括不计分的前两次初始化呈现。正式 Session 预期有 192 行数据，练习 Session 预期有 14 行数据。

它包含以下重要信息：

#### 身份与位置

```text
ParticipantID
SessionID
BlockIndex
BlockSequenceID
SegmentIndex
PresentationIndexInBlock
TrialIndexGlobal
TrialIndexWithinBlock
TrialIndexWithinSegment
Scored
ChainID
```

#### Similarity 与边界

```text
PreviousSegmentSimilarity
CurrentSegmentSimilarity
SimilarityTransition
IsNoOpBoundary
IsFirstTrialAfterBoundary
TrialsSinceTransition
BoundaryPositionWithinBlock
```

#### 物体和结构

```text
ReferenceObjectID
CurrentObjectID
ReferenceFamilyID
CurrentFamilyID
ObjectFamilyID
PartSetID
StimulusBankVersion
StimulusSeed
PartCount
ReferenceRelationSignature
CurrentRelationSignature
TrialPairType
RetainedRelations
StructuralDistance
```

#### Rotation 和观看动画

```text
ReferenceRotationX
CurrentRotationX
RotationDeltaX
ConditionRotationAxis
PresentationAnimationAxis
PresentationAnimationEnabled
PresentationAnimationRevolutions
PresentationDurationMs
PresentationAnimationSpeedDegPerSec
```

#### 结构熟悉度

```text
ReferenceObjectPriorExposures
CurrentObjectPriorExposures
ReferenceFamilyPriorExposures
CurrentFamilyPriorExposures
TrialsSinceObjectLastSeen
TrialsSinceFamilyLastSeen
```

#### 回答和结果

```text
ExpectedAnswer
ParticipantAnswer
ResponseValid
Timeout
Correct
Outcome
ReactionTimeMs
```

其中 `Outcome` 的含义是：

```text
Hit               Target + Same
Miss              Target + Different
FalseAlarm        Non-target + Same
CorrectRejection  Non-target + Different
NoResponse        在规定时间内没有回答
```

#### 反馈和时间

```text
FeedbackMode
FeedbackShown
FeedbackText
FeedbackOnsetTimestamp
FeedbackOffsetTimestamp
FixationOnsetTimestamp
StimulusOnsetTimestamp
StimulusOffsetTimestamp
ResponseTimestamp
SegmentBoundaryTimestamp
EEGMarkerTimestamp
ECGMarkerTimestamp
EEGSignalQuality
ECGSignalQuality
```

### 10.3 主 CSV 示例

以下只展示核心列，实际文件还包含上面列出的结构和时间字段：

```csv
ParticipantID,SessionID,BlockIndex,PresentationIndexInBlock,Scored,ReferenceObjectID,CurrentObjectID,ReferenceRotationX,CurrentRotationX,RotationDeltaX,ExpectedAnswer,ParticipantAnswer,ResponseValid,Timeout,Correct,Outcome,ReactionTimeMs
P001,P001_20260826_143012_245,0,0,0,,F022_X023,0.0000,0.0000,,,0,0,-1,,
P001,P001_20260826_143012_245,0,2,1,F022_X023,F007_X008,0.0000,90.0000,90.0000,Different,Different,1,0,1,CorrectRejection,842.5000
P001,P001_20260826_143012_245,0,3,1,F023_X024,F011_X012,0.0000,0.0000,0.0000,Same,Same,1,0,1,Hit,621.3000
P001,P001_20260826_143012_245,0,4,1,F007_X008,F019_X020,90.0000,270.0000,180.0000,Different,,0,1,0,NoResponse,
```

第一行是初始化呈现：`Scored=0`、没有正确答案、不参与计分。第四行示范超时：没有 ParticipantAnswer，`Timeout=1`，`Outcome=NoResponse`。

### 10.4 事件 CSV

事件 CSV 用于恢复完整时间线，可能包含：

```text
SessionStart
ModeBannerOnset / ModeBannerOffset
BlockReadyOnset / BlockReadyOffset
BlockStart
SegmentBoundary
FixationOnset / FixationOffset
StimulusOnset / StimulusOffset
Response
FeedbackOnset / FeedbackOffset
InterTrialOnset / InterTrialOffset
PauseOnset / PauseOffset
BlockEnd
BlockPauseOnset
BlockRatingSubmitted
BlockPauseOffset
SessionEnd
SessionStopped
```

事件 CSV 的字段为：

```text
ParticipantID
SessionID
EventName
UtcTimestamp
UnixTimeMilliseconds
UnityRealtimeSeconds
BlockIndex
BlockSequenceID
PresentationIndexInBlock
SegmentIndex
TrialIndexGlobal
Detail
```

示例：

```csv
ParticipantID,SessionID,EventName,UtcTimestamp,UnixTimeMilliseconds,UnityRealtimeSeconds,BlockIndex,BlockSequenceID,PresentationIndexInBlock,SegmentIndex,TrialIndexGlobal,Detail
P001,P001_20260826_143012_245,SessionStart,2026-08-26T14:30:12.245Z,1787754612245,120.331,-1,,-1,-1,-1,
P001,P001_20260826_143012_245,StimulusOnset,2026-08-26T14:30:13.800Z,1787754613800,121.886,0,A,2,0,0,Object=F007_X008;RotationX=90.0
P001,P001_20260826_143012_245,Response,2026-08-26T14:30:16.420Z,1787754616420,124.506,0,A,2,0,0,Different
P001,P001_20260826_143012_245,StimulusOffset,2026-08-26T14:30:23.801Z,1787754623801,131.887,0,A,2,0,0,
```

时间戳含义：

- `UtcTimestamp`：UTC ISO 8601 时间；
- `UnixTimeMilliseconds`：Unix 毫秒时间；
- `UnityRealtimeSeconds`：Unity 程序启动后的相对秒数。

对 EEG/ECG 对齐时，优先使用同一个同步测试确认过的 UTC/Unix 或外部设备 marker；`UnityRealtimeSeconds` 用于恢复 Unity 内部的精细相对顺序。

### 10.5 Block 评分 CSV

正式 Block 结束后，在 Launcher 中点击“保存评分并继续”才会产生或追加：

```text
ParticipantID
SessionID
BlockIndex
BlockSequenceID
EffortRating1to7
RestSeconds
RatingUtcTimestamp
RatingUnixTimeMilliseconds
RatingUnityRealtimeSeconds
Note
```

示例：

```csv
ParticipantID,SessionID,BlockIndex,BlockSequenceID,EffortRating1to7,RestSeconds,RatingUtcTimestamp,RatingUnixTimeMilliseconds,RatingUnityRealtimeSeconds,Note
P001,P001_20260826_143012_245,0,A,5,74.320,2026-08-26T14:42:18.500Z,1787757738500,850.221,Comfortable
```

练习模式通常不要求 Block 心理努力评分，因此可能没有 `_blocks.csv`。

### 10.6 Session JSON 副本

`_session.json` 是本次实际播放的完整计划副本，包含：

- Participant number 和 ID；
- Block 顺序；
- 每个 Presentation 的物体顺序；
- Reference 和 Current ObjectID；
- Target/Non-target；
- Similarity transition；
- `t−2` 关系；
- X 轴角度；
- RotationDelta；
- 题库版本和 Seed；
- 运行时可实例化的物体定义。

它使数据分析可以确认参与者当时实际看到了什么，即使以后代码发生变化。

---

## 11. EEG/ECG：当前状态和未来采集方式

### 11.1 当前状态

Unity 当前不直接采集 EEG 或 ECG，也没有自动把生理信号写入日志。当前 `IMarkerSink` 的默认实现只是调试用途的 `DebugMarkerSink`，不会替代真实的硬件 trigger、LSL 或厂商 marker 接口。

因此当前主 CSV 中：

- `EEGMarkerTimestamp` 通常为空；
- `ECGMarkerTimestamp` 通常为空；
- `EEGSignalQuality` 通常为空；
- `ECGSignalQuality` 通常为空。

这不是行为数据丢失，而是表示 Unity 没有直接拥有生理设备数据。

### 11.2 推荐的未来外部采集架构

```text
Unity / ExperimentRunner
        │
        ├── 主 CSV：行为、刺激条件、Unity 时间
        └── events.csv：UTC、Unix、Unity 相对时间、事件名

EEG 设备 ──┐
           ├── 外部设备文件 + marker / LSL / TTL
ECG 设备 ──┘

分析阶段：使用同步测试和事件名对齐所有时间轴
```

正式接入时，至少需要向外部系统发送：

```text
SessionStart
BlockStart
SegmentBoundary
FixationOnset
StimulusOnset
Response
StimulusOffset
FeedbackOnset / FeedbackOffset
BlockEnd
SessionEnd
```

建议在正式招募前完成：

1. Unity 和 EEG/ECG 同时启动；
2. 发送 5–10 次已知顺序的同步 marker；
3. 检查 marker 是否数量一致、顺序一致；
4. 测量 marker 到实际信号的延迟；
5. 记录 EEG 采样率、通道、参考、滤波和阻抗；
6. 记录 ECG 导联、采样率和 R-peak 处理方法；
7. 保存同步测试文件，不要只保存最终任务文件。

### 11.3 建议的生理采集内容

这部分由外部设备和分析程序负责，Unity 不替代它们：

#### EEG

- 原始连续 EEG；
- 设备采样率和通道信息；
- 参考方式和滤波设置；
- 事件 marker 流；
- 坏道、眼动和肌电伪迹记录；
- stimulus-locked 和 transition-locked 分析所需的时间轴。

#### ECG

- 原始 ECG；
- R peak 或 IBI；
- 心率时间序列；
- 设备采样率；
- 电极/导联信息；
- 与 Block、Segment 和 transition 对齐后的窗口。

23–28 秒的 Segment 可以支持心率和短时变化的探索，但不应仅凭一个 Segment 宣称得到稳定的传统 HRV。若报告 HRV，应使用更长连续窗口，并明确分析性质。

---

## 12. 参与者采集 SOP

### 12.1 进入实验室

1. 说明任务、设备、风险、退出权利和数据用途；
2. 签署知情同意；
3. 分配匿名 ParticipantID，例如 P001；
4. ParticipantID 与姓名的对应表单独保存，不写进 Unity 日志；
5. 询问视力、VR 使用经验和当前不适；
6. 佩戴 Quest Pro、EEG 和 ECG（如本轮采集）；
7. 检查头显显示、镜片位置、追踪和手柄电量；
8. 检查 EEG/ECG 电极质量和采样状态；
9. 进行外部设备同步测试；
10. 保存静息基线（如研究方案要求）。

### 12.2 练习和规则确认

实验员需要让参与者理解：

- 比较的是 `t−2`，不是上一个物体；
- 同一物体换方向仍然可能是 Same；
- High 是不同物体；
- 前两次初始化不需要回答；
- Quest 是 B=Same、A=Different；键盘映射必须按当前 Session 说明。

然后运行练习 Session。建议达到 80% 正确率后再进入正式实验；若没有达到，解释规则后重复练习。

### 12.3 正式实验

1. 在 Launcher 中选择正确的 `session_Pxxx.json`；
2. 再次确认 ParticipantID；
3. 运行 preflight；
4. 运行首个 Block 测试或完整 Session；
5. 每个 Block 结束后填写心理努力评分；
6. 让参与者休息并检查设备，但不要根据一次评分临时改变题目；
7. 完成全部 6 个 Block；
8. 记录 VR 不适、设备掉线、暂停和异常情况；
9. 结束后保存问卷或策略访谈（如研究方案要求）。

### 12.4 离开实验室前

实验员应确认：

- 主 CSV 存在；
- events CSV 存在；
- `_session.json` 存在；
- 正式 Block 评分已写入（如使用）；
- Session 文件夹名称与 ParticipantID 一致；
- 事件顺序没有明显中断；
- EEG/ECG 文件和同步测试文件已保存；
- 备份已经完成；
- 设备异常已写入单独的 QC 表。

---

## 13. 数据质量控制和备份

### 13.1 Unity 文件完整性

正式完整 Session 应满足：

```text
1 个主 CSV
1 个 events CSV
1 个 session JSON
每个正式 Block 有评分时：1 个 blocks CSV
主 CSV 数据行 = 192
Block 数 = 6
每个 Block 呈现数 = 32
每个 Block scored 数 = 30
```

练习完整 Session 应满足：

```text
主 CSV 数据行 = 14
1 个 Block
12 个 scored trials
2 个初始化呈现
```

### 13.2 行为 QC

建议在外部保存 `participant_qc.csv`，至少包括：

```text
ParticipantID
SessionID
PracticeAccuracy
FormalResponseRate
FormalAccuracy
MedianReactionTime
ExtremeReactionTimeCount
VRDiscomfort
KeyboardOrControllerIssue
EEGStatus
ECGStatus
MarkerStatus
IncludedForBehavior
IncludedForEEG
IncludedForECG
ExclusionReason
```

程序会记录 NoResponse，但不会自动替研究者决定是否排除参与者或 trial。排除标准必须在正式分析前冻结。

### 13.3 生理 QC

行为数据和 EEG/ECG 质量应分开判断。EEG 或 ECG 失败时，不应自动删除同一 trial 的行为数据；应在 QC 表中分别标记。

### 13.4 备份原则

每个参与者完成后至少保留：

1. Unity 日志原始副本；
2. EEG 原始文件；
3. ECG 原始文件；
4. 同步测试文件；
5. 外部评分和问卷；
6. `session.json` 副本；
7. 设备异常和实验员备注。

不要直接覆盖原始数据。分析清洗结果应写入新的 `derivatives` 或分析输出目录。

---

## 14. 建议的数据分析顺序

### 14.1 第一步：确认数据能够对齐

- 检查 Unity events.csv 的事件数量和顺序；
- 检查 EEG/ECG marker 是否与 Unity 事件对应；
- 检查 SessionStart、StimulusOnset、Response、StimulusOffset、BlockEnd；
- 标记中断、暂停、设备异常和缺失文件；
- 分别建立行为、EEG、ECG 的有效数据标记。

### 14.2 第二步：证明 Similarity 操作确实产生行为差异

先在 Non-target 中比较：

- High 是否比 Low 有更多 False Alarm；
- High 是否反应更慢；
- Medium 是否大体位于两者之间；
- 是否出现天花板或地板效应。

如果没有可解释梯度，不能直接把 L/M/H 写成心理难度等级。

### 14.3 第三步：分析 Rotation

Target 主要用于研究跨视角识别：

- 0°、90°、180°的 Hit rate；
- 反应时间是否随 RotationDelta 增加；
- 物体熟悉度是否降低旋转代价。

Non-target 可以研究 Rotation 与 Similarity 是否共同增加混淆。

### 14.4 第四步：分析 transition

以每个 boundary 为中心建立相对 trial 序列：

```text
边界前 -2、-1
边界后  0、+1、+2、+3……
```

比较：

- 上升与下降；
- 小幅变化与大幅变化；
- 真正变化与 No-op；
- 边界后的影响持续几次 trial；
- Target 和 Non-target 是否不同。

### 14.5 第五步：分析结构熟悉

使用 prior-exposure 字段研究：

- 具体物体出现次数增加后，Rotation cost 是否下降；
- 物体家族更熟悉后，High Non-target 的 False Alarm 是否减少；
- 这些趋势在控制 Global TrialIndex 和 BlockIndex 后是否仍存在。

这应称为 **within-task structural familiarization**，不应直接称为长期 N-back 训练迁移。

### 14.6 统计模型

同一个人贡献多个 trial，同一个物体也可能重复出现，因此不应把所有 trial 当作完全独立观测。

建议至少考虑：

```text
固定效应：Similarity、RotationDelta、Target/Non-target、Transition、TrialsSinceTransition、Block、Trial position
随机效应：Participant、Object 或 Family
```

反应正确与否可以使用 logistic mixed-effects model；反应时间可以在合理清洗后使用线性或广义 mixed-effects model。

EEG 分析应区分 Target/Non-target，并分别进行 stimulus-locked ERP、时频和 transition-locked 分析。ECG 以 HR、IBI 和较慢的边界前后变化为主，避免从很短窗口过度解释 HRV。

---

## 15. 当前已完成和仍未完成的内容

### 15.1 已完成

- 四零件物体生成和结构签名；
- 3/2/1/0 retained relation 配对逻辑；
- 正式题库和独立练习物体；
- 物体配对矩阵和候选覆盖检查；
- 24 份正式 Session；
- 六种 Block sequence 和参与者间顺序轮换；
- 每人 6 Block、180 scored trials、每种 transition 两次；
- RotationDelta 0°/90°/180°；
- 前两次初始化呈现固定 X=0°；
- 外层 Y 轴完整自转；
- 练习与正式反馈差异；
- PC 键盘输入和 Quest Pro A/B 输入；
- Launcher 外部启动、暂停、重启和 Block 评分；
- 每次呈现、每个事件和每个 Block 的数据日志；
- Participant/Session 分层日志目录；
- 运行前 preflight 检查；
- 运行时代码和 Editor 代码编译通过。

### 15.2 仍需要 pilot 或外部系统完成

- 用真人确认 High/Medium/Low 的感知难度梯度；
- 确认参与者确实比较 `t−2`，而不是上一个物体；
- 确认物体自转速度、大小、距离和遮挡是否易于理解；
- 在实际 Quest Link 环境中完成端到端运行测试；
- 测量 EEG/ECG marker 延迟；
- 冻结正式 EEG/ECG 采集和预处理方案；
- 冻结行为、生理和参与者排除标准；
- 确定最终 power analysis；
- 建立可靠的每日备份和 QC 流程。

### 15.3 明确不属于当前实现

- Unity 内直接读取 EEG/ECG 原始信号；
- Unity 自动把生理数据和行为数据合并成一个数据库；
- 头显内的独立评分界面；
- 独立 Quest Build 的正式部署保证；
- 每帧显卡输出级的精确显示时间；
- 自动删除减少参与者人数后多余的旧 Session 文件。

---

## 16. 已知限制和解释边界

### 16.1 Similarity × Rotation 不是严格完整的 3×3 factorial

当前程序分别平衡：

- Similarity 各 60 个；
- RotationDelta 各 60 个。

但没有强制每个 Similarity × Rotation 组合完全相等。因此分析交互时应检查实际交叉表，并在模型中处理不完全平衡。

### 16.2 Transition × Rotation 也不是每人都能覆盖所有组合

每人只有 18 个 Segment boundary，而有 9 种 transition × 3 种 Rotation 的 27 个联合组合。因此不能声称每个人在每个 boundary 都覆盖了所有旋转条件。Rotation 是 trial level 条件，transition 是 segment boundary 条件，二者的分析单位不同。

### 16.3 结构规则不是心理难度证明

High/Medium/Low 的 2/3、1/3、0/3 关系规则很明确，但人的视觉感受还会受零件显著性、遮挡、对称性和投影影响。必须做 pilot。

### 16.4 软件时间不是硬件显示时间

`UnityRealtimeSeconds` 是 Unity 软件时钟，不等于 EEG 放大器采集到的实际光学或显示帧时间。若研究需要毫秒级硬件对齐，必须使用外部 marker 和延迟测量。

### 16.5 练习反馈和正式反馈不同

练习会显示对错，正式只显示记录确认。正式 CSV 仍包含正确答案和正确性，但参与者看不到。

### 16.6 样本量边界

24 名有效参与者适合群体层面的行为和初步生理分析，不足以证明每个人都能训练出可靠的个性化自适应模型。建议先做 6–8 人 pilot，再根据真实方差和信号丢失率做 simulation-based power analysis。

---

## 17. 修改规则：以后改项目时怎么做

### 17.1 只修改参与者人数

1. 打开 `Tools > StimGen > Builder`；
2. 修改 `participantCount`；
3. 确认 `firstParticipantNumber` 和 `participantIdPrefix`；
4. 点击“② 排会话 + 运行前检查”；
5. 检查生成的 `session_Pxxx.json` 数量；
6. 注意旧的多余 JSON 不会自动删除；
7. 确认后再人工归档旧文件。

题库不变，通常不需要点击“① 建刺激库”。

### 17.2 修改零件数量或题库规则

1. 修改 `copiesPerShape` 或 bank 设置；
2. 点击“① 建刺激库”；
3. 确认新的 `stimulus_bank.json`；
4. 点击“② 排会话 + 运行前检查”；
5. 重新检查题库版本和 Session 数量；
6. 重新做 pilot，不能直接把新题库当成旧题库使用。

### 17.3 修改 Rotation 或时序

如果改变以下内容：

- RotationDelta 集合；
- 起始姿态；
- X/Y 旋转轴；
- stimulus duration；
- feedback timing；
- Segment 或 Block 结构；

必须同时：

1. 修改代码；
2. 重新生成 Session JSON；
3. 运行 preflight；
4. 更新本协议文档；
5. 重新做至少一轮 pilot；
6. 给协议版本和数据目录加上新的版本说明。

### 17.4 更换受试者编号或重新做一轮

同一 Participant 可以有多轮 Session，但每轮都必须保留独立的 SessionID 和日志目录。不要把第二轮追加到第一轮 CSV 中，也不要手动改旧日志中的 ParticipantID。

---

## 18. Pilot 通过标准

建议先完成 6–8 名 pilot，至少检查：

### 18.1 任务理解

- 参与者能说出 `t−2` 是哪一个；
- 初始化呈现不会被误认为需要回答；
- 能理解同一物体换方向仍然可能是 Same；
- 能记住当前设备对应的按键映射。

### 18.2 行为分布

- 正确率不接近 100%；
- 正确率不接近随机；
- High Non-target 具有比 Low 更高的混淆趋势或更长 RT；
- 不存在明显按键节奏策略；
- NoResponse 比例可接受；
- 反应时间没有大量异常极值。

### 18.3 视觉和 VR

- 每个物体都能在自转中看到主要空间关系；
- 没有明显固定 15°倾斜；
- X 轴 90°和 180°的差异能被理解；
- 物体大小、距离和亮度合适；
- Quest Link 不出现严重掉帧、黑屏或输入延迟；
- 参与者 VR 不适可接受。

### 18.4 数据和同步

- 每个 Session 都生成四类预期文件；
- 主 CSV 行数正确；
- events.csv 事件顺序正确；
- Block 评分写入正确；
- EEG/ECG marker 可以与 Unity 事件对齐；
- 设备失败率已经被记录并纳入最终招募数考虑。

pilot 通过后，冻结：

```text
题库版本
Session JSON
Rotation 协议
stimulus duration
输入映射
marker schema
排除标准
主要统计模型
```

---

## 19. 正式实验开始前最终检查表

### 代码和题库

- [ ] Unity 编译无 C# 错误；
- [ ] `stimulus_bank.json` 版本已记录；
- [ ] Session JSON 与当前 Rotation 协议一致；
- [ ] 前两次初始化 X 角度均为 0°；
- [ ] 没有旧版 45° Rotation 规则残留；
- [ ] 24 份正式 Session 或实际计划数量已经确认。

### 运行

- [ ] SampleScene 正确打开；
- [ ] Launcher 能读取目标 Session；
- [ ] preflight 通过；
- [ ] PC 键盘映射已确认；
- [ ] Quest B/A 已测试；
- [ ] 头显中英文提示清晰可见；
- [ ] 物体自转正常；
- [ ] 练习反馈正常；
- [ ] 正式模式不显示正确答案；
- [ ] 暂停、继续、停止和重启均测试过；
- [ ] Block 评分按钮能写入 blocks CSV。

### 数据

- [ ] `StimGenLogs` 路径已确认；
- [ ] Pxxx 文件夹结构正确；
- [ ] 主 CSV、events CSV、Session JSON 都存在；
- [ ] 备份位置已确认；
- [ ] EEG/ECG 同步测试已完成；
- [ ] participant QC 表已准备；
- [ ] 设备异常和 VR 不适记录方式已确定。

---

## 20. 最终结论

当前项目已经是一个可以在 Windows PC/Unity Editor + Quest Link 环境中进行端到端测试的视觉 2-back 实验框架。正式实验的核心题库、Session 排程、输入、反馈、暂停、Block 评分、日志和时间戳结构已经存在。

当前最重要的实验定义是：

```text
四零件三维物体
当前物体与 t−2 比较
Similarity：Segment level
RotationDelta：X轴 0° / 90° / 180°
初始化起始 X 角度：0°
观看动画：Y轴自转 1 圈
正式 Session：6 × 30 scored trials
```

当前日志可以支持：

- 正确率、Hit、False Alarm、RT；
- Similarity 主效应；
- Rotation 主效应；
- Similarity × Rotation 的探索性分析；
- transition 前后行为变化；
- 物体和家族熟悉度变化；
- 与外部 EEG/ECG 时间轴的后续对齐。

正式招募前真正不可省略的工作不是再增加更多按钮，而是完成真人 pilot、VR Link 稳定性测试、外部 EEG/ECG marker 同步测试、数据 QC 和排除标准冻结。

---

## 21. 关键文件索引

- [正式题库](../Assets/StimulusSets/stimulus_bank.json)
- [P001 Session](../Assets/StimulusSets/session_P001.json)
- [练习 Session](../Assets/StimulusSets/practice_session.json)
- [运行器](../Assets/Scripts/StimGen/Runtime/ExperimentRunner.cs)
- [Session 生成器](../Assets/Scripts/StimGen/Runtime/TrialGenerator.cs)
- [实验设计常量](../Assets/Scripts/StimGen/Runtime/ExperimentDesign.cs)
- [旋转控制器](../Assets/Scripts/StimGen/Runtime/RotationController.cs)
- [日志实现](../Assets/Scripts/StimGen/Runtime/ExperimentLogger.cs)
- [练习生成器](../Assets/Scripts/StimGen/Runtime/PracticeSessionFactory.cs)
- [Builder](../Assets/Scripts/StimGen/Editor/StimulusSetBuilder.cs)
- [Launcher](../Assets/Scripts/StimGen/Editor/ExperimentLauncher.cs)
- [补充协议](../Assets/StimulusSets/PROTOCOL_ADDENDUM_20260826.md)
