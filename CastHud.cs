using UnityEngine;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias
{
    internal static class CastHud
    {
        public static void Draw()
        {
            float progress = JavaliControl.CastProgress;
            if (progress <= 0f) return;

            float width = Screen.width * 0.38f;
            float height = 22f;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height - 96f;

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(x - 2f, y - 2f, width + 4f, height + 4f), Texture2D.whiteTexture);
            GUI.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
            GUI.color = new Color(0.85f, 0.72f, 0.25f, 0.95f);
            GUI.DrawTexture(new Rect(x, y, width * Mathf.Clamp01(progress), height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 14
            };
            GUI.Label(new Rect(x, y, width, height), "Invocando montaria...", style);
        }
    }
}
