# StimGen 说明文档

> **当前协议提示（2026-08-30）：** 本项目已从 2-back 改为独立 Pairing：`Reference A -> Comparison B -> Same / Different`。
> 当前协议、字段和 pilot 边界请以 [`Docs/VR_Pairing_Similarity_Transition_Protocol_20260830.md`](Docs/VR_Pairing_Similarity_Transition_Protocol_20260830.md) 为准。
> 本文件中仍出现的 2-back、t-2、ChainID、初始化呈现和两条记忆链描述属于历史版本，暂不作为当前运行说明。

对应方案：**VR 2-back 三维结构相似度与状态转移实验计划（四零件修订版）**

这份文档解释这个程序在做什么、为什么这么做、每个设置是干嘛的、你该怎么操作。
只想赶紧跑起来，跳到第 8 节。

---

## 1. 一句话概括

> 生成 4 零件三维物体的刺激库，算出全部物体两两之间的结构关系，
> 据此为每个参与者排出 6 个 block 的 2-back 序列，播放并记录行为与事件标记。

三个阶段，必须按顺序：

```
【建库】Unity 编辑器里跑一次
   生成家族 → 逐层检查 → 算全物体配对矩阵 → 冻结成 bank.json

【排程】每个参与者一次
   用配对矩阵排出 6 blocks × 32 呈现 → 运行前检查 → session_Pxxx.json

【运行】被试面前跑
   读 session JSON → 注视点/刺激/空白 → 收按键 → 写 CSV + 事件标记
```

**运行期不做任何随机，也不根据被试表现改变刺激。** 所有序列在被试戴上设备之前
就已经生成并通过检查。

---

## 2. 实验结构：三级

```
Experiment
  └── Block（6 个，每个结束后休息 + 心理努力评分）
        └── Segment（4 个，结构相似度在这一层固定）
              └── Trial（旋转幅度在这一层变化）
```

| 层级 | 数量 | 说明 |
|---|---|---|
| Block | 6 / 人 | 30 scored trials + 2 个不计分的初始化呈现 = 32 次呈现 |
| Segment | 4 / block | 长度 **8、7、8、7**，合计 30 |
| Boundary | 3 / block | 共 18 个 / 人：**12 个真改变 + 6 个 No-op** |
| Trial | 180 / 人 | 每 block 约 10 Target + 20 Non-target |

**segment 边界不暂停、不提示音、不显示文字**，被试不知道边界在哪。

### 为什么是 4 个长 segment 而不是 6 个短的

5 个 trial 只有约 16 秒，其中约 1/3 是 Target，真正体现相似度的 Non-target 只有
3–4 个，谈不上形成了一个稳定状态。而且每个 segment 开头要占用 2 个位置强制设为
Non-target，6 个 segment 就要占 12 个，而整个 block 才 20 个 Non-target——
剩余位置的 Target 概率会异常升高，被试可能学会预测。

---

## 3. 六种 block sequence

L / M / H = Low / Medium / High structural similarity。

| Sequence | Seg1 | Seg2 | Seg3 | Seg4 | 三个边界 |
|---|---|---|---|---|---|
| A | L | L | M | H | **L→L**、L→M、M→H |
| B | L | H | H | M | L→H、**H→H**、H→M |
| C | M | M | L | H | **M→M**、M→L、L→H |
| D | M | H | H | L | M→H、**H→H**、H→L |
| E | H | L | M | M | H→L、L→M、**M→M** |
| F | H | M | L | L | H→M、M→L、**L→L** |

粗体是 No-op（相似度不变，但同样走完边界规则）。**已用程序验证过的平衡性质**：

- L / M / H 各占 **60 个 scored trials**（8+7+8+8+7+7+8+7 = 60，三个等级都精确对上）
- 六种有方向的改变各 2 次，三种 No-op 各 2 次
- 每个 block 都包含 L、M、H
- 每个 condition 在四个 segment 位置出现次数相同（各 2 次）

参与者之间用**循环拉丁方**轮换 block 顺序（`ExperimentDesign.BlockOrderFor`），
编号偶数正序、奇数逆序。Same/Different 的左右手映射也按编号奇偶平衡。

> ⚠️ **一个已知的设计缺口**：H→H 这个 No-op 在六种 sequence 里**只出现在第二个边界**
> （B 和 D），而 L→L 和 M→M 出现在第一和第三个边界。三种 No-op × 三个边界位置需要
> 9 种组合，6 种 sequence 装不下。所以 "H→H 的效应" 和 "第二个边界位置的效应"
> 在这套设计里是混淆的。分析时不要单独解释 H→H，或者把边界位置作为协变量。

---

## 4. 物体与相似度

### 4.1 每个物体

固定 4 个同色零件，各一个：**方块 + 圆柱 + 胶囊 + 椭球**。

