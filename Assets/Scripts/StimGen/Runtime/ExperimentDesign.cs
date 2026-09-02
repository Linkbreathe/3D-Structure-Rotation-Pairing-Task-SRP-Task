using System;
using System.Collections.Generic;
using UnityEngine;

namespace StimGen
{
    /// <summary>
    /// 实验的三级结构常量：Experiment ▸ Block ▸ Segment ▸ Trial。
    ///
        /// 每个 trial 都是独立的 Reference A → Comparison B 判断。
        /// Similarity 在 segment 层面受控改变，Rotation difference 在 pair 层面变化。
        /// segment 之间没有休息、提示音或可见的切换画面，被试不知道边界在哪。
    /// </summary>
    public static class ExperimentDesign
    {
        /// <summary>每个 block 的 segment 长度（scored trials）。</summary>
        public static readonly int[] SegmentLengths = { 8, 7, 8, 7 };

        public static int ScoredTrialsPerBlock
        {
            get
            {
                int n = 0;
                for (int i = 0; i < SegmentLengths.Length; i++) n += SegmentLengths[i];
                return n;   // 8+7+8+7 = 30
            }
        }

        public static int SegmentCount { get { return SegmentLengths.Length; } }

        public static int PresentationsPerBlock
        {
            get { return ScoredTrialsPerBlock; }   // 30 个完整 Pairing trials
        }

        /// <summary>每个 block 的 Target 个数（约 1/3）。</summary>
        public const int TargetsPerBlock = 10;

        /// <summary>
        /// 每个 Pairing trial 中 Comparison B 相对 Reference A 的 X 轴角度差。
        /// </summary>
        public static readonly float[] RotationOptions = { 0f, 180f };

        /// <summary>
        /// Active non-target structural-similarity conditions for the 2 x 2 protocol.
        /// Medium remains in the serialized enum only so legacy JSON cannot be
        /// misinterpreted after an enum-value shift.
        /// </summary>
        public static readonly SimilarityLevel[] ActiveSimilarityLevels =
        {
            SimilarityLevel.High,
            SimilarityLevel.Low,
        };

        public const string TaskProtocolVersion = "Pairing_Similarity_Transition_2x2_v1";
        public const string RotationProtocolVersion = "Pair_XDelta_0_180_YSpin_v1";
        public const string ConditionRotationAxis = "X";
        public const string PresentationAnimationAxis = "Y";

        /// <summary>
        /// 四种 block sequence。L/H 分别是 Low / High structural similarity。
        ///
        /// 顺序与最终实验计划第 8 节完全一致。
        /// </summary>
        public static readonly SimilarityLevel[][] BlockSequences =
        {
            // A: L L H H   →  L→L(No-op), L→H, H→H(No-op)
            new[] { SimilarityLevel.Low,  SimilarityLevel.Low,  SimilarityLevel.High, SimilarityLevel.High },
            // B: L H L H   →  L→H, H→L, L→H
            new[] { SimilarityLevel.Low,  SimilarityLevel.High, SimilarityLevel.Low,  SimilarityLevel.High },
            // C: H L H L   →  H→L, L→H, H→L
            new[] { SimilarityLevel.High, SimilarityLevel.Low,  SimilarityLevel.High, SimilarityLevel.Low },
            // D: H H L L   →  H→H(No-op), H→L, L→L(No-op)
            new[] { SimilarityLevel.High, SimilarityLevel.High, SimilarityLevel.Low,  SimilarityLevel.Low },
        };

        public static readonly string[] BlockSequenceIds = { "A", "B", "C", "D" };

        public const int BlocksPerParticipant = 4;

        /// <summary>
        /// 2 x 2 方案使用四组平衡的参与者间 block 顺序。
        /// 按参与者编号循环轮换；24 名参与者时每组恰好 6 人。
        /// </summary>
        public static int[] BlockOrderFor(int participantNumber)
        {
            int[][] balancedOrders =
            {
                new[] { 0, 1, 3, 2 }, // A → B → D → C
                new[] { 1, 2, 0, 3 }, // B → C → A → D
                new[] { 2, 3, 1, 0 }, // C → D → B → A
                new[] { 3, 0, 2, 1 }, // D → A → C → B
            };

            int zeroBased = participantNumber - 1;
            int group = zeroBased % balancedOrders.Length;
            if (group < 0) group += balancedOrders.Length;
            return (int[])balancedOrders[group].Clone();
        }

        /// <summary>Same/Different 的左右手映射在参与者之间平衡。</summary>
        public static bool SwapResponseKeysFor(int participantNumber)
        {
            return participantNumber % 2 == 1;
        }

        /// <summary>transition 的可读标签，例如 Low_to_High。</summary>
        public static string TransitionLabel(SimilarityLevel from, SimilarityLevel to)
        {
            return from + "_to_" + to;
        }

        public static bool IsNoOp(SimilarityLevel from, SimilarityLevel to)
        {
            return from == to;
        }

        /// <summary>某个 scored trial 属于第几个 segment，以及它在 segment 内的序号。</summary>
        public static void LocateSegment(int scoredIndex, out int segmentIndex, out int indexWithinSegment)
        {
            int acc = 0;
            for (int s = 0; s < SegmentLengths.Length; s++)
            {
                if (scoredIndex < acc + SegmentLengths[s])
                {
                    segmentIndex = s;
                    indexWithinSegment = scoredIndex - acc;
                    return;
                }
                acc += SegmentLengths[s];
            }
            segmentIndex = SegmentLengths.Length - 1;
            indexWithinSegment = SegmentLengths[SegmentLengths.Length - 1] - 1;
        }
    }
}
