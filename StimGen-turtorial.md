# StimGen Documentation

> **Current protocol note (2026-08-30):** This project has changed from 2-back to independent Pairing: `Reference A -> Comparison B -> Same / Different`.
> For the current protocol, fields, and pilot boundary, refer to [`Docs/VR_Pairing_Similarity_Transition_Protocol_20260830.md`](Docs/VR_Pairing_Similarity_Transition_Protocol_20260830.md).
> The 2-back, t-2, ChainID, initialization-presentation, and two-memory-chain descriptions that still appear in this file belong to the historical version and are not current runtime instructions for now.

Corresponding plan: **VR 2-back 3D Structural Similarity and State-Transition Experiment Plan (Four-Part Revision)**

This document explains what this program does, why it does it, what each setting is for, and how you should operate it.
If you just want to get it running quickly, jump to Section 8.

---

## 1. One-sentence overview

> Generate a stimulus bank of 4-part 3D objects, calculate the structural relationships between every pair of objects,
> and use this to schedule a 6-block 2-back sequence for each participant, then play it back and record behavior and event markers.

Three stages, which must be performed in order:

```
[Build the bank] Run once in the Unity Editor
   Generate families → inspect layer by layer → calculate the full object-pair matrix → freeze it as bank.json

[Schedule] Once per participant
   Use the pair matrix to schedule 6 blocks × 32 presentations → run preflight checks → session_Pxxx.json

[Run] Run in front of the participant
   Read the session JSON → fixation/stimulus/blank interval → collect key presses → write CSV + event markers
```

**The runtime performs no randomization and does not change stimuli based on participant performance.** All sequences are generated and checked
before the participant puts on the device.

---

## 2. Experimental structure: three levels

```
Experiment
  └── Block (6 per participant; rest + mental-effort rating after each)
        └── Segment (4; structural similarity is fixed at this level)
              └── Trial (rotation magnitude varies at this level)
```

| Level | Count | Description |
|---|---|---|
| Block | 6 / participant | 30 scored trials + 2 unscored initialization presentations = 32 presentations |
| Segment | 4 / block | Length **8, 7, 8, 7**, total 30 |
| Boundary | 3 / block | 18 per participant: **12 real changes + 6 No-op** |
| Trial | 180 / participant | Approximately 10 Target + 20 Non-target per block |

**Segment boundaries do not pause, play a tone, or display text**, and the participant does not know where the boundaries are.

### Why 4 long segments instead of 6 short ones

Five trials last only about 16 seconds; approximately one third of that is Target, leaving only
3–4 Non-targets that genuinely reflect similarity, which is not enough to form a stable state. In addition, the first 2 positions of each segment must be forced to
Non-target, so 6 segments would require 12 of them, while the whole block has only 20 Non-targets—
the Target probability in the remaining positions would become abnormally high, and the participant might learn to predict it.

---

## 3. Six block sequences

L / M / H = Low / Medium / High structural similarity.

| Sequence | Seg1 | Seg2 | Seg3 | Seg4 | Three boundaries |
|---|---|---|---|---|---|
| A | L | L | M | H | **L→L**, L→M, M→H |
| B | L | H | H | M | L→H, **H→H**, H→M |
| C | M | M | L | H | **M→M**, M→L, L→H |
| D | M | H | H | L | M→H, **H→H**, H→L |
| E | H | L | M | M | H→L, L→M, **M→M** |
| F | H | M | L | L | H→M, M→L, **L→L** |

Bold is No-op (similarity does not change, but the boundary rules are still completed). **The verified balancing properties are**:

- L / M / H each account for **60 scored trials** (8+7+8+8+7+7+8+7 = 60; all three levels match exactly)
- Each of the six directional changes occurs twice, and each of the three No-ops occurs twice
- Every block contains L, M, and H
- Each condition appears equally often in each of the four segment positions (2 times each)

Across participants, the block order is rotated using a **cyclic Latin square** (`ExperimentDesign.BlockOrderFor`);
even-numbered IDs use forward order and odd-numbered IDs use reverse order. The left/right hand mapping for Same/Different is also balanced by ID parity.

> ⚠️ **A known design gap**: The H→H No-op appears **only at the second boundary** in the six sequences
> (B and D), while L→L and M→M appear at the first and third boundaries. The 3 No-op types × 3 boundary positions require
> 9 combinations, which cannot fit into 6 sequences. Therefore, the “effect of H→H” and the “effect of the second-boundary position”
> are confounded in this design. Do not interpret H→H separately in analysis, or include boundary position as a covariate.

---

## 4. Objects and similarity

### 4.1 Each object

Four fixed same-color parts, one of each: **cube + cylinder + capsule + ellipsoid**.

