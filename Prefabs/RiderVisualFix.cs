using System.Collections.Generic;
using MagicaCloth2;
using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    [DefaultExecutionOrder(10000)]
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
        private Transform _leftLeg;
        private Transform _rightLeg;
        private Quaternion _leftBeforePose;
        private Quaternion _rightBeforePose;
        private Quaternion _leftAfterPose;
        private Quaternion _rightAfterPose;
        private bool _hasLegPose;

        private void LateUpdate()
        {
            if (_player == null)
                _player = GetComponent<Player>();
            if (_player == null)
                return;

            if (!JavaliControl.TryGetRidden(_player, out var mount, out _, out _) || mount == null)
            {
                RestoreLegPose();
                RestoreCape();
                return;
            }

            AdjustCape();
            BlockCapeWithMount(mount);
            ApplyLegPose(mount.gameObject);
        }

        private void ApplyLegPose(GameObject mount)
        {
            UndoPreviousLegPose();

            if (_animator == null || !_animator.isHuman)
            {
                _animator = _player.GetComponentInChildren<Animator>();
                if (_animator == null || !_animator.isHuman)
                    return;
                _leftLeg = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                _rightLeg = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            }

            if (_leftLeg == null || _rightLeg == null)
                return;

            var profile = MountSettings.Find(mount);
            float spread = profile?.LegSpread != null
                ? Mathf.Clamp(profile.LegSpread.Value, 0f, 45f)
                : 10f;
            if (spread <= 0f)
                return;

            _leftBeforePose = _leftLeg.localRotation;
            _rightBeforePose = _rightLeg.localRotation;
            var forward = _player.transform.forward;
            _leftLeg.rotation = Quaternion.AngleAxis(-spread, forward) * _leftLeg.rotation;
            _rightLeg.rotation = Quaternion.AngleAxis(spread, forward) * _rightLeg.rotation;
            _leftAfterPose = _leftLeg.localRotation;
            _rightAfterPose = _rightLeg.localRotation;
            _hasLegPose = true;
        }

        private void UndoPreviousLegPose()
        {
            if (!_hasLegPose)
                return;

            if (_leftLeg != null && Quaternion.Angle(_leftLeg.localRotation, _leftAfterPose) < 0.05f)
                _leftLeg.localRotation = _leftBeforePose;
            if (_rightLeg != null && Quaternion.Angle(_rightLeg.localRotation, _rightAfterPose) < 0.05f)
                _rightLeg.localRotation = _rightBeforePose;
            _hasLegPose = false;
        }

        private void RestoreLegPose()
        {
            UndoPreviousLegPose();
            _animator = null;
            _leftLeg = null;
            _rightLeg = null;
        }

        private void AdjustCape()
        {
            var renderers = _player.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var capeTransform = CapeTransform(renderer.transform);
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
            var cloths = _player.GetComponentsInChildren<MagicaCloth>();
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
            var attach = MountHub.FindAttach(mount.gameObject);
            var seat = attach != null ? attach.position : mountTr.position + up;

            var profile = MountSettings.Find(mount.gameObject);
            float scale = profile?.Scale != null ? Mathf.Clamp(profile.Scale.Value, 0.5f, 3f) : 1f;
            float radius = 0.35f;
            if (Access.Get(mount, "m_collider") is CapsuleCollider capsule && capsule != null)
                radius = capsule.radius * Mathf.Max(Mathf.Abs(mountTr.lossyScale.x), Mathf.Abs(mountTr.lossyScale.z));
            radius = Mathf.Clamp(radius * scale, 0.2f, 1.2f);

            var spine = seat - up * (radius + 0.06f);
            var blockerTr = _capeBlocker.transform;
            blockerTr.SetPositionAndRotation(spine - forward * (radius * 0.95f), Quaternion.LookRotation(forward, up));

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
            ReleaseCapeCloth();
        }

        private void OnDisable()
        {
            RestoreLegPose();
            RestoreCape();
        }

        private void OnDestroy()
        {
            ReleaseCapeCloth();
        }
    }
}
