using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RadiantPool.EditorTools
{
    /// <summary>Adapter for Explosive's Warrior Pack Bundle 2 FREE.  The licensed FBXs
    /// are installed selectively under LocalLicensed and remain gitignored; this class
    /// gives their four humanoid attacks stable semantic names for the shared controller.</summary>
    public static class WarriorPackArt
    {
        public enum Motion { OneHanded, TwoHanded, Ranged, Cast }

        private const string Root = "Assets/LocalLicensed/WarriorPack2";
        private static readonly (Motion motion, string file, string clip)[] Specs =
        {
            (Motion.OneHanded, "Warrior_1H_Attack", "Warrior_1H_Attack"),
            (Motion.TwoHanded, "Warrior_2H_Attack", "Warrior_2H_Attack"),
            (Motion.Ranged, "Warrior_Ranged_Attack", "Warrior_Ranged_Attack"),
            (Motion.Cast, "Warrior_Cast", "Warrior_Cast"),
        };

        private static readonly Dictionary<Motion, AnimationClip> Clips =
            new Dictionary<Motion, AnimationClip>();

        public static bool Available => Specs.All(s => Get(s.motion) != null);

        public static AnimationClip Get(Motion motion)
        {
            Clips.TryGetValue(motion, out var clip);
            return clip;
        }

        public static void Setup()
        {
            Clips.Clear();
            if (!AssetDatabase.IsValidFolder(Root))
            {
                Debug.Log("[WarriorPack] Not installed; KayKit action fallbacks remain active.");
                return;
            }

            foreach (var spec in Specs)
            {
                string modelPath = AssetDatabase.FindAssets("t:Model", new[] { Root })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(path => string.Equals(
                        Path.GetFileNameWithoutExtension(path), spec.file,
                        StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(modelPath))
                {
                    Debug.LogError($"[WarriorPack] Missing local animation {spec.file}.fbx");
                    continue;
                }

                var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (importer == null)
                {
                    Debug.LogError($"[WarriorPack] No model importer for {modelPath}");
                    continue;
                }

                importer.importAnimation = true;
                importer.animationType = ModelImporterAnimationType.Human;
                var imported = importer.clipAnimations;
                if (imported == null || imported.Length == 0)
                    imported = importer.defaultClipAnimations;
                if (imported.Length == 0)
                {
                    Debug.LogError($"[WarriorPack] No clip take in {modelPath}");
                    continue;
                }
                imported[0].name = spec.clip;
                imported[0].loopTime = false;
                imported[0].loopPose = false;
                imported[0].lockRootRotation = true;
                imported[0].lockRootHeightY = true;
                imported[0].lockRootPositionXZ = true;
                importer.clipAnimations = imported;
                importer.SaveAndReimport();

                var clip = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name == spec.clip);
                if (clip == null)
                    Debug.LogError($"[WarriorPack] Imported clip {spec.clip} was not created");
                else
                    Clips[spec.motion] = clip;
            }

            string summary = string.Join(", ", Specs.Select(s =>
                $"{s.motion}:{(Get(s.motion) != null ? Get(s.motion).name : "MISSING")}"));
            Debug.Log($"[WarriorPack] {Clips.Count}/4 combat clips ready - {summary}");
        }
    }
}
