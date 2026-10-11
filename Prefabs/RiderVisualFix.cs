using System.Collections.Generic;
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

        private readonly List<CapeTransformState> _capeTransforms = new List<CapeTransformState>();
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
        }

        private void OnDisable()
        {
            RestoreLegPose();
            RestoreCape();
        }
    }
}
