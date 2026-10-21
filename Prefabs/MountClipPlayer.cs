using System.Collections.Generic;
using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    /// <summary>
    /// Plays the clips that come inside the .skin. Idle, the gait and swim are blended by
    /// weight from the body's real speed (measured from the transform, so it works for the
    /// rider, the owner and everyone else). The gait is built from the walk clip leg by leg:
    /// each leg reads the clip at its own phase and with its own share of the cycle in the
    /// air, so one clip gives a walk, a trot and a gallop that blend into each other with the
    /// speed. The cadence follows the ground so the planted feet do not slide.
    /// </summary>
    // First of the late updates: the saddle, the rider and the camera follow the bones it poses.
    [DefaultExecutionOrder(-300)]
    internal sealed class MountClipPlayer : MonoBehaviour
    {
        private const float WeightRate = 4f;
        private const float MinTurnCadence = 0.7f; // cycles per second while turning in place
        private const float MaxCadence = 5f;

        // Below TrotFrom the clip's own walk; the trot is complete at the walk speed and the
        // gallop at the run speed. Offsets are where each leg's swing peaks in the cycle,
        // relative to the left hind leg; Swing is the share of the cycle a foot is in the air;
        // Sweep amplifies how far the thigh and upper arm swing, for a longer stride.
        private const float TrotFrom = 1.2f;
        private static readonly Gait Trot = new Gait { HindR = 0.5f, ForeL = 0.5f, ForeR = 0f, Swing = 0.5f, Sweep = 1.2f };
        private static readonly Gait Gallop = new Gait { HindR = 0.1f, ForeL = 0.55f, ForeR = 0.45f, Swing = 0.62f, Sweep = 1.5f };
        // Galloping, the front of the body rises while the front legs are in the air.
        private const float GallopPitch = 5f;

        private struct Gait
        {
            public float HindR, ForeL, ForeR, Swing, Sweep;
        }

        /// <summary>How a clip is read: backwards (its planted feet slide toward the head), and
        /// how many frame steps the loop takes from the last frame back to the first (the
        /// capybara walk lacks several frames at the seam, so one step would be a jump).</summary>
        private sealed class ClipInfo
        {
            public bool Reversed;
            public int Gap = 1;
        }

        private sealed class Leg
        {
            public string Name;
            public int[] Bones;
            public Transform Swing;      // thigh / upper arm, amplified by the gait's sweep
            public Quaternion SwingMean; // its mean local rotation over the walk
            public float Peak;           // walk phase where the foot is highest
            public float Air;            // share of the walk cycle the foot is up
        }

        private Transform[] _bones;
        private MountSkin.Clip _idle, _walk, _run, _swim;
        private readonly Dictionary<MountSkin.Clip, ClipInfo> _info = new Dictionary<MountSkin.Clip, ClipInfo>();
        private Leg[] _legs; // hind L, hind R, fore L, fore R; null = play the clips as they are
        private float _stride;  // how far a hind foot travels on the ground per cycle, model units
        private float _wIdle = 1f, _wWalk, _wRun, _wSwim;
        private float _idleTime, _swimTime, _gaitPhase;
        private float _trotMix, _gallopMix;
        private float _speed, _yawRate;
        private Vector3 _lastPos;
        private float _lastYaw;
        private bool _hasLast;
        private Character _character;
        private Vector3[] _accT, _t, _legT;
        private Quaternion[] _accQ, _q, _legQ;
        private Vector3[] _accS, _s, _legS;
        private Transform _spine;

        public void Setup(Transform[] bones, MountSkin.Clip[] clips)
        {
            _bones = bones;
            _idle = FindClip(clips, "idle") ?? (clips != null && clips.Length > 0 ? clips[0] : null);
            _walk = FindClip(clips, "walk");
            _run = FindClip(clips, "run") ?? _walk;
            _swim = FindClip(clips, "swim");
            _character = GetComponentInParent<Character>();
            int n = bones != null ? bones.Length : 0;
            _accT = new Vector3[n]; _t = new Vector3[n]; _legT = new Vector3[n];
            _accQ = new Quaternion[n]; _q = new Quaternion[n]; _legQ = new Quaternion[n];
            _accS = new Vector3[n]; _s = new Vector3[n]; _legS = new Vector3[n];
            _spine = FindBone("Spine.1");
            if (bones == null || clips == null) return;
            foreach (var clip in clips)
                if (clip != null && clip.Frames != null && clip.Frames.Length > 1)
                    _info[clip] = Measure(clip);
            _legs = MeasureLegs();
            string legs = _legs == null ? "none"
                : string.Join(", ", System.Array.ConvertAll(_legs, l => $"{l.Name} peak {l.Peak:0.00} air {l.Air:0.00}"));
            Plugin.Log.LogInfo("ValheimMontarias: gait " +
                string.Join(", ", System.Linq.Enumerable.Select(_info, p => $"{p.Key.Name}{(p.Value.Reversed ? " reversed" : "")} gap {p.Value.Gap}")) +
                $"; stride {_stride:0.00}; legs {legs}");
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
            _trotMix = Mathf.Clamp01(Mathf.InverseLerp(TrotFrom, walk, abs));
            _gallopMix = runMix;

            // Clocks.
            _idleTime += dt;
            _swimTime += dt;
            if (_walk != null)
            {
                float cadence = Cadence(abs);
                if (turning && cadence < MinTurnCadence) cadence = MinTurnCadence;
                if (_speed < -0.2f) cadence = -cadence;
                _gaitPhase = Mathf.Repeat(_gaitPhase + dt * cadence, 1f);
            }

            Pose();
            if (_legs != null)
            {
                SweepLegs();
                PitchBody();
            }
        }

        /// <summary>Cycles per second that keep a planted foot still on the ground: in one cycle
        /// the body moves as far as a foot sweeps while it is down.</summary>
        private float Cadence(float speed)
        {
            if (_legs == null || _stride <= 0.0001f)
            {
                // No legs found: the clip's own cadence, sped up with the speed.
                float cycle = Mathf.Lerp(CycleLength(_walk), CycleLength(_run), _gallopMix);
                return Mathf.Lerp(1.6f, 2.6f, _gallopMix) * Mathf.Clamp01(speed) / Mathf.Max(0.001f, cycle);
            }
            var gait = CurrentGait(0);
            float ground = 1f - gait.Swing;
            float stride = _stride * gait.Sweep * Mathf.Abs(transform.lossyScale.z);
            return Mathf.Min(MaxCadence, speed * ground / Mathf.Max(0.01f, stride));
        }

        /// <summary>Leg <paramref name="leg"/>'s swing peak relative to the left hind leg, its
        /// share of the cycle in the air and the sweep, for the current walk/trot/gallop mix.</summary>
        private Gait CurrentGait(int leg)
        {
            var natural = new Gait
            {
                HindR = Mathf.Repeat(_legs[1].Peak - _legs[0].Peak, 1f),
                ForeL = Mathf.Repeat(_legs[2].Peak - _legs[0].Peak, 1f),
                ForeR = Mathf.Repeat(_legs[3].Peak - _legs[0].Peak, 1f),
                Swing = _legs[leg].Air,
                Sweep = 1f
            };
            var a = Mix(natural, Trot, _trotMix);
            return Mix(a, Gallop, _gallopMix);
        }

        private static Gait Mix(Gait a, Gait b, float t)
        {
            return new Gait
            {
                HindR = LerpCycle(a.HindR, b.HindR, t),
                ForeL = LerpCycle(a.ForeL, b.ForeL, t),
                ForeR = LerpCycle(a.ForeR, b.ForeR, t),
                Swing = Mathf.Lerp(a.Swing, b.Swing, t),
                Sweep = Mathf.Lerp(a.Sweep, b.Sweep, t)
            };
        }

        private static float LerpCycle(float a, float b, float t)
        {
            float d = Mathf.Repeat(b - a + 0.5f, 1f) - 0.5f;
            return Mathf.Repeat(a + d * t, 1f);
        }

        private static float OffsetOf(Gait gait, int leg)
        {
            switch (leg)
            {
                case 1: return gait.HindR;
                case 2: return gait.ForeL;
                case 3: return gait.ForeR;
                default: return 0f;
            }
        }

        /// <summary>The walk-clip phase leg <paramref name="leg"/> reads at the gait clock: its
        /// place in the cycle around its swing peak, with the air time stretched or squeezed
        /// from the walk's to the gait's. At the plain walk it is the clock itself.</summary>
        private float LegPhase(int leg)
        {
            var info = _legs[leg];
            var gait = CurrentGait(leg);
            // The clock is the walk phase; leg 0 keeps its own peak and the rest follow it.
            float peak = Mathf.Repeat(_legs[0].Peak + OffsetOf(gait, leg), 1f);
            float c = Mathf.Repeat(_gaitPhase - peak + 0.5f, 1f) - 0.5f;
            float want = Mathf.Clamp(gait.Swing, 0.05f, 0.95f);
            float have = Mathf.Clamp(info.Air, 0.05f, 0.95f);
            float half = want * 0.5f;
            float mapped = Mathf.Abs(c) <= half
                ? c * have / want
                : Mathf.Sign(c) * (have * 0.5f + (Mathf.Abs(c) - half) * (1f - have) / (1f - want));
            return Mathf.Repeat(info.Peak + mapped, 1f);
        }

        /// <summary>The gait pose: the body at the clock, each leg at its own phase.</summary>
        private void SampleGait()
        {
            Sample(_walk, _gaitPhase, _t, _q, _s);
            for (int leg = 0; leg < _legs.Length; leg++)
            {
                Sample(_walk, LegPhase(leg), _legT, _legQ, _legS);
                foreach (int i in _legs[leg].Bones)
                {
                    _t[i] = _legT[i];
                    _q[i] = _legQ[i];
                    _s[i] = _legS[i];
                }
            }
        }

        /// <summary>Swings the thighs and upper arms further around their mean pose.</summary>
        private void SweepLegs()
        {
            float moving = Mathf.Clamp01(_wWalk + _wRun);
            if (moving <= 0.001f) return;
            for (int leg = 0; leg < _legs.Length; leg++)
            {
                var l = _legs[leg];
                if (l.Swing == null) continue;
                float gain = Mathf.Lerp(1f, CurrentGait(leg).Sweep, moving);
                var delta = Quaternion.Inverse(l.SwingMean) * l.Swing.localRotation;
                l.Swing.localRotation = l.SwingMean * Quaternion.SlerpUnclamped(Quaternion.identity, delta, gain);
            }
        }

        /// <summary>Galloping, the front rises while the front legs reach and drops as they land.</summary>
        private void PitchBody()
        {
            if (_spine == null) return;
            float amount = _gallopMix * Mathf.Clamp01(_wWalk + _wRun) * GallopPitch;
            if (amount <= 0.01f) return;
            float forePeak = Mathf.Repeat(_legs[0].Peak + Gallop.ForeL, 1f);
            float angle = amount * Mathf.Cos(2f * Mathf.PI * (_gaitPhase - forePeak));
            _spine.rotation = Quaternion.AngleAxis(-angle, transform.right) * _spine.rotation;
        }

        private Transform FindBone(string name)
        {
            if (_bones == null) return null;
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null && _bones[i].name == name)
                    return _bones[i];
            return null;
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
            if (_legs != null)
            {
                float gait = _wWalk + _wRun;
                if (gait > 0.001f)
                {
                    SampleGait();
                    AddPose(gait, ref total);
                }
            }
            else
            {
                Accumulate(_walk, _gaitPhase, _wWalk, ref total);
                Accumulate(_run, _gaitPhase, _wRun, ref total);
            }
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
            Sample(clip, phase, _t, _q, _s);
            AddPose(weight, ref total);
        }

        /// <summary>Blends the pose in _t/_q/_s into the accumulators.</summary>
        private void AddPose(float weight, ref float total)
        {
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

        /// <summary>One loop: the frames plus the steps from the last back to the first.</summary>
        private float CycleLength(MountSkin.Clip clip)
        {
            if (clip == null || clip.Frames == null || clip.Frames.Length < 2) return 1f;
            float step = Mathf.Max(0.001f, clip.Duration) / (clip.Frames.Length - 1);
            return step * (clip.Frames.Length - 1 + GapOf(clip));
        }

        private int GapOf(MountSkin.Clip clip) =>
            clip != null && _info.TryGetValue(clip, out var info) ? info.Gap : 1;

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

        /// <summary>Samples at a 0..1 phase. The loop runs through the frames and then takes
        /// the clip's gap steps from the last frame back to the first, so a clip whose ends do
        /// not meet still loops without a jump.</summary>
        private void Sample(MountSkin.Clip clip, float phase, Vector3[] t, Quaternion[] q, Vector3[] s)
        {
            int count = clip.Frames.Length;
            ClipInfo info = null;
            if (_info != null) _info.TryGetValue(clip, out info);
            int gap = info != null ? info.Gap : 1;
            phase = Mathf.Repeat(phase, 1f);
            if (info != null && info.Reversed) phase = 1f - phase;
            int last = count - 1;
            float frameTime = phase * (last + gap);
            int a, b;
            float u;
            if (frameTime < last)
            {
                a = Mathf.Clamp(Mathf.FloorToInt(frameTime), 0, last);
                b = Mathf.Min(a + 1, last);
                u = frameTime - a;
            }
            else
            {
                a = last;
                b = 0;
                u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((frameTime - last) / gap));
            }
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

        // ---- Measuring the clips once, at setup ----

        private static readonly string[] FootBones = { "Foot_L", "Foot_R", "Hand_L", "Hand_R" };

        /// <summary>Puts a pose on the bones and returns where the feet are, in the model's space.</summary>
        private Vector3[] Feet(Vector3[] t, Quaternion[] q, Vector3[] s, Transform[] feet)
        {
            for (int i = 0; i < _bones.Length && i < t.Length; i++)
            {
                if (_bones[i] == null) continue;
                _bones[i].localPosition = t[i];
                _bones[i].localRotation = q[i];
                _bones[i].localScale = s[i];
            }
            var result = new Vector3[feet.Length];
            for (int i = 0; i < feet.Length; i++)
                result[i] = feet[i] != null ? transform.InverseTransformPoint(feet[i].position) : Vector3.zero;
            return result;
        }

        private Transform[] FootTransforms()
        {
            var feet = new Transform[FootBones.Length];
            for (int i = 0; i < feet.Length; i++)
                feet[i] = FindBone(FootBones[i]);
            return feet;
        }

        /// <summary>The seam gap and the direction of a clip, from its feet.</summary>
        private ClipInfo Measure(MountSkin.Clip clip)
        {
            var info = new ClipInfo();
            var feet = FootTransforms();
            if (feet[0] == null || feet[1] == null) return info;
            int count = clip.Frames.Length;
            var pos = new Vector3[count][];
            for (int f = 0; f < count; f++)
            {
                var frame = clip.Frames[f];
                for (int i = 0; i < _bones.Length && i < frame.Length; i++)
                {
                    _t[i] = frame[i].T; _q[i] = frame[i].Q; _s[i] = frame[i].S;
                }
                pos[f] = Feet(_t, _q, _s, feet);
            }

            // The gap: how many ordinary frame steps the jump from the last frame to the first is.
            var steps = new List<float>(count);
            for (int f = 0; f + 1 < count; f++)
                steps.Add(Travel(pos[f], pos[f + 1]));
            steps.Sort();
            float median = steps.Count > 0 ? steps[steps.Count / 2] : 0f;
            float seam = Travel(pos[count - 1], pos[0]);
            if (median > 0.0001f)
                info.Gap = Mathf.Clamp(Mathf.RoundToInt(seam / median), 1, count / 2);

            // The direction: a hind foot on the ground must slide toward the tail.
            float drift = 0f;
            for (int foot = 0; foot < 2; foot++)
            {
                float min = float.MaxValue, max = float.MinValue;
                for (int f = 0; f < count; f++)
                {
                    min = Mathf.Min(min, pos[f][foot].y);
                    max = Mathf.Max(max, pos[f][foot].y);
                }
                float ground = min + 0.15f * (max - min);
                for (int f = 0; f + 1 < count; f++)
                    if (pos[f][foot].y <= ground && pos[f + 1][foot].y <= ground)
                        drift += pos[f + 1][foot].z - pos[f][foot].z;
            }
            info.Reversed = median > 0.0001f && drift > median;
            return info;
        }

        private static float Travel(Vector3[] a, Vector3[] b)
        {
            float sum = 0f;
            for (int i = 0; i < a.Length; i++)
                sum += Vector3.Distance(a[i], b[i]);
            return sum;
        }

        /// <summary>Each leg's bones, swing peak and air time in the walk, and the stride.</summary>
        private Leg[] MeasureLegs()
        {
            if (_walk == null || !_info.ContainsKey(_walk)) return null;
            string[] roots = { "Thigh_L", "Thigh_R", "Clavicle_L", "Clavicle_R" };
            string[] swings = { "Thigh_L", "Thigh_R", "UpperArm_L", "UpperArm_R" };
            var feet = FootTransforms();
            var legs = new Leg[4];
            for (int leg = 0; leg < 4; leg++)
            {
                var root = FindBone(roots[leg]);
                if (root == null || feet[leg] == null) return null;
                var list = new List<int>();
                for (int i = 0; i < _bones.Length; i++)
                    if (_bones[i] != null && _bones[i].IsChildOf(root))
                        list.Add(i);
                legs[leg] = new Leg { Name = roots[leg], Bones = list.ToArray(), Swing = FindBone(swings[leg]) };
            }

            const int samples = 96;
            var pos = new Vector3[samples][];
            var swingSum = new Vector4[4];
            for (int k = 0; k < samples; k++)
            {
                Sample(_walk, (float)k / samples, _t, _q, _s);
                pos[k] = Feet(_t, _q, _s, feet);
                for (int leg = 0; leg < 4; leg++)
                {
                    if (legs[leg].Swing == null) continue;
                    var r = legs[leg].Swing.localRotation;
                    var v = new Vector4(r.x, r.y, r.z, r.w);
                    if (k > 0 && Vector4.Dot(v, swingSum[leg]) < 0f) v = -v;
                    swingSum[leg] += v;
                }
            }

            float stride = 0f;
            for (int leg = 0; leg < 4; leg++)
            {
                var s = swingSum[leg].normalized;
                legs[leg].SwingMean = new Quaternion(s.x, s.y, s.z, s.w);
                float min = float.MaxValue, max = float.MinValue;
                int peak = 0;
                for (int k = 0; k < samples; k++)
                {
                    float y = pos[k][leg].y;
                    if (y < min) min = y;
                    if (y > max) { max = y; peak = k; }
                }
                // Front feet barely leave the ground at a walk: 30% of the lift keeps the
                // planted part from counting as air.
                float ground = min + 0.3f * (max - min);
                int up = 0;
                float zMin = float.MaxValue, zMax = float.MinValue;
                for (int k = 0; k < samples; k++)
                {
                    if (pos[k][leg].y > ground) { up++; continue; }
                    zMin = Mathf.Min(zMin, pos[k][leg].z);
                    zMax = Mathf.Max(zMax, pos[k][leg].z);
                }
                legs[leg].Peak = (float)peak / samples;
                legs[leg].Air = Mathf.Clamp((float)up / samples, 0.1f, 0.9f);
                if (leg < 2 && zMax > zMin) stride += 0.5f * (zMax - zMin);
            }
            _stride = stride;
            return legs;
        }
    }
}
