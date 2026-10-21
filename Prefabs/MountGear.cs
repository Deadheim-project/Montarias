using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Text;

namespace ValheimMontarias.Prefabs
{
    // After MountClipPlayer (-300) poses the skin bones, before RiderVisualFix (-200) puts the
    // rider on the attach point; all of it before the camera (0), which follows the rider's head.
    [DefaultExecutionOrder(-250)]
    public sealed class MountSaddleAnchor : MonoBehaviour
    {
        /// <summary>The mount this saddle sits on.</summary>
        public GameObject FollowMount;

        /// <summary>A few frames after the mount shows, the saddle is put on top of the posed
        /// body (<see cref="MountGear.SitOnPosedBack"/>), once: the pose it rests in is not the
        /// bind pose the saddle was first placed on (the Moder lies down, the capybara's skin
        /// was authored apart from its anchor).</summary>
        public bool FitToPose = true;
        private int _fitAtFrame = -1;

        /// <summary>The saddle's rotation on the seat (SelaRotacao*), as Attach built it.</summary>
        public Quaternion RestRotation = Quaternion.identity;

        /// <summary>
        /// Where one sits, on top of the saddle, y up and z forward, in metres. Every frame it
        /// takes the place it had against the trunk bone under it (<see cref="Bone"/>), so the
        /// saddle hanging from it and the attach point put on it go with the body as it walks,
        /// sways, tilts on a slope or flies. Not a child of the bone: the bones carry scales,
        /// not always the same on each axis, that stretched everything put under them. Null
        /// until the first fit.
        /// </summary>
        public Transform Seat;
        public Transform Bone;
        public Vector3 SeatInBone;
        public Quaternion SeatTurnInBone = Quaternion.identity;
        /// <summary>The rider's root on the seat, in the seat's space (hips over the seat,
        /// less where the riding pose carries them).</summary>
        public Vector3 RootLocal;
        /// <summary>The body under the seat, for the rider's legs.</summary>
        internal MountFlanks Flanks;
        /// <summary>The posed body's size at the last fit (metres).</summary>
        public Vector3 BodySize;
        /// <summary>Counts the fits, so what was measured against an older one is measured again.</summary>
        public int Fits;

        private Transform _attach;
        private int _easedFit = -1;
        private Vector3 _restPosition;
        private Quaternion _restTurn = Quaternion.identity;
        // How much of the spine's side to side sway, turn and roll the seat follows.
        private const float SideSway = 0.25f;

        private static float Signed(float degrees) => degrees > 180f ? degrees - 360f : degrees;
        // How quickly the seat catches up with the bone under it (seconds).
        private const float SeatEase = 0.1f;

        private void LateUpdate()
        {
            if (FollowMount == null) return;
            if (FitToPose)
            {
                if (_fitAtFrame < 0) _fitAtFrame = Time.frameCount + 5;
                else if (Time.frameCount >= _fitAtFrame && AnyRendererOn(FollowMount))
                {
                    FitToPose = false;
                    MountGear.SitOnPosedBack(FollowMount, transform);
                }
            }
            PlaceAttach();
        }

