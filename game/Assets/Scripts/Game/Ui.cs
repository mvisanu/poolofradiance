using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>IMGUI scaling for readable text on any display: call Begin() first in
    /// every OnGUI, and lay out against W/H instead of Screen.width/height. Scales the
    /// whole GUI with resolution (720p = 1.15x baseline, 4K ≈ 3.4x) and enforces a
    /// legible base font.</summary>
    public static class Ui
    {
        public static float Scale => Mathf.Max(1.15f, Screen.height / 630f);
        public static float W => Screen.width / Scale;
        public static float H => Screen.height / Scale;

        public static void Begin()
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
            GUI.skin.label.fontSize = 15;
            GUI.skin.button.fontSize = 15;
            GUI.skin.textField.fontSize = 15;
            GUI.skin.box.fontSize = 15;
            GUI.skin.toggle.fontSize = 15;
        }
    }
}
