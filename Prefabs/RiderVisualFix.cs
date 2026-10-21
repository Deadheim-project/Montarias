using System.Collections.Generic;
using MagicaCloth2;
using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    // After MountSaddleAnchor (-250) puts the attach point on the seat; before the camera (0),
    // which follows the rider's head.
    [DefaultExecutionOrder(-200)]
    internal sealed class RiderVisualFix : MonoBehaviour
    {
        private sealed class CapeTransformState
        {
            public Transform Transform;
            public Vector3 BaseLocalPosition;
            public Vector3 AppliedOffset;
            public Vector3 LastAppliedPosition;
        }

        private sealed class ClothState
        {
            public MagicaCloth Cloth;
            public List<ColliderComponent> Original;
            public ColliderCollisionConstraint.Mode OriginalMode;
        }

        private readonly List<CapeTransformState> _capeTransforms = new List<CapeTransformState>();
        private readonly List<ClothState> _cloths = new List<ClothState>();
        private MagicaCapsuleCollider _capeBlocker;
        private float _blockerRadius;
        private Player _player;
        private Animator _animator;
        private RiderPose _riderPose;
        private readonly RiderPose.Settings _settings = new RiderPose.Settings();
        private MountReins _reins;
        /// <summary>Where the head was when the pose was done this frame (world), for the photo
        /// booth to see whether anything moves it afterwards.</summary>
        internal Vector3 HeadAfterPose;

        // A riding pose takes a moment to blend in after mounting or switching.
        private const float PoseSettles = 0.75f;
        private string _pose;
        private float _poseSince;
        private string _hipsMeasured;
        private float _refittedAt = -10f;
        private int _reportedMount;
        private int _reportedFits = -1;
        private string _reportedPose;

        // The cape's transforms and cloth, read from the body again only every BodyScanEvery or
        // when one of them is gone (unequipped): walking every renderer of the rider each frame
        // cost each client per rider in sight.
        private const float BodyScanEvery = 0.25f;
        private readonly List<Transform> _capes = new List<Transform>();
        private static readonly MagicaCloth[] NoCloth = new MagicaCloth[0];
        private MagicaCloth[] _clothsOn = NoCloth;
        private float _nextBodyScan;

        private void LateUpdate()
        {
            if (_player == null)
                _player = GetComponent<Player>();
            if (_player == null)
                return;

            var mount = MountControl.RiddenBy(_player);
            if (mount == null)
            {
                _animator = null;
                _riderPose = null;
                _reins?.Hide();
                RestoreCape();
                _pose = null;
                return;
            }

            string pose = MountControl.PoseOf(mount);
            if (pose != _pose)
            {
                _pose = pose;
                _poseSince = Time.time;
            }

            SitOnSeat(mount);
            PoseRider(mount);
            ScanBody();
            AdjustCape();
            BlockCapeWithMount(mount);
            // Only the rider's own client places their seat; everyone else sees where it put them.
            if (_player == Player.m_localPlayer)
                MeasureHips(mount);
        }

        private bool PoseSettled => _pose != null && Time.time - _poseSince >= PoseSettles;

        private static MountSaddleAnchor Anchor(Character mount)
        {
            if (mount.TryGetComponent(out MountControl live)) return live.SaddleAnchor;
            var saddle = MountGear.FindSaddle(mount.gameObject);
            var anchor = saddle != null ? saddle.GetComponent<MountSaddleAnchor>() : null;
            return anchor != null && anchor.Seat != null ? anchor : null;
        }

        /// <summary>
        /// Puts the rider's root on the attach point for this frame's picture. The game moves an
        /// attached player there only in FixedUpdate, and any other player only where the
        /// network last said: on a mount that walks, sways and tilts, the rider lagged behind
        /// the saddle and shook on it.
        /// </summary>
        private void SitOnSeat(Character mount)
        {
            if (Anchor(mount) == null) return;
            if (_player == Player.m_localPlayer && !_player.IsAttached()) return;
            var attach = MountHub.FindAttach(mount.gameObject);
            if (attach != null)
                _player.transform.SetPositionAndRotation(attach.position, attach.rotation);
        }

        /// <summary>The riding pose (<see cref="RiderPose"/>) over the animation, with the
        /// mount's numbers (PoseCodigo off leaves the game's pose alone).</summary>
        private void PoseRider(Character mount)
        {
            var anchor = Anchor(mount);
            var profile = MountSettings.Find(mount.gameObject);
            if (anchor == null || profile == null || !MountSettings.CodedPoseFor(profile))
            {
                _reins?.Hide();
                return;
            }
            if (_animator == null || _riderPose == null)
            {
                _animator = _player.GetComponentInChildren<Animator>();
                _riderPose = new RiderPose(_animator);
            }
            if (!_riderPose.Valid) return;
            MountSettings.FillPose(profile, _settings);
            _riderPose.Apply(anchor.Seat, anchor.Flanks, _settings);
            var head = _animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) HeadAfterPose = head.position;

            // The reins only where the hands hold on to them.
            if (_settings.Hands.sqrMagnitude < 1e-4f)
            {
                _reins?.Hide();
                return;
            }
            if (_reins == null) _reins = new MountReins(transform);
            _reins.Show(anchor.Seat, anchor.Flanks, _settings.Hands.z, _riderPose.Hand(0), _riderPose.Hand(1));
        }

        /// <summary>
        /// Once per pose, settled and with the mount standing still: where the pose carries the
        /// rider's hips against their root. The seats assume the values measured in 1.0.17; if
        /// this body or this version differs, they are fitted again.
        /// </summary>
        private void MeasureHips(Character mount)
        {
            if (!PoseSettled) return;
            if (_animator == null) _animator = _player.GetComponentInChildren<Animator>();
            if (_animator == null || !_animator.isHuman) return;
            var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) return;
            var profile = MountSettings.Find(mount.gameObject);
            // With the coded pose the hips are put on the seat directly; this is only for the
            // game's own poses.
            if (_pose != _hipsMeasured && mount.GetVelocity().magnitude < 0.3f
                && (profile == null || !MountSettings.CodedPoseFor(profile)))
            {
                _hipsMeasured = _pose;
                if (MountGear.RecordHips(_pose, _player.transform.InverseTransformPoint(hips.position)))
                {
                    MountGear.Fit(mount.gameObject);
                    _refittedAt = Time.time;
                }
            }
            // The rider follows a moved seat on the next physics step.
            if (Time.time - _refittedAt >= 0.5f)
                Report(mount, hips);
        }

        /// <summary>Once per mount, fit and pose, in the log: where the rider ended up on the seat.</summary>
        private void Report(Character mount, Transform hips)
        {
            var anchor = Anchor(mount);
            if (anchor == null) return;
            int id = mount.GetInstanceID();
            if (id == _reportedMount && anchor.Fits == _reportedFits && _pose == _reportedPose) return;
            _reportedMount = id;
            _reportedFits = anchor.Fits;
            _reportedPose = _pose;
            var at = anchor.Seat.InverseTransformPoint(hips.position);
            string legs = _riderPose != null && _riderPose.Valid
                ? $"; knees {_riderPose.Knees[0]:0.00} / {_riderPose.Knees[1]:0.00}, feet {_riderPose.Feet[0]:0.00} / {_riderPose.Feet[1]:0.00}"
                : "";
            Plugin.Log.LogInfo($"ValheimMontarias: rider on {mount.name} ({_pose}): hips {at.y:0.00} m over the seat, " +
                $"{at.z:0.00} m ahead, {at.x:0.00} m aside{legs}");
        }

        private void ScanBody()
        {
            if (Time.unscaledTime < _nextBodyScan && !BodyChanged()) return;
            _nextBodyScan = Time.unscaledTime + BodyScanEvery;

            _capes.Clear();
            var renderers = _player.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;
                var cape = CapeTransform(renderers[i].transform);
                if (cape != null && !_capes.Contains(cape))
                    _capes.Add(cape);
            }
            _clothsOn = _player.GetComponentsInChildren<MagicaCloth>();
        }

        private bool BodyChanged()
        {
            for (int i = 0; i < _capes.Count; i++)
                if (_capes[i] == null) return true;
            for (int i = 0; i < _clothsOn.Length; i++)
                if (_clothsOn[i] == null || !_clothsOn[i].gameObject.activeInHierarchy) return true;
            return false;
        }

        private void AdjustCape()
        {
            // Several renderers can sit under one cape: each is moved once (it came to the same).
            for (int i = 0; i < _capes.Count; i++)
            {
                var capeTransform = _capes[i];
                if (capeTransform == null)
                    continue;

                var state = FindCapeState(capeTransform);
                if (state == null)
                {
                    state = new CapeTransformState
                    {
                        Transform = capeTransform,
                        BaseLocalPosition = capeTransform.localPosition
                    };
                    _capeTransforms.Add(state);
                }

                if (state.AppliedOffset != Vector3.zero
                    && Vector3.SqrMagnitude(capeTransform.localPosition - state.LastAppliedPosition) < 0.0001f)
                    capeTransform.localPosition = state.BaseLocalPosition;
                else
                    state.BaseLocalPosition = capeTransform.localPosition;

                var parent = capeTransform.parent;
                var worldOffset = -_player.transform.forward * 0.22f + _player.transform.up * 0.08f;
                state.AppliedOffset = parent != null ? parent.InverseTransformVector(worldOffset) : worldOffset;
                capeTransform.localPosition = state.BaseLocalPosition + state.AppliedOffset;
                state.LastAppliedPosition = capeTransform.localPosition;
            }
        }

        /// <summary>
        /// The cape is MagicaCloth that only knows the rider's own body (VisEquipment.SetupCloth
        /// hands it m_clothColliders), so it fell straight through the mount's back. A capsule
        /// along the mount's spine, under the seat, is added to the cape's colliders while riding
        /// so it drapes over the back. Only MagicaCloth reads it: it is not a physics collider.
        /// </summary>
        private void BlockCapeWithMount(Character mount)
        {
            var cloths = _clothsOn;
            if (cloths.Length == 0)
            {
                ReleaseCapeCloth();
                return;
            }

            EnsureCapeBlocker();
            PlaceCapeBlocker(mount);

            for (int i = 0; i < cloths.Length; i++)
            {
                var cloth = cloths[i];
                var constraint = cloth.SerializeData.colliderCollisionConstraint;
                var list = constraint.colliderList;
                if (list != null && list.Contains(_capeBlocker))
                    continue;

                var state = FindClothState(cloth);
                if (state == null)
                {
                    state = new ClothState { Cloth = cloth, OriginalMode = constraint.mode };
                    _cloths.Add(state);
                }
                // Taken again each time it is missing: equipping rebuilds the cape's list.
                state.Original = list;

                // A copy: SetupCloth may hand the cape VisEquipment's own m_clothColliders list,
                // and adding to that one would leak the blocker into every item equipped later.
                var withMount = list != null ? new List<ColliderComponent>(list) : new List<ColliderComponent>();
                withMount.Add(_capeBlocker);
                constraint.colliderList = withMount;
                if (constraint.mode == ColliderCollisionConstraint.Mode.None)
                    constraint.mode = ColliderCollisionConstraint.Mode.Point;
                cloth.SetParameterChange();
            }
        }

        private void EnsureCapeBlocker()
        {
            if (_capeBlocker != null)
                return;
            var go = new GameObject("MontariasCapeBlocker");
            _capeBlocker = go.AddComponent<MagicaCapsuleCollider>();
            _capeBlocker.direction = MagicaCapsuleCollider.Direction.Z;
            _capeBlocker.alignedOnCenter = true;
            _blockerRadius = -1f;
        }

        /// <summary>Sized from the mount's body capsule and its configured scale, so it fits a
        /// Neck as well as Eikthyr; the top of the capsule sits just under the seat and it runs
        /// from a little ahead of the seat back to the rump.</summary>
        private void PlaceCapeBlocker(Character mount)
        {
            var mountTr = mount.transform;
            var up = mountTr.up;
            var forward = mountTr.forward;
            // Under the seat itself, as it leans and sways; the attach point is the rider's root,
            // which the riding pose may carry well below the seat.
            var anchor = Anchor(mount);
            Vector3 seat;
            if (anchor != null)
            {
                seat = anchor.Seat.position;
                up = anchor.Seat.up;
                forward = anchor.Seat.forward;
            }
            else
            {
                var attach = MountHub.FindAttach(mount.gameObject);
                seat = attach != null ? attach.position : mountTr.position + up;
            }

            var profile = MountSettings.Find(mount.gameObject);
            float scale = profile?.Scale != null ? Mathf.Clamp(profile.Scale.Value, 0.5f, 3f) : 1f;
            float radius = 0.35f;
            if (Access.Get(mount, "m_collider") is CapsuleCollider capsule && capsule != null)
                radius = capsule.radius * Mathf.Max(Mathf.Abs(mountTr.lossyScale.x), Mathf.Abs(mountTr.lossyScale.z));
            radius = radius * scale;
            // As wide as the back measured behind the seat, where the cape falls: the physics
            // capsule is often narrower than the body drawn around it.
            if (anchor != null && anchor.Flanks != null)
                for (float z = -0.2f; z >= -0.8f; z -= 0.2f)
                    radius = Mathf.Max(radius, Mathf.Max(anchor.Flanks.Widest(-1f, -0.5f, 0f, z),
                        anchor.Flanks.Widest(1f, -0.5f, 0f, z)) + 0.05f);
            radius = Mathf.Clamp(radius, 0.2f, 1.5f);

            // Behind the rider's hips, where the cape hangs from: a capsule under them kept
            // pushing the cape's top against the rider, and it shook.
            var spine = seat - up * (radius + 0.02f);
            var blockerTr = _capeBlocker.transform;
            blockerTr.SetPositionAndRotation(spine - forward * (0.15f + radius * 1.45f), Quaternion.LookRotation(forward, up));

            if (Mathf.Abs(radius - _blockerRadius) > 0.001f)
            {
                _blockerRadius = radius;
                _capeBlocker.SetSize(radius, radius, radius * 2.9f);
                _capeBlocker.UpdateParameters();
            }
        }

        private ClothState FindClothState(MagicaCloth cloth)
        {
            for (int i = 0; i < _cloths.Count; i++)
            {
                if (_cloths[i].Cloth == cloth)
                    return _cloths[i];
            }
            return null;
        }

        private void ReleaseCapeCloth()
        {
            for (int i = 0; i < _cloths.Count; i++)
            {
                var state = _cloths[i];
                if (state.Cloth == null)
                    continue;
                var constraint = state.Cloth.SerializeData.colliderCollisionConstraint;
                if (constraint.colliderList == null || !constraint.colliderList.Contains(_capeBlocker))
                    continue;
                constraint.colliderList = state.Original ?? new List<ColliderComponent>();
                constraint.mode = state.OriginalMode;
                state.Cloth.SetParameterChange();
            }
            _cloths.Clear();

            if (_capeBlocker != null)
                Destroy(_capeBlocker.gameObject);
            _capeBlocker = null;
        }

        private Transform CapeTransform(Transform transform)
        {
            Transform cape = null;
            while (transform != null)
            {
                if (transform == _player.transform)
                    break;
                string name = transform.name;
                if (name.IndexOf("cape", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("cloak", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    cape = transform;
                transform = transform.parent;
            }
            return cape;
        }

        private CapeTransformState FindCapeState(Transform capeTransform)
        {
            for (int i = 0; i < _capeTransforms.Count; i++)
            {
                if (_capeTransforms[i].Transform == capeTransform)
                    return _capeTransforms[i];
            }
            return null;
        }

        private void RestoreCape()
        {
            for (int i = 0; i < _capeTransforms.Count; i++)
            {
                var state = _capeTransforms[i];
                if (state.Transform != null)
                    state.Transform.localPosition = state.BaseLocalPosition;
            }
            _capeTransforms.Clear();
            _capes.Clear();
            _clothsOn = NoCloth;
            _nextBodyScan = 0f;
            ReleaseCapeCloth();
        }

        private void OnDisable()
        {
            _animator = null;
            _riderPose = null;
            _reins?.Hide();
            RestoreCape();
        }

        private void OnDestroy()
        {
            _reins?.Destroy();
            ReleaseCapeCloth();
        }
    }
}
