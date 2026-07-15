using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RadiantPool.EditorTools
{
    /// <summary>Integration for the CC0 KayKit character packs (Adventurers + Skeletons):
    /// URP material remap per character texture, looping import settings and a shared
    /// AnimatorController built from the Rig_Medium clips, and one prefab per character
    /// under Resources/Characters so runtime code can swap capsules for real people.</summary>
    public static class KayKitArt
    {
        private const string Root = "Assets/Art/KayKit";
        private const string ControllerPath = Root + "/CharacterAnimator.controller";
        private const string PrefabDir = "Assets/Resources/Characters";

        public static void Setup()
        {
            if (!AssetDatabase.IsValidFolder(Root))
            {
                Debug.LogWarning("[Bootstrap] No KayKit assets found; characters stay capsules.");
                return;
            }
            SetupMaterialsAndImport(WarriorPackArt.Available);
            var controller = BuildAnimator();
            BuildPrefabs(controller);
            BuildWeaponPrefabs();
            Debug.Log("[Bootstrap] KayKit characters ready.");
        }

        /// <summary>Weapon prefabs under Resources/Weapons so equipment changes can swap
        /// hand models at runtime.</summary>
        private static void BuildWeaponPrefabs()
        {
            string weaponsDir = $"{Root}/Weapons";
            if (!AssetDatabase.IsValidFolder(weaponsDir)) return;
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Weapons"))
                AssetDatabase.CreateFolder("Assets/Resources", "Weapons");
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { weaponsDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) continue;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.SaveAsPrefabAsset(instance, $"Assets/Resources/Weapons/{name}.prefab");
                Object.DestroyImmediate(instance);
            }
        }

        private static void SetupMaterialsAndImport(bool useHumanoidRetargeting)
        {
            foreach (string folder in new[] { "Characters", "Skeletons" })
            {
                string dir = $"{Root}/{folder}";
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string baseName = System.IO.Path.GetFileNameWithoutExtension(path);

                    // Texture: <name>_texture.png, else the folder's shared texture.
                    string texName = baseName.ToLowerInvariant().Replace("_hooded", "") + "_texture";
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{texName}.png")
                              ?? AssetDatabase.FindAssets("t:Texture2D", new[] { dir })
                                  .Select(AssetDatabase.GUIDToAssetPath)
                                  .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
                                  .FirstOrDefault();

                    string matPath = $"{dir}/M_{baseName}.mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat == null)
                    {
                        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        if (tex != null) mat.SetTexture("_BaseMap", tex);
                        mat.SetFloat("_Smoothness", 0.05f);
                        AssetDatabase.CreateAsset(mat, matPath);
                    }

                    var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                    bool changed = false;
                    // KayKit defaults to Generic. The Warrior Pack is Humanoid, so opt
                    // party and skeleton bodies into Mecanim retargeting only when the
                    // complete local licensed motion set is present.
                    if (useHumanoidRetargeting
                        && importer.animationType != ModelImporterAnimationType.Human)
                    {
                        importer.animationType = ModelImporterAnimationType.Human;
                        changed = true;
                    }
                    var embedded = AssetDatabase.LoadAllAssetsAtPath(path)
                        .OfType<Material>().Select(m => m.name).Distinct();
                    var map = importer.GetExternalObjectMap();
                    foreach (string name in embedded)
                    {
                        var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
                        if (map.ContainsKey(id)) continue;
                        importer.AddRemap(id, mat);
                        changed = true;
                    }
                    if (changed) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }

            // Weapon FBXes borrow character materials (their UVs sit on those atlases).
            string weaponsDir = $"{Root}/Weapons";
            if (AssetDatabase.IsValidFolder(weaponsDir))
            {
                var knightMat = AssetDatabase.LoadAssetAtPath<Material>(
                    $"{Root}/Characters/M_Knight.mat");
                var skelMat = AssetDatabase.LoadAssetAtPath<Material>(
                    $"{Root}/Skeletons/M_Skeleton_Warrior.mat");
                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { weaponsDir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var mat = path.Contains("Skeleton") ? skelMat : knightMat;
                    if (mat == null) continue;
                    var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                    bool changed = false;
                    var map = importer.GetExternalObjectMap();
                    foreach (string name in AssetDatabase.LoadAllAssetsAtPath(path)
                                 .OfType<Material>().Select(m => m.name).Distinct())
                    {
                        var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
                        if (map.ContainsKey(id)) continue;
                        importer.AddRemap(id, mat);
                        changed = true;
                    }
                    if (changed) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }

            // Animation FBXes: mark idle/walk/run clips as looping.
            string animDir = $"{Root}/Animations";
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { animDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                bool importerChanged = false;
                if (useHumanoidRetargeting
                    && importer.animationType != ModelImporterAnimationType.Human)
                {
                    importer.animationType = ModelImporterAnimationType.Human;
                    importerChanged = true;
                }
                var clips = importer.defaultClipAnimations;
                if (clips.Length == 0) continue;
                bool changed = false;
                foreach (var clip in clips)
                {
                    bool shouldLoop = clip.name.Contains("Idle") || clip.name.Contains("Walking")
                        || clip.name.Contains("Running");
                    if (shouldLoop && !clip.loopTime) { clip.loopTime = true; changed = true; }
                }
                if (changed || importerChanged || importer.clipAnimations.Length == 0)
                {
                    importer.clipAnimations = clips;
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
        }

        private static AnimationClip FindClip(AnimationClip[] all, params string[] keywords)
        {
            foreach (string k in keywords)
            {
                var clip = all.FirstOrDefault(c => c.name.Contains(k));
                if (clip != null) return clip;
            }
            return null;
        }

        private static AnimatorController BuildAnimator()
        {
            var clips = AssetDatabase.FindAssets("t:Model", new[] { $"{Root}/Animations" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .SelectMany(AssetDatabase.LoadAllAssetsAtPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__"))
                .ToArray();
            Debug.Log($"[Bootstrap] KayKit animation clips: {clips.Length}");

            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Attack1H", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Attack2H", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AttackRanged", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Cast", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Victory", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

            Debug.Log("[Bootstrap] All KayKit clips: "
                + string.Join(", ", clips.Select(c => c.name).OrderBy(n => n)));

            var sm = controller.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            idle.motion = FindClip(clips, "Idle");
            sm.defaultState = idle;
            var move = sm.AddState("Move");
            move.motion = FindClip(clips, "Walking_A", "Walking", "Running") ?? idle.motion;
            // The public KayKit free tier has no melee clips. When the locally licensed
            // Warrior Pack is installed, all four authored motions retarget onto these
            // Humanoid bodies. Every state retains the old action fallback for clean
            // builds on machines that do not own/import the pack.
            var fallbackAttack = FindClip(clips, "1H_Melee_Attack_Slice", "1H_Melee_Attack",
                "Melee_Attack", "Attack", "Throw", "Interact") ?? idle.motion;
            var attack = sm.AddState("Attack");
            attack.motion = WarriorPackArt.Get(WarriorPackArt.Motion.OneHanded)
                ?? fallbackAttack;
            var attack1H = sm.AddState("Attack1H");
            attack1H.motion = WarriorPackArt.Get(WarriorPackArt.Motion.OneHanded)
                ?? fallbackAttack;
            var attack2H = sm.AddState("Attack2H");
            attack2H.motion = WarriorPackArt.Get(WarriorPackArt.Motion.TwoHanded)
                ?? fallbackAttack;
            var attackRanged = sm.AddState("AttackRanged");
            attackRanged.motion = WarriorPackArt.Get(WarriorPackArt.Motion.Ranged)
                ?? fallbackAttack;
            var cast = sm.AddState("Cast");
            cast.motion = WarriorPackArt.Get(WarriorPackArt.Motion.Cast)
                ?? FindClip(clips, "Spellcast", "Spell", "Magic", "Throw", "Interact")
                ?? attack.motion ?? idle.motion;
            var victory = sm.AddState("Victory");
            victory.motion = FindClip(clips, "Victory", "Cheer", "Dance", "Interact")
                ?? idle.motion;
            var hit = sm.AddState("Hit");
            hit.motion = FindClip(clips, "Hit_A", "Hit") ?? idle.motion;
            var dead = sm.AddState("Dead");
            dead.motion = FindClip(clips, "Death_A", "Death") ?? idle.motion;

            var toMove = idle.AddTransition(move);
            toMove.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");
            toMove.hasExitTime = false;
            toMove.duration = 0.12f;
            var toIdle = move.AddTransition(idle);
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.15f, "Speed");
            toIdle.hasExitTime = false;
            toIdle.duration = 0.12f;

            Debug.Log($"[Bootstrap] Animator clips — idle:{idle.motion?.name} " +
                $"move:{move.motion?.name} 1h:{attack1H.motion?.name} " +
                $"2h:{attack2H.motion?.name} ranged:{attackRanged.motion?.name} " +
                $"cast:{cast.motion?.name} " +
                $"hit:{hit.motion?.name} death:{dead.motion?.name}");

            var anyAttack = sm.AddAnyStateTransition(attack);
            anyAttack.AddCondition(AnimatorConditionMode.If, 0, "Attack");
            anyAttack.hasExitTime = false;
            anyAttack.duration = 0.05f;
            anyAttack.canTransitionToSelf = false;
            var attackDone = attack.AddTransition(idle);
            attackDone.hasExitTime = true;
            attackDone.exitTime = 0.9f;
            attackDone.duration = 0.1f;

            AddAction(sm, attack1H, idle, "Attack1H");
            AddAction(sm, attack2H, idle, "Attack2H");
            AddAction(sm, attackRanged, idle, "AttackRanged");

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
            // Critical: without this, the any-state transition re-enters Dead every
            // frame while the bool is true, freezing the pose at frame 0 (standing).
            anyDead.canTransitionToSelf = false;
            var revive = dead.AddTransition(idle);
            revive.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            revive.hasExitTime = false;
            revive.duration = 0.1f;

            return controller;
        }

        private static void AddAction(AnimatorStateMachine sm, AnimatorState action,
            AnimatorState idle, string trigger)
        {
            var enter = sm.AddAnyStateTransition(action);
            enter.AddCondition(AnimatorConditionMode.If, 0, trigger);
            enter.hasExitTime = false;
            enter.duration = 0.05f;
            enter.canTransitionToSelf = false;
            var done = action.AddTransition(idle);
            done.hasExitTime = true;
            done.exitTime = 0.9f;
            done.duration = 0.1f;
        }

        private static void BuildPrefabs(AnimatorController controller)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(PrefabDir))
                AssetDatabase.CreateFolder("Assets/Resources", "Characters");

            foreach (string folder in new[] { "Characters", "Skeletons" })
            {
                string dir = $"{Root}/{folder}";
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string name = System.IO.Path.GetFileNameWithoutExtension(path);
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (model == null) continue;

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    // Normalize height to ~1.85 m so all bodies match the controller capsule.
                    var renderers = instance.GetComponentsInChildren<Renderer>();
                    if (renderers.Length > 0)
                    {
                        var bounds = renderers[0].bounds;
                        foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                        if (bounds.size.y > 0.01f)
                            instance.transform.localScale =
                                Vector3.one * (1.85f / bounds.size.y);
                    }
                    // Explicit null check — Unity's fake-null objects defeat "??".
                    var animator = instance.GetComponent<Animator>();
                    if (animator == null) animator = instance.AddComponent<Animator>();
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;

                    PrefabUtility.SaveAsPrefabAsset(instance, $"{PrefabDir}/{name}.prefab");
                    Object.DestroyImmediate(instance);
                }
            }
        }
    }
}
