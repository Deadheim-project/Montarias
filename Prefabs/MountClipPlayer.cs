using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    /// <summary>
    /// Plays the clips that come inside the .skin. Idle, walk and run are blended by weight
    /// from the body's real speed (measured from the transform, so it works for the rider,
    /// the owner and everyone else), and walk/run share one gait phase so switching between
    /// them never pops the legs. The cadence follows the speed.
    /// </summary>
    internal sealed class MountClipPlayer : MonoBehaviour
    {
        // The capybara.skin walk/run frames are stored back to front: a planted foot slides
        // toward the head. Played in reverse they step forward.
        private static readonly string[] ReversedClips = { "walk", "run" };

        // Cadence (clip playback rate) at walk speed and at run speed. The model's own stride is
        // about 0.4 m/s at 1x, far below the configured 5.5/12 m/s, so the rate cannot match the
        // ground exactly without looking frantic; these keep a natural step frequency.
        private const float WalkCadence = 1.6f;
        private const float RunCadence = 2.6f;
        private const float StrideSpeed = 0.4f;
        private const float WeightRate = 4f;

        // The clip barely moves the hips (a 4 cm bob, no side sway), so the rump looked glued
        // in place. On top of it the rear rolls and swings in one smooth wave per stride, timed
        // to the hind feet, pivoting at Spine.1 so the front and the seat stay steady.
        private const float WalkRoll = 6f, WalkYaw = 5f;
        private const float RunRoll = 4f, RunYaw = 3f;
        private const float BobGain = 1.4f;
        private const float BodyFollow = 0.2f;  // share of the sway the front of the body keeps

        private Transform[] _bones;
        private MountSkin.Clip _idle, _walk, _run, _swim;
        private float _wIdle = 1f, _wWalk, _wRun, _wSwim;
        private float _idleTime, _swimTime, _gaitPhase;
        private float _speed, _yawRate;
        private Vector3 _lastPos;
        private float _lastYaw;
        private bool _hasLast;
        private Character _character;
        private Vector3[] _accT, _t;
        private Quaternion[] _accQ, _q;
        private Vector3[] _accS, _s;
        private Transform _pelvis, _spine;
        private Vector3 _pelvisRest;
        private float _walkSwayAt, _runSwayAt;  // clip phase where the left hind foot is highest

        public void Setup(Transform[] bones, MountSkin.Clip[] clips)
        {
            _bones = bones;
            _idle = FindClip(clips, "idle") ?? (clips != null && clips.Length > 0 ? clips[0] : null);
            _walk = FindClip(clips, "walk");
            _run = FindClip(clips, "run") ?? _walk;
            _swim = FindClip(clips, "swim");
            _character = GetComponentInParent<Character>();
            int n = bones != null ? bones.Length : 0;
            _accT = new Vector3[n]; _t = new Vector3[n];
            _accQ = new Quaternion[n]; _q = new Quaternion[n];
            _accS = new Vector3[n]; _s = new Vector3[n];
            _pelvis = FindBone("Pelvis");
            _spine = FindBone("Spine.1");
            if (_pelvis != null) _pelvisRest = _pelvis.localPosition;
            _walkSwayAt = SwayPeak(_walk);
            _runSwayAt = SwayPeak(_run);
        }

        private void LateUpdate()
        {
            if (_bones == null || _idle == null) return;
            if (_character == null)
                _character = GetComponentInParent<Character>();
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            MeasureMotion(dt);
            float walk = Mathf.Max(0.5f, ReadSpeed("m_walkSpeed", MountSettings.Walk));
            float run = Mathf.Max(walk + 0.5f, ReadSpeed("m_runSpeed", MountSettings.Run));
            float abs = Mathf.Abs(_speed);
            bool swimming = _character != null && _character.IsSwimming() && _swim != null;
            bool turning = Mathf.Abs(_yawRate) > 45f;

            // Target weights.
            float move = swimming ? 0f : Mathf.Clamp01(Mathf.InverseLerp(0.2f, 1.2f, abs));
            if (!swimming && turning) move = Mathf.Max(move, 0.6f);
            float runMix = _walk == null ? 0f : Mathf.Clamp01(Mathf.InverseLerp(walk * 1.1f, run * 0.85f, abs));
            if (_walk == null) move = 0f;
            float k = dt * WeightRate;
            _wSwim = Mathf.MoveTowards(_wSwim, swimming ? 1f : 0f, k);
            _wWalk = Mathf.MoveTowards(_wWalk, move * (1f - runMix), k);
            _wRun = Mathf.MoveTowards(_wRun, move * runMix, k);
            _wIdle = Mathf.MoveTowards(_wIdle, swimming ? 0f : 1f - move, k);

            // Clocks.
            _idleTime += dt;
            _swimTime += dt;
            if (_walk != null)
            {
                float rate = Cadence(abs, walk, run);
                if (turning && rate < 0.7f) rate = 0.7f;
                if (_speed < -0.2f) rate = -rate;
                float cycle = Mathf.Lerp(CycleLength(_walk), CycleLength(_run), runMix);
                _gaitPhase = Mathf.Repeat(_gaitPhase + dt * rate / cycle, 1f);
            }

            Pose();
            SwayHips();
        }

        /// <summary>Rolls and swings the rump toward the hind leg on the ground, as a sine over
        /// the gait phase, so it is smooth whatever the feet do frame to frame.</summary>
        private void SwayHips()
        {
            if (_pelvis == null || _spine == null) return;
            float moving = _wWalk + _wRun;
            float move = Mathf.Clamp01(moving) * (1f - Mathf.Clamp01(_wSwim));
            if (move <= 0.001f) return;
            float runMix = _wRun / moving;

            _pelvis.localPosition = _pelvisRest + (_pelvis.localPosition - _pelvisRest) * Mathf.Lerp(1f, BobGain, move);

            // +1 while the left hind foot is up (standing on the right one), -1 the other way.
            float side = Mathf.Lerp(SwayWave(_walk, _walkSwayAt), SwayWave(_run, _runSwayAt), runMix) * move;
            // The hip of the lifted leg drops and the rump moves over the planted one.
            float roll = -side * Mathf.Lerp(WalkRoll, RunRoll, runMix);
            float yaw = side * Mathf.Lerp(WalkYaw, RunYaw, runMix);
            Quaternion q = Quaternion.AngleAxis(roll, transform.forward) * Quaternion.AngleAxis(yaw, transform.up);

            Vector3 pivot = _spine.position;
            Quaternion spine = _spine.rotation;
            _pelvis.SetPositionAndRotation(pivot + q * (_pelvis.position - pivot), q * _pelvis.rotation);
            _spine.rotation = Quaternion.Slerp(Quaternion.identity, q, BodyFollow) * spine;
        }

        private float SwayWave(MountSkin.Clip clip, float peak)
        {
            if (clip == null) return 0f;
            return Mathf.Cos(2f * Mathf.PI * (ClipPhase(clip, _gaitPhase) - peak));
        }

        /// <summary>The phase <see cref="Sample"/> actually reads for this clip.</summary>
        private static float ClipPhase(MountSkin.Clip clip, float phase)
        {
            phase = Mathf.Repeat(phase, 1f);
            return System.Array.IndexOf(ReversedClips, clip.Name) >= 0 ? 1f - phase : phase;
        }

        /// <summary>Poses every frame of the clip once and finds, from the first harmonic of
        /// (left - right) hind-foot height, the phase where the left foot is up.</summary>
        private float SwayPeak(MountSkin.Clip clip)
        {
            var footL = FindBone("Foot_L");
            var footR = FindBone("Foot_R");
            if (clip == null || clip.Frames == null || footL == null || footR == null) return 0f;
            int count = clip.Frames.Length;
            float c = 0f, s = 0f;
            for (int f = 0; f < count; f++)
            {
                var frame = clip.Frames[f];
                int n = Mathf.Min(_bones.Length, frame.Length);
                for (int i = 0; i < n; i++)
                {
                    if (_bones[i] == null) continue;
                    _bones[i].localPosition = frame[i].T;
                    _bones[i].localRotation = frame[i].Q;
                }
                float lift = transform.InverseTransformPoint(footL.position).y
                    - transform.InverseTransformPoint(footR.position).y;
                float a = 2f * Mathf.PI * f / count;
                c += lift * Mathf.Cos(a);
                s += lift * Mathf.Sin(a);
            }
            return Mathf.Repeat(Mathf.Atan2(s, c) / (2f * Mathf.PI), 1f);
        }

        private Transform FindBone(string name)
        {
            if (_bones == null) return null;
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null && _bones[i].name == name)
                    return _bones[i];
            return null;
        }

        /// <summary>Real cadence at a crawl (feet stick), the natural curve above it.</summary>
        private static float Cadence(float speed, float walk, float run)
        {
            float natural = WalkCadence * Mathf.Pow(Mathf.Max(0f, speed) / walk, 0.6f);
            if (speed > walk)
                natural = Mathf.Lerp(WalkCadence, RunCadence, Mathf.InverseLerp(walk, run, speed));
            return Mathf.Min(speed / StrideSpeed, natural);
        }

        private void MeasureMotion(float dt)
        {
            var root = _character != null ? _character.transform : transform;
            Vector3 pos = root.position;
            float yaw = root.eulerAngles.y;
            if (_hasLast)
            {
                Vector3 delta = pos - _lastPos;
                delta.y = 0f;
                float forward = Vector3.Dot(delta, root.forward) / dt;
                if (delta.sqrMagnitude > 25f) forward = 0f; // teleport
                float yawRate = Mathf.DeltaAngle(_lastYaw, yaw) / dt;
                float s = 1f - Mathf.Exp(-dt * 10f);
                _speed = Mathf.Lerp(_speed, forward, s);
                _yawRate = Mathf.Lerp(_yawRate, yawRate, s);
            }
            _lastPos = pos;
            _lastYaw = yaw;
            _hasLast = true;
        }

        private float ReadSpeed(string field, float fallback)
        {
            if (_character == null) return fallback;
            var value = Access.Get(_character, field);
            return value is float speed && speed > 0.1f ? speed : fallback;
        }

        private void Pose()
        {
            float total = 0f;
            Accumulate(_idle, _idleTime / Mathf.Max(0.001f, CycleLength(_idle)), _wIdle, ref total);
            Accumulate(_walk, _gaitPhase, _wWalk, ref total);
            if (_run != _walk)
                Accumulate(_run, _gaitPhase, _wRun, ref total);
            else
                Accumulate(_walk, _gaitPhase, _wRun, ref total);
            Accumulate(_swim, _swimTime / Mathf.Max(0.001f, CycleLength(_swim)), _wSwim, ref total);
            if (total <= 0.0001f) return;

            for (int i = 0; i < _bones.Length; i++)
            {
                var bone = _bones[i];
                if (bone == null) continue;
                bone.localPosition = _accT[i] / total;
                bone.localRotation = _accQ[i];
                bone.localScale = ClampScale(_accS[i] / total);
            }
        }

        private void Accumulate(MountSkin.Clip clip, float phase, float weight, ref float total)
        {
            if (clip == null || weight <= 0.001f || clip.Frames == null || clip.Frames.Length == 0) return;
            if (System.Array.IndexOf(ReversedClips, clip.Name) >= 0)
                phase = 1f - Mathf.Repeat(phase, 1f);
            Sample(clip, phase, _t, _q, _s);
            float before = total;
            total += weight;
            float u = weight / total;
            for (int i = 0; i < _bones.Length; i++)
            {
                if (before <= 0f)
                {
                    _accT[i] = _t[i] * weight;
                    _accQ[i] = _q[i];
                    _accS[i] = _s[i] * weight;
                    continue;
                }
                _accT[i] += _t[i] * weight;
                _accS[i] += _s[i] * weight;
                _accQ[i] = Quaternion.Slerp(_accQ[i], _q[i], u);
            }
        }

        /// <summary>One loop: the frames plus the step from the last back to the first.</summary>
        private static float CycleLength(MountSkin.Clip clip)
        {
            if (clip == null || clip.Frames == null || clip.Frames.Length < 2) return 1f;
            float step = Mathf.Max(0.001f, clip.Duration) / (clip.Frames.Length - 1);
            return step * clip.Frames.Length;
        }

        private static MountSkin.Clip FindClip(MountSkin.Clip[] clips, string name)
        {
            if (clips == null) return null;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].Name == name)
                    return clips[i];
            }
            return null;
        }

        private static Vector3 ClampScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Clamp(scale.x, 0.35f, 2.5f),
                Mathf.Clamp(scale.y, 0.35f, 2.5f),
                Mathf.Clamp(scale.z, 0.35f, 2.5f));
        }

        /// <summary>Samples at a 0..1 phase, wrapping the last frame into the first so the
        /// loop has no hold or jump of a whole frame at the seam.</summary>
        private static void Sample(MountSkin.Clip clip, float phase, Vector3[] t, Quaternion[] q, Vector3[] s)
        {
            int count = clip.Frames.Length;
            float frameTime = Mathf.Repeat(phase, 1f) * count;
            int a = Mathf.Clamp(Mathf.FloorToInt(frameTime), 0, count - 1);
            int b = (a + 1) % count;
            float u = frameTime - a;
            var fa = clip.Frames[a];
            var fb = clip.Frames[b];
            int n = Mathf.Min(t.Length, Mathf.Min(fa.Length, fb.Length));
            for (int i = 0; i < n; i++)
            {
                t[i] = Vector3.Lerp(fa[i].T, fb[i].T, u);
                q[i] = Quaternion.Slerp(fa[i].Q, fb[i].Q, u);
                s[i] = Vector3.Lerp(fa[i].S, fb[i].S, u);
            }
        }
    }
}
