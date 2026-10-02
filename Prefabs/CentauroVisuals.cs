using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimMontarias.Prefabs
{
    internal static class CentauroVisuals
    {
        public const string OverlayName = "CentauroVisual";
        public const float TargetHeight = 2.35f;

        private static CapybaraSkin _skin;
        private static Mesh _staticMesh;
        private static Texture2D _albedo;
        private static bool _loggedMissing;

        public static void Apply(GameObject go)
        {
            if (go == null) return;
            MountGear.RescueAttach(go);
            var parent = MountHub.FindVisual(go) ?? go.transform;
            var existing = parent.Find(OverlayName);
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);

            var skin = Skin();
            if (skin != null && skin.Mesh != null)
            {
                ApplySkinned(go, parent, skin);
                return;
            }

            ApplyStatic(go, parent);
        }

        private static void ApplySkinned(GameObject go, Transform parent, CapybaraSkin skin)
        {
            var overlay = new GameObject(OverlayName);
            overlay.transform.SetParent(parent, false);
            overlay.transform.localRotation = Quaternion.identity;
            var bounds = skin.Mesh.bounds;
            float height = Mathf.Max(0.01f, bounds.size.y);
            float scale = TargetHeight / height;
            overlay.transform.localScale = Vector3.one * scale;
            overlay.transform.localPosition = new Vector3(
                -bounds.center.x * scale,
                -bounds.min.y * scale,
                -bounds.center.z * scale);

            var bones = BuildBones(overlay.transform, skin);
            var renderer = overlay.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = skin.Mesh;
            renderer.bones = bones;
            renderer.rootBone = RootBone(bones, skin);
            renderer.quality = SkinQuality.Bone4;
            renderer.updateWhenOffscreen = true;
            renderer.sharedMaterial = MakeMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            var localBounds = skin.Mesh.bounds;
            localBounds.Expand(Mathf.Max(1.5f, localBounds.extents.magnitude));
            renderer.localBounds = localBounds;

            var player = overlay.AddComponent<CapybaraClipPlayer>();
            player.Setup(bones, skin.Clips);

            HideVanillaMeshes(go);
            DisableLod(go);
            // Horse back in mesh space: behind the human torso (+Z), on the barrel.
            MountGear.Attach(go, overlay.transform, 0.68f, -0.40f, new Vector3(0f, 0.53f, -0.06f));
        }

        private static void ApplyStatic(GameObject go, Transform parent)
        {
            var mesh = StaticMesh();
            if (mesh == null)
            {
                if (!_loggedMissing)
                {
                    _loggedMissing = true;
                    Plugin.Log.LogWarning("ValheimMontarias: centauro.skin/bin not found, keeping vanilla mesh");
                }
                return;
            }

            float height = Mathf.Max(0.01f, mesh.bounds.size.y);
            float scale = TargetHeight / height;

            var overlay = new GameObject(OverlayName);
            overlay.transform.SetParent(parent, false);
            overlay.transform.localRotation = Quaternion.identity;
            overlay.transform.localScale = Vector3.one * scale;
            overlay.transform.localPosition = new Vector3(
                -mesh.bounds.center.x * scale,
                -mesh.bounds.min.y * scale,
                -mesh.bounds.center.z * scale);

            overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = overlay.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MakeMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.On;

            HideVanillaMeshes(go);
            DisableLod(go);
            MountGear.Attach(go, overlay.transform, 0.62f, 0.14f, Vector3.zero);
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
                string name = skin.Bones[i].Name;
                if (name == "root" || name == "Root" || name == "DEF-hips" || name == "hips")
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
            var path = AssetPath("centauro.skin");
            if (!File.Exists(path)) return null;
            _skin = CapybaraSkin.Load(File.ReadAllBytes(path));
            return _skin;
        }

        private static Mesh StaticMesh()
        {
            if (_staticMesh != null) return _staticMesh;
            var path = AssetPath("centauro.bin");
            if (!File.Exists(path)) return null;
            _staticMesh = LoadPeca(File.ReadAllBytes(path));
            return _staticMesh;
        }

        private static Mesh LoadPeca(byte[] data)
        {
            if (data == null || data.Length < 12) return null;
            if (data[0] != (byte)'P' || data[1] != (byte)'E' || data[2] != (byte)'C' || data[3] != (byte)'A')
                return null;

            int offset = 4;
            int vertCount = System.BitConverter.ToInt32(data, offset); offset += 4;
            int triCount = System.BitConverter.ToInt32(data, offset); offset += 4;

            var vertices = new Vector3[vertCount];
            for (int i = 0; i < vertCount; i++)
            {
                vertices[i] = new Vector3(
                    System.BitConverter.ToSingle(data, offset),
                    System.BitConverter.ToSingle(data, offset + 4),
                    System.BitConverter.ToSingle(data, offset + 8));
                offset += 12;
            }

            var uvs = new Vector2[vertCount];
            for (int i = 0; i < vertCount; i++)
            {
                uvs[i] = new Vector2(
                    System.BitConverter.ToSingle(data, offset),
                    System.BitConverter.ToSingle(data, offset + 4));
                offset += 8;
            }

            var triangles = new int[triCount * 3];
            for (int i = 0; i < triangles.Length; i++)
            {
                triangles[i] = System.BitConverter.ToInt32(data, offset);
                offset += 4;
            }

            var mesh = new Mesh
            {
                name = "Centauro",
                indexFormat = vertCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            ApplySmoothNormals(mesh);
            mesh.RecalculateBounds();
            Plugin.Log.LogInfo($"ValheimMontarias: loaded static centaur mesh ({vertCount} verts, {triCount} tris)");
            return mesh;
        }

        private static void ApplySmoothNormals(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var accum = new Vector3[vertices.Length];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                accum[a] += n;
                accum[b] += n;
                accum[c] += n;
            }

            var groups = new Dictionary<long, List<int>>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                long key = ((long)Mathf.Round(v.x * 8000f) & 0x1FFFFF)
                    | (((long)Mathf.Round(v.y * 8000f) & 0x1FFFFF) << 21)
                    | (((long)Mathf.Round(v.z * 8000f) & 0x1FFFFF) << 42);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<int>(4);
                    groups[key] = list;
                }
                list.Add(i);
            }

            var normals = new Vector3[vertices.Length];
            foreach (var list in groups.Values)
            {
                Vector3 n = Vector3.zero;
                for (int i = 0; i < list.Count; i++)
                    n += accum[list[i]];
                if (n.sqrMagnitude < 1e-12f)
                    n = Vector3.up;
                n.Normalize();
                for (int i = 0; i < list.Count; i++)
                    normals[list[i]] = n;
            }
            mesh.normals = normals;
        }

        private static Texture2D Albedo()
        {
            if (_albedo != null) return _albedo;
            var path = AssetPath("centauro.png");
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!TryLoadImage(tex, File.ReadAllBytes(path)))
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
            return Path.Combine(Paths.PluginPath, "ValheimMontarias", "Assets", fileName);
        }
    }
}
