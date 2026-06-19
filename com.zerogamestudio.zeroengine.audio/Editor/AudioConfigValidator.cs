using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Audio.Editor
{
    public enum AudioValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class AudioValidationIssue
    {
        public AudioValidationIssue(AudioValidationSeverity severity, string assetName, string fieldPath, string message)
        {
            Severity = severity;
            AssetName = assetName;
            FieldPath = fieldPath;
            Message = message;
        }

        public AudioValidationSeverity Severity { get; }
        public string AssetName { get; }
        public string FieldPath { get; }
        public string Message { get; }

        public override string ToString()
        {
            return $"[{Severity}] {AssetName}.{FieldPath}: {Message}";
        }
    }

    public static class AudioConfigValidator
    {
        public static IReadOnlyList<AudioValidationIssue> Validate(
            IEnumerable<AudioCueSO> cues = null,
            IEnumerable<AudioMusicSO> musicTracks = null)
        {
            bool loadAll = cues == null && musicTracks == null;
            var cueList = Resolve(cues, loadAll);
            var musicList = Resolve(musicTracks, loadAll);
            var issues = new List<AudioValidationIssue>();

            foreach (var cue in cueList)
                ValidateCue(issues, cue);
            foreach (var music in musicList)
                ValidateMusic(issues, music);

            return issues;
        }

        public static IReadOnlyList<T> LoadAssets<T>() where T : UnityEngine.Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset != null)
                .ToList();
        }

        private static List<T> Resolve<T>(IEnumerable<T> source, bool loadAll) where T : UnityEngine.Object
        {
            if (source != null)
                return source.ToList();

            return loadAll ? LoadAssets<T>().ToList() : new List<T>();
        }

        private static void ValidateCue(List<AudioValidationIssue> issues, AudioCueSO cue)
        {
            if (cue == null)
                return;

            if (cue.Clips == null || cue.Clips.Length == 0)
            {
                Add(issues, AudioValidationSeverity.Error, cue, "Clips", "Audio cue must contain at least one clip.");
            }
            else
            {
                for (int i = 0; i < cue.Clips.Length; i++)
                {
                    if (cue.Clips[i] == null)
                        Add(issues, AudioValidationSeverity.Error, cue, $"Clips[{i}]", "Audio cue contains an empty clip reference.");
                }
            }

            if (cue.SpatialBlend < 0f || cue.SpatialBlend > 1f)
                Add(issues, AudioValidationSeverity.Error, cue, "SpatialBlend", "Spatial blend must be between 0 and 1.");
            ValidateRange(issues, cue, "VolumeRange", cue.VolumeRange, 0f, 1f);
            ValidateRange(issues, cue, "PitchRange", cue.PitchRange, 0.01f, 4f);
            if (cue.Cooldown < 0f)
                Add(issues, AudioValidationSeverity.Error, cue, "Cooldown", "Cooldown cannot be negative.");
        }

        private static void ValidateMusic(List<AudioValidationIssue> issues, AudioMusicSO music)
        {
            if (music == null)
                return;

            if (music.IntroClip == null && music.LoopClip == null)
                Add(issues, AudioValidationSeverity.Error, music, "LoopClip", "Music track must define an intro or loop clip.");
            if (music.Volume < 0f || music.Volume > 1f)
                Add(issues, AudioValidationSeverity.Error, music, "Volume", "Music volume must be between 0 and 1.");
        }

        private static void ValidateRange(
            List<AudioValidationIssue> issues,
            UnityEngine.Object asset,
            string fieldPath,
            Vector2 range,
            float minAllowed,
            float maxAllowed)
        {
            if (range.x > range.y)
                Add(issues, AudioValidationSeverity.Error, asset, fieldPath, "Range minimum cannot exceed maximum.");
            if (range.x < minAllowed || range.y > maxAllowed)
                Add(issues, AudioValidationSeverity.Error, asset, fieldPath, $"Range must stay within [{minAllowed}, {maxAllowed}].");
        }

        private static void Add(List<AudioValidationIssue> issues, AudioValidationSeverity severity, UnityEngine.Object asset, string fieldPath, string message)
        {
            issues.Add(new AudioValidationIssue(severity, string.IsNullOrEmpty(asset.name) ? asset.GetType().Name : asset.name, fieldPath, message));
        }
    }
}
