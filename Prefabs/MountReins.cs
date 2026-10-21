using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    /// <summary>
    /// A rope around the mount's neck, a little ahead of where the rider holds on, and a rein
    /// from each side of it to each hand. Drawn by the rider (one per player), in world space,
    /// from the neck measured with the flanks and the hands where the pose put them.
    /// </summary>
    internal sealed class MountReins
    {
        // How far ahead of the hands the rope goes round the neck, and its thickness (metres).
        private const float AheadOfHands = 0.05f;
        private const float Thickness = 0.045f;
        private const int RingPoints = 24;
        private const int ReinPoints = 8;
        private static readonly Color Rope = new Color(0.32f, 0.22f, 0.12f, 1f);
        private static Material _material;

        private readonly LineRenderer _ring, _rein;
        private readonly Vector3[] _ringPoints = new Vector3[RingPoints];
        private readonly Vector3[] _reinPoints = new Vector3[ReinPoints * 2 + 3];

        public MountReins(Transform owner)
        {
            _ring = Line(owner, "MountReinsRing", true);
            _rein = Line(owner, "MountReinsRein", false);
        }

        public bool Valid => _ring != null && _rein != null;

        private static LineRenderer Line(Transform owner, string name, bool loop)
        {
            if (_material == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) return null;
                _material = new Material(shader) { color = Rope };
            }
            var go = new GameObject(name);
            go.transform.SetParent(owner, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.useWorldSpace = true;
            line.loop = loop;
            line.widthMultiplier = Thickness;
            line.startColor = line.endColor = Rope;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        public void Hide()
        {
            if (_ring != null) _ring.enabled = false;
            if (_rein != null) _rein.enabled = false;
        }

        // The rope's place, measured once per fit and then carried by the neck bone nearest it.
        private MountFlanks _measured;
        private Transform _bone;
        private Vector3 _centreInBone;
        private Quaternion _turnInBone;
        private float _left, _right, _high;

        /// <param name="handsAhead">How far ahead of the seat the hands hold on (seat space z).</param>
        public void Show(Transform seat, MountFlanks flanks, float handsAhead, Transform leftHand, Transform rightHand)
        {
            if (!Valid) return;
            if (seat == null || flanks == null || leftHand == null || rightHand == null)
            {
                Hide();
                return;
            }
            if (flanks != _measured)
            {
                _measured = flanks;
                _bone = Measure(seat, flanks, handsAhead + AheadOfHands);
            }
            if (_bone == null)
            {
                Hide();
                return;
            }

            var centre = _bone.position + _bone.rotation * _centreInBone;
            var turn = _bone.rotation * _turnInBone;
            for (int i = 0; i < RingPoints; i++)
            {
                float a = i * Mathf.PI * 2f / RingPoints;
                float x = Mathf.Sin(a) * (Mathf.Sin(a) < 0f ? _left : _right);
                _ringPoints[i] = centre + turn * new Vector3(x, Mathf.Cos(a) * _high, 0f);
            }
            _ring.positionCount = RingPoints;
            _ring.SetPositions(_ringPoints);
            _ring.enabled = true;

            // Each rein from the side of the ring, a little up the neck, to the hand, hanging slack.
            var leftKnot = centre + turn * new Vector3(-_left, _high * 0.6f, 0f);
            var rightKnot = centre + turn * new Vector3(_right, _high * 0.6f, 0f);
            var up = turn * Vector3.up;
            int n = 0;
            for (int i = 0; i <= ReinPoints; i++)
                _reinPoints[n++] = Slack(leftHand.position, leftKnot, up, i / (float)ReinPoints);
            _reinPoints[n++] = centre + up * _high;
            for (int i = 0; i <= ReinPoints; i++)
                _reinPoints[n++] = Slack(rightKnot, rightHand.position, up, i / (float)ReinPoints);
            _rein.positionCount = n;
            _rein.SetPositions(_reinPoints);
            _rein.enabled = true;
        }

        /// <summary>The neck's section at <paramref name="z"/> (seat space) as the fit measured it,
        /// kept against the trunk bone nearest its middle. That bone, or null where there is no neck.</summary>
        private Transform Measure(Transform seat, MountFlanks flanks, float z)
        {
            float top = flanks.Top(z);
            if (float.IsNaN(top)) return null;
            float bottom = top, left = 0f, right = 0f;
            for (float y = top - MountFlanks.Step; y > top - 1.2f; y -= MountFlanks.Step)
            {
                float l = flanks.At(-1f, y, z), r = flanks.At(1f, y, z);
                if (l < 0.01f && r < 0.01f) break;
                bottom = y;
                left = Mathf.Max(left, l);
                right = Mathf.Max(right, r);
            }
            if (left < 0.01f || right < 0.01f) return null;
            _left = left + Thickness;
            _right = right + Thickness;
            _high = (top - bottom) * 0.5f + Thickness;

            var centre = seat.TransformPoint(new Vector3(0f, (top + bottom) * 0.5f, z));
            var bone = MountGear.TrunkBoneNear(seat.parent != null ? seat.parent : seat, centre, false, true);
            if (bone == null) return null;
            _centreInBone = Quaternion.Inverse(bone.rotation) * (centre - bone.position);
            _turnInBone = Quaternion.Inverse(bone.rotation) * seat.rotation;
            Plugin.Log?.LogInfo($"ValheimMontarias: reins round the neck on {bone.name}, {z:0.00} m ahead of the seat, " +
                $"{_left + _right:0.00} m wide, {_high * 2f:0.00} m deep");
            return bone;
        }

        private static Vector3 Slack(Vector3 from, Vector3 to, Vector3 up, float t) =>
            Vector3.Lerp(from, to, t) - up * (Mathf.Sin(t * Mathf.PI) * 0.04f);

        public void Destroy()
        {
            if (_ring != null) Object.Destroy(_ring.gameObject);
            if (_rein != null) Object.Destroy(_rein.gameObject);
        }
    }
}
