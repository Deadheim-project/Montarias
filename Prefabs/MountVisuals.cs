using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimMontarias.Prefabs
{
    internal static class MountVisuals
    {
        public const string OverlayName = "CapybaraVisual";

        private static CapybaraSkin _skin;
        private static Texture2D _albedo;
        private static bool _loggedMissing;

        public static void Apply(GameObject go)
        {
            if (go == null) return;
            MountGear.RescueAttach(go);
            var parent = FindVisual(go) ?? go.transform;
            var existing = parent.Find(OverlayName);
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);

            var skin = Skin();
            if (skin == null || skin.Mesh == null)
            {
                if (!_loggedMissing)
                {
                    _loggedMissing = true;
                    Plugin.Log.LogWarning("ValheimMontarias: capybara.skin not found, keeping vanilla boar mesh");
                }
                return;
            }

            var overlay = new GameObject(OverlayName);
            overlay.transform.SetParent(parent, false);
            overlay.transform.localPosition = Vector3.zero;
            overlay.transform.localRotation = Quaternion.identity;
            overlay.transform.localScale = Vector3.one * skin.UnityScale;

            var bones = BuildBones(overlay.transform, skin);
            var renderer = overlay.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = skin.Mesh;
            renderer.bones = bones;
            renderer.rootBone = RootBone(bones, skin);
            renderer.quality = SkinQuality.Bone4;
            renderer.updateWhenOffscreen = true;
            renderer.sharedMaterial = MakeMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            if (skin.Mesh != null)
                renderer.localBounds = skin.Mesh.bounds;

            var player = overlay.AddComponent<CapybaraClipPlayer>();
            player.Setup(bones, skin.Clips);

            HideVanillaMeshes(go);
            DisableLod(go);
            // Bind-pose rump, not AABB top (that lands on the head).
            MountGear.Attach(go, overlay.transform, 0.52f, -0.38f, new Vector3(0.23f, 5.2f, -0.40f), false);
        }

        public static Transform Overlay(GameObject go)
        {
            if (go == null) return null;
            var found = go.transform.Find(OverlayName);
            if (found != null) return found;
            var all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == OverlayName)
                    return all[i];
            }
            return null;
        }

        private static Transform[] BuildBones(Transform root, CapybaraSkin skin)
        {
            var bones = new Transform[skin.Bones.Length];
            for (int i = 0; i < skin.Bones.Length; i++)
            {
                var go = new GameObject(skin.Bones[i].Name);
                bones[i] = go.transform;
            }

            for (int i = 0; i < skin.Bones.Length; i++)
            {
                int parent = skin.Bones[i].Parent;
                bones[i].SetParent(parent >= 0 && parent < bones.Length ? bones[parent] : root, false);
                bones[i].localPosition = skin.Bones[i].RestT;
                bones[i].localRotation = skin.Bones[i].RestQ;
                bones[i].localScale = skin.Bones[i].RestS;
            }
            return bones;
        }

        private static Transform RootBone(Transform[] bones, CapybaraSkin skin)
        {
            for (int i = 0; i < skin.Bones.Length; i++)
            {
                if (skin.Bones[i].Name == "Root")
                    return bones[i];
            }
            return bones.Length > 0 ? bones[0] : null;
        }

        private static void HideVanillaMeshes(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                if (renderer.GetComponentInParent<Player>() != null) continue;
                if (OverlayParent(renderer.transform)) continue;
                if (MountGear.IsSaddle(renderer.transform)) continue;
                renderer.enabled = false;
            }
        }

        private static bool OverlayParent(Transform t)
        {
            while (t != null)
            {
                if (t.name == OverlayName) return true;
                t = t.parent;
            }
            return false;
        }

        private static void DisableLod(GameObject go)
        {
            var character = go.GetComponent<Character>();
            if (Access.Get(character, "m_lodGroup") is LODGroup lod)
                lod.enabled = false;
        }

        private static Transform FindVisual(GameObject go)
        {
            var character = go.GetComponent<Character>();
            if (Access.Get(character, "m_visual") is GameObject visual && visual != null)
                return visual.transform;
            return go.transform.Find("Visual");
        }

        private static Material MakeMaterial()
        {
            Shader shader = null;
            foreach (var name in new[]
            {
                "Unlit/Texture",
                "Particles/Standard Unlit",
                "Legacy Shaders/Diffuse",
                "Diffuse",
                "Standard",
                "Sprites/Default"
            })
            {
                shader = Shader.Find(name);
                if (shader != null)
                    break;
            }

            var mat = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
            if (mat.HasProperty("_Color"))
                mat.color = Color.white;
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", 0.15f);
            if (mat.HasProperty("_MainTex_ST"))
                mat.SetVector("_MainTex_ST", new Vector4(1f, 1f, 0f, 0f));

            var albedo = Albedo();
            if (albedo != null)
            {
                mat.mainTexture = albedo;
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", albedo);
            }
            return mat;
        }

        private static CapybaraSkin Skin()
        {
            if (_skin != null) return _skin;
            var path = AssetPath("capybara.skin");
            if (!File.Exists(path)) return null;
            _skin = CapybaraSkin.Load(File.ReadAllBytes(path));
            return _skin;
        }

        private static Texture2D Albedo()
        {
            if (_albedo != null) return _albedo;
            var path = AssetPath("capybara.png");
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!TryLoadImage(tex, bytes))
                return null;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            _albedo = tex;
            return _albedo;
        }

        private static bool TryLoadImage(Texture2D tex, byte[] bytes)
        {
            try
            {
                var type = System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", false);
                var method = type?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                if (method == null) return false;
                return (bool)method.Invoke(null, new object[] { tex, bytes });
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: LoadImage failed: {e.Message}");
                return false;
            }
        }

        private static string AssetPath(string fileName)
        {
            return Path.Combine(Plugin.AssetsDir, fileName);
        }
    }
}
