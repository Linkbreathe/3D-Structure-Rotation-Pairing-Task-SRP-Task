using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace StimGen
{
    /// <summary>行为结果的四分类。</summary>
    public enum Outcome
    {
        Hit = 0,               // Target + Same
        Miss = 1,              // Target + Different
        FalseAlarm = 2,        // Non-target + Same
        CorrectRejection = 3,  // Non-target + Different
        NoResponse = 4,
    }

    /// <summary>与 EEG / ECG 采集系统对接的事件标记接口。第一版用日志占位实现。</summary>
    public interface IMarkerSink
    {
        void SendMarker(string eventName, PresentationRecord record, float timestamp);
    }

    /// <summary>默认实现：只写进 Unity Console，方便先跑通时序。</summary>
    public class DebugMarkerSink : IMarkerSink
    {
        public bool verbose = false;

        public void SendMarker(string eventName, PresentationRecord record, float timestamp)
        {
            if (verbose)
                Debug.Log("[Marker] " + eventName + " @" + timestamp.ToString("F4") +
                          " block" + record.blockIndex + " pres" + record.presentationIndexInBlock);
        }
    }

    /// <summary>
    /// 记录 Pairing trial 的 A/B 刺激、行为、transition 和同步时间戳。
    /// 一行一个完整 trial，边跑边写并 flush。
    /// </summary>
    public class ExperimentLogger : IDisposable
    {
        private StreamWriter _writer;
        private StreamWriter _eventWriter;
        private string _fileNamePart;

        public string FilePath { get; private set; }
        public string EventFilePath { get; private set; }
        public string ParticipantDirectoryPath { get; private set; }
        public string SessionDirectoryPath { get; private set; }
        public string ParticipantId { get; private set; }
        public string SessionId { get; private set; }

        private const string Header =
            "ParticipantID,SessionID,TaskProtocolVersion," +
            "BlockIndex,BlockSequenceID,SegmentIndex,PresentationIndexInBlock," +
            "TrialIndexGlobal,TrialIndexWithinBlock,TrialIndexWithinSegment,Scored," +
            "PreviousSegmentSimilarity,SegmentSimilarity,SimilarityTransition," +
            "IsNoOpBoundary,IsFirstTrialAfterBoundary,TrialsSinceTransition,BoundaryPositionWithinBlock," +
            "ReferenceObjectID,ComparisonObjectID,ReferenceFamilyID,ComparisonFamilyID,PartSetID,StimulusBankVersion,RotationProtocolVersion,StimulusSeed,PartCount," +
            "ReferenceRelationSignature,ComparisonRelationSignature," +
            "TrialPairType,RetainedRelations,StructuralDistance," +
            "ReferenceRotationX,ComparisonRotationX,RotationDeltaX,ConditionRotationAxis," +
            "PresentationAnimationAxis,PresentationAnimationEnabled,PresentationAnimationRevolutions," +
            "ReferenceDurationMs,ComparisonDurationMs,PairDurationMs," +
            "ReferenceAnimationSpeedDegPerSec,ComparisonAnimationSpeedDegPerSec," +
            "ReferenceObjectPriorExposures,ComparisonObjectPriorExposures,ReferenceFamilyPriorExposures,ComparisonFamilyPriorExposures," +
            "TrialsSinceReferenceObjectLastSeen,TrialsSinceComparisonObjectLastSeen,TrialsSinceReferenceFamilyLastSeen,TrialsSinceComparisonFamilyLastSeen," +
            "ExpectedAnswer,ParticipantAnswer,ResponseValid,Timeout,Correct,Outcome,ReactionTimeMs," +
            "FeedbackMode,FeedbackShown,FeedbackText,FeedbackOnsetTimestamp,FeedbackOffsetTimestamp," +
            "FixationOnsetTimestamp,ReferenceOnsetTimestamp,ReferenceOffsetTimestamp,ComparisonOnsetTimestamp,ComparisonOffsetTimestamp,ResponseTimestamp," +
            "SegmentBoundaryTimestamp,EEGMarkerTimestamp,ECGMarkerTimestamp," +
            "EEGSignalQuality,ECGSignalQuality";

        public ExperimentLogger(string participantId, string directory = null)
        {
            ParticipantId = string.IsNullOrEmpty(participantId) ? "UNKNOWN" : participantId;

            string rootDirectory = string.IsNullOrEmpty(directory)
                ? Path.Combine(Application.persistentDataPath, "StimGenLogs")
                : directory;
            Directory.CreateDirectory(rootDirectory);

            // Keep every participant's files together while retaining the original
            // ParticipantID in every CSV row. Invalid filename characters are
            // replaced so an operator-entered ID cannot escape this directory.
            _fileNamePart = SafeFileNamePart(ParticipantId, "UNKNOWN");
            ParticipantDirectoryPath = Path.Combine(rootDirectory, _fileNamePart);
            Directory.CreateDirectory(ParticipantDirectoryPath);

            string baseSessionId = DateTime.UtcNow.ToString(
                "yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            SessionId = baseSessionId;
            int duplicate = 1;
            string sessionFolderName = _fileNamePart + "_" + SessionId;
            while (Directory.Exists(Path.Combine(
                       ParticipantDirectoryPath, sessionFolderName)))
            {
                SessionId = baseSessionId + "_" +
                    (duplicate++).ToString("D2", CultureInfo.InvariantCulture);
                sessionFolderName = _fileNamePart + "_" + SessionId;
            }

            SessionDirectoryPath = Path.Combine(ParticipantDirectoryPath,
                                                sessionFolderName);
            Directory.CreateDirectory(SessionDirectoryPath);

            FilePath = Path.Combine(SessionDirectoryPath,
                                    sessionFolderName + ".csv");
            _writer = new StreamWriter(FilePath, false, new UTF8Encoding(true));
            _writer.WriteLine(Header);
            _writer.Flush();

            EventFilePath = Path.Combine(SessionDirectoryPath,
                                         sessionFolderName + "_events.csv");
            _eventWriter = new StreamWriter(EventFilePath, false, new UTF8Encoding(true));
            _eventWriter.WriteLine(
                "ParticipantID,SessionID,EventName,UtcTimestamp,UnixTimeMilliseconds," +
                "UnityRealtimeSeconds,BlockIndex,BlockSequenceID,PresentationIndexInBlock," +
                "SegmentIndex,TrialIndexGlobal,Detail");
            _eventWriter.Flush();
        }

        public static Outcome Classify(bool isTarget, TrialResponse response)
        {
            if (!response.responded) return Outcome.NoResponse;
            if (isTarget) return response.saidSame ? Outcome.Hit : Outcome.Miss;
            return response.saidSame ? Outcome.FalseAlarm : Outcome.CorrectRejection;
        }

        public void Log(PresentationRecord p, TrialResponse response,
                        float segmentBoundaryTimestamp = -1f,
                        float eegMarkerTimestamp = -1f, float ecgMarkerTimestamp = -1f,
                        string eegQuality = "", string ecgQuality = "",
                        bool presentationAnimationEnabled = false,
                        float presentationAnimationRevolutions = 0f)
        {
            if (_writer == null) return;

            Outcome outcome = Classify(p.IsTarget, response);
            bool correct = p.scored && response.responded && response.saidSame == p.expectedSame;

            var sb = new StringBuilder();
            Add(sb, ParticipantId);
            Add(sb, SessionId);
            Add(sb, ExperimentDesign.TaskProtocolVersion);

            Add(sb, p.blockIndex);
            Add(sb, p.blockSequenceId);
            Add(sb, p.segmentIndex);
            Add(sb, p.presentationIndexInBlock);

            Add(sb, p.trialIndexGlobal);
            Add(sb, p.trialIndexWithinBlock);
            Add(sb, p.trialIndexWithinSegment);
            Add(sb, p.scored ? 1 : 0);

            Add(sb, p.scored ? p.previousSegmentSimilarity.ToString() : "");
            Add(sb, p.scored ? p.segmentSimilarity.ToString() : "");
            Add(sb, p.similarityTransition);
            Add(sb, p.isNoOpBoundary ? 1 : 0);
            Add(sb, p.isFirstTrialAfterBoundary ? 1 : 0);
            Add(sb, p.trialsSinceTransition);
            Add(sb, p.boundaryPositionWithinBlock);

            Add(sb, p.referenceObjectId);
            Add(sb, p.comparisonObjectId);
            Add(sb, p.referenceFamilyId);
            Add(sb, p.comparisonFamilyId);
            Add(sb, p.partSetId);
            Add(sb, p.stimulusBankVersion);
            Add(sb, ExperimentDesign.RotationProtocolVersion);
            Add(sb, p.stimulusSeed);
            Add(sb, p.partCount);
            Add(sb, p.referenceRelationSignature);
            Add(sb, p.comparisonRelationSignature);

            Add(sb, p.scored ? p.trialPairType.ToString() : "");
            Add(sb, p.retainedRelations);
            Add(sb, p.structuralDistance);

            Add(sb, F(p.referenceRotationX));
            Add(sb, F(p.comparisonRotationX));
            Add(sb, p.scored ? F(p.rotationDeltaX) : "");
            Add(sb, p.conditionRotationAxis);
            Add(sb, p.presentationAnimationAxis);
            Add(sb, presentationAnimationEnabled ? 1 : 0);
            Add(sb, F(presentationAnimationRevolutions));
            float referenceDurationSeconds = Duration(response.referenceOnset,
                                                       response.referenceOffset);
            float comparisonDurationSeconds = Duration(response.comparisonOnset,
                                                        response.comparisonOffset);
            Add(sb, F(referenceDurationSeconds * 1000f));
            Add(sb, F(comparisonDurationSeconds * 1000f));
            Add(sb, F((referenceDurationSeconds + comparisonDurationSeconds) * 1000f));
            float referenceAnimationSpeed = presentationAnimationEnabled &&
                                            referenceDurationSeconds > 0f
                ? 360f * Mathf.Max(0f, presentationAnimationRevolutions) /
                  referenceDurationSeconds : 0f;
            float comparisonAnimationSpeed = presentationAnimationEnabled &&
                                              comparisonDurationSeconds > 0f
                ? 360f * Mathf.Max(0f, presentationAnimationRevolutions) /
                  comparisonDurationSeconds : 0f;
            Add(sb, F(referenceAnimationSpeed));
            Add(sb, F(comparisonAnimationSpeed));

            Add(sb, p.referenceObjectPriorExposures);
            Add(sb, p.comparisonObjectPriorExposures);
            Add(sb, p.referenceFamilyPriorExposures);
            Add(sb, p.comparisonFamilyPriorExposures);
            Add(sb, p.trialsSinceReferenceObjectLastSeen);
            Add(sb, p.trialsSinceComparisonObjectLastSeen);
            Add(sb, p.trialsSinceReferenceFamilyLastSeen);
            Add(sb, p.trialsSinceComparisonFamilyLastSeen);

            Add(sb, p.scored ? (p.expectedSame ? "Same" : "Different") : "");
            Add(sb, response.responded ? (response.saidSame ? "Same" : "Different") : "");
            Add(sb, response.responded ? 1 : 0);
            Add(sb, response.timeout ? 1 : 0);
            Add(sb, p.scored ? (correct ? 1 : 0) : -1);
            Add(sb, p.scored ? outcome.ToString() : "");
            Add(sb, response.responded ? F(response.reactionTime * 1000f) : "");

            Add(sb, response.feedbackMode);
            Add(sb, response.feedbackShown ? 1 : 0);
            Add(sb, response.feedbackText);
            Add(sb, response.feedbackShown ? F(response.feedbackOnset) : "");
            Add(sb, response.feedbackShown ? F(response.feedbackOffset) : "");

            Add(sb, F(response.fixationOnset));
            Add(sb, F(response.referenceOnset));
            Add(sb, F(response.referenceOffset));
            Add(sb, F(response.comparisonOnset));
            Add(sb, F(response.comparisonOffset));
            Add(sb, response.responded ? F(response.responseTimestamp) : "");
            Add(sb, segmentBoundaryTimestamp >= 0f ? F(segmentBoundaryTimestamp) : "");
            Add(sb, eegMarkerTimestamp >= 0f ? F(eegMarkerTimestamp) : "");
            Add(sb, ecgMarkerTimestamp >= 0f ? F(ecgMarkerTimestamp) : "");
            Add(sb, eegQuality);
            AddLast(sb, ecgQuality);

            _writer.WriteLine(sb.ToString());
            _writer.Flush();
        }

        /// <summary>Block 结束后的心理努力评分等，单独写一个文件。</summary>
        public void LogBlockSummary(int blockIndex, string sequenceId, int effortRating,
                                    float restSeconds, string note,
                                    float unityRealtimeTimestamp = -1f)
        {
            string dir = Path.GetDirectoryName(FilePath);
            string path = Path.Combine(dir, _fileNamePart + "_" + SessionId + "_blocks.csv");
            bool exists = File.Exists(path);
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            float unityTime = unityRealtimeTimestamp >= 0f
                ? unityRealtimeTimestamp : Time.realtimeSinceStartup;

            using (var w = new StreamWriter(path, true, new UTF8Encoding(true)))
            {
                if (!exists) w.WriteLine(
                    "ParticipantID,SessionID,BlockIndex,BlockSequenceID,EffortRating1to7," +
                    "RestSeconds,RatingUtcTimestamp,RatingUnixTimeMilliseconds," +
                    "RatingUnityRealtimeSeconds,Note");
                w.WriteLine(string.Join(",", new[]
                {
                    Csv(ParticipantId), Csv(SessionId), blockIndex.ToString(), Csv(sequenceId),
                    effortRating.ToString(), F(restSeconds),
                    Csv(utcNow.ToString("O", CultureInfo.InvariantCulture)),
                    utcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                    F(unityTime), Csv(note),
                }));
            }
        }

        /// <summary>
        /// 额外保存一份按事件排列的时间表。UTC/Unix 时间用于和外部设备时钟人工对齐，
        /// UnityRealtimeSeconds 用于 Unity 内部的精细相对时序。
        /// </summary>
        public void LogEvent(string eventName, PresentationRecord record, float unityRealtime,
                             string detail = "")
        {
            LogEvent(eventName,
                     record != null ? record.blockIndex : -1,
                     record != null ? record.blockSequenceId : "",
                     record != null ? record.presentationIndexInBlock : -1,
                     record != null ? record.segmentIndex : -1,
                     record != null ? record.trialIndexGlobal : -1,
                     unityRealtime, detail);
        }

        public void LogEvent(string eventName, int blockIndex, string blockSequenceId,
                             int presentationIndex, int segmentIndex, int trialIndexGlobal,
                             float unityRealtime, string detail = "")
        {
            if (_eventWriter == null) return;

            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            _eventWriter.WriteLine(string.Join(",", new[]
            {
                Csv(ParticipantId),
                Csv(SessionId),
                Csv(eventName),
                Csv(utcNow.ToString("O", CultureInfo.InvariantCulture)),
                utcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                F(unityRealtime >= 0f ? unityRealtime : Time.realtimeSinceStartup),
                blockIndex.ToString(CultureInfo.InvariantCulture),
                Csv(blockSequenceId),
                presentationIndex.ToString(CultureInfo.InvariantCulture),
                segmentIndex.ToString(CultureInfo.InvariantCulture),
                trialIndexGlobal.ToString(CultureInfo.InvariantCulture),
                Csv(detail),
            }));
            _eventWriter.Flush();
        }

        /// <summary>把整份会话计划存一份副本，保证事后可复现。</summary>
        public void SaveSessionPlan(SessionPlan plan)
        {
            string dir = Path.GetDirectoryName(FilePath);
            string path = Path.Combine(dir, _fileNamePart + "_" + SessionId + "_session.json");
            File.WriteAllText(path, JsonUtility.ToJson(plan, true), new UTF8Encoding(true));
        }

        private static string SafeFileNamePart(string value, string fallback)
        {
            string result = string.IsNullOrEmpty(value) ? fallback : value.Trim();
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(result.Length);
            for (int i = 0; i < result.Length; i++)
            {
                char c = result[i];
                bool isInvalid = false;
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (c == invalid[j])
                    {
                        isInvalid = true;
                        break;
                    }
                }
                builder.Append(isInvalid ? '_' : c);
            }

            result = builder.ToString().Trim().TrimEnd('.');
            return string.IsNullOrEmpty(result) ? fallback : result;
        }

        private static void Add(StringBuilder sb, string v) { sb.Append(Csv(v)).Append(','); }
        private static void Add(StringBuilder sb, int v) { sb.Append(v).Append(','); }
        private static void AddLast(StringBuilder sb, string v) { sb.Append(Csv(v)); }

        private static string F(float v) { return v.ToString("F4", CultureInfo.InvariantCulture); }

        private static float Duration(float onset, float offset)
        {
            return onset >= 0f && offset >= onset ? offset - onset : 0f;
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') < 0 && s.IndexOf('"') < 0 && s.IndexOf('\n') < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        public void Dispose()
        {
            if (_writer != null)
            {
                _writer.Flush();
                _writer.Dispose();
                _writer = null;
            }
            if (_eventWriter != null)
            {
                _eventWriter.Flush();
                _eventWriter.Dispose();
                _eventWriter = null;
            }
        }
    }
}
