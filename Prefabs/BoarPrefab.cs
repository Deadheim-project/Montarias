using System;
using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    /// <summary>
    /// Runtime clone of vanilla Boar. Mesh stays vanilla until a custom asset replaces it.
    /// Saddle collider/mesh is copied from Lox/Asksvin but hidden — we only need Sadle.
    /// </summary>
    internal static class BoarPrefab
    {
        public const string PrefabName = "JavaliMontaria";
        public const string DisplayName = "Capivara";

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
            ApplyMoveSpeed(character, MountSettings.Find(character != null ? character.gameObject : null) ?? MountSettings.Javali);
        }

        public static void ApplyMoveSpeed(Character character, MountProfile profile)
        {
            if (character == null) return;
            var mountProfile = profile ?? MountSettings.Find(character != null ? character.gameObject : null) ?? MountSettings.Javali;
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
            var go = character.gameObject;
            if (Prefab == null || go == Prefab) return 1f;
            var player = Player.m_localPlayer;
            if (player == null) return 1f;
            long owner = JavaliControl.OwnerId(go);
            return owner != 0L && owner == player.GetPlayerID() ? MountRoster.SpeedBonus : 1f;
        }

        public static void ApplyName(Character character)
        {
            ApplyName(character, MountSettings.Find(character != null ? character.gameObject : null) ?? MountSettings.Javali);
        }

        public static void ApplyName(Character character, MountProfile profile)
        {
            if (character == null) return;
            string name = profile != null ? profile.Name : (MountSettings.Javali != null ? MountSettings.Javali.Name : DisplayName);
            Access.Set(character, "m_name", name);
        }

        public static void ApplyScale(GameObject go)
        {
            ApplyScale(go, MountSettings.Find(go) ?? MountSettings.Javali);
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
            MountVisuals.Apply(go);
            FitSeat(go);
        }

        public static void ApplyStats(GameObject go)
        {
            ApplyStats(go, MountSettings.Find(go) ?? MountSettings.Javali);
        }

        public static void ApplyStats(GameObject go, MountProfile profile)
        {
            if (go == null) return;
            float health = profile != null ? Mathf.Max(1f, profile.MaxHealth.Value) : MountSettings.Health;
            float stamina = profile != null ? Mathf.Max(1f, profile.MaxStamina.Value) : MountSettings.Stamina;
            float drain = profile != null ? Mathf.Max(0f, profile.StaminaDrain.Value) : MountSettings.Drain;

            var character = go.GetComponent<Character>();
            if (character != null)
            {
                Access.Set(character, "m_health", health);
                var nview = character.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid())
                {
                    float oldMax = Mathf.Max(1f, character.GetMaxHealth());
                    float oldHp = character.GetHealth();
                    Access.Call(character, "SetMaxHealth", health);
                    float pct = Mathf.Clamp01(oldHp / oldMax);
                    if (pct > 0.98f)
                        pct = 1f;
                    character.SetHealth(health * pct);
                }
            }

            var sadle = go.GetComponentInChildren<Sadle>(true);
            if (sadle == null) return;

            float oldMaxSta = stamina;
            if (Access.Call(sadle, "GetMaxStamina") is float maxSta)
                oldMaxSta = Mathf.Max(1f, maxSta);
            float oldSta = oldMaxSta;
            if (Access.Call(sadle, "GetStamina") is float curSta)
                oldSta = curSta;

            Access.Set(sadle, "m_maxStamina", stamina);
            Access.Set(sadle, "m_runStaminaDrain", drain);
            Access.Set(sadle, "m_staminaDrain", drain);
            Access.Set(sadle, "m_staminaDrainPerSec", drain);
            Access.Set(sadle, "m_swimStaminaDrain", drain * 2f);
            Access.Call(sadle, "SetMaxStamina", stamina);
            float staPct = Mathf.Clamp01(oldSta / oldMaxSta);
            if (staPct > 0.98f)
                staPct = 1f;
            var staminaView = Access.Get(sadle, "m_nview") as ZNetView;
            if (staminaView != null && staminaView.IsValid() && staminaView.GetZDO() != null)
                Access.Call(sadle, "SetStamina", stamina * staPct);
        }

        public static void ApplyAll(GameObject go)
        {
            if (go == null) return;
            var profile = MountSettings.Find(go) ?? MountSettings.Javali;
            ApplyMoveSpeed(go.GetComponent<Character>(), profile);
            ApplyScale(go, profile);
            ApplyStats(go, profile);
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
                ApplyStats(target, profile);
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
            ApplyMoveSpeed(Prefab != null ? Prefab.GetComponent<Character>() : null);
            ApplyScale(Prefab);
            ApplyStats(Prefab);
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
                || go.GetComponent<JavaliControl>() != null;
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
                if (profile.Id == MountSettings.Javali?.Id && Prefab == null)
                    Prefab = existing;
                ApplyMoveSpeed(existing != null ? existing.GetComponent<Character>() : null, profile);
                ApplyStats(existing, profile);
                return;
            }

            if (profile.Id == MountSettings.Javali?.Id && Prefab != null)
            {
                Access.AddScenePrefab(scene, Prefab);
                return;
            }

            var boar = FindPrefab(scene, "Boar");
            if (boar == null)
            {
                Plugin.Log.LogError("ValheimMontarias: vanilla Boar prefab not found");
                return;
            }

            var clone = UnityEngine.Object.Instantiate(boar, Hidden);
            clone.name = profile.PrefabName;

            var nview = clone.GetComponent<ZNetView>();
            if (nview != null)
                nview.m_persistent = false;

            var tame = clone.GetComponent<Tameable>();
            if (tame != null)
            {
                Access.Set(tame, "m_commandable", true);
                Access.Set(tame, "m_startsTamed", true);
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

            AttachSaddle(scene, clone, tame, character);
            ApplyStats(clone, profile);
            clone.AddComponent<JavaliControl>();

            if (profile.Id == MountSettings.Javali?.Id)
                Prefab = clone;
            Access.AddScenePrefab(scene, clone);
            Plugin.Log.LogInfo($"ValheimMontarias: mount prefab {profile.Name} registered");
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
            Access.Set(sadle, "m_maxStamina", MountSettings.Stamina);
            Access.Set(sadle, "m_runStaminaDrain", MountSettings.Drain);
            Access.Set(sadle, "m_staminaDrain", MountSettings.Drain);
            Access.Set(sadle, "m_staminaDrainPerSec", MountSettings.Drain);
            Access.Set(sadle, "m_swimStaminaDrain", MountSettings.Drain * 2f);
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
