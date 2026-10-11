using UnityEngine;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias
{
    internal static class CastHud
    {
        private const float BarWidth = 300f;
        private const float BarHeight = 24f;
        private const float BottomOffset = 100f;

        private static float _smoothProgress;
        private static float _animationTime;
        private static float _remainingTime;

        private static GUIStyle _labelStyle;
        private static Texture2D _roundedTexture;
        private static bool _initialized;

        public static void Draw()
        {
            float progress = Mathf.Clamp01(JavaliControl.CastProgress);

            if (progress <= 0f)
            {
                _smoothProgress = 0f;
                _remainingTime = 0f;
                return;
            }

            Initialize();

            // Tempo independente da taxa de quadros.
            float deltaTime = Time.unscaledDeltaTime;
            _animationTime += deltaTime;

            // Suaviza o preenchimento da barra.
            _smoothProgress = Mathf.Lerp(
                _smoothProgress,
                progress,
                1f - Mathf.Exp(-12f * deltaTime)
            );

            // Estimativa do tempo restante em segundos.
            _remainingTime = Mathf.Max(
                0f,
                (1f - progress) * JavaliControl.CastDuration
            );

            float width = Mathf.Min(BarWidth, Screen.width - 32f);
            float height = BarHeight;

            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height - BottomOffset;

            Rect barRect = new Rect(x, y, width, height);

            float pulse = 0.5f + 0.5f *
                Mathf.Sin(_animationTime * 4f);

            // Azul no início, dourado no meio e verde no final.
            Color baseColor;

            if (progress < 0.5f)
            {
                baseColor = Color.Lerp(
                    new Color(0.20f, 0.65f, 1f),
                    new Color(0.95f, 0.72f, 0.25f),
                    progress * 2f
                );
            }
            else
            {
                baseColor = Color.Lerp(
                    new Color(0.95f, 0.72f, 0.25f),
                    new Color(0.35f, 1f, 0.55f),
                    (progress - 0.5f) * 2f
                );
            }

            // Sombra externa.
            DrawRounded(
                new Rect(x - 4f, y - 4f, width + 8f, height + 8f),
                new Color(0f, 0f, 0f, 0.70f),
                1f
            );

            // Halo pulsante.
            Color glowColor = new Color(
                baseColor.r,
                baseColor.g,
                baseColor.b,
                0.10f + pulse * 0.12f
            );

            DrawRounded(
                new Rect(x - 2f, y - 2f, width + 4f, height + 4f),
                glowColor,
                1f
            );

            // Fundo da barra.
            DrawRounded(
                barRect,
                new Color(0.045f, 0.055f, 0.075f, 0.96f),
                1f
            );

            // Preenchimento.
            float fillWidth = width * _smoothProgress;

            if (fillWidth > 0.5f)
            {
                DrawRounded(
                    new Rect(x, y, fillWidth, height),
                    new Color(
                        baseColor.r,
                        baseColor.g,
                        baseColor.b,
                        0.95f + pulse * 0.05f
                    ),
                    1f
                );

                // Reflexo animado sobre o preenchimento.
                float shinePosition = Mathf.Repeat(
                    _animationTime * 110f,
                    width + 40f
                ) - 20f;

                float shineX = x + shinePosition;

                if (shineX < x + fillWidth &&
                    shineX + 18f > x)
                {
                    float clippedLeft = Mathf.Max(shineX, x);
                    float clippedRight = Mathf.Min(
                        shineX + 18f,
                        x + fillWidth
                    );

                    if (clippedRight > clippedLeft)
                    {
                        DrawRounded(
                            new Rect(
                                clippedLeft,
                                y + 3f,
                                clippedRight - clippedLeft,
                                height - 6f
                            ),
                            new Color(1f, 1f, 1f, 0.22f * pulse),
                            1f
                        );
                    }
                }
            }

            // Borda superior discreta.
            DrawRounded(
                new Rect(x + 2f, y + 1f, width - 4f, 1f),
                new Color(1f, 1f, 1f, 0.22f),
                1f
            );

            // Texto central com o tempo restante.
            GUI.color = Color.white;

            string text = progress >= 0.999f
                ? "Montaria invocada!"
                : $"Invocando montaria... {_remainingTime:0.0}s";

            GUI.Label(barRect, text, _labelStyle);

            GUI.color = Color.white;
        }

        private static void Initialize()
        {
            if (_initialized)
                return;

            // Uma única textura reutilizada em todos os desenhos.
            _roundedTexture = CreateRoundedTexture(64, 64, 12f);

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                clipping = TextClipping.Clip
            };

            _labelStyle.normal.textColor = Color.white;
            _labelStyle.hover.textColor = Color.white;
            _labelStyle.focused.textColor = Color.white;
            _labelStyle.active.textColor = Color.white;

            _initialized = true;
        }

        private static Texture2D CreateRoundedTexture(
            int width,
            int height,
            float radius)
        {
            var texture = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false
            );

            texture.name = "CastHud_Rounded";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;

            Color[] pixels = new Color[width * height];

            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    float cx = Mathf.Clamp(
                        px + 0.5f,
                        radius,
                        width - radius
                    );

                    float cy = Mathf.Clamp(
                        py + 0.5f,
                        radius,
                        height - radius
                    );

                    float distance = Vector2.Distance(
                        new Vector2(px + 0.5f, py + 0.5f),
                        new Vector2(cx, cy)
                    );

                    float alpha = 1f - Mathf.SmoothStep(
                        radius - 1f,
                        radius + 1f,
                        distance
                    );

                    pixels[py * width + px] =
                        new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);

            return texture;
        }

        private static void DrawRounded(Rect rect, Color color, float unused)
        {
            if (rect.width <= 0f || rect.height <= 0f)
                return;

            GUI.color = color;

            // A mesma textura é reutilizada, sem alocações por frame.
            GUI.DrawTexture(rect, _roundedTexture);

            GUI.color = Color.white;
        }
    }
}