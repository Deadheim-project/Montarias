using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

namespace ValheimMontarias.Prefabs
{
    // After MountClipPlayer poses the skin bones, before RiderVisualFix (10000) reads the seat.
    [DefaultExecutionOrder(1000)]
    public sealed class MountSaddleAnchor : MonoBehaviour
    {
        public float SitHeight = 0.12f;

        /// <summary>Set when the saddle rides a bone of the body (every mount sways its body
        /// while walking/idling): the seat is refitted after each animated frame so the rider
        /// goes along with the saddle.</summary>
        public GameObject FollowMount;

        /// <summary>Set for mounts whose resting pose is far from the bind pose the saddle was
        /// placed on (the Moder lies down): a few frames after it shows, the saddle is put back
        /// on top of the posed body (<see cref="MountGear.SitOnPosedBack"/>).</summary>
        public bool FitToPose;
        private int _fitAtFrame = -1;

        private void LateUpdate()
        {
            if (FitToPose && FollowMount != null)
            {
                if (_fitAtFrame < 0) _fitAtFrame = Time.frameCount + 5;
                else if (Time.frameCount >= _fitAtFrame && AnyRendererOn(FollowMount))
                {
                    FitToPose = false;
                    MountGear.SitOnPosedBack(FollowMount, transform);
                }
            }
            if (FollowMount != null)
                MountGear.Fit(FollowMount, transform);
        }

        /// <summary>The mount is born hidden; its animator may not pose it until it shows.</summary>
        private static bool AnyRendererOn(GameObject mount)
        {
            foreach (var renderer in mount.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (renderer.enabled && !MountGear.IsSaddle(renderer.transform)) return true;
            return false;
        }
    }

    internal static class MountGear
    {
        public const string VisualName = "MountSaddleVisual";

        private sealed class SaddleVisual
        {
            public Mesh Mesh;
            public Material Material;
            public Quaternion Rotation;
        }

        private static Mesh _saddleMesh;
        private static Mesh _centeredMesh;
        private static Material _saddleMat;
        private static Quaternion _saddleRot = Quaternion.identity;
        private static readonly Dictionary<string, SaddleVisual> _configuredVisuals = new Dictionary<string, SaddleVisual>();

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

            // FindSaddle, not overlay.Find: a saddle that follows the body is under a bone.
            var existing = FindSaddle(mount);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            if (overlayLocal.sqrMagnitude < 1e-6f)
                overlayLocal = SaddleBackPoint(overlay);

            var saddle = new GameObject(VisualName);
            saddle.transform.SetParent(overlay, false);
            saddle.transform.localPosition = overlayLocal;
            var profile = MountSettings.Find(mount);
            var configured = ConfiguredVisual(profile);
            Mesh mesh = configured?.Mesh ?? VisualMesh();
            var baseRotation = useCapturedRotation
                ? configured?.Rotation ?? _saddleRot
                : Quaternion.identity;
            saddle.transform.localRotation = baseRotation * MountSettings.SaddleRotationFor(profile);

            Vector3 parentScale = overlay.lossyScale;
            float sx = Mathf.Max(0.01f, Mathf.Abs(parentScale.x));
            float sy = Mathf.Max(0.01f, Mathf.Abs(parentScale.y));
            float sz = Mathf.Max(0.01f, Mathf.Abs(parentScale.z));

            var b = mesh.bounds;
            float have = Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
            float uniform = saddleLength / (have * sx);
            if (configured != null || _centeredMesh != null)
                saddle.transform.localScale = Vector3.one * uniform;
            else
                saddle.transform.localScale = new Vector3(
                    saddleLength / sx,
                    0.11f / sy,
                    (saddleLength * 0.72f) / sz);

            var filter = saddle.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = saddle.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = configured?.Material ?? (_saddleMat != null ? _saddleMat : FallbackMat());
            renderer.shadowCastingMode = ShadowCastingMode.On;

            var anchor = saddle.AddComponent<MountSaddleAnchor>();
            anchor.SitHeight = sitHeight;
            if (FollowBone(mount, saddle.transform))
            {
                anchor.FollowMount = mount;
                anchor.FitToPose = profile != null && profile.Flies;
            }
            Fit(mount);
        }