All objects satisfy: the number of part types is the same, volumes are exactly equal (all 0.30), the same textureless low-reflectance material is used,
all parts are connected, with no floating parts/severe overlap/complete occlusion, unified overall size and center point, and exclusion of high symmetry and coplanarity.

Because all objects use exactly the same four parts, the participant **cannot decide based on “whether there is a cylinder”**;
they must judge what the cylinder is connected to, the direction of the capsule, and the overall relationship among the four parts.

### 4.2 Spatial relations = the basis of similarity

Four parts connected as a tree → **3 spatial relations**. A relation contains two things:

```
Which two parts are connected  +  the direction in which the child part lies relative to the parent part
```

For example, `Cube>Cylinder@YPlus` (the cylinder is above the cube). If “the cube is still connected to the cylinder,
but the cylinder moves from above to behind,” this relation **counts as changed**.

> **Key implementation detail**: Relations are **independent of the tree root**. The same spatial arrangement, constructed with the cube as the root
> (“the cylinder is above the cube”) and with the cylinder as the root (“the cube is below the cylinder”), must count as the same relation;
> otherwise cross-object comparisons would produce absurd results. The program sorts the shape pair by enumeration order, and simultaneously reverses the direction when swapping the order. **Verified by the program**:
> The 20 objects were each re-expressed with every part as the root, for a total of 80 times,
> and the relation set was always identical.

| Pair type | Generation rule | Retained spatial relations |
|---|---|---|
| Target | Exactly the same Object ID; only change the presentation angle | 3/3 |
| High Non-target | Change 1 relation | 2/3 |
| Medium Non-target | Change 2 relations | 1/3 |
| Low Non-target | Change all 3 relations or rebuild the main connection order | 0/3 |

One “change” may be: remove a part (together with its branch) and attach it elsewhere, or keep the connected objects unchanged
but switch to another legal direction. **The program never adds, removes, or replaces parts.**

### 4.3 Why the full pairwise matrix for all objects is required

This is the biggest architectural difference between this version and the previous one.

Base / High / Medium / Low are only **starting structures when generating a family**. But in 2-back,
**any current object becomes the new reference after two trials**:

```
Trial 2: Object B (High relative to A)
Trial 4: Object C (High relative to B)  ← the reference here is B, not A
```

Therefore, the “baseline → variant” level of relationships alone is not enough. Before freezing the object bank, the relationships between **every pair of formal objects**
must be calculated into a matrix, and the Trial Generator can only select candidates from that matrix.

Each matrix cell is `Target / High / Medium / Low / Invalid`; classification must pass two gates:

1. **Program gate**: the number of retained relations must be exactly 2 / 1 / 0
2. **Visual gate**: the silhouette overlap at 0°/45°/90° must fall within the level’s interval at all three angles, and the three angles must be consistent

There is also a special type of Invalid: **the relations are exactly the same (3/3), but the Object IDs are different**—
structurally it is the same thing, but it looks different because the part orientations differ. It can be neither a Target
(ID is different) nor a Non-target, so it is excluded directly.

### 4.4 Coverage: at least 2 candidates for each object at each level

If an object has no High candidates, it will get stuck as soon as it appears in a position where it continues to serve as the reference.
Therefore, the final bank-building stage has an **automatic supplementation** step: for any (object, level) combination with too few candidates, directly derive new variants
and add them to the bank; each new object also becomes a candidate for others.

**Measured (24 families × 4 = 96 initial objects)**:

```
Pair matrix: 4551 valid pairs
Coverage supplementation: +35 new objects → 131 formal objects (plus 20 practice objects)

High  : 3.6 candidates on average, minimum 2, maximum 9
Medium: 26.0 on average, minimum 13, maximum 40
Low   : 100.2 on average, minimum 85, maximum 113
```

> ⚠️ **High is the only tight level**. The probability that two random 4-part objects happen to share 2 relations is low,
> so High candidates rely almost entirely on within-family derivation and automatic supplementation. If you increase `formalFamilies`
> or `variantsPerLevel`, the High row is the one that deserves the closest attention.

---

## 5. Rotation

RotationDelta is the **directional difference between the current object and the t−2 object**, not the absolute angle relative to world coordinates:
0° means identical, then 45° and 90°. Only the entire Root is rotated; the first version rotates around a unified vertical axis.

**Verified**: Each participant has exactly 60 scored trials at each of 0°/45°/90°.
Angles are balanced separately within Target and Non-target groups; the angle of the first trial after each of the three boundaries is rotated across blocks,
so that all six transition types can be covered at different angles.

> ⚠️ Each directional transition appears only twice per participant, so it is **impossible to cover all
> 3 angles at the individual-participant level**. This balance holds only at the group level.

---

## 6. Timing of one trial