所有物体满足：零件种类数量相同、体积严格相等（都是 0.30）、同一种无纹理低反光材质、
全部连接、无悬空/严重重叠/完全遮挡、整体尺寸与中心点统一、排除高度对称与共面。

因为所有物体都用完全相同的四个零件，被试**不能靠"有没有圆柱"判断**，
必须判断圆柱连在哪里、胶囊在哪个方向、四个零件的整体关系。

### 4.2 空间关系 = 相似度的基础

4 个零件树状连接 → **3 条空间关系**。一条关系包含两件事：

```
哪两个零件相连  +  子零件位于父零件的哪个方向
```

例如 `Cube>Cylinder@YPlus`（圆柱在方块上方）。如果"方块仍然连着圆柱，
但圆柱从上方移到后方"，这条关系**算作改变了**。

> **关键实现细节**：关系是**与树根无关**的。同一个空间摆法，用方块当根搭
> （"圆柱在方块上方"）和用圆柱当根搭（"方块在圆柱下方"）必须算同一条关系，
> 否则跨物体比较会得出荒唐结果。程序把形状对按枚举序排好，交换顺序时同时把
> 方向取反。**已用程序验证**：把 20 个物体分别以每个零件为根重新表达共 80 次，
> 关系集合始终一致。

| Pair 类型 | 生成规则 | 保留的空间关系 |
|---|---|---|
| Target | 完全相同的 Object ID，只改呈现角度 | 3/3 |
| High Non-target | 改 1 条 | 2/3 |
| Medium Non-target | 改 2 条 | 1/3 |
| Low Non-target | 3 条全改或重建主连接顺序 | 0/3 |

一次"改变"可以是：把一个零件（连同它的分支）取下接到别处，或者保持连接对象不变
但换一个合法方向。**程序绝不增加、删除或替换零件。**

### 4.3 为什么必须算"全物体两两配对矩阵"

这是这一版和之前最大的架构差别。

Base / High / Medium / Low 只是**生成一个家族时的起始结构**。但 2-back 里
**任何当前物体在两个 trial 之后都会成为新的参照**：

```
Trial 2: 物体 B（相对 A 是 High）
Trial 4: 物体 C（相对 B 是 High）  ← 这里参照是 B，不是 A
```

所以只有"基准→变体"这一层关系是不够的。物体库冻结前必须计算**所有正式物体
两两之间**的关系，建成矩阵，Trial Generator 只能从矩阵里挑候选。

矩阵每一格是 `Target / High / Medium / Low / Invalid`，判定要过两道关：

1. **程序关卡**：保留关系数必须正好是 2 / 1 / 0
2. **视觉关卡**：0°/45°/90° 三个角度的轮廓重合度都落在该等级区间，且三个角度一致

还有一类特殊的 Invalid：**关系完全相同（3/3）但不是同一个 Object ID**——
结构上是同一个东西，零件朝向不同所以看起来不一样。它既不能当 Target
（ID 不同）也不该当 Non-target，直接排除。

### 4.4 覆盖度：每个物体每级至少 2 个候选

如果某个物体在 High 下没有候选，它一旦出现在会继续成为参照的位置就会卡死。
所以建库最后有一个**自动补充**环节：对候选不足的 (物体, 等级) 组合直接派生新变体
加入库中，新物体同时也会成为别人的候选。

**实测（24 家族 × 4 = 96 个初始物体）**：

```
配对矩阵：合法配对 4551 对
覆盖度补充：新增 35 个物体 → 正式物体 131 个（另有练习物体 20 个）

High  ：平均 3.6 个候选，最少 2，最多 9
Medium：平均 26.0，最少 13，最多 40
Low   ：平均 100.2，最少 85，最多 113
```

> ⚠️ **High 是唯一紧张的等级**。两个随机 4 零件物体恰好共享 2 条关系的概率很低，
> 所以 High 候选几乎全靠家族内部派生和自动补充撑起来。如果你提高 `formalFamilies`
> 或 `variantsPerLevel`，最该关注的就是 High 那一行。

---

## 5. Rotation

RotationDelta 是**当前物体与 t−2 物体的方向差**，不是相对世界坐标的绝对角度：
0° 相同、45°、90°。只旋转整个 Root，第一版只绕统一的垂直轴。

**已验证**：每人 0°/45°/90° 各恰好 60 个 scored trial。
角度在 Target 组和 Non-target 组内部分别均衡；三个边界后的第一个 trial 按 block
轮换角度，让六种 transition 都能覆盖到不同角度。

> ⚠️ 每种有方向的 transition 每人只出现 2 次，所以**单个参与者层面**不可能覆盖全
> 3 种角度。这个平衡只在群体层面成立。

---

## 6. 一个 trial 的时序

