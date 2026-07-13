using System.IO;
using RadiantPool.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RadiantPool.EditorTools
{
    /// <summary>Bakes one icon per item into Resources/ItemIcons/&lt;id&gt;.png.
    ///
    /// Weapons and shields are RENDERED FROM THE MODEL THE CHARACTER ACTUALLY WIELDS
    /// (GameItem.HandModel → Resources/Weapons): the picture in the bag is the thing in your
    /// hand, and a new weapon needs no new art — give it a HandModel and its icon appears.
    /// No CC0 pack ships armour, so those icons are DRAWN IN CODE (original ⇒ no licence
    /// attaches, same reasoning as scripts/make_beasts.py).
    ///
    /// Runs from ProjectBootstrap. The output is ordinary PNG assets, so the game reads them
    /// with Resources.Load and the build needs no editor code. Re-baking is idempotent.</summary>
    public static class ItemIconBaker
    {
        private const string IconDir = "Assets/Resources/ItemIcons";
        private const string WeaponDir = "Assets/Resources/Weapons";
        private const int Size = 128;

        public static void Bake()
        {
            if (!AssetDatabase.IsValidFolder(IconDir))
                AssetDatabase.CreateFolder("Assets/Resources", "ItemIcons");

            int rendered = 0, drawn = 0;
            foreach (var item in GameItem.All.Values)
            {
                var tex = RenderModel(item);
                if (tex != null) rendered++;
                else { tex = DrawIcon(item); if (tex != null) drawn++; }
                if (tex == null)
                {
                    Debug.LogWarning($"[Icons] no icon for '{item.Id}' — the bag will show text");
                    continue;
                }
                File.WriteAllBytes($"{IconDir}/{item.Id}.png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }

            AssetDatabase.Refresh();
            foreach (var item in GameItem.All.Values) Import($"{IconDir}/{item.Id}.png");
            Debug.Log($"[Icons] baked {rendered} from models, {drawn} drawn in code " +
                      $"→ {IconDir}");
        }

        /// <summary>Which model stands in for this item. Weapons name their own; a shield is
        /// worn, not held in the weapon slot, so it has no HandModel and is named here.</summary>
        private static string ModelOf(GameItem item) =>
            !string.IsNullOrEmpty(item.HandModel) ? item.HandModel
            : item.Slot == ItemSlot.Shield ? "shield_badge"
            : null;

        /// <summary>Photographs the model on a stage far above the world — an orthographic
        /// frame this tight cannot catch anything else, and the stage is torn down before the
        /// scene is ever saved. Transparent background; the item is framed from its own
        /// bounds, so a dagger and a two-handed sword both fill the icon.</summary>
        private static Texture2D RenderModel(GameItem item)
        {
            string model = ModelOf(item);
            if (string.IsNullOrEmpty(model)) return null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{WeaponDir}/{model}.prefab");
            if (prefab == null) return null;

            var stage = new GameObject("IconStage");
            stage.transform.position = new Vector3(0f, 5000f, 0f);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, stage.transform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, 28f, 0f);   // a touch of 3/4

            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { Object.DestroyImmediate(stage); return null; }
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            // Look down the item's THINNEST axis: a blade seen edge-on is a line, seen flat
            // it is a sword. The radius of the bounding sphere frames it whatever the roll.
            Vector3 e = bounds.extents;
            Vector3 dir = e.x <= e.y && e.x <= e.z ? Vector3.right
                        : e.y <= e.z ? Vector3.up : Vector3.forward;
            Vector3 up = dir == Vector3.up ? Vector3.forward : Vector3.up;
            float radius = Mathf.Max(0.05f, bounds.extents.magnitude);

            var camGo = new GameObject("IconCam");
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<UniversalAdditionalCameraData>();
            cam.orthographic = true;
            cam.orthographicSize = radius * 1.06f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = radius * 20f + 50f;
            cam.transform.position = bounds.center + dir * (radius * 4f + 2f);
            cam.transform.rotation = Quaternion.LookRotation(-dir, up);
            cam.transform.Rotate(0f, 0f, 32f, Space.Self);   // lie the item across the square

            var lightGo = new GameObject("IconLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.LookRotation(
                Quaternion.Euler(28f, -25f, 0f) * -dir);

            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32)
                { antiAliasing = 8 };
            cam.targetTexture = rt;
            cam.Render();

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            Object.DestroyImmediate(stage);
            return tex;
        }

        // ---------------- code-drawn icons (no model exists for these) ----------------

        private static Texture2D DrawIcon(GameItem item) => item.Id switch
        {
            "leather_armor" => Armor(new Color32(0x8A, 0x5A, 0x33, 0xFF),
                                     new Color32(0x53, 0x33, 0x1B, 0xFF), Weave.Stitched),
            "scale_mail" => Armor(new Color32(0xA8, 0xB0, 0xB8, 0xFF),
                                  new Color32(0x5D, 0x66, 0x70, 0xFF), Weave.Scaled),
            "chain_mail" => Armor(new Color32(0x8E, 0x96, 0xA0, 0xFF),
                                  new Color32(0x4C, 0x53, 0x5B, 0xFF), Weave.Ringed),
            "torch" => Torch(),
            "potion_healing" => Potion(),
            _ => null
        };

        private enum Weave { Stitched, Scaled, Ringed }

        /// <summary>A cuirass: two pauldrons over a tapering body, with the weave of its kind
        /// worked into the plate so leather, scale and chain read apart at 32 px.</summary>
        private static Texture2D Armor(Color body, Color trim, Weave weave)
        {
            var tex = Blank();
            var shade = Color.Lerp(body, Color.black, 0.18f);
            var light = Color.Lerp(body, Color.white, 0.22f);

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                bool inPlate = InPlate(x, y);
                if (!inPlate) continue;

                // Edges get the darker trim; the body is lit from the upper left.
                bool edge = !(InPlate(x - 3, y) && InPlate(x + 3, y)
                              && InPlate(x, y - 3) && InPlate(x, y + 3));
                Color c = edge ? trim : Color.Lerp(light, shade, (x + y) / (2f * Size));

                if (!edge)
                    switch (weave)
                    {
                        case Weave.Stitched:            // seams down the leather
                            if (x % 26 == 0 || (y % 30 == 0 && y > 40)) c = trim;
                            break;
                        case Weave.Scaled:              // overlapping scale rows
                            if ((y % 12 < 2 && (x + (y / 12 % 2) * 6) % 12 > 5)
                                || y % 12 == 0) c = Color.Lerp(c, trim, 0.75f);
                            break;
                        case Weave.Ringed:              // mail rings, offset row to row
                            if ((x + (y / 7 % 2) * 3) % 7 < 2 && y % 7 < 2)
                                c = Color.Lerp(c, trim, 0.65f);
                            break;
                    }
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        /// <summary>The cuirass silhouette, in pixels: body + shoulders − collar. Texture
        /// space runs bottom-up, so the HEM is low y and the COLLAR is high y — get that
        /// backwards (as the first cut of this did) and the armour comes out as a dress,
        /// narrow at the shoulders and flared at the hem.</summary>
        private static bool InPlate(int x, int y)
        {
            const float hem = 20f, collar = Size - 22f;
            float cx = Size / 2f;
            float t = Mathf.InverseLerp(hem, collar, y);   // 0 at the hem, 1 at the collar
            if (y < hem || y > collar) return false;

            float halfWidth = Mathf.Lerp(0.22f, 0.34f, t) * Size;   // waist → broad shoulders
            bool body = Mathf.Abs(x - cx) <= halfWidth;
            bool pauldron = InCircle(x, y, cx - 0.30f * Size, collar - 12f, 16f)
                            || InCircle(x, y, cx + 0.30f * Size, collar - 12f, 16f);
            bool collarCut = InCircle(x, y, cx, collar + 4f, 13f);
            return (body || pauldron) && !collarCut;
        }

        private static Texture2D Torch()
        {
            var tex = Blank();
            var wood = new Color32(0x6B, 0x47, 0x2A, 0xFF);
            var grain = new Color32(0x4A, 0x30, 0x1C, 0xFF);
            var flame = new Color32(0xF2, 0x8B, 0x24, 0xFF);
            var core = new Color32(0xFF, 0xE3, 0x86, 0xFF);

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                // Shaft: a slim column standing in the lower two thirds.
                if (y < 76 && Mathf.Abs(x - Size / 2f) <= 8f)
                    tex.SetPixel(x, y, Mathf.Abs(x - Size / 2f) > 6f ? grain : (Color)wood);

                // Flame: a teardrop over the head of the shaft, hot in the middle.
                float fx = (x - Size / 2f) / 22f;
                float fy = (y - 92f) / 30f;
                float d = fx * fx + fy * fy * (fy > 0f ? 1.6f : 0.7f);
                if (d <= 1f)
                    tex.SetPixel(x, y, d < 0.35f ? (Color)core : Color.Lerp(core, flame, d));
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D Potion()
        {
            var tex = Blank();
            var glass = new Color32(0x9E, 0x2B, 0x33, 0xFF);
            var shine = new Color32(0xE8, 0x6A, 0x6F, 0xFF);
            var cork = new Color32(0x8A, 0x63, 0x3A, 0xFF);

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = (x - Size / 2f) / 34f, dy = (y - 46f) / 36f;
                bool flask = dx * dx + dy * dy <= 1f;                       // round belly
                bool neck = Mathf.Abs(x - Size / 2f) <= 11f && y >= 78 && y <= 104;
                bool stopper = Mathf.Abs(x - Size / 2f) <= 14f && y > 104 && y <= 116;
                if (stopper) tex.SetPixel(x, y, cork);
                else if (flask || neck)
                    tex.SetPixel(x, y, dx < -0.35f && dy > 0.1f ? (Color)shine : (Color)glass);
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D Blank()
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            var pixels = new Color[Size * Size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
            tex.SetPixels(pixels);
            return tex;
        }

        private static bool InCircle(int x, int y, float cx, float cy, float r) =>
            (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;

        /// <summary>UI art, not world art: no mips, no compression blocks eating the alpha.</summary>
        private static void Import(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.GUI;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
