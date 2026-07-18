using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RadiantPool.EditorTools
{
    /// <summary>Integration for CC0 Quaternius creatures (converted from the packs'
    /// .blend files to FBX with all actions baked — see scratchpad blend2fbx.py).
    /// Per-model URP material from the pack atlas, looping locomotion clips, a
    /// per-model AnimatorController with the same parameters the game drives
    /// (Speed/Attack/Hit/Dead), and a prefab per creature under Resources/Characters
    /// so CombatManager.MonsterModels picks them up by name.</summary>
    public static class QuaterniusArt
    {
        private const string Root = "Assets/Art/Quaternius";
        private const string PrefabDir = "Assets/Resources/Characters";

        public static void Setup()
        {
            if (!AssetDatabase.IsValidFolder(Root))
            {
                Debug.Log("[Bootstrap] No Quaternius assets; skipping.");
                return;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                SetupModel(path, name);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Bootstrap] Quaternius creatures ready.");
        }

        private static void SetupModel(string path, string name)
        {
            string dir = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');

            // Material: the atlas next to the model (Atlas_*.png or the .fbm copy).
            var tex = AssetDatabase.FindAssets("t:Texture2D", new[] { dir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
                .FirstOrDefault(t => t != null && t.name.ToLowerInvariant().Contains("atlas"));
            string matPath = $"{dir}/M_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (tex != null) mat.SetTexture("_BaseMap", tex);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            // Outside the creation guard so a retune reaches already-baked mats.
            mat.SetFloat("_Smoothness", 0.18f);

            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            bool changed = false;
            var map = importer.GetExternalObjectMap();
            foreach (string embedded in AssetDatabase.LoadAllAssetsAtPath(path)
                         .OfType<Material>().Select(m => m.name).Distinct())
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded);
                if (map.ContainsKey(id)) continue;
                importer.AddRemap(id, mat);
                changed = true;
            }

            // Loop locomotion clips.
            var clips = importer.defaultClipAnimations;
            if (clips.Length > 0)
            {
                bool clipChange = importer.clipAnimations.Length == 0;
                foreach (var clip in clips)
                {
                    bool shouldLoop = clip.name.Contains("Idle") || clip.name.Contains("Walk")
                        || clip.name.Contains("Run");
                    if (shouldLoop && !clip.loopTime) { clip.loopTime = true; clipChange = true; }
                }
                if (clipChange) { importer.clipAnimations = clips; changed = true; }
            }
            if (changed) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var controller = BuildController(dir, name, path);
            BuildPrefab(path, name, controller);
        }

        private static AnimationClip Find(AnimationClip[] all, params string[] keys)
        {
            foreach (string k in keys)
            {
                var c = all.FirstOrDefault(x => x.name.Contains(k));
                if (c != null) return c;
            }
            return null;
        }

        /// <summary>Same state/parameter layout as the KayKit controller so runtime code
        /// (CharacterVisuals.Trigger/SetDead, MotionAnimator) drives both identically.</summary>
        private static AnimatorController BuildController(string dir, string name, string modelPath)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__")).ToArray();
            string ctrlPath = $"{dir}/{name}.controller";
            AssetDatabase.DeleteAsset(ctrlPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Cast", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Victory", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

            var sm = controller.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            idle.motion = Find(clips, "Idle");
            sm.defaultState = idle;
            var move = sm.AddState("Move");
            move.motion = Find(clips, "Walk", "Run") ?? idle.motion;
            var attack = sm.AddState("Attack");
            attack.motion = Find(clips, "Weapon", "Punch", "Bite", "Attack") ?? idle.motion;
            var cast = sm.AddState("Cast");
            cast.motion = Find(clips, "Spell", "Magic", "Cast", "Throw")
                ?? attack.motion ?? idle.motion;
            var victory = sm.AddState("Victory");
            victory.motion = Find(clips, "Victory", "Cheer", "Roar", "Idle")
                ?? idle.motion;
            var hit = sm.AddState("Hit");
            hit.motion = Find(clips, "HitReact", "Hit") ?? idle.motion;
            var dead = sm.AddState("Dead");
            dead.motion = Find(clips, "Death") ?? idle.motion;

            var toMove = idle.AddTransition(move);
            toMove.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");
            toMove.hasExitTime = false;
            toMove.duration = 0.12f;
            var toIdle = move.AddTransition(idle);
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.15f, "Speed");
            toIdle.hasExitTime = false;
            toIdle.duration = 0.12f;

            var anyAttack = sm.AddAnyStateTransition(attack);
            anyAttack.AddCondition(AnimatorConditionMode.If, 0, "Attack");
            anyAttack.hasExitTime = false;
            anyAttack.duration = 0.05f;
            anyAttack.canTransitionToSelf = false;
            var attackDone = attack.AddTransition(idle);
            attackDone.hasExitTime = true;
            attackDone.exitTime = 0.9f;
            attackDone.duration = 0.1f;

            var anyCast = sm.AddAnyStateTransition(cast);
            anyCast.AddCondition(AnimatorConditionMode.If, 0, "Cast");
            anyCast.hasExitTime = false;
            anyCast.duration = 0.05f;
            anyCast.canTransitionToSelf = false;
            var castDone = cast.AddTransition(idle);
            castDone.hasExitTime = true;
            castDone.exitTime = 0.9f;
            castDone.duration = 0.1f;

            var anyVictory = sm.AddAnyStateTransition(victory);
            anyVictory.AddCondition(AnimatorConditionMode.If, 0, "Victory");
            anyVictory.hasExitTime = false;
            anyVictory.duration = 0.05f;
            anyVictory.canTransitionToSelf = false;
            var victoryDone = victory.AddTransition(idle);
            victoryDone.hasExitTime = true;
            victoryDone.exitTime = 0.9f;
            victoryDone.duration = 0.1f;

            var anyHit = sm.AddAnyStateTransition(hit);
            anyHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
            anyHit.hasExitTime = false;
            anyHit.duration = 0.05f;
            anyHit.canTransitionToSelf = false;
            var hitDone = hit.AddTransition(idle);
            hitDone.hasExitTime = true;
            hitDone.exitTime = 0.85f;
            hitDone.duration = 0.1f;

            var anyDead = sm.AddAnyStateTransition(dead);
            anyDead.AddCondition(AnimatorConditionMode.If, 0, "Dead");
            anyDead.hasExitTime = false;
            anyDead.duration = 0.1f;
            anyDead.canTransitionToSelf = false;   // see KayKitArt: else Death freezes
            var revive = dead.AddTransition(idle);
            revive.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            revive.hasExitTime = false;
            revive.duration = 0.1f;

            return controller;
        }

        private static void BuildPrefab(string path, string name, AnimatorController controller)
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir))
                AssetDatabase.CreateFolder("Assets/Resources", "Characters");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) return;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);

            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                if (bounds.size.y > 0.01f)
                    instance.transform.localScale = Vector3.one * (1.85f / bounds.size.y);
            }
            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            PrefabUtility.SaveAsPrefabAsset(instance, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(instance);
        }
    }
}