```
注视点 0.5s
  ↓  StimulusOnset 事件标记
物体 2.5s ── 被试按 Same / Different
  ↓  即使提前作答也显示满 2.5s，保证每个人观察时间相同
  ↓  超时记为 Timeout，不当作错误按键，序列照常推进
物体消失 → 空白 0.3–0.5s
  ↓  写日志，进入下一个 trial
```

一个 trial 约 3.3–3.5 秒，一个 segment（7–8 trials）约 23–28 秒。
**正式实验不给对错反馈**，只有练习阶段提供。

注视点出现、物体出现、按键、物体消失分别发送事件标记（`IMarkerSink` 接口），
以便与 EEG / ECG 对齐。

---

## 7. 代码结构

| 模块 | 文件 | 职责 |
|---|---|---|
| Part Library | `PartLibrary.cs` | 4 种零件的 mesh、6 个 socket 方向、统一材质。胶囊是程序化生成的（内置胶囊非均匀缩放会把两头压扁） |
| Object Generator | `ObjectGenerator.cs` + `ObjectLayout.cs` + `ShapeMetrics.cs` + `ShapeSdf.cs` | 按 seed 拼装 4 零件组合；关系→坐标；等体积尺寸；SDF 重叠判定 |
| Variant Generator | `VariantGenerator.cs` | 按 2/3、1/3、0/3 生成版本 |
| Object Validator | `ObjectValidator.cs`（几何）+ `SilhouetteAnalyzer.cs`（轮廓/遮挡） | 连接、重叠、遮挡、对称、共面、尺寸、多视角轮廓 |
| Stimulus Bank | `StimulusBank.cs` + `StimulusBankBuilder.cs` | 家族、模型、Seed、**配对矩阵**、覆盖度补充与报告 |
| Block/Segment Scheduler | `ExperimentDesign.cs` | 六种 sequence、segment 长度、参与者间轮换 |
| 2-back Trial Generator | `TrialGenerator.cs` | 两条链、Target 比例、边界 Non-target、重复限制、曝光均衡 |
| Rotation Controller | `RotationController.cs` | 只旋转 Root |
| Experiment Logger | `ExperimentLogger.cs` | CSV + block 汇总 + session 副本 |
| Preflight Validator | `PreflightValidator.cs` | 运行前拦下任何不平衡或不合法的序列 |
| 运行 | `ExperimentRunner.cs` | 注视点/刺激/空白时序、按键、事件标记 |
| 工具窗口 | `Editor/StimulusSetBuilder.cs` | `Tools ▸ StimGen ▸ Builder` |

数据结构在 `StimTypes.cs`（物体、关系、配对类型）和 `TrialTypes.cs`
（呈现记录、block、session）。

---

## 8. 操作流程

### 第 1 步：打开项目
打开 Unity，等编译完，Console 无红色报错。
菜单 `Tools ▸ StimGen ▸ Builder`。窗口顶部会显示实验设计常量和当前物体构成。

### 第 2 步：材质
确认「零件颜色」为白色，点「创建 / 刷新零件材质」→ `Assets/Materials/StimulusPart.mat`。
把场景里手搭的 `similar-levelN` 物体禁用或删掉。

### 第 3 步：肉眼验收拼装 ★
点「在场景中预览 12 个样例」，Scene 视图里转着看。核对：4 个零件、四种形状各一个、
全部连接、不穿模、不共面。觉得发白糊成一片就降 Directional Light 强度；
觉得接缝太松/太紧改 `ShapeMetrics.ContactOverlap`。

### 第 4 步：先跑纯几何建库
关掉「执行轮廓/遮挡检查」，点 **① 建刺激库**。不到 1 秒跑完。
看状态栏的**配对覆盖度**报告，确认三个等级都没有"不足 2 个候选的物体"。
这一步只确认管线通，别看结论。

### 第 5 步：打开视觉检查，校准 IoU 阈值 ★ 唯一需要判断的一步
勾上「执行轮廓/遮挡检查」，再点 **① 建刺激库**。这次慢很多。
重点看**配对覆盖度**和"轮廓不符"的淘汰数：

| 情况 | 怎么办 |
|---|---|
| 三个等级覆盖度都够 | ✅ 进第 6 步 |
| High 覆盖度掉到 0–1 | 把「High IoU ≥」往下调（0.80 → 0.75） |
| Low 覆盖度掉很多 | 把「Low IoU ≤」往上调（0.50 → 0.60） |
| 各等级都掉，提示三角度不一致 | 把「三角度最大跨度」往上调 |

### 第 6 步：排会话
设好「起始参与者编号」和「生成几个参与者」，点 **② 排会话 + 运行前检查**。
每人一个 `session_Pxxx.json`。状态栏会逐人报告是否通过运行前检查——
**任何一个没通过都不能拿去跑实验**。