```
Fixation point 0.5s
  ↓  StimulusOnset event marker
Object 2.5s ── participant presses Same / Different
  ↓  Even if the participant responds early, the full 2.5s is still displayed, ensuring the same observation time for everyone
  ↓  Timeout is recorded as Timeout and is not treated as an incorrect key press; the sequence proceeds normally
Object disappears → blank 0.3–0.5s
  ↓  Write the log and proceed to the next trial
```

One trial lasts approximately 3.3–3.5 seconds, and one segment (7–8 trials) lasts approximately 23–28 seconds.
**The formal experiment provides no correctness feedback**; feedback is provided only during practice.

The appearance of the fixation point, object appearance, key press, and object disappearance each send an event marker (`IMarkerSink`)
so that they can be aligned with EEG / ECG.

---

## 7. Code structure

| Module | File | Responsibility |
|---|---|---|
| Part Library | `PartLibrary.cs` | Meshes for 4 part types, 6 socket directions, and a unified material. The capsule is generated procedurally (the built-in capsule’s two ends are flattened by non-uniform scaling) |
| Object Generator | `ObjectGenerator.cs` + `ObjectLayout.cs` + `ShapeMetrics.cs` + `ShapeSdf.cs` | Assemble 4-part combinations from a seed; relations → coordinates; equal-volume dimensions; SDF overlap check |
| Variant Generator | `VariantGenerator.cs` | Generate variants with 2/3, 1/3, and 0/3 relations |
| Object Validator | `ObjectValidator.cs` (geometry) + `SilhouetteAnalyzer.cs` (silhouette/occlusion) | Connectivity, overlap, occlusion, symmetry, coplanarity, dimensions, and multi-view silhouette |
| Stimulus Bank | `StimulusBank.cs` + `StimulusBankBuilder.cs` | Families, models, seeds, **pair matrix**, coverage supplementation and report |
| Block/Segment Scheduler | `ExperimentDesign.cs` | Six sequences, segment lengths, rotation across participants |
| 2-back Trial Generator | `TrialGenerator.cs` | Two chains, Target ratio, boundary Non-targets, repetition limits, exposure balancing |
| Rotation Controller | `RotationController.cs` | Rotate only the Root |
| Experiment Logger | `ExperimentLogger.cs` | CSV + block summary + session copy |
| Preflight Validator | `PreflightValidator.cs` | Stop any unbalanced or invalid sequence before running |
| Runtime | `ExperimentRunner.cs` | Fixation/stimulus/blank timing, key presses, event markers |
| Tool window | `Editor/StimulusSetBuilder.cs` | `Tools ▸ StimGen ▸ Builder` |

Data structures are in `StimTypes.cs` (objects, relations, pair types) and `TrialTypes.cs`
(presentation records, blocks, sessions).

---

## 8. Operation flow

### Step 1: Open the project
Open Unity, wait for compilation to finish, and make sure there are no red errors in the Console.
Menu: `Tools ▸ StimGen ▸ Builder`. The top of the window displays the experimental design constants and the current object composition.

### Step 2: Materials
Confirm that “Part Color” is white, then click “Create / Refresh Part Material” → `Assets/Materials/StimulusPart.mat`.
Disable or delete the hand-built `similar-levelN` objects in the scene.

### Step 3: Visually inspect the assemblies ★
Click “Preview 12 samples in the scene” and rotate them in the Scene view to inspect them. Check: 4 parts, one of each of the four shapes,
all connected, no interpenetration, no coplanarity. If everything looks washed out and blurred together, lower the Directional Light intensity;
if the seams look too loose/tight, adjust `ShapeMetrics.ContactOverlap`.

### Step 4: First run the pure-geometry bank build
Turn off “Perform silhouette/occlusion checks” and click **① Build Stimulus Bank**. It should finish in under 1 second.
Check the **pairing coverage** report in the status bar and confirm that no object has “fewer than 2 candidates at any level”.
This step only confirms that the pipeline works; do not interpret the result.

### Step 5: Enable visual checks and calibrate the IoU thresholds ★ The only step requiring judgment
Check “Perform silhouette/occlusion checks” and click **① Build Stimulus Bank** again. This run is much slower.
Focus on **pairing coverage** and the number discarded for “silhouette mismatch”:

| Situation | What to do |
|---|---|
| Coverage is sufficient at all three levels | ✅ Go to Step 6 |
| High coverage falls to 0–1 | Lower “High IoU ≥” (0.80 → 0.75) |
| Low coverage drops substantially | Raise “Low IoU ≤” (0.50 → 0.60) |
| All levels drop, with a message that the three angles are inconsistent | Raise “Maximum spread across three angles” |

### Step 6: Schedule sessions
Set “starting participant ID” and “how many participants to generate”, then click **② Schedule Sessions + Preflight Check**.
One `session_Pxxx.json` is generated per person. The status bar reports whether each person passed the preflight check—
**if any one fails, it cannot be used to run the experiment**.

