using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    internal sealed class CapybaraClipPlayer : MonoBehaviour
    {
        private Transform[] _bones;
        private CapybaraSkin.Clip[] _clips;
        private CapybaraSkin.Clip _current;
        private CapybaraSkin.Clip _previous;
        private float _time;
        private float _blend;
        private Character _character;
        private Vector3[] _curT;
        private Quaternion[] _curQ;
        private Vector3[] _curS;
        private Vector3[] _prevT;
        private Quaternion[] _prevQ;
        private Vector3[] _prevS;

        public void Setup(Transform[] bones, CapybaraSkin.Clip[] clips)
        {
            _bones = bones;
            _clips = clips;
            _current = FindClip("idle") ?? (clips != null && clips.Length > 0 ? clips[0] : null);
            _previous = _current;
            _character = GetComponentInParent<Character>();
            int n = bones != null ? bones.Length : 0;
            _curT = new Vector3[n];
            _curQ = new Quaternion[n];
            _curS = new Vector3[n];
            _prevT = new Vector3[n];
            _prevQ = new Quaternion[n];
            _prevS = new Vector3[n];
        }

        private void LateUpdate()
        {
            if (_bones == null || _current == null) return;
            if (_character == null)
                _character = GetComponentInParent<Character>();

            var wanted = ChooseClip();
            if (wanted != null && wanted != _current)
            {
                _previous = _current;
                _current = wanted;
                _blend = 0f;
                _time = 0f;
            }

            _time += Time.deltaTime;
            _blend = Mathf.MoveTowards(_blend, 1f, Time.deltaTime * 6f);
            Apply(_current, _previous, _time, _blend);
        }

        private CapybaraSkin.Clip ChooseClip()
        {
            if (_character != null && _character.IsSwimming())
                return FindClip("swim") ?? _current;

            if (IsLocalRider())
            {
                Vector3 stick = Access.RideStick();
                if (Mathf.Abs(stick.z) < 0.15f)
                    return FindClip("idle") ?? _current;
                if (Access.RunHeld())
                    return FindClip("run") ?? FindClip("walk") ?? _current;
                return FindClip("walk") ?? _current;
            }

            float walk = WalkSpeed();
            float speed = HorizontalSpeed();
            if (speed < 0.35f)
                return FindClip("idle") ?? _current;
            if (speed < Mathf.Max(3.5f, walk * 0.9f))
                return FindClip("walk") ?? _current;
            return FindClip("run") ?? FindClip("walk") ?? _current;
        }

        private float WalkSpeed()
        {
            if (_character == null) return MountSettings.Walk;
            var value = Access.Get(_character, "m_walkSpeed");
            if (value is float speed && speed > 0.1f)
                return speed;
            return MountSettings.Walk;
        }

        private float HorizontalSpeed()
        {
            if (_character == null) return 0f;
            Vector3 vel = _character.GetVelocity();
            return new Vector3(vel.x, 0f, vel.z).magnitude;
        }

        private bool IsLocalRider()
        {
            var player = Player.m_localPlayer;
            if (player == null || _character == null) return false;
            var attach = player.transform.parent;
            if (attach == null) return false;
            return attach.GetComponentInParent<Character>() == _character;
        }

        private CapybaraSkin.Clip FindClip(string name)
        {
            if (_clips == null) return null;
            for (int i = 0; i < _clips.Length; i++)
            {
                if (_clips[i] != null && _clips[i].Name == name)
                    return _clips[i];
            }
            return null;
        }

        private void Apply(CapybaraSkin.Clip current, CapybaraSkin.Clip previous, float time, float blend)
        {
            int boneCount = _bones.Length;
            Sample(current, time, _curT, _curQ, _curS);
            if (previous != null && previous != current && blend < 0.999f)
            {
                Sample(previous, time, _prevT, _prevQ, _prevS);
                for (int i = 0; i < boneCount; i++)
                {
                    var bone = _bones[i];
                    if (bone == null) continue;
                    bone.localPosition = Vector3.Lerp(_prevT[i], _curT[i], blend);
                    bone.localRotation = Quaternion.Slerp(_prevQ[i], _curQ[i], blend);
                    bone.localScale = ClampScale(Vector3.Lerp(_prevS[i], _curS[i], blend));
                }
                return;
            }

            for (int i = 0; i < boneCount; i++)
            {
                var bone = _bones[i];
                if (bone == null) continue;
                bone.localPosition = _curT[i];
                bone.localRotation = _curQ[i];
                bone.localScale = ClampScale(_curS[i]);
            }
        }

        private static Vector3 ClampScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Clamp(scale.x, 0.35f, 2.5f),
                Mathf.Clamp(scale.y, 0.35f, 2.5f),
                Mathf.Clamp(scale.z, 0.35f, 2.5f));
        }

        private static void Sample(CapybaraSkin.Clip clip, float time, Vector3[] t, Quaternion[] q, Vector3[] s)
        {
            int boneCount = t.Length;
            if (clip == null || clip.Frames == null || clip.Frames.Length == 0)
                return;

            float duration = Mathf.Max(0.001f, clip.Duration);
            float wrapped = time % duration;
            float frameTime = wrapped / duration * (clip.Frames.Length - 1);
            int a = Mathf.Clamp(Mathf.FloorToInt(frameTime), 0, clip.Frames.Length - 1);
            int b = Mathf.Min(a + 1, clip.Frames.Length - 1);
            float u = frameTime - a;
            var fa = clip.Frames[a];
            var fb = clip.Frames[b];
            int n = Mathf.Min(boneCount, Mathf.Min(fa.Length, fb.Length));
            for (int i = 0; i < n; i++)
            {
                t[i] = Vector3.Lerp(fa[i].T, fb[i].T, u);
                q[i] = Quaternion.Slerp(fa[i].Q, fb[i].Q, u);
                s[i] = Vector3.Lerp(fa[i].S, fb[i].S, u);
            }
        }
    }
}