        /// <summary>The attach point (where the game puts the rider's root) onto the seat as
        /// the body is posed this frame.</summary>
        public void PlaceAttach()
        {
            if (Seat == null || FollowMount == null) return;
            if (Bone != null)
            {
                var parent = Seat.parent;
                var position = Bone.TransformPoint(SeatInBone);
                var rotation = Bone.rotation * SeatTurnInBone;
                if (parent == null || _easedFit != Fits)
                {
                    _easedFit = Fits;
                    _restPosition = parent != null ? parent.InverseTransformPoint(position) : position;
                    _restTurn = parent != null ? Quaternion.Inverse(parent.rotation) * rotation : rotation;
                    Seat.SetPositionAndRotation(position, rotation);
                }
                else
                {
                    // Only part of the spine's sway: a lizard's body snakes from side to side as
                    // it walks, and a saddle that turned and rolled with it threw the rider
                    // askew. The rise and fall and the pitch are kept whole.
                    var rest = parent.rotation * _restTurn;
                    var restAt = parent.TransformPoint(_restPosition);
                    var moved = Quaternion.Inverse(rest) * (position - restAt);
                    moved.x *= SideSway;
                    var turned = (Quaternion.Inverse(rest) * rotation).eulerAngles;
                    var target = rest * Quaternion.Euler(Signed(turned.x), Signed(turned.y) * SideSway, Signed(turned.z) * SideSway);

                    // Eased toward it over a few frames: a running body shakes its spine faster
                    // than a saddle on it would follow, and the rider shook with it.
                    float t = 1f - Mathf.Exp(-Time.deltaTime / SeatEase);
                    var local = parent.InverseTransformPoint(restAt + rest * moved);
                    var turn = Quaternion.Inverse(parent.rotation) * target;
                    Seat.localPosition = Vector3.Lerp(Seat.localPosition, local, t);
                    Seat.localRotation = Quaternion.Slerp(Seat.localRotation, turn, t);
                }
            }
            if (_attach == null) _attach = MountHub.FindAttach(FollowMount);
            if (_attach != null)
                _attach.SetPositionAndRotation(Seat.TransformPoint(RootLocal), Seat.rotation);
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
        private static readonly Dictionary<string, string> _vanillaRidePoses = new Dictionary<string, string>();

        /// <summary>
        /// The player animation (Sadle.m_attachAnimation) for a mount's PoseMontado. "Lox" and
        /// "Asksvin" are read from those creatures' own saddles, so the name is always one the
        /// player's animator has; "Cadeira" is the seated pose the mod used before (hands on
        /// the knees, legs hanging in front); a name starting with attach_ is used as it is.
        /// Until the scene is loaded (or with no such saddle) it falls back to the other
        /// vanilla pose, then to the seated one.
        /// </summary>
        public static string RideAnimation(MountProfile profile)
        {
            string wanted = profile?.RidePose?.Value?.Trim();
            if (string.IsNullOrEmpty(wanted))
                wanted = "Lox";
            if (wanted.Equals("Cadeira", System.StringComparison.OrdinalIgnoreCase))
                return "attach_chair";
            if (wanted.StartsWith("attach_", System.StringComparison.OrdinalIgnoreCase))
                return wanted;
            string creature = wanted.Equals("Asksvin", System.StringComparison.OrdinalIgnoreCase) ? "Asksvin" : "Lox";
            return VanillaRidePose(creature)
                ?? VanillaRidePose(creature == "Lox" ? "Asksvin" : "Lox")
                ?? "attach_chair";
        }

        private static string VanillaRidePose(string creature)
        {
            if (_vanillaRidePoses.TryGetValue(creature, out var known))
                return known;
            var scene = ZNetScene.instance;
            var prefab = scene != null ? scene.GetPrefab(creature) : null;
            if (prefab == null)
                return null;
            var sadle = prefab.GetComponentInChildren<Sadle>(true);
            string anim = sadle != null ? Access.Get(sadle, "m_attachAnimation") as string : null;
            if (string.IsNullOrEmpty(anim))
                anim = null;
            _vanillaRidePoses[creature] = anim;
            Plugin.Log?.LogInfo($"ValheimMontarias: riding pose of the {creature} saddle: {anim ?? "none"}");
            return anim;
        }

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

        /// <summary>Builds the saddle at <paramref name="overlayLocal"/> (the bind pose's back, a
        /// first guess) and seats the rider on it; a few frames after the mount shows, both are
        /// moved onto the posed body (<see cref="SitOnPosedBack"/>).</summary>
        public static void Attach(GameObject mount, Transform overlay, float saddleLength, Vector3 overlayLocal, bool useCapturedRotation = true)
        {
            if (overlay == null || mount == null) return;
            RescueAttach(mount);
            EnsureCaptured();

            var existing = FindSaddle(mount);
            if (existing != null)
            {
                var oldSeat = existing.GetComponent<MountSaddleAnchor>()?.Seat;
                Object.DestroyImmediate(oldSeat != null ? oldSeat.gameObject : existing.gameObject);
            }

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
            anchor.FollowMount = mount;
            anchor.RestRotation = saddle.transform.localRotation;
            Fit(mount);
        }

        /// <summary>Fits the saddle and the seat again on the body as it is posed now.</summary>
        public static void Refit(GameObject mount)
        {
            var saddle = FindSaddle(mount);
            if (saddle != null)
                SitOnPosedBack(mount, saddle);
        }

        /// <summary>
        /// Puts the saddle on the back of the posed body and the seat on top of the saddle, and
        /// hangs both from the trunk bone under them. The saddle was first placed on the bind
        /// pose's back, and each body rests in another pose (the Moder lies down) or was
        /// authored apart from its anchor (the capybara skin). The saddle is as long as the back
        /// is wide and leans with it, front to back. The body under the seat is kept for the
        /// rider's legs. AjusteSela* only nudge the result.
        /// </summary>
        internal static void SitOnPosedBack(GameObject mount, Transform saddle)
        {
            if (mount == null || saddle == null) return;
            var overlay = MountVisuals.Overlay(mount);
            var root = overlay != null ? overlay : mount.transform;
            var mountTr = mount.transform;
            var mountSpace = Matrix4x4.TRS(mountTr.position, mountTr.rotation, Vector3.one);
            var toMount = mountSpace.inverse;
            var report = new StringBuilder();
            MountBody body;
            try
            {
                body = MountBody.Bake(root, toMount, report);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogWarning($"ValheimMontarias: could not read the posed body of {mount.name}: {e.Message}");
                return;
            }
            if (body == null || !FindBack(body, root, toMount, mountTr.position, out var back, out string measured))
            {
                Plugin.Log?.LogWarning($"ValheimMontarias: no back found on {mount.name}; the saddle stays where it was");
                return;
            }

            var profile = MountSettings.Find(mount);
            float seatBack = MountSettings.SeatBackFor(profile);
            if (Mathf.Abs(seatBack) > 0.001f && BackAt(body, back, -seatBack, out float there))
                back = new Vector3(back.x, there, back.z - seatBack);

            // On the middle of the back, not over the bone: a lizard's spine is seldom straight
            // under it. And turned with the back, where the body lies askew of the mount.
            if (Middle(body, back + Vector3.down * 0.12f, out float middle)
                && body.Vertical(new Vector3(middle, back.y - 0.3f, back.z), 1f, out float up))
                back = new Vector3(middle, back.y - 0.3f + up, back.z);
            float turn = 0f;
            if (Middle(body, back + new Vector3(0f, -0.15f, TurnReach), out float ahead)
                && Middle(body, back + new Vector3(0f, -0.15f, -TurnReach), out float behind))
                turn = Mathf.Clamp(Mathf.Atan2(ahead - behind, 2f * TurnReach) * Mathf.Rad2Deg, -MaxTurn, MaxTurn);
            measured += $", {seatBack:0.00} m back, {back.x:0.00} m aside, turned {turn:0} deg";

            float width = BackWidth(body, back);
            float length = Mathf.Clamp(width * SaddleOverBack, MinSaddle, MaxSaddle) * MountSettings.SaddleSizeFor(profile);
            float slope = Slope(body, back, length * 0.5f);

            // The seat's axes: the mount's, turned and leaning with the back.
            var seatRotation = mountTr.rotation * Quaternion.Euler(0f, turn, 0f) * Quaternion.Euler(-slope, 0f, 0f);
            var anchor = saddle.GetComponent<MountSaddleAnchor>();
            var restRotation = anchor != null ? anchor.RestRotation : saddle.localRotation;
            var shape = ShapeOf(saddle.GetComponent<MeshFilter>()?.sharedMesh);
            float scale = length / shape.Length;

            // The underside of the saddle's middle on the back, the seat on top of the saddle.
            var saddleRotation = seatRotation * restRotation;
            var onBack = mountSpace.MultiplyPoint3x4(back)
                + seatRotation * (MountSettings.SaddleAdjustFor(profile) + Vector3.up * 0.01f);
            var saddlePosition = onBack - saddleRotation * (shape.Bottom * scale);
            var seatPosition = saddlePosition + saddleRotation * (shape.Seat * scale);

            var bone = TrunkBoneNear(root, seatPosition, false) ?? root;
            var seat = new GameObject(SeatName).transform;
            seat.SetParent(mountTr, false);
            seat.localScale = Vector3.one * Inverse(mountTr.lossyScale.x);
            seat.SetPositionAndRotation(seatPosition, seatRotation);

            var oldSeat = anchor != null ? anchor.Seat : null;
            Vector3 before = saddle.position;
            saddle.SetParent(seat, false);
            saddle.localRotation = restRotation;
            saddle.localScale = Vector3.one * scale;
            saddle.position = saddlePosition;
            if (oldSeat != null && oldSeat != seat)
                Object.Destroy(oldSeat.gameObject);

            if (anchor != null)
            {
                anchor.Seat = seat;
                anchor.Bone = bone;
                anchor.SeatInBone = bone.InverseTransformPoint(seatPosition);
                anchor.SeatTurnInBone = Quaternion.Inverse(bone.rotation) * seatRotation;
                anchor.BodySize = body.Bounds.size;
                anchor.Flanks = MountFlanks.Measure(body, toMount * Matrix4x4.TRS(seatPosition, seatRotation, Vector3.one));
                anchor.Fits++;
            }
            Plugin.Log?.LogInfo($"ValheimMontarias: {mount.name} saddle set on the posed back {before} -> {saddle.position} " +
                $"({measured}; back {width:0.00} m wide, slope {slope:0} deg, saddle {length:0.00} m, " +
                $"seat {seatPosition.y - mountTr.position.y:0.00} m over the root, on {bone.name}; " +
                $"body {body.Bounds.size}, {body.TriangleCount} triangles{report})");
            Fit(mount, saddle);
        }

        public const string SeatName = "MountSeat";
        // The saddle's length against the width of the back under it, and its bounds (metres).
        private const float SaddleOverBack = 1.3f;
        private const float MinSaddle = 0.55f;
        private const float MaxSaddle = 1.4f;
        private const float MaxSlope = 10f;
        // How far ahead and behind the seat the back's line is read, and how far it may turn.
        private const float TurnReach = 0.3f;
        private const float MaxTurn = 25f;

        private static float Inverse(float value) => Mathf.Abs(value) < 1e-4f ? 1f : 1f / value;

        /// <summary>
        /// The top of the back over the middle of the body, in the mount's space: the first
        /// surface straight above the trunk bone nearest the mount's root, over the root (or over
        /// the bone, for a rig whose root is off the trunk). Going up from the bone, the skin of
        /// the back comes first; a wing raised over it, antlers or a spike come after. With no
        /// trunk bone, the top of the body over the root.
        /// </summary>
        private static bool FindBack(MountBody body, Transform root, Matrix4x4 toMount, Vector3 middle,
            out Vector3 back, out string measured)
        {
            var spine = TrunkBoneNear(root, middle, true);
            if (spine != null)
            {
                var bone = toMount.MultiplyPoint3x4(spine.position);
                foreach (var column in new[] { new Vector3(0f, bone.y, 0f), bone })
                {
                    if (!body.Vertical(column, 1f, out float above)) continue;
                    back = column + Vector3.up * above;
                    measured = $"back {above:0.00} m above {spine.name}";
                    return true;
                }
            }
            var top = new Vector3(0f, body.Bounds.max.y + 1f, 0f);
            if (body.Vertical(top, -1f, out float down))
            {
                back = top + Vector3.down * down;
                measured = "top of the body";
                return true;
            }
            back = default;
            measured = null;
            return false;
        }

        /// <summary>Halfway between the body's sides, across from a point inside it.</summary>
        private static bool Middle(MountBody body, Vector3 point, out float middle)
        {
            middle = point.x;
            if (!body.Sideways(point, -1f, out float left) || !body.Sideways(point, 1f, out float right))
                return false;
            middle = point.x + (right - left) * 0.5f;
            return true;
        }

        /// <summary>How wide the back is a little under its top, where the saddle's sides lie.</summary>
        private static float BackWidth(MountBody body, Vector3 back)
        {
            var under = back + Vector3.down * 0.12f;
            bool hasLeft = body.Sideways(under, -1f, out float left);
            bool hasRight = body.Sideways(under, 1f, out float right);
            if (hasLeft && hasRight) return left + right;
            if (hasLeft || hasRight) return 2f * (hasLeft ? left : right);
            return 0.5f;
        }

        /// <summary>How much the back rises toward the front under the saddle, in degrees.</summary>
        private static float Slope(MountBody body, Vector3 back, float half)
        {
            if (!BackAt(body, back, half, out float front) || !BackAt(body, back, -half, out float rear))
                return 0f;
            return Mathf.Clamp(Mathf.Atan2(front - rear, 2f * half) * Mathf.Rad2Deg, -MaxSlope, MaxSlope);
        }

        private static bool BackAt(MountBody body, Vector3 back, float along, out float top)
        {
            var from = back + new Vector3(0f, -0.3f, along);
            top = 0f;
            if (!body.Vertical(from, 1f, out float up)) return false;
            top = from.y + up;
            return true;
        }

        /// <summary>The trunk bone of the body nearest a point, measured along the ground only
        /// when <paramref name="horizontal"/>.</summary>
        internal static Transform TrunkBoneNear(Transform root, Vector3 point, bool horizontal, bool neck = false)
        {
            Transform best = null;
            float nearest = float.PositiveInfinity;
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (skin == null || !skin.enabled || skin.bones == null || IsSaddle(skin.transform)) continue;
                if (skin.GetComponentInParent<Player>() != null) continue;
                foreach (var bone in skin.bones)
                {
                    if (bone == null || !IsTrunk(bone, neck)) continue;
                    var d = bone.position - point;
                    if (horizontal) d.y = 0f;
                    if (d.sqrMagnitude < nearest)
                    {
                        nearest = d.sqrMagnitude;
                        best = bone;
                    }
                }
            }
            return best;
        }