        /// <summary>
        /// Puts the saddle on the highest point of the posed body right around it. The saddle
        /// was placed on the bind pose's back; a body that rests in another pose (the Moder lies
        /// down) took it, and the rider, through itself or far above. The mesh is baked once in
        /// its current pose; SelaOffsetY still applies on top.
        /// </summary>
        internal static void SitOnPosedBack(GameObject mount, Transform saddle)
        {
            if (mount == null || saddle == null) return;
            var overlay = MountVisuals.Overlay(mount);
            var root = overlay != null ? overlay : mount.transform;
            var points = new System.Collections.Generic.List<Vector3>();
            var baked = new Mesh();
            try
            {
                foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin == null || skin.sharedMesh == null || IsSaddle(skin.transform)) continue;
                    if (skin.GetComponentInParent<Player>() != null) continue;
                    // Baked without the renderer's scale, then the renderer's full matrix: the
                    // Moder's mesh is in centimetres under a scaled transform.
                    skin.BakeMesh(baked, false);
                    var vertices = baked.vertices;
                    var toWorld = skin.transform.localToWorldMatrix;
                    for (int i = 0; i < vertices.Length; i++)
                        points.Add(toWorld.MultiplyPoint3x4(vertices[i]));
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"ValheimMontarias: could not read the posed body of {mount.name}: {e.Message}");
                return;
            }
            finally
            {
                Object.Destroy(baked);
            }
            if (points.Count == 0) return;

