using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimMontarias.Prefabs
{
    public sealed class MountSaddleAnchor : MonoBehaviour
    {
        public float SitHeight = 0.12f;
    }

    internal static class MountGear
    {
        public const string VisualName = "MountSaddleVisual";

        private static Mesh _saddleMesh;
        private static Mesh _centeredMesh;
        private static Material _saddleMat;
        private static Quaternion _saddleRot = Quaternion.identity;

        public static void Capture(GameObject source)
        {
            if (_centeredMesh != null || source == null) return;
            CaptureFromTameable(source);
            if (_saddleMesh == null)
                TryCapture(source, true);
            if (_saddleMesh == null)
            {
                var sadle = source.GetComponentInChildren<Sadle>(true);
                if (sadle != null)
                    TryCapture(sadle.gameObject, true);
            }
            FinishMesh();
        }

        public static void CaptureFromDb()
        {
            if (_centeredMesh != null) return;
            TryCaptureItem("SaddleLox");
            TryCaptureItem("SaddleAsksvin");
            TryCaptureItem("LoxSaddle");
            TryCaptureItem("AsksvinSaddle");
            ScanPrefabsForSaddle();
            FinishMesh();
        }

        private static void EnsureCaptured()
        {
            if (_centeredMesh != null) return;
            var scene = ZNetScene.instance;
            if (scene != null)
            {
                var lox = scene.GetPrefab("Lox") ?? scene.GetPrefab("Asksvin");
                if (lox != null)
                    Capture(lox);
            }
            if (_centeredMesh != null) return;
            CaptureFromDb();
            if (_centeredMesh == null)
                Plugin.Log?.LogWarning("ValheimMontarias: SaddleLox mesh not found, saddle stays as cube");
        }

        private static void CaptureFromTameable(GameObject creature)
        {
            var tame = creature.GetComponent<Tameable>();
            if (tame == null) return;
            var item = Access.Get(tame, "m_saddleItem");
            if (item is GameObject go)
                TryCapture(go, false);
            else if (item is ItemDrop drop && drop.gameObject != null)
                TryCapture(drop.gameObject, false);
        }

        private static void ScanPrefabsForSaddle()
        {
            if (_saddleMesh != null) return;
            var db = ObjectDB.instance;
            if (db != null && db.m_items != null)
            {
                for (int i = 0; i < db.m_items.Count; i++)
                {
                    var item = db.m_items[i];
                    if (item == null || !NameLooksSaddle(item.transform)) continue;
                    TryCapture(item, false);
                    if (_saddleMesh != null) return;
                }
            }
            var scene = ZNetScene.instance;
            if (scene == null || scene.m_prefabs == null) return;
            for (int i = 0; i < scene.m_prefabs.Count; i++)
            {
                var prefab = scene.m_prefabs[i];
                if (prefab == null || !NameLooksSaddle(prefab.transform)) continue;
                if (prefab.GetComponent<Character>() != null) continue;
                TryCapture(prefab, false);
                if (_saddleMesh != null) return;
            }
        }

        private static void FinishMesh()
        {
            if (_saddleMesh == null || _centeredMesh != null) return;
            _centeredMesh = CenterMesh(_saddleMesh);
            Plugin.Log?.LogInfo($"ValheimMontarias: Lox saddle mesh '{_saddleMesh.name}' verts={_saddleMesh.vertexCount}");
        }

        private static void TryCaptureItem(string prefab)
        {
            if (_saddleMesh != null) return;
            var db = ObjectDB.instance;
            if (db == null) return;
            var item = db.GetItemPrefab(prefab);
            if (item != null)
                TryCapture(item, false);
        }

        private static void TryCapture(GameObject source, bool nameMustMatch)
        {
            int bestVerts = 0;
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                if (nameMustMatch && !NameLooksSaddle(filter.transform)) continue;
                if (filter.sharedMesh.vertexCount <= bestVerts) continue;
                _saddleMesh = filter.sharedMesh;
                bestVerts = filter.sharedMesh.vertexCount;
                var renderer = filter.GetComponent<Renderer>();
                if (renderer != null)
                {
                    _saddleMat = renderer.sharedMaterial;
                    _saddleRot = renderer.transform.localRotation;
                }
            }
            foreach (var skin in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                if (nameMustMatch && !NameLooksSaddle(skin.transform)) continue;
                if (skin.sharedMesh.vertexCount <= bestVerts) continue;
                _saddleMesh = skin.sharedMesh;
                bestVerts = skin.sharedMesh.vertexCount;
                _saddleMat = skin.sharedMaterial;
                _saddleRot = skin.transform.localRotation;
            }
        }

        private static bool NameLooksSaddle(Transform t)
        {
            while (t != null)
            {
                if (t.name.IndexOf("saddle", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || t.name.IndexOf("sela", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                t = t.parent;
            }
            return false;
        }

        public static Transform RescueAttach(GameObject mount)
        {
            if (mount == null) return null;
            var attach = MountHub.FindAttach(mount);
            if (attach == null)
            {
                attach = new GameObject("AttachPoint").transform;
                attach.SetParent(mount.transform, false);
                var sadle = mount.GetComponentInChildren<Sadle>(true);
                if (sadle != null)
                    Access.Set(sadle, "m_attachPoint", attach);
                return attach;
            }
            if (attach.parent != mount.transform)
                attach.SetParent(mount.transform, true);
            return attach;
        }

        public static void Attach(GameObject mount, Transform overlay, float saddleLength, float sitHeight, Vector3 overlayLocal, bool useCapturedRotation = true)
        {
            if (overlay == null || mount == null) return;
            RescueAttach(mount);
            EnsureCaptured();

            var existing = overlay.Find(VisualName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            if (overlayLocal.sqrMagnitude < 1e-6f)
                overlayLocal = BackPoint(overlay);

            var saddle = new GameObject(VisualName);
            saddle.transform.SetParent(overlay, false);
            saddle.transform.localPosition = overlayLocal;
            saddle.transform.localRotation = (useCapturedRotation && _centeredMesh != null)
                ? _saddleRot
                : Quaternion.identity;

            Vector3 parentScale = overlay.lossyScale;
            float sx = Mathf.Max(0.01f, Mathf.Abs(parentScale.x));
            float sy = Mathf.Max(0.01f, Mathf.Abs(parentScale.y));
            float sz = Mathf.Max(0.01f, Mathf.Abs(parentScale.z));

            Mesh mesh = VisualMesh();
            var b = mesh.bounds;
            float have = Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
            float uniform = saddleLength / (have * sx);
            if (_centeredMesh != null)
                saddle.transform.localScale = Vector3.one * uniform;
            else
                saddle.transform.localScale = new Vector3(
                    saddleLength / sx,
                    0.11f / sy,
                    (saddleLength * 0.72f) / sz);

            var filter = saddle.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = saddle.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _saddleMat != null ? _saddleMat : FallbackMat();
            renderer.shadowCastingMode = ShadowCastingMode.On;

            saddle.AddComponent<MountSaddleAnchor>().SitHeight = sitHeight;
            Fit(mount);
        }

        public static void Fit(GameObject mount)
        {
            if (mount == null) return;
            var attach = RescueAttach(mount);
            if (attach == null) return;

            var saddle = FindSaddle(mount);
            if (saddle == null) return;

            float sit = 0.12f;
            var anchor = saddle.GetComponent<MountSaddleAnchor>();
            if (anchor != null)
                sit = anchor.SitHeight;

            Vector3 world = saddle.position + mount.transform.up * sit;
            attach.localPosition = mount.transform.InverseTransformPoint(world);
            attach.localRotation = Quaternion.identity;
        }

        public static bool IsSaddle(Transform t)
        {
            while (t != null)
            {
                if (t.name == VisualName) return true;
                t = t.parent;
            }
            return false;
        }

        private static Transform FindSaddle(GameObject mount)
        {
            if (mount == null) return null;
            var all = mount.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == VisualName)
                    return all[i];
            }
            return null;
        }

        private static Vector3 BackPoint(Transform overlay)
        {
            var skin = overlay.GetComponent<SkinnedMeshRenderer>();
            var mesh = skin != null ? skin.sharedMesh : overlay.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null)
                return new Vector3(0f, 0.55f, 0.05f);
            var b = mesh.bounds;
            return new Vector3(0f, b.min.y + b.size.y * 0.88f, b.center.z);
        }

        private static Mesh VisualMesh()
        {
            return _centeredMesh != null ? _centeredMesh : FallbackCube();
        }

        private static Mesh CenterMesh(Mesh src)
        {
            if (src == null) return null;
            Vector3[] verts;
            try
            {
                verts = src.vertices;
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"ValheimMontarias: saddle mesh not readable ({e.Message})");
                return src;
            }
            if (verts == null || verts.Length == 0) return src;
            var mesh = new Mesh { name = "MountSaddleCentered", indexFormat = src.indexFormat };
            var bounds = src.bounds;
            var pivot = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            for (int i = 0; i < verts.Length; i++)
                verts[i] -= pivot;
            mesh.vertices = verts;
            if (src.normals != null && src.normals.Length == verts.Length)
                mesh.normals = src.normals;
            if (src.uv != null && src.uv.Length == verts.Length)
                mesh.uv = src.uv;
            mesh.triangles = src.triangles;
            mesh.RecalculateBounds();
            if (src.normals == null || src.normals.Length != verts.Length)
                mesh.RecalculateNormals();
            return mesh;
        }

        private static Mesh FallbackCube()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(cube);
            return mesh;
        }

        private static Material FallbackMat()
        {
            var shader = Shader.Find("Unlit/Color")
                ?? Shader.Find("Unlit/Texture")
                ?? Shader.Find("Legacy Shaders/Diffuse")
                ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            var leather = new Color(0.42f, 0.22f, 0.10f);
            if (mat.HasProperty("_Color"))
                mat.color = leather;
            else if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", leather);
            return mat;
        }
    }
}
