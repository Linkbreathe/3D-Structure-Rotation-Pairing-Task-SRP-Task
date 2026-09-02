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
        public static readonly float[] RotationOptions = { 0f, 90f, 180f };

        public const string TaskProtocolVersion = "Pairing_Similarity_Transition_v1";
        public const string RotationProtocolVersion = "Pair_XDelta_0_90_180_YSpin_v1";
        public const string ConditionRotationAxis = "X";
        public const string PresentationAnimationAxis = "Y";

        /// <summary>
        /// 六种 block sequence。L/M/H 分别是 Low / Medium / High structural similarity。
        ///
        /// 顺序与最终实验计划第 8 节完全一致。
        /// </summary>
        public static readonly SimilarityLevel[][] BlockSequences =
        {
            // A: L L M H   →  L→L(No-op), L→M, M→H
            new[] { SimilarityLevel.Low,    SimilarityLevel.Low,    SimilarityLevel.Medium, SimilarityLevel.High },
            // B: L H M M   →  L→H, H→M, M→M(No-op)
            new[] { SimilarityLevel.Low,    SimilarityLevel.High,   SimilarityLevel.Medium,  SimilarityLevel.Medium },
            // C: M L H H   →  M→L, L→H, H→H(No-op)
            new[] { SimilarityLevel.Medium, SimilarityLevel.Low,    SimilarityLevel.High,   SimilarityLevel.High },
            // D: M M H L   →  M→M(No-op), M→H, H→L
            new[] { SimilarityLevel.Medium, SimilarityLevel.Medium, SimilarityLevel.High,   SimilarityLevel.Low },
            // E: H M L L   →  H→M, M→L, L→L(No-op)
            new[] { SimilarityLevel.High,   SimilarityLevel.Medium, SimilarityLevel.Low,    SimilarityLevel.Low },
            // F: H H L M   →  H→H(No-op), H→L, L→M
            new[] { SimilarityLevel.High,   SimilarityLevel.High,   SimilarityLevel.Low,    SimilarityLevel.Medium },
        };

        public static readonly string[] BlockSequenceIds = { "A", "B", "C", "D", "E", "F" };

        public const int BlocksPerParticipant = 6;

        /// <summary>
        /// 最终计划第 9 节指定的六组参与者间 block 顺序。
        /// P001/P007/P013/P019 使用第 1 组，随后按编号循环轮换；
        /// 这样人数增加时仍然可以继续生成，而前 24 人每组正好 4 人。
        /// </summary>
        public static int[] BlockOrderFor(int participantNumber)
        {
            int[][] balancedOrders =
            {
                new[] { 0, 1, 5, 2, 4, 3 }, // A → B → F → C → E → D
                new[] { 1, 2, 0, 3, 5, 4 }, // B → C → A → D → F → E
                new[] { 2, 3, 1, 4, 0, 5 }, // C → D → B → E → A → F
                new[] { 3, 4, 2, 5, 1, 0 }, // D → E → C → F → B → A
                new[] { 4, 5, 3, 0, 2, 1 }, // E → F → D → A → C → B
                new[] { 5, 0, 4, 1, 3, 2 }, // F → A → E → B → D → C
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