        private static readonly string[] TrunkBones = { "spine", "pelvis", "hip", "chest", "torso" };
        // A bone of one side (Hip.L, LeftHip...) belongs to a leg, not to the trunk.
        private static readonly string[] SideMarks = { ".l", ".r", "_l", "_r", " l", " r", "left", "right" };

        private static bool IsTrunk(Transform bone, bool neck = false)
        {
            string name = bone.name.ToLowerInvariant();
            bool trunk = neck && name.Contains("neck");
            for (int i = 0; i < TrunkBones.Length && !trunk; i++)
                trunk = name.Contains(TrunkBones[i]);
            if (!trunk) return false;
            for (int i = 0; i < SideMarks.Length; i++)
            {
                string mark = SideMarks[i];
                if (mark.Length > 2 ? name.Contains(mark) : name.EndsWith(mark))
                    return false;
            }
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

        /// <summary>
        /// Places the attach point (where the rider's root goes) so that the rider's hips sit on
        /// the middle of the saddle, <see cref="HipsOverSeat"/> above its seat. Each pose
        /// carries the hips somewhere else than the root -- the Lox one half a metre behind it,
        /// the chair one half a metre above -- so that is taken off (<see cref="HipsOffset"/>).
        /// Once the saddle is on the posed back, this is a place on its seat, and the anchor
        /// moves the attach point with the seat every frame.
        /// </summary>
        public static void Fit(GameObject mount, Transform saddle)
        {
            if (mount == null || saddle == null) return;
            var attach = RescueAttach(mount);
            if (attach == null) return;

            var mountTr = mount.transform;
            var profile = MountSettings.Find(mount);
            var sadle = mount.GetComponentInChildren<Sadle>(true);
            string pose = sadle != null ? Access.Get(sadle, "m_attachAnimation") as string : null;
            // In the seat's axes (y up, z forward).
            var root = Vector3.up * HipsOverSeat + MountSettings.RiderAdjustFor(profile) - HipsOffset(pose);

            var anchor = saddle.GetComponent<MountSaddleAnchor>();
            if (anchor != null && anchor.Seat != null)
            {
                anchor.RootLocal = root;
                anchor.PlaceAttach();
                return;
            }
            // Until then on the saddle where Attach put it, in the mount's axes.
            attach.localPosition = mountTr.InverseTransformPoint(SeatPoint(saddle) + mountTr.rotation * root);
            attach.localRotation = Quaternion.identity;
        }

        /// <summary>How far above the seat the hips bone sits: the thickness of the buttocks.</summary>
        public const float HipsOverSeat = 0.1f;

        // The player's hips against their root in each riding pose, in the root's space, as
        // measured in the game (1.0.17). RecordHips corrects them from the rider in front of us.
        private static readonly Dictionary<string, Vector3> _hipsOffset = new Dictionary<string, Vector3>
        {
            { "attach_lox", new Vector3(0.04f, 0f, -0.53f) },
            { "attach_asksvin", new Vector3(0f, 0.05f, 0.02f) },
            { "attach_chair", new Vector3(0f, 0.5f, -0.03f) },
        };

        public static Vector3 HipsOffset(string pose)
        {
            return !string.IsNullOrEmpty(pose) && _hipsOffset.TryGetValue(pose, out var offset) ? offset : Vector3.zero;
        }

        /// <summary>The hips of the local rider, measured with the pose settled. True when that
        /// moved the hips' place in the pose enough that the seats need fitting again.</summary>
        public static bool RecordHips(string pose, Vector3 offset)
        {
            if (string.IsNullOrEmpty(pose) || offset.magnitude > 1.5f) return false;
            if (_hipsOffset.TryGetValue(pose, out var known) && (known - offset).magnitude < 0.03f) return false;
            _hipsOffset[pose] = offset;
            Plugin.Log?.LogInfo($"ValheimMontarias: hips in {pose} at {offset} from the rider's root");
            return true;
        }

        private sealed class SaddleShape
        {
            public Vector3 Bottom;
            public Vector3 Seat;
            public float Length;
        }

        // How far up the middle of the saddle the top of its cushion is (the Lox saddle, in the game).
        private const float CushionTop = 0.3f;

        private static readonly Dictionary<Mesh, SaddleShape> _shapes = new Dictionary<Mesh, SaddleShape>();

        /// <summary>The middle of the saddle mesh, in its own space: the underside that rests on
        /// the back and the top one sits on (the bottom of the seat, below the pommel and the
        /// cantle); and how long the mesh is.</summary>
        private static SaddleShape ShapeOf(Mesh mesh)
        {
            if (mesh == null) return new SaddleShape { Length = 1f, Seat = Vector3.up * 0.1f };
            if (_shapes.TryGetValue(mesh, out var known)) return known;
            var b = mesh.bounds;
            var shape = new SaddleShape
            {
                Length = Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z)),
                Bottom = new Vector3(b.center.x, b.min.y, b.center.z),
                Seat = new Vector3(b.center.x, Mathf.Lerp(b.min.y, b.max.y, CushionTop), b.center.z),
            };
            if (mesh.isReadable)
            {
                var vertices = mesh.vertices;
                float radius = shape.Length * 0.12f;
                var column = new List<float>();
                for (int i = 0; i < vertices.Length; i++)
                {
                    float dx = vertices[i].x - b.center.x, dz = vertices[i].z - b.center.z;
                    if (dx * dx + dz * dz <= radius * radius) column.Add(vertices[i].y);
                }
                if (column.Count > 0)
                {
                    // The cushion is the lower part of the column; above it there is only frame
                    // (the Lox saddle's bars, as high as its posts), which nobody sits on.
                    column.Sort();
                    shape.Bottom.y = column[0];
                    shape.Seat.y = Mathf.Lerp(column[0], column[column.Count - 1], CushionTop);
                }
            }
            _shapes[mesh] = shape;
            return shape;
        }

        /// <summary>Where one sits on the saddle, in world space.</summary>
        public static Vector3 SeatPoint(Transform saddle)
        {
            var mesh = saddle.GetComponent<MeshFilter>()?.sharedMesh;
            return mesh == null ? saddle.position : saddle.TransformPoint(ShapeOf(mesh).Seat);
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

        internal static Transform FindSaddle(GameObject mount)
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