### 第 7 步：跑一个 block 验收数据
场景里新建空 GameObject，挂 `ExperimentRunner`，把 `session_P001.json` 拖进
`Session Json`，指定 `Fixation Visual`（一个十字或小球），
**把 Main Camera 从 z = −10 移到 z ≈ −4**。
进 Play，右键组件标题 → `Run First Block Only`。**F = Different，J = Same**。

打开 CSV 验收：

- [ ] 32 行（一个 block）
- [ ] 前 2 行 `Scored = 0`
- [ ] `TrialPairType = Target` 的有 10 行
- [ ] 每个 segment 的前两个 trial 都是 Non-target
- [ ] `RetainedRelations`：Target = 3，HighNT = 2，MediumNT = 1，LowNT = 0
- [ ] `RotationDelta` 三种各约 10 个
- [ ] `IsFirstTrialAfterBoundary = 1` 的有 3 行
- [ ] `ReactionTimeMs` 有数字，超时行 `Timeout = 1` 且 `Correct` 不为 1

---

## 9. 产出文件

| 文件 | 位置 | 内容 |
|---|---|---|
| `StimulusPart.mat` | `Assets/Materials/` | 共用材质 |
| `stimulus_bank.json` | `Assets/StimulusSets/` | 冻结的刺激库：全部物体 + 家族 + 配对矩阵 |
| `session_Pxxx.json` | `Assets/StimulusSets/` | 一个参与者的完整序列，自包含（含用到的物体定义） |
| `Pxxx_<时间>.csv` | `%USERPROFILE%\AppData\LocalLow\<公司>\<项目>\StimGenLogs\` | 一行一次呈现 |
| `Pxxx_<时间>_blocks.csv` | 同上 | 每 block 的心理努力评分、休息时长、备注 |
| `Pxxx_<时间>_session.json` | 同上 | 本次实际使用的序列副本 |

CSV 的列直接对应方案第 11 节的数据结构：位置（Block/Segment/Trial 索引、ChainID）、
similarity 与 transition（前后 similarity、transition 标签、IsNoOpBoundary、
TrialsSinceTransition）、刺激（Object/Family/PartSet ID、Seed、关系签名、
TrialPairType、RetainedRelations、StructuralDistance）、朝向、答案与四分类结果
（Hit/Miss/FalseAlarm/CorrectRejection/NoResponse）、以及全部时间戳。

---

## 10. 想改东西时改哪里

| 想改什么 | 改哪里 |
|---|---|
| segment 长度、block 数、Target 数、六种 sequence | `ExperimentDesign.cs`（**Pilot 后只允许改 segment 长度**） |
| 家族数、每级变体数、覆盖度要求 | Builder 窗口「① 刺激库」 |
| 几何与视觉合格标准 | Builder 窗口，每项都有悬停说明 |
| Target 连续限制、重复窗口、家族冷却 | Builder 窗口「② 会话排程」 |
| 零件体积、咬合深度、各形状长径比 | `ShapeMetrics.cs` |
| 用哪几种形状 | `StimTypes.cs` → `StimConfig.ShapesInUse` |
| 注视点/呈现/空白时长、按键 | `ExperimentRunner` 组件 Inspector |
| EEG/ECG 事件标记接入 | 实现 `IMarkerSink`，在 `ExperimentRunner.SetMarkerSink` 注入 |

---

## 11. 当前状态与待办

### 已实测通过

```
建库（24 家族，跳过视觉检查）：
  正式物体 96 → 覆盖度补充 +35 → 131 个；练习物体 20 个
  配对矩阵 4551 对合法配对
  三个等级覆盖度全部达标（High 最少 2，Medium 最少 13，Low 最少 85）
  耗时 0.4 秒

关系定义的树根无关性：80 次重新定根，关系集合始终一致
家族内部关系：107 对全部落在 2/1/0 上，零件构成不变

排会话：30 个参与者全部通过运行前检查
  每人 180 scored trials，L/M/H 各 60，0°/45°/90° 各 60
  Target 60，Non-target 120，18 个边界（12 真 + 6 No-op）
```

### 尚未实跑

- **轮廓 / 遮挡检查**（`SilhouetteAnalyzer`）需要 Unity 编辑器渲染，只做过编译验证。
  第 5 步就是为了校准它，IoU 阈值大概率要按实测分布调整。
- **VR 呈现**：`ExperimentRunner` 目前是桌面键盘版。VR 手柄按键、头显中的
  固定视距与视觉大小、注视点在 VR 中的呈现方式都还没做。
- **EEG / ECG 接入**：`IMarkerSink` 接口和 CSV 列已就位，但只有一个写 Console 的
  占位实现，没有接真实采集系统。
- **练习流程**：练习物体已单独生成并与正式库隔离，但"1–2 个短练习 + 对错反馈 +
  理解确认"的流程还没写。
- **Block 后评分**：`LogBlockSummary` 已就位，但采集心理努力 1–7 分的 UI 还没做。
