using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    /// <summary>
    /// The riding pose, laid over whatever the player's animator played this frame: hips on the
    /// seat, trunk leaning a little forward, thighs and shins around the mount's flanks (as
    /// close as FolgaPernas lets them), feet forward and hands in front, where the reins would
    /// be. The game's riding poses were made for one body each (the Lox's back is two metres
    /// wide, the Asksvin's is a raptor's); this one is measured on the body under the rider.
    /// Every bone is set from scratch each frame, so it never builds on itself.
    /// </summary>
    internal sealed class RiderPose
    {
        public sealed class Settings
        {
            /// <summary>Degrees the thigh points forward of straight down.</summary>
            public float KneeForward = 35f;
            /// <summary>Degrees the thighs open out from straight down, at the least (more where
            /// the flanks push them).</summary>
            public float ThighOut = 40f;
            /// <summary>Degrees the shin hangs behind straight down.</summary>
            public float ShinBack = 30f;
            /// <summary>Degrees the trunk leans forward of upright.</summary>
            public float Lean = 20f;
            /// <summary>Room between the legs and the flanks (metres).</summary>
            public float Gap = 0.07f;
            /// <summary>The hips over the seat, in the seat's space (metres).</summary>
            public Vector3 Hips = new Vector3(0f, 0.1f, 0f);
            /// <summary>The hands, in the seat's space; x is each hand's distance from the middle.
            /// Where the body is under them (z ahead of the seat), they hold on to it instead, on
            /// its top at that z, and y is only used where there is none. Zero length leaves the
            /// arms to the animation.</summary>
            public Vector3 Hands = new Vector3(0.17f, 0.35f, 0.65f);
        }

        private readonly Transform _hips, _spine, _head;
        // The trunk from the hips up, eased frame to frame: the game animates the player in
        // FixedUpdate (50 a second) and the screen draws faster, so the chest and head stepped
        // between animation frames and the rider shook from the waist up.
        private readonly Transform[] _trunk;
        private readonly Quaternion[] _eased;
        private bool _easing;
        private const float TrunkEase = 0.08f;
        private readonly Transform[] _thigh = new Transform[2], _shin = new Transform[2], _foot = new Transform[2], _toes = new Transform[2];
        private readonly Transform[] _arm = new Transform[2], _forearm = new Transform[2], _hand = new Transform[2];

        /// <summary>Where the knees and feet ended up last frame, in the seat's space (left, right).</summary>
        public readonly Vector3[] Knees = new Vector3[2], Feet = new Vector3[2];

        public bool Valid { get; }

        /// <summary>The hand bone (0 left, 1 right), or null.</summary>
        public Transform Hand(int i) => _hand[i];

        public RiderPose(Animator animator)
        {
            if (animator == null || !animator.isHuman) return;
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            _head = animator.GetBoneTransform(HumanBodyBones.Head);
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                _thigh[i] = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                _shin[i] = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                _foot[i] = animator.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                _toes[i] = animator.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                _arm[i] = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                _forearm[i] = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                _hand[i] = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            }
            var trunk = new System.Collections.Generic.List<Transform>();
            foreach (var bone in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
                HumanBodyBones.Neck, HumanBodyBones.Head })
            {
                var t = animator.GetBoneTransform(bone);
                if (t != null) trunk.Add(t);
            }
            _trunk = trunk.ToArray();
            _eased = new Quaternion[_trunk.Length + 1];
            Valid = _hips != null && _spine != null && _head != null
                && _thigh[0] != null && _thigh[1] != null && _shin[0] != null && _shin[1] != null
                && _foot[0] != null && _foot[1] != null;
        }

        public void Apply(Transform seat, MountFlanks flanks, Settings settings)
        {
            if (!Valid || seat == null) return;
            _hips.position = seat.TransformPoint(settings.Hips);
            float t = 1f - Mathf.Exp(-Time.deltaTime / TrunkEase);
            _hips.localRotation = Ease(0, _hips.localRotation, t);
            LeanTrunk(seat, settings.Lean);
            for (int i = 0; i < _trunk.Length; i++)
                _trunk[i].localRotation = Ease(i + 1, _trunk[i].localRotation, t);
            _easing = true;
            for (int i = 0; i < 2; i++)
                Leg(i, seat, flanks, settings);
            if (settings.Hands.sqrMagnitude > 1e-4f)
                for (int i = 0; i < 2; i++)
                    Arm(i, seat, Grip(i, flanks, settings.Hands));
        }

        private Quaternion Ease(int i, Quaternion target, float t)
        {
            _eased[i] = _easing ? Quaternion.Slerp(_eased[i], target, t) : target;
            return _eased[i];
        }

        /// <summary>The line from the hips to the head, <paramref name="lean"/> degrees forward of
        /// the seat's up, whatever slant the animation gave it.</summary>
        private void LeanTrunk(Transform seat, float lean)
        {
            var now = _head.position - _hips.position;
            var wanted = Quaternion.AngleAxis(lean, seat.right) * seat.up;
            _spine.rotation = Quaternion.FromToRotation(now, wanted) * _spine.rotation;
        }

        /// <summary>
        /// The thigh goes forward KneeForward degrees and out ThighOut degrees, or further if the
        /// knee would still be in the flank; the shin then hangs ShinBack degrees behind straight down, as close
        /// in as the body under the knee lets it -- so on a round body the shin wraps under the
        /// widest part, and on a slim one the legs simply hang.
        /// </summary>
        private void Leg(int i, Transform seat, MountFlanks flanks, Settings settings)
        {
            float side = i == 0 ? -1f : 1f;
            var hip = seat.InverseTransformPoint(_thigh[i].position);
            float thighLength = Vector3.Distance(_thigh[i].position, _shin[i].position);
            float shinLength = Vector3.Distance(_shin[i].position, _foot[i].position);

            var knee = Swing(hip, thighLength, settings.KneeForward, side, Mathf.Clamp(settings.ThighOut, 0f, 85f), 85f, flanks, settings.Gap);
            var foot = Swing(knee, shinLength, -settings.ShinBack, side, -35f, 85f, flanks, settings.Gap);
            Knees[i] = knee;
            Feet[i] = foot;

            var kneeWorld = seat.TransformPoint(knee);
            var footWorld = seat.TransformPoint(foot);
            // Thigh onto the knee, then turned about itself so the knee bends in the plane of
            // thigh and shin; then the shin, a pure bend, onto the foot.
            var thighNow = _shin[i].position - _thigh[i].position;
            var thighWanted = kneeWorld - _thigh[i].position;
            _thigh[i].rotation = Quaternion.FromToRotation(thighNow, thighWanted) * _thigh[i].rotation;
            var shinNow = _foot[i].position - _shin[i].position;
            var shinWanted = footWorld - kneeWorld;
            var axis = thighWanted.normalized;
            var a = Vector3.ProjectOnPlane(shinNow, axis);
            var b = Vector3.ProjectOnPlane(shinWanted, axis);
            if (a.sqrMagnitude > 1e-4f && b.sqrMagnitude > 1e-4f)
                _thigh[i].rotation = Quaternion.AngleAxis(Vector3.SignedAngle(a, b, axis), axis) * _thigh[i].rotation;
            shinNow = _foot[i].position - _shin[i].position;
            _shin[i].rotation = Quaternion.FromToRotation(shinNow, shinWanted) * _shin[i].rotation;

            // The foot along the body, toes a little out and down.
            if (_toes[i] != null)
            {
                var toesNow = _toes[i].position - _foot[i].position;
                var toesWanted = Quaternion.AngleAxis(side * 15f, seat.up) * Quaternion.AngleAxis(20f, seat.right) * seat.forward;
                _foot[i].rotation = Quaternion.FromToRotation(toesNow, toesWanted) * _foot[i].rotation;
            }
        }

        /// <summary>
        /// From <paramref name="from"/> (seat space), a limb of <paramref name="length"/> pointing
        /// <paramref name="forward"/> degrees ahead of straight down and turned out to the side by
        /// the least angle (between <paramref name="minOut"/> and <paramref name="maxOut"/>) that
        /// keeps its end and middle <paramref name="gap"/> off the body. Where it ends.
        /// </summary>
        private static Vector3 Swing(Vector3 from, float length, float forward, float side, float minOut, float maxOut,
            MountFlanks flanks, float gap)
        {
            var hang = Quaternion.AngleAxis(-forward, Vector3.right) * Vector3.down;
            Vector3 end = from;
            for (float angle = minOut; angle <= maxOut; angle += 2.5f)
            {
                var dir = Quaternion.AngleAxis(side * angle, Vector3.forward) * hang;
                end = from + dir * length;
                if (Clear(end, side, flanks, gap) && Clear(from + dir * (length * 0.5f), side, flanks, gap)
                    && Clear(from + dir * (length * 0.75f), side, flanks, gap))
                    return end;
            }
            return end;
        }

        private static bool Clear(Vector3 point, float side, MountFlanks flanks, float gap) =>
            flanks == null || side * point.x >= flanks.At(side, point.y, point.z) + gap;

        /// <summary>Where hand <paramref name="i"/> goes, in the seat's space: on the body ahead of the
        /// seat, a hand on each side of its top, as if holding the mane; in the air only where
        /// there is no body to hold.</summary>
        private static Vector3 Grip(int i, MountFlanks flanks, Vector3 hands)
        {
            float side = i == 0 ? -1f : 1f;
            float top = flanks != null ? flanks.Top(hands.z) : float.NaN;
            if (float.IsNaN(top)) return new Vector3(side * hands.x, hands.y, hands.z);
            float x = Mathf.Clamp(flanks.At(side, top - 0.08f, hands.z), 0.06f, hands.x);
            return new Vector3(side * x, top + 0.03f, hands.z);
        }

        /// <summary>Two-bone reach of the hand to <paramref name="grip"/>, the elbow out and down.</summary>
        private void Arm(int i, Transform seat, Vector3 grip)
        {
            if (_arm[i] == null || _forearm[i] == null || _hand[i] == null) return;
            float side = i == 0 ? -1f : 1f;
            var target = seat.TransformPoint(grip);
            var shoulder = _arm[i].position;
            float upper = Vector3.Distance(shoulder, _forearm[i].position);
            float lower = Vector3.Distance(_forearm[i].position, _hand[i].position);
            var toTarget = target - shoulder;
            float reach = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upper - lower) + 0.01f, upper + lower - 0.01f);
            var dir = toTarget.normalized;
            float along = (upper * upper - lower * lower + reach * reach) / (2f * reach);
            float height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            var pole = Vector3.ProjectOnPlane(seat.right * side - seat.up * 0.7f - seat.forward * 0.3f, dir).normalized;
            var elbow = shoulder + dir * along + pole * height;
            var hand = shoulder + dir * reach;

            _arm[i].rotation = Quaternion.FromToRotation(_forearm[i].position - shoulder, elbow - shoulder) * _arm[i].rotation;
            _forearm[i].rotation = Quaternion.FromToRotation(_hand[i].position - _forearm[i].position, hand - elbow) * _forearm[i].rotation;
        }
    }
}