            // The middle of the posed body (wings folded in or not), then the highest point of
            // the body right around it: the top of the back.
            var bounds = new Bounds(points[0], Vector3.zero);
            for (int i = 1; i < points.Count; i++) bounds.Encapsulate(points[i]);
            Vector3 center = bounds.center;
            float radius = Mathf.Clamp(Mathf.Min(bounds.size.x, bounds.size.z) * 0.12f, 0.2f, 1.5f);
            float top = float.NegativeInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                float dx = points[i].x - center.x, dz = points[i].z - center.z;
                if (dx * dx + dz * dz <= radius * radius && points[i].y > top)
                    top = points[i].y;
            }
            if (float.IsNegativeInfinity(top)) return;

            var profile = MountSettings.Find(mount);
            var offset = MountSettings.SaddlePositionOffsetFor(profile);
            float scale = Mathf.Abs(root.lossyScale.y);
            Vector3 before = saddle.position;
            saddle.position = new Vector3(center.x, top, center.z) + mount.transform.TransformDirection(offset * scale);
            // Ride the trunk bone nearest to the new place.
            saddle.SetParent(root, true);
            FollowBone(mount, saddle);
            Plugin.Log?.LogInfo($"ValheimMontarias: {mount.name} saddle set on the posed back {before} -> {saddle.position} " +
                $"(body {bounds.size}, bone {saddle.parent?.name})");
            Fit(mount, saddle);
        }

        /// <summary>Bones a saddle must never ride: the capybara's nearest one was a front leg,
        /// and the saddle swung with every step.</summary>
        private static readonly string[] LimbBones =
        {
            "arm", "leg", "hand", "foot", "toe", "finger", "thigh", "calf", "shin", "knee",
            "clavicle", "shoulder", "tail", "head", "jaw", "eye", "wing", "neck", "hoof", "paw"
        };

        private static bool IsLimb(Transform bone)
        {
            string name = bone.name;
            for (int i = 0; i < LimbBones.Length; i++)
                if (name.IndexOf(LimbBones[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        /// <summary>Moves the saddle under the body bone nearest to it, keeping where it is, so
        /// it sways with the body. Only trunk bones count (no limbs, head, tail or wings); if a
        /// body has none, the nearest of all. With a custom skin (capivara) the bones are the overlay's,
        /// since the vanilla body under it is hidden; otherwise the vanilla SkinnedMeshRenderer's.</summary>
        private static bool FollowBone(GameObject mount, Transform saddle)
        {
            Transform best = null;
            float bestDist = float.PositiveInfinity;
            Transform anyBest = null;
            float anyDist = float.PositiveInfinity;
            var overlay = MountVisuals.Overlay(mount);
            var root = overlay != null ? overlay : mount.transform;
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin == null || skin.bones == null || IsSaddle(skin.transform)) continue;
                if (skin.GetComponentInParent<Player>() != null) continue;
                foreach (var bone in skin.bones)
                {
                    if (bone == null) continue;
                    float dist = (bone.position - saddle.position).sqrMagnitude;
                    if (dist < anyDist)
                    {
                        anyDist = dist;
                        anyBest = bone;
                    }
                    if (dist < bestDist && !IsLimb(bone))
                    {
                        bestDist = dist;
                        best = bone;
                    }
                }
            }
            if (best == null) best = anyBest;
            if (best == null) return false;
            saddle.SetParent(best, true);
            return true;
        }

        private static SaddleVisual ConfiguredVisual(MountProfile profile)
        {
            string prefabName = profile?.SaddlePrefab?.Value?.Trim();
            if (string.IsNullOrEmpty(prefabName)) return null;
            if (_configuredVisuals.TryGetValue(prefabName, out var cached))
                return cached;

            var source = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            if (source == null)
            {
                Plugin.Log?.LogWarning($"ValheimMontarias: saddle prefab '{prefabName}' not found; using default saddle");
                _configuredVisuals[prefabName] = null;
                return null;
            }

            Mesh mesh = null;
            Material material = null;
            Quaternion rotation = Quaternion.identity;
            int bestVertexCount = 0;
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                var candidate = filter.sharedMesh;
                if (candidate == null || candidate.vertexCount <= bestVertexCount) continue;
                bestVertexCount = candidate.vertexCount;
                mesh = candidate;
                var renderer = filter.GetComponent<Renderer>();
                if (renderer != null)
                {
                    material = renderer.sharedMaterial;
                    rotation = renderer.transform.localRotation;
                }
            }
            foreach (var skin in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var candidate = skin.sharedMesh;
                if (candidate == null || candidate.vertexCount <= bestVertexCount) continue;
                bestVertexCount = candidate.vertexCount;
                mesh = candidate;
                material = skin.sharedMaterial;
                rotation = skin.transform.localRotation;
            }

            if (mesh == null)
            {
                Plugin.Log?.LogWarning($"ValheimMontarias: saddle prefab '{prefabName}' has no mesh; using default saddle");
                _configuredVisuals[prefabName] = null;
                return null;
            }

            var visual = new SaddleVisual { Mesh = CenterMesh(mesh), Material = material, Rotation = rotation };
            _configuredVisuals[prefabName] = visual;
            return visual;
        }

        public static void Fit(GameObject mount)
        {
            Fit(mount, FindSaddle(mount));
        }

        public static void Fit(GameObject mount, Transform saddle)
        {
            if (mount == null || saddle == null) return;
            var attach = RescueAttach(mount);
            if (attach == null) return;

            float sit = 0.12f;
            var anchor = saddle.GetComponent<MountSaddleAnchor>();
            if (anchor != null)
                sit = anchor.SitHeight;
            var profile = MountSettings.Find(mount);
            sit += MountSettings.SeatHeightOffsetFor(profile);

            Vector3 world = saddle.position + mount.transform.up * sit;
            attach.localPosition = mount.transform.InverseTransformPoint(world) + MountSettings.SeatPositionOffsetFor(profile);
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

        public static Vector3 SaddleBackPoint(Transform overlay)
        {
            var skin = overlay.GetComponent<SkinnedMeshRenderer>();
            var mesh = skin != null ? skin.sharedMesh : overlay.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh != null)
            {
                var bounds = mesh.bounds;
                return new Vector3(0f, bounds.min.y + bounds.size.y * 0.88f, bounds.center.z);
            }

            // Mesh bounds in each renderer's own space, never Renderer.bounds: the mount is
            // fitted while hidden (renderers disabled), and a disabled renderer reports empty
            // bounds at the world origin, which threw the seat -- and the rider -- far away.
            var renderers = overlay.GetComponentsInChildren<Renderer>(true);
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || IsSaddle(renderer.transform)) continue;
                if (renderer.GetComponentInParent<Player>() != null) continue;
                Mesh rendererMesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
                if (rendererMesh == null) continue;
                var bounds = rendererMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3(
                        (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                    var local = overlay.InverseTransformPoint(renderer.transform.TransformPoint(point));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    found = true;
                }
            }

            if (!found)
                return new Vector3(0f, 0.55f, 0.05f);
            var size = max - min;
            return new Vector3((min.x + max.x) * 0.5f, min.y + size.y * 0.88f, (min.z + max.z) * 0.5f);
        }

        private static Mesh VisualMesh()
        {
            return _centeredMesh != null ? _centeredMesh : FallbackCube();
        }

        private static Mesh CenterMesh(Mesh src)
        {
            if (src == null) return null;
            // Reading an unreadable mesh's vertices only logs an error and returns nothing.
            if (!src.isReadable) return src;
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
