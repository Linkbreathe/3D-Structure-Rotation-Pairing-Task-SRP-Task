using System;
using System.Collections.Generic;
using UnityEngine;

namespace StimGen
{
    /// <summary>
    /// 一个完整 Pairing trial 的定义。
    /// 生成期就全部确定，运行期只负责按表播放。
    /// </summary>
    [Serializable]
    public class PresentationRecord
    {
        // ---- 位置 ----
        public int blockIndex;                 // 该参与者的第几个 block（0..5）
        public string blockSequenceId = "";    // A..F
        public int presentationIndexInBlock;   // 0..29
        public bool scored;
        public int trialIndexGlobal = -1;      // 0..179，仅 scored
        public int trialIndexWithinBlock = -1; // 0..29
        public int segmentIndex = -1;          // 0..3
        public int trialIndexWithinSegment = -1;

        // ---- similarity 与 transition ----
        public SimilarityLevel previousSegmentSimilarity = SimilarityLevel.Identical;
        public SimilarityLevel segmentSimilarity = SimilarityLevel.Identical;
        public string similarityTransition = "";
        public bool isNoOpBoundary;
        public bool isFirstTrialAfterBoundary;
        public int trialsSinceTransition = -1;
        public int boundaryPositionWithinBlock = -1;

        // ---- 刺激 ----
        public string referenceObjectId = "";  // Reference A
        public string comparisonObjectId = "";
        public string referenceFamilyId = "";
        public string comparisonFamilyId = "";
        public string partSetId = "";
        public string stimulusBankVersion = "";
        public int stimulusSeed;
        public int partCount;
        public string referenceRelationSignature = "";
        public string comparisonRelationSignature = "";
        public PairClass trialPairType = PairClass.Invalid;
        public int retainedRelations = -1;
        public int structuralDistance = -1;

        // ---- 姿态与观看动画 ----
        // 实验条件：Comparison B 相对 Reference A 绕 X 轴的角度差。
        public float referenceRotationX;
        public float comparisonRotationX;
        public float rotationDeltaX;
        public string conditionRotationAxis = "X";

        // 所有试次共用的观看动画轴。圈数和速度属于运行设置，写入行为 CSV。
        public string presentationAnimationAxis = "Y";

        // ---- 结构熟悉度 ----
        public int referenceObjectPriorExposures = -1;
        public int comparisonObjectPriorExposures = 0;
        public int referenceFamilyPriorExposures = -1;
        public int comparisonFamilyPriorExposures = 0;
        public int trialsSinceReferenceObjectLastSeen = -1;
        public int trialsSinceComparisonObjectLastSeen = -1;
        public int trialsSinceReferenceFamilyLastSeen = -1;
        public int trialsSinceComparisonFamilyLastSeen = -1;

        // ---- 正确答案 ----
        public bool expectedSame;

        public bool IsTarget { get { return trialPairType == PairClass.Target; } }
    }

    /// <summary>一个 block 的完整计划。</summary>
    [Serializable]
    public class BlockPlan
    {
        public int blockIndex;
        public int sequenceIndex;              // 0..5
        public string sequenceId = "";         // A..F
        public List<SimilarityLevel> segmentSimilarity = new List<SimilarityLevel>();
        public List<int> segmentLengths = new List<int>();
        public List<PresentationRecord> presentations = new List<PresentationRecord>();

        public int TargetCount()
        {
            int n = 0;
            for (int i = 0; i < presentations.Count; i++)
                if (presentations[i].scored && presentations[i].IsTarget) n++;
            return n;
        }

        public int NonTargetCount()
        {
            int n = 0;
            for (int i = 0; i < presentations.Count; i++)
                if (presentations[i].scored && !presentations[i].IsTarget) n++;
            return n;
        }
    }

    /// <summary>
    /// 一个参与者的完整会话计划：6 个 block + 用到的全部物体定义。
    /// 自包含，运行期只读这一个文件。
    /// </summary>
    [Serializable]
    public class SessionPlan
    {
        public string participantId = "";
        public int participantNumber;
        public int masterSeed;
        public string generatedUtc = "";
        public string bankId = "";
        public string taskProtocolVersion = "";
        public string rotationProtocolVersion = "";

        /// <summary>Same/Different 的左右手映射是否互换（参与者间平衡）。</summary>
        public bool swapResponseKeys;

        public List<int> blockOrder = new List<int>();
        public List<BlockPlan> blocks = new List<BlockPlan>();

        /// <summary>本会话用到的全部物体，运行期直接实例化。</summary>
        public List<ObjectDefinition> objects = new List<ObjectDefinition>();

        [NonSerialized] private Dictionary<string, ObjectDefinition> _byId;

        public void BuildIndex()
        {
            _byId = new Dictionary<string, ObjectDefinition>(objects.Count);
            for (int i = 0; i < objects.Count; i++) _byId[objects[i].objectId] = objects[i];
        }

        public ObjectDefinition Find(string objectId)
        {
            if (_byId == null) BuildIndex();
            ObjectDefinition def;
            return _byId.TryGetValue(objectId, out def) ? def : null;
        }

        public int ScoredTrialCount()
        {
            int n = 0;
            for (int b = 0; b < blocks.Count; b++)
                for (int i = 0; i < blocks[b].presentations.Count; i++)
                    if (blocks[b].presentations[i].scored) n++;
            return n;
        }
    }

    /// <summary>被试在一次 trial 上的反应。</summary>
    [Serializable]
    public struct TrialResponse
    {
        public bool responded;
        public bool saidSame;
        public bool timeout;
        public float reactionTime;      // 秒，从刺激出现算起

        public float fixationOnset;
        public float referenceOnset;
        public float referenceOffset;
        public float comparisonOnset;
        public float comparisonOffset;
        public float responseTimestamp;

        public bool feedbackShown;
        public string feedbackMode;
        public string feedbackText;
        public float feedbackOnset;
        public float feedbackOffset;
    }
}