### Step 7: Run one block to validate the data
Create a new empty GameObject in the scene, attach `ExperimentRunner`, drag `session_P001.json` into
`Session Json`, and assign `Fixation Visual` (a cross or small sphere).
**Move the Main Camera from z = −10 to approximately z ≈ −4**.
Enter Play mode, right-click the component header → `Run First Block Only`. **F = Different, J = Same**.

Open the CSV to validate:

- [ ] 32 rows (one block)
- [ ] The first 2 rows have `Scored = 0`
- [ ] There are 10 rows where `TrialPairType = Target`
- [ ] The first two trials of every segment are Non-target
- [ ] `RetainedRelations`: Target = 3, HighNT = 2, MediumNT = 1, LowNT = 0
- [ ] Approximately 10 of each of the three `RotationDelta` values
- [ ] There are 3 rows with `IsFirstTrialAfterBoundary = 1`
- [ ] `ReactionTimeMs` contains numbers; timeout rows have `Timeout = 1` and `Correct` is not 1

---

## 9. Output files

| File | Location | Contents |
|---|---|---|
| `StimulusPart.mat` | `Assets/Materials/` | Shared material |
| `stimulus_bank.json` | `Assets/StimulusSets/` | Frozen stimulus bank: all objects + families + pair matrix |
| `session_Pxxx.json` | `Assets/StimulusSets/` | Complete sequence for one participant, self-contained (including the definitions of the objects used) |
| `Pxxx_<time>.csv` | `%USERPROFILE%\AppData\LocalLow\<company>\<project>\StimGenLogs\` | One row per presentation |
| `Pxxx_<time>_blocks.csv` | Same location | Mental-effort rating, rest duration, and notes for each block |
| `Pxxx_<time>_session.json` | Same location | Copy of the sequence actually used for this run |

The CSV columns directly correspond to the data structure in Section 11: position (Block/Segment/Trial indices, ChainID),
similarity and transition (previous/current similarity, transition label, IsNoOpBoundary,
TrialsSinceTransition), stimulus (Object/Family/PartSet ID, Seed, relation signature,
TrialPairType, RetainedRelations, StructuralDistance), orientation, answer and four-way outcome
(Hit/Miss/FalseAlarm/CorrectRejection/NoResponse), and all timestamps.

---

## 10. Where to make changes

| What to change | Where |
|---|---|
| Segment length, number of blocks, number of Targets, six sequences | `ExperimentDesign.cs` (**after the pilot, only segment length may be changed**) |
| Number of families, number of variants per level, coverage requirement | Builder window, “① Stimulus Bank” |
| Geometry and visual acceptance criteria | Builder window; every item has a hover tooltip |
| Target consecutive limit, repetition window, family cooldown | Builder window, “② Session Scheduler” |
| Part volume, interlocking depth, aspect ratios of each shape | `ShapeMetrics.cs` |
| Which shapes to use | `StimTypes.cs` → `StimConfig.ShapesInUse` |
| Fixation/stimulus/blank durations, keys | `ExperimentRunner` component Inspector |
| EEG/ECG event-marker integration | Implement `IMarkerSink`, inject it with `ExperimentRunner.SetMarkerSink` |

---

## 11. Current status and TODO

### Tested and passed

```
Bank build (24 families, visual checks skipped):
  Formal objects 96 → coverage supplementation +35 → 131; 20 practice objects
  Pair matrix: 4551 valid pairs
  Coverage at all three levels passed (High minimum 2, Medium minimum 13, Low minimum 85)
  Runtime: 0.4 seconds

Tree-root independence of relation definitions: 80 re-rootings, relation set always identical
Within-family relations: all 107 pairs fell into 2/1/0, with unchanged part composition

Session scheduling: all 30 participants passed preflight
  180 scored trials per participant, 60 each for L/M/H, 60 each for 0°/45°/90°
  Target 60, Non-target 120, 18 boundaries (12 real + 6 No-op)
```

### Not yet run

- **Silhouette / occlusion checks** (`SilhouetteAnalyzer`) require Unity Editor rendering; only compilation has been verified.
  Step 5 exists to calibrate it; the IoU thresholds will probably need to be adjusted according to the measured distribution.
- **VR presentation**: `ExperimentRunner` is currently a desktop keyboard version. VR controller input, fixed viewing distance and visual size in the headset,
  and the way the fixation point is presented in VR have not yet been implemented.
- **EEG / ECG integration**: the `IMarkerSink` interface and CSV columns are in place, but there is only a placeholder implementation that writes to the Console; no real acquisition system is connected.
- **Practice flow**: practice objects have been generated separately and isolated from the formal bank, but the “1–2 short practices + correctness feedback +
  comprehension confirmation” flow has not yet been written.
- **Post-block rating**: `LogBlockSummary` is in place, but there is no UI yet for collecting the mental-effort score from 1–7.
