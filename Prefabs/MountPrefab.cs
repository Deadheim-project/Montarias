using System;
using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    /// <summary>
    /// Runtime clone of vanilla Boar. Mesh stays vanilla until a custom asset replaces it.
    /// Saddle collider/mesh is copied from Lox/Asksvin but hidden — we only need Sadle.
    /// </summary>
    internal static class MountPrefab
    {
        public const string PrefabName = "JavaliMontaria";
        public const string DisplayName = "Capivara";
        private const float InfiniteStamina = 1000000f;

        internal static GameObject Prefab { get; private set; }

        private static Transform _hidden;

        private static Transform Hidden
        {
            get
            {
                if (_hidden != null) return _hidden;
                var go = new GameObject("ValheimMontarias_HiddenPrefabs");
                go.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(go);
                _hidden = go.transform;
                return _hidden;
            }
        }

        public static void ApplyMoveSpeed(Character character)
        {
            ApplyMoveSpeed(character, MountSettings.Find(character != null ? character.gameObject : null) ?? MountSettings.Default);
        }

        public static void ApplyMoveSpeed(Character character, MountProfile profile)
        {
            if (character == null) return;
            var mountProfile = profile ?? MountSettings.Find(character != null ? character.gameObject : null) ?? MountSettings.Default;
            float walk = mountProfile != null ? mountProfile.WalkSpeed.Value : MountSettings.Walk;
            float run = mountProfile != null ? mountProfile.RunSpeed.Value : MountSettings.Run;
            float jump = mountProfile != null ? mountProfile.JumpHeight.Value : MountSettings.Jump;
            float bonus = RiderBonus(character);
            Access.Set(character, "m_speed", walk * bonus);
            Access.Set(character, "m_walkSpeed", walk * bonus);
            Access.Set(character, "m_runSpeed", run * bonus);
            Access.Set(character, "m_acceleration", 12f);
            Access.Set(character, "m_jumpForce", jump);
            Access.Set(character, "m_jumpForceForward", 1.2f);
            ApplyName(character, mountProfile);
        }

        /// <summary>The riding skill bonus of the local player, for a mount that is theirs. The
        /// owner's client is the one that moves the mount, so that is the only place the bonus
        /// has to exist; the prefab and everyone else's mounts stay at the configured speed.</summary>
        private static float RiderBonus(Character character)
        {
            if (character == null) return 1f;
            var go = character.gameObject;
            var profile = MountSettings.Find(go);
            if (profile == null) return 1f;
            var player = Player.m_localPlayer;
            if (player == null) return 1f;
            long owner = MountControl.OwnerId(go);
            return owner != 0L && owner == player.GetPlayerID() ? MountRoster.SpeedBonus : 1f;
        }

        public static void ApplyName(Character character)
        {
            ApplyName(character, MountSettings.Find(character != null ? character.gameObject : null) ?? MountSettings.Default);
        }

        public static void ApplyName(Character character, MountProfile profile)
        {
            if (character == null) return;
            string name = profile != null ? profile.Name : (MountSettings.Default != null ? MountSettings.Default.Name : DisplayName);
            Access.Set(character, "m_name", name);
        }

        public static void ApplyScale(GameObject go)
        {
            ApplyScale(go, MountSettings.Find(go) ?? MountSettings.Default);
        }

        public static void ApplyScale(GameObject go, MountProfile profile)
        {
            if (go == null) return;
            float size = profile != null ? Mathf.Clamp(profile.Scale.Value, 0.5f, 3f) : MountSettings.Size;
            go.transform.localScale = Vector3.one;

            var visualTr = FindVisual(go);
            if (visualTr != null && visualTr != go.transform)
                visualTr.localScale = Vector3.one * size;
            else
                go.transform.localScale = Vector3.one * size;

            HideSaddleVisuals(go);
            MountVisuals.Apply(go, profile);
            FitSeat(go);
        }

        public static void ApplyAll(GameObject go)
        {
            if (go == null) return;
            var profile = MountSettings.Find(go) ?? MountSettings.Default;
            ApplyMoveSpeed(go.GetComponent<Character>(), profile);
            ApplyScale(go, profile);
        }

        public static void ApplyInfiniteStamina(GameObject go)
        {
            var saddle = go != null ? go.GetComponentInChildren<Sadle>(true) : null;
            if (saddle == null) return;

            Access.Set(saddle, "m_maxStamina", InfiniteStamina);
            Access.Set(saddle, "m_runStaminaDrain", 0f);
            Access.Set(saddle, "m_staminaDrain", 0f);
            Access.Set(saddle, "m_staminaDrainPerSec", 0f);
            Access.Set(saddle, "m_swimStaminaDrain", 0f);
            Access.Call(saddle, "SetMaxStamina", InfiniteStamina);
            Access.Call(saddle, "SetStamina", InfiniteStamina);
        }

        public static void ApplyProfile(MountProfile profile)
        {
            if (profile == null) return;
            foreach (var character in Character.GetAllCharacters())
            {
                if (character == null || character.gameObject == null) continue;
                if (MountSettings.Find(character.gameObject) != profile) continue;
                ApplyAll(character.gameObject);
            }
            var target = string.IsNullOrEmpty(profile.PrefabName) ? Prefab : ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(profile.PrefabName) : null;
            if (target != null)
            {
                ApplyMoveSpeed(target.GetComponent<Character>(), profile);
                ApplyScale(target, profile);
            }
        }

        public static Transform FindAttach(GameObject go)
        {
            if (go == null) return null;
            var named = go.transform.Find("AttachPoint");
            if (named != null) return named;
            var sadle = go.GetComponentInChildren<Sadle>(true);
            return Access.Get(sadle, "m_attachPoint") as Transform;
        }

        /// <summary>
        /// Seat on the mesh back in root space. Visual is scaled separately;
        /// Y comes from the boar skin (never the rider). Z stays near the spine.
        /// </summary>
        public static void FitSeat(GameObject go)
        {
            MountGear.Fit(go);
        }

        public static void KeepHumanScale(Player player)
        {
            if (player == null) return;
            var parent = player.transform.parent;
            if (parent == null) return;
            var lossy = parent.lossyScale;
            player.transform.localScale = new Vector3(
                ApproxInverse(lossy.x),
                ApproxInverse(lossy.y),
                ApproxInverse(lossy.z));
        }

        private static Transform FindVisual(GameObject go)
        {
            if (go == null) return null;
            var character = go.GetComponent<Character>();
            if (Access.Get(character, "m_visual") is GameObject visual && visual != null)
                return visual.transform;
            return go.transform.Find("Visual");
        }

        public static void HideSaddleVisuals(GameObject go)
        {
            if (go == null) return;
            var saddle = go.transform.Find("Saddle");
            if (saddle == null) return;
            var renderers = saddle.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = false;
            }
            var colliders = saddle.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = false;
            }
        }

        private static float ApproxInverse(float value)
        {
            return Mathf.Abs(value) < 0.001f ? 1f : 1f / value;
        }

        public static void ApplyToAll()
        {
            if (MountSettings.All != null)
            {
                for (int i = 0; i < MountSettings.All.Length; i++)
                {
                    var profile = MountSettings.All[i];
                    if (profile != null)
                        ApplyProfile(profile);
                }
            }
            if (Prefab != null)
            {
                ApplyMoveSpeed(Prefab.GetComponent<Character>(), MountSettings.Find(Prefab) ?? MountSettings.Default);
                ApplyScale(Prefab, MountSettings.Find(Prefab) ?? MountSettings.Default);
            }
            foreach (var character in Character.GetAllCharacters())
            {
                if (character == null || !IsOurs(character.gameObject)) continue;
                ApplyAll(character.gameObject);
            }
        }

        public static bool IsOurs(GameObject go)
        {
            if (go == null) return false;
            if (MountSettings.All != null)
            {
                for (int i = 0; i < MountSettings.All.Length; i++)
                {
                    var profile = MountSettings.All[i];
                    if (profile != null && profile.IsInstance(go))
                        return true;
                }
            }
            return go.name.StartsWith(PrefabName, StringComparison.Ordinal)
                || go.GetComponent<MountControl>() != null;
        }

        public static void Register(ZNetScene scene)
        {
            if (scene == null || MountSettings.All == null) return;
            for (int i = 0; i < MountSettings.All.Length; i++)
            {
                var profile = MountSettings.All[i];
                if (profile != null)
                    Register(scene, profile);
            }
        }

        public static void Register(ZNetScene scene, MountProfile profile)
        {
            if (scene == null || profile == null || string.IsNullOrEmpty(profile.PrefabName)) return;
            if (scene.m_prefabs != null && scene.m_prefabs.Exists(p => p != null && p.name == profile.PrefabName))
            {
                var existing = scene.m_prefabs.Find(p => p != null && p.name == profile.PrefabName);
                if (profile.Id == MountSettings.Default?.Id && Prefab == null)
                    Prefab = existing;
                ApplyMoveSpeed(existing != null ? existing.GetComponent<Character>() : null, profile);
                return;
            }

            if (profile.Id == MountSettings.Default?.Id && Prefab != null)
            {
                Access.AddScenePrefab(scene, Prefab);
                return;
            }

            var template = FindTemplate(scene, profile);
            if (template == null)
            {
                Plugin.Log.LogError($"ValheimMontarias: no vanilla template found for {profile.Name}");
                return;
            }

            var clone = UnityEngine.Object.Instantiate(template, Hidden);
            clone.name = profile.PrefabName;

            var nview = clone.GetComponent<ZNetView>();
            if (nview != null)
                nview.m_persistent = false;

            var tame = clone.GetComponent<Tameable>() ?? clone.AddComponent<Tameable>();
            Access.Set(tame, "m_commandable", true);
            Access.Set(tame, "m_startsTamed", true);

            var ai = clone.GetComponent<BaseAI>();
            if (ai != null)
            {
                // Eikthyr is summoned asleep (its sleeping pose is the rearing one) and only
                // MonsterAI.UpdateSleep wakes it, which our ridden AI never runs.
                Access.Set(ai, "m_sleeping", false);
                Access.Set(ai, "m_enableHuntPlayer", false);
                Access.Set(ai, "m_aggravatable", false);
                if (profile.Id == "eikthyr")
                    Access.Set(ai, "m_canBeAlerted", false);
            }

            var character = clone.GetComponent<Character>();
            if (character != null)
            {
                Access.Set(character, "m_isMount", true);
                ApplyMoveSpeed(character, profile);
                Access.Set(character, "m_jumpStaminaUsage", 0f);
            }

            var procreation = clone.GetComponent<Procreation>();
            if (procreation != null)
                procreation.enabled = false;

            if (profile.Id == "eikthyr")
                SuppressBossEffects(clone);

            AttachSaddle(scene, clone, tame, character);
            clone.AddComponent<MountControl>();

            if (profile.Id == MountSettings.Default?.Id)
                Prefab = clone;
            Access.AddScenePrefab(scene, clone);
            Plugin.Log.LogInfo($"ValheimMontarias: mount prefab {profile.Name} registered");
        }

        private static void SuppressBossEffects(GameObject mount)
        {
            var noEffects = new EffectList();
            Access.Set(noEffects, "m_effectPrefabs", Array.Empty<EffectList.EffectData>());

            // The neigh it gave right after being summoned: its idle call (every few seconds
            // from Start), the boss music and whatever sound plays on its own when it spawns.
            foreach (var character in mount.GetComponentsInChildren<Character>(true))
            {
                character.m_boss = false;
                character.m_bossEvent = string.Empty;
                character.m_defeatSetGlobalKey = string.Empty;
                character.m_jumpEffects = noEffects;
                if (character is Humanoid humanoid)
                    humanoid.m_equipEffects = noEffects;
            }

            foreach (var sfx in mount.GetComponentsInChildren<ZSFX>(true))
                sfx.m_playOnAwake = false;
            foreach (var audio in mount.GetComponentsInChildren<AudioSource>(true))
                audio.playOnAwake = false;

            foreach (var ai in mount.GetComponentsInChildren<BaseAI>(true))
            {
                ai.m_idleSound = noEffects;
                ai.m_idleSoundChance = 0f;
                Access.Set(ai, "m_alertedEffects", noEffects);
                Access.Set(ai, "m_alertedMessage", string.Empty);
                Access.Set(ai, "m_spawnMessage", string.Empty);
            }

            foreach (var ai in mount.GetComponentsInChildren<MonsterAI>(true))
            {
                Access.Set(ai, "m_wakeupEffects", noEffects);
                Access.Set(ai, "m_sleepEffects", noEffects);
            }

            foreach (var attack in mount.GetComponentsInChildren<Attack>(true))
            {
                Access.Set(attack, "m_startEffect", noEffects);
                Access.Set(attack, "m_hitEffect", noEffects);
                Access.Set(attack, "m_hitTerrainEffect", noEffects);
                Access.Set(attack, "m_triggerEffect", noEffects);
                Access.Set(attack, "m_trailStartEffect", noEffects);
                Access.Set(attack, "m_burstEffect", noEffects);
                Access.Set(attack, "m_attackProjectile", null);
                Access.Set(attack, "m_spawnOnTrigger", null);
                Access.Set(attack, "m_spawnOnHit", null);
            }

            foreach (var particles in mount.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.playOnAwake = false;
                var emission = particles.emission;
                emission.enabled = false;
            }
        }

        public static void Finish(ZNetScene scene)
        {
            if (scene == null) return;
            if (Prefab != null)
                Access.EnsureNamedPrefab(scene, Prefab);
            foreach (var profile in MountSettings.All ?? Array.Empty<MountProfile>())
            {
                if (profile == null || string.IsNullOrEmpty(profile.PrefabName)) continue;
                var prefab = scene.GetPrefab(profile.PrefabName);
                if (prefab != null)
                    Access.EnsureNamedPrefab(scene, prefab);
            }
        }

        private static GameObject FindTemplate(ZNetScene scene, MountProfile profile)
        {
            if (scene == null || profile == null) return null;
            var names = new[]
            {
                profile.Id,
                profile.Id switch
                {
                    "capivara" => "Boar",
                    "neck" => "Neck",
                    "deer" => "Deer",
                    "boar" => "Boar",
                    "eikthyr" => "Eikthyr",
                    _ => "Boar"
                },
                "Boar",
                "Deer",
                "Neck",
                "Eikthyr",
                "Lox"
            };
            for (int i = 0; i < names.Length; i++)
            {
                var template = FindPrefab(scene, names[i]);
                if (template != null)
                    return template;
            }
            return null;
        }

        private static void AttachSaddle(ZNetScene scene, GameObject boar, Tameable tame, Character character)
        {
            var source = FindPrefab(scene, "Lox") ?? FindPrefab(scene, "Asksvin");
            var sadleSrc = source != null ? source.GetComponentInChildren<Sadle>(true) : null;
            GameObject saddleGo;
            Sadle sadle;
            if (sadleSrc != null)
            {
                MountGear.Capture(source);
                saddleGo = UnityEngine.Object.Instantiate(sadleSrc.gameObject, boar.transform);
                saddleGo.name = "Saddle";
                saddleGo.transform.localPosition = Vector3.zero;
                saddleGo.transform.localRotation = Quaternion.identity;
                saddleGo.transform.localScale = Vector3.one;
                sadle = saddleGo.GetComponent<Sadle>();
            }
            else
            {
                Plugin.Log.LogWarning("ValheimMontarias: no Lox/Asksvin saddle, adding empty Sadle");
                saddleGo = new GameObject("Saddle");
                saddleGo.transform.SetParent(boar.transform, false);
                sadle = saddleGo.AddComponent<Sadle>();
            }

            foreach (var renderer in saddleGo.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            foreach (var collider in saddleGo.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            for (int i = saddleGo.transform.childCount - 1; i >= 0; i--)
            {
                var child = saddleGo.transform.GetChild(i);
                if (child != null)
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            foreach (var extraView in saddleGo.GetComponentsInChildren<ZNetView>(true))
            {
                if (extraView != null && extraView.gameObject != boar)
                    UnityEngine.Object.DestroyImmediate(extraView);
            }
            foreach (var mesh in saddleGo.GetComponents<MeshRenderer>())
                UnityEngine.Object.DestroyImmediate(mesh);
            foreach (var filter in saddleGo.GetComponents<MeshFilter>())
                UnityEngine.Object.DestroyImmediate(filter);
            foreach (var skin in saddleGo.GetComponents<SkinnedMeshRenderer>())
                UnityEngine.Object.DestroyImmediate(skin);

            var attach = new GameObject("AttachPoint").transform;
            attach.SetParent(boar.transform, false);
            attach.localPosition = new Vector3(0f, 0.88f, 0.04f);
            attach.localRotation = Quaternion.identity;

            Access.Set(sadle, "m_character", character);
            Access.Set(sadle, "m_tameable", tame);
            Access.Set(sadle, "m_attachPoint", attach);
            Access.Set(sadle, "m_attachAnimation", "attach_chair");
            Access.Set(sadle, "m_maxUseRange", 4.5f);
            Access.Set(sadle, "m_dropSaddleOnDeath", false);
            Access.Set(sadle, "m_maxStamina", InfiniteStamina);
            Access.Set(sadle, "m_runStaminaDrain", 0f);
            Access.Set(sadle, "m_staminaDrain", 0f);
            Access.Set(sadle, "m_staminaDrainPerSec", 0f);
            Access.Set(sadle, "m_swimStaminaDrain", 0f);
            if (tame != null)
                Access.Set(tame, "m_saddle", sadle);
        }

        private static GameObject FindPrefab(ZNetScene scene, string name)
        {
            if (scene?.m_prefabs == null) return null;
            return scene.m_prefabs.Find(p => p != null && p.name == name);
        }
    }
}
