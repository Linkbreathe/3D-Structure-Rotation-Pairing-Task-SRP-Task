# StimGen Pairing 版实验协议（2026-08-30）

本文件是当前 Unity 实现的权威协议说明。它取代早期的 VR 2-back 版本；旧文档仍保留作历史记录，不代表当前运行配置。

## 1. 核心任务

每个 trial 都是一个完整、独立的 Pairing trial：

```text
Reference A -> Comparison B -> Same / Different
```

参与者不需要记住 `t-2`，程序也不维护 2-back memory chain 或 `ChainID`。因此任务的主要认知变量是 3D structural discrimination 和 mental rotation，而不是 working memory load。

- Target：`A ObjectID == B ObjectID`，答案为 Same。
- Non-target：`A ObjectID != B ObjectID`，答案为 Different。
- Non-target 的结构关系由冻结的 StimulusBank pairing matrix 决定。

## 2. 受控条件

### Structural similarity

`SimilarityLevel` 表示 Reference A 与 Comparison B 之间保留的空间关系数量，不直接等同于经人类验证的 difficulty：

| 条件 | 保留关系 | 任务答案 |
|---|---:|---|
| Target | 同一 Object，3/3 | Same |
| High | 2/3 | Different |
| Medium | 1/3 | Different |
| Low | 0/3 | Different |

Similarity 在 segment 层面固定，四个 segment 的长度为 `8 + 7 + 8 + 7 = 30`。

### Rotation

每个 pair 独立记录 `RotationDeltaX`，只取 `0° / 90° / 180°`。当前实现让 A 使用基准 X 角度，B 使用 `A + RotationDeltaX`（按 360°取模），所以角度是 pair 内的相对条件，不依赖前一个或 t-2 trial。

所有呈现都可以同时使用外层 Y 轴自转；Y animation 是观察方式，不是 X 轴实验条件。Y 自转对 Rotation 操纵的影响必须在 pilot 中重新检查。

## 3. Session 结构

- 每位参与者 6 个 block。
- 每个 block 4 个 segment，长度 `8 / 7 / 8 / 7`。
- 每个 block 30 个完整 Pairing trial，全部 `scored=true`。
- 每个 block 10 个 Target、20 个 Non-target。
- 每位参与者共 180 个 scored trials。
- 不再有 block 开头的 initialization trials；不再固定 segment 开头的 Non-target 来重建记忆链。
- 保留现有 6 种 block sequence 和参与者间 block order counterbalancing。

四个 segment 之间继续形成 3 个 boundary。每位参与者共有 18 个 boundary；9 种 transition（L/M/H 的有向组合）各出现两次，其中 6 个是 no-op，12 个是 level change。

## 4. 当前协议版本与主要数据字段

- `TaskProtocolVersion`: `Pairing_Similarity_Transition_v1`
- `RotationProtocolVersion`: `Pair_XDelta_0_90_180_YSpin_v1`
- 主记录一行代表一个完整 Pairing trial，而不是一次单物体 presentation。
- 关键字段包括 `ReferenceObjectID`、`ComparisonObjectID`、两端 family/signature、`SegmentSimilarity`、`SimilarityTransition`、`RotationDeltaX`、两端 onset/offset 时间戳和 response。
- 运行时 session 必须同时嵌入 A、B 两端用到的 ObjectDefinition。
- EEG/ECG 仍通过 `IMarkerSink` 和事件 CSV 对齐；当前接口支持不等于真实设备同步已经完成人体验证。

## 5. 运行时顺序

当前场景默认值为：Reference A 2 s、Comparison B 3 s、ITI 0.4 s。一个 trial 的逻辑顺序为：

```text
Fixation
  -> Reference A onset / presentation / offset
  -> Comparison B onset / response window / offset
  -> feedback（按 practice/formal 设置）
  -> ITI
```

回答只在 B 出现后接受。正式实验不显示逐题正确性；练习模式可以显示 Correct/Incorrect。超时记录为 NoResponse，序列继续推进。

## 6. 研究问题与数据阶段

### Study 1: Characterization

先用固定的 Similarity × Rotation 条件采集 Accuracy、RT，以及条件允许时的 Eye、EEG/ECG，回答：

1. Similarity 和 Rotation 如何影响 3D structure discrimination？
2. demand transition 后，performance/state 在前 1–3 个 trial 如何恢复或恶化？
3. 个体是否具有不同的 adaptation speed 或 difficulty threshold？

当前生成器只提供可复现的条件、transition 和日志结构；ceiling/floor、High 的 false alarm、Rotation cost、transition recovery 和 Y-spin 干扰都必须通过 pilot/正式数据验收，不能由代码预检替代。

### Study 2: Adaptive XR

Study 1 建立 user-state model 后，再比较：

- Fixed；
- performance-only Adaptive；
- transition-aware Adaptive。

模型不需要预测系统已经知道的 Low/Medium/High 标签，而应根据最近 trial 的 Accuracy、RT、Eye、EEG/ECG 与 transition history，预测 stable / adapting / struggling 或 future error probability。系统随后控制 Similarity 与 Rotation。

## 7. Pilot 验收重点

- High 的 false alarm 是否高于 Low，并且条件差异可解释；
- Rotation 是否产生稳定的 RT/accuracy cost；
- 是否出现明显 ceiling 或 floor；
- boundary 后前 1–3 个 trial 是否存在可测变化；
- A/B 呈现和单 trial 时长是否足以避免不必要疲劳；
- Y-axis 自转是否削弱或混淆 X-axis Rotation 操纵；
- PC/Quest Link、输入、marker、日志和 EEG/ECG 对齐是否完成端到端验证。

## 8. Unity 操作

1. 打开项目并等待编译完成。
2. 使用 `Tools > StimGen > Builder` 检查 bank；只有改变物体规则、视觉规则或 bank 规则时才重建 bank。
3. 只改变参与者数量或 session 排程时，使用 `Tools > StimGen > Regenerate 24 Sessions From Existing Bank`。
4. 运行前通过 Launcher 的 session preflight；失败时不得进入实验。
5. 练习 session 是独立的 Pairing 试次，不包含初始化呈现。

正式 session 由 `session_P001.json` 至 `session_P024.json` 提供；生成后的文件必须满足上述 6 × 30、transition、similarity、rotation 和 A/B 完整性检查。代码/结构检查通过不等于 VR 人体 pilot 已通过。
