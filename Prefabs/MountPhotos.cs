using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    /// <summary>
    /// Admin test (console "montarias_fotos [id ...]", or Ctrl+F9): rides each mount in turn and
    /// photographs rider and saddle from fixed angles -- standing, walking and running, and the
    /// Moder also in the air -- at noon and without the HUD, into
    /// BepInEx/montarias-fotos/&lt;date_time&gt;/. Each mount goes straight ahead a few dozen
    /// metres where the rider looks: start it somewhere open. The same command again stops it.
    /// </summary>
    internal sealed class MountPhotos : MonoBehaviour
    {
        private static MountPhotos _running;
        private string _dir;
        private MountPhotoCamera _camera;
        private bool _hadDebugTime;
        private float _debugTime;
        private bool _hudWasHidden;

        public static void Run(IList<string> ids)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            if (!Access.IsAdmin())
            {
                player.Message(MessageHud.MessageType.Center, "Apenas administradores.");
                return;
            }
            if (_running != null)
            {
                Destroy(_running.gameObject);
                player.Message(MessageHud.MessageType.Center, "Fotos interrompidas.");
                return;
            }
            var profiles = new List<MountProfile>();
            foreach (var profile in MountSettings.All ?? Array.Empty<MountProfile>())
            {
                if (profile != null && (ids == null || ids.Count == 0 || ids.Contains(profile.Id)))
                    profiles.Add(profile);
            }
            var go = new GameObject("MontariasFotos");
            DontDestroyOnLoad(go);
            _running = go.AddComponent<MountPhotos>();
            _running.StartCoroutine(_running.Shoot(profiles));
        }

        private static void Log(string text) => Plugin.Log?.LogInfo("ValheimMontarias: [fotos] " + text);

        private IEnumerator Shoot(List<MountProfile> profiles)
        {
            _dir = Path.Combine(Paths.BepInExRootPath, "montarias-fotos", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(_dir);
            Log($"start: {_dir}");
            HideConsole();
            var env = EnvMan.instance;
            if (env != null)
            {
                _hadDebugTime = env.m_debugTimeOfDay;
                _debugTime = env.m_debugTime;
                env.m_debugTimeOfDay = true;
                env.m_debugTime = 0.5f;
            }
            if (Hud.instance != null)
            {
                _hudWasHidden = Hud.instance.m_userHidden;
                Hud.instance.m_userHidden = true;
            }
            if (GameCamera.instance != null)
                _camera = GameCamera.instance.gameObject.AddComponent<MountPhotoCamera>();

            foreach (var profile in profiles)
                yield return OneMount(profile);

            var player = Player.m_localPlayer;
            if (player != null && MountControl.IsRiding(player))
                MountControl.Dismount(player);
            Log("done");
            Destroy(gameObject);
        }

        private IEnumerator OneMount(MountProfile profile)
        {
            var player = Player.m_localPlayer;
            if (player == null) yield break;
            if (MountControl.IsRiding(player) || MountControl.FindOwned(player, MountHub.IsOurs) != null)
            {
                MountControl.Dismount(player);
                yield return new WaitForSeconds(2f);
            }

            MountControl.TrySummonProfile(player, profile);
            Character mount = null;
            float until = Time.time + 25f;
            while (Time.time < until)
            {
                var ridden = MountControl.RiddenBy(player);
                if (ridden != null && MountSettings.Find(ridden.gameObject) == profile)
                {
                    mount = ridden;
                    break;
                }
                yield return null;
            }
            if (mount == null)
            {
                Log($"{profile.Id}: did not get on it");
                yield break;
            }
            yield return new WaitForSeconds(3f);
            var anchor = Anchor(mount);
            if (anchor == null)
            {
                Log($"{profile.Id}: no fitted saddle");
                yield break;
            }

            string id = profile.Id;
            var size = anchor.BodySize;
            float reach = Mathf.Clamp(Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 1.1f, 3.2f, 22f);
            float close = Mathf.Max(2.6f, reach * 0.5f);
            Aim(mount, anchor, new Vector3(1f, 0.12f, 0f) * reach);
            yield return Shot($"{id}-1-direita");
            Aim(mount, anchor, new Vector3(-1f, 0.12f, 0f) * reach);
            yield return Shot($"{id}-2-esquerda");
            Aim(mount, anchor, new Vector3(0.6f, 0.35f, 0.75f) * reach);
            yield return Shot($"{id}-3-frente");
            Aim(mount, anchor, new Vector3(-0.55f, 0.45f, -0.75f) * reach);
            yield return Shot($"{id}-4-tras");
            Aim(mount, anchor, new Vector3(0.1f, 0.9f, -0.35f) * reach);
            yield return Shot($"{id}-5-cima");
            Aim(mount, anchor, new Vector3(1f, 0.05f, 0.15f) * close);
            yield return Shot($"{id}-6-perto");

            Aim(mount, anchor, new Vector3(1f, 0.12f, 0f) * reach);
            MountControl.TestStick = new Vector3(0f, 0f, 1f);
            MountControl.TestRun = false;
            yield return new WaitForSeconds(1.5f);
            for (int i = 0; i < 3; i++)
            {
                yield return Shot($"{id}-7-andando-{i + 1}");
                yield return new WaitForSeconds(0.2f);
            }
            yield return Shake($"{id}-andando", mount, anchor, 2f);
            MountControl.TestRun = true;
            yield return new WaitForSeconds(1.5f);
            yield return Shake($"{id}-correndo", mount, anchor, 2f);
            for (int i = 0; i < 3; i++)
            {
                yield return Shot($"{id}-8-correndo-{i + 1}");
                yield return new WaitForSeconds(0.15f);
            }
            MountControl.TestRun = false;
            MountControl.TestStick = Vector3.zero;
            yield return new WaitForSeconds(1.5f);

            if (profile.Flies && mount != null)
            {
                MountControl.TakeOff(mount);
                yield return new WaitForSeconds(5f);
                Aim(mount, anchor, new Vector3(1f, 0.1f, 0f) * reach);
                yield return Shot($"{id}-9-voando-direita");
                Aim(mount, anchor, new Vector3(-0.5f, 0.35f, -0.8f) * reach);
                yield return Shot($"{id}-9-voando-tras");
                Aim(mount, anchor, new Vector3(1f, -0.25f, 0.2f) * reach);
                yield return Shot($"{id}-9-voando-baixo");
                MountControl.TestVertical = -1f;
                float landBy = Time.time + 25f;
                while (mount != null && mount.IsFlying() && Time.time < landBy)
                    yield return null;
                MountControl.TestVertical = 0f;
                yield return new WaitForSeconds(3f);
                Aim(mount, anchor, new Vector3(1f, 0.12f, 0f) * reach);
                yield return Shot($"{id}-10-pousado");
            }
            MountControl.TestStick = null;
            Log($"{id}: photos taken");
        }

        private static MountSaddleAnchor Anchor(Character mount)
        {
            var saddle = MountGear.FindSaddle(mount.gameObject);
            var anchor = saddle != null ? saddle.GetComponent<MountSaddleAnchor>() : null;
            return anchor != null && anchor.Seat != null ? anchor : null;
        }

        /// <summary>The camera at <paramref name="offset"/> from the seat, in the mount's axes
        /// (x right, y up, z forward), looking at the rider.</summary>
        private void Aim(Character mount, MountSaddleAnchor anchor, Vector3 offset)
        {
            if (_camera == null) return;
            _camera.Frame = mount != null ? mount.transform : null;
            _camera.Look = anchor != null ? anchor.Seat : null;
            _camera.Offset = offset;
        }

        private IEnumerator Shot(string name)
        {
            // Let the camera get there and the frame render.
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            Probe(name);
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, name + ".png"));
            yield return null;
            yield return null;
        }

        /// <summary>Where the rider's parts are against the seat (its space, metres) as the
        /// frame is drawn: x right, y up, z forward.</summary>
        private void Probe(string name)
        {
            var player = Player.m_localPlayer;
            var seat = _camera != null ? _camera.Look : null;
            if (player == null || seat == null) return;
            string At(Vector3 world) => seat.InverseTransformPoint(world).ToString("0.00");
            var text = new System.Text.StringBuilder($"{name}: root {At(player.transform.position)}");
            var attach = _camera.Frame != null ? MountHub.FindAttach(_camera.Frame.gameObject) : null;
            if (attach != null) text.Append($" attach {At(attach.position)}");
            var animator = player.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null) text.Append($" hips {At(hips.position)}");
                text.Append($" animator {At(animator.transform.position)}");
            }
            foreach (var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (skin.name.IndexOf("body", StringComparison.OrdinalIgnoreCase) < 0) continue;
                text.Append($" {skin.name} centre {At(skin.bounds.center)}");
                break;
            }
            var saddle = seat.Find(MountGear.VisualName);
            var renderer = saddle != null ? saddle.GetComponent<Renderer>() : null;
            if (renderer != null) text.Append($" saddle centre {At(renderer.bounds.center)} size {renderer.bounds.size.ToString("0.00")}");
            Log(text.ToString());
        }

        /// <summary>
        /// How much each part shakes as the frames are drawn, over <paramref name="seconds"/>: the
        /// mean and the worst change of its velocity from one frame to the next (mm per frame),
        /// in the mount's frame and in the world. Smooth motion keeps it near zero; a part
        /// pulled back and forth between two places shows it at once.
        /// </summary>
        private IEnumerator Shake(string name, Character mount, MountSaddleAnchor anchor, float seconds)
        {
            var player = Player.m_localPlayer;
            if (player == null || mount == null) yield break;
            var animator = player.GetComponentInChildren<Animator>();
            Transform hips = null, head = null, spine = null, chest = null, neck = null;
            if (animator != null && animator.isHuman)
            {
                hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                head = animator.GetBoneTransform(HumanBodyBones.Head);
                spine = animator.GetBoneTransform(HumanBodyBones.Spine);
                chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            }
            var fix = player.GetComponent<RiderVisualFix>();
            float movedAfter = 0f, movedWorst = 0f;
            int movedFrames = 0;
            Renderer cape = null;
            foreach (var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                string n = renderer.name.ToLowerInvariant();
                if (n.Contains("cape") || n.Contains("cloak")) { cape = renderer; break; }
            }
            var parts = new (string Name, Func<Vector3?> At)[]
            {
                ("mount", () => mount != null ? mount.transform.position : (Vector3?)null),
                ("seat", () => anchor != null && anchor.Seat != null ? anchor.Seat.position : (Vector3?)null),
                ("root", () => player.transform.position),
                ("hips", () => hips != null ? hips.position : (Vector3?)null),
                ("head", () => head != null ? head.position : (Vector3?)null),
                ("cape", () => cape != null ? cape.bounds.center : (Vector3?)null),
                ("spine", () => spine != null ? spine.position : (Vector3?)null),
                ("chest", () => chest != null ? chest.position : (Vector3?)null),
                ("neck", () => neck != null ? neck.position : (Vector3?)null),
            };
            int count = parts.Length;
            var world = new List<Vector3>[count];
            var local = new List<Vector3>[count];
            for (int i = 0; i < count; i++) { world[i] = new List<Vector3>(); local[i] = new List<Vector3>(); }
            int frames = 0;
            float until = Time.time + seconds;
            while (Time.time < until && mount != null)
            {
                yield return new WaitForEndOfFrame();
                frames++;
                if (fix != null && head != null)
                {
                    float d = (head.position - fix.HeadAfterPose).magnitude * 1000f;
                    movedAfter += d;
                    movedWorst = Mathf.Max(movedWorst, d);
                    movedFrames++;
                }
                for (int i = 0; i < count; i++)
                {
                    var at = parts[i].At();
                    if (at == null) continue;
                    world[i].Add(at.Value);
                    local[i].Add(mount.transform.InverseTransformPoint(at.Value));
                }
            }
            var text = new System.Text.StringBuilder($"{name}: shake over {frames} frames (~{frames / seconds:0} fps)");
            if (animator != null) text.Append($", animator {animator.updateMode}/{animator.cullingMode}");
            if (movedFrames > 0) text.Append($", head moved after the pose {movedAfter / movedFrames:0.0}/{movedWorst:0.0} mm");
            for (int i = 0; i < count; i++)
                text.Append($"; {parts[i].Name} {Jerk(local[i])} on the mount, {Jerk(world[i])} in the world");
            Log(text.ToString());
        }

        private static string Jerk(List<Vector3> points)
        {
            if (points.Count < 3) return "-";
            float sum = 0f, worst = 0f;
            for (int i = 2; i < points.Count; i++)
            {
                float j = (points[i] - 2f * points[i - 1] + points[i - 2]).magnitude * 1000f;
                sum += j;
                worst = Mathf.Max(worst, j);
            }
            return $"{sum / (points.Count - 2):0.0}/{worst:0.0}";
        }

        private static void HideConsole()
        {
            var console = global::Console.instance;
            if (console != null && Access.Get(console, "m_chatWindow") is RectTransform window && window != null)
                window.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            MountControl.TestStick = null;
            MountControl.TestRun = false;
            MountControl.TestVertical = 0f;
            var env = EnvMan.instance;
            if (env != null)
            {
                env.m_debugTimeOfDay = _hadDebugTime;
                env.m_debugTime = _debugTime;
            }
            if (Hud.instance != null)
                Hud.instance.m_userHidden = _hudWasHidden;
            if (_camera != null)
                Destroy(_camera);
            if (_running == this)
                _running = null;
        }
    }

    /// <summary>Holds the camera where MountPhotos aims it, after GameCamera has placed it.</summary>
    [DefaultExecutionOrder(30000)]
    internal sealed class MountPhotoCamera : MonoBehaviour
    {
        /// <summary>The offset's axes, flattened onto the ground.</summary>
        public Transform Frame;
        /// <summary>What the camera looks at (the seat); the offset is measured from it.</summary>
        public Transform Look;
        public Vector3 Offset;

        private void LateUpdate()
        {
            if (Frame == null || Look == null) return;
            var forward = Vector3.ProjectOnPlane(Frame.forward, Vector3.up);
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var target = Look.position + Vector3.up * 0.35f;
            var position = target + right * Offset.x + Vector3.up * Offset.y + forward * Offset.z;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            Mark(0, Look.position, Color.green);
            var attach = MountHub.FindAttach(Frame.gameObject);
            if (attach != null) Mark(1, attach.position, Color.blue);
            var animator = Player.m_localPlayer != null ? Player.m_localPlayer.GetComponentInChildren<Animator>() : null;
            var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips != null) Mark(2, hips.position, Color.red);
        }

        // Seat (green), attach point (blue) and the rider's hips bone (red), drawn in the photos.
        private readonly GameObject[] _marks = new GameObject[3];

        private void Mark(int index, Vector3 at, Color color)
        {
            if (_marks[index] == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(go.GetComponent<Collider>());
                go.transform.localScale = Vector3.one * 0.07f;
                var renderer = go.GetComponent<Renderer>();
                renderer.material = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default")) { color = color };
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _marks[index] = go;
            }
            _marks[index].transform.position = at;
        }

        private void OnDestroy()
        {
            foreach (var mark in _marks)
                if (mark != null) Destroy(mark);
        }
    }
}
