using System;
using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    internal static class CentauroPrefab
    {
        public const string PrefabName = "CentauroMontaria";
        public const string DisplayName = "Centauro";

        internal static GameObject Prefab { get; private set; }

        private static Transform _hidden;

        private static Transform Hidden
        {
            get
            {
                if (_hidden != null) return _hidden;
                var go = new GameObject("ValheimMontarias_HiddenCentauro");
                go.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(go);
                _hidden = go.transform;
                return _hidden;
            }
        }

        private static MountProfile Profile => MountSettings.Centauro;

        public static void ApplyMoveSpeed(Character character)
        {
            if (character == null) return;
            float walk = Profile != null ? Profile.WalkSpeed.Value : 6f;
            float run = Profile != null ? Profile.RunSpeed.Value : 14f;
            float jump = Profile != null ? Profile.JumpHeight.Value : 9f;
            Access.Set(character, "m_speed", walk);
            Access.Set(character, "m_walkSpeed", walk);
            Access.Set(character, "m_runSpeed", run);
            Access.Set(character, "m_acceleration", 12f);
            Access.Set(character, "m_jumpForce", jump);
            Access.Set(character, "m_jumpForceForward", 1.4f);
            Access.Set(character, "m_pushForce", Vector3.zero);
            string name = Profile != null ? Profile.Name : DisplayName;
            Access.Set(character, "m_name", name);
        }

        public static void ApplyScale(GameObject go)
        {
            if (go == null) return;
            float size = Profile != null ? Mathf.Clamp(Profile.Scale.Value, 0.5f, 3f) : 1f;
            go.transform.localScale = Vector3.one;

            var visualTr = MountHub.FindVisual(go);
            if (visualTr != null && visualTr != go.transform)
                visualTr.localScale = Vector3.one * size;
            else
                go.transform.localScale = Vector3.one * size;

            BoarPrefab.HideSaddleVisuals(go);
            CentauroVisuals.Apply(go);
            FitSeat(go);
        }

        public static void ApplyStats(GameObject go)
        {
            if (go == null) return;
            float health = Profile != null ? Mathf.Max(1f, Profile.MaxHealth.Value) : 200f;
            float stamina = Profile != null ? Mathf.Max(1f, Profile.MaxStamina.Value) : 300f;
            float drain = Profile != null ? Mathf.Max(0f, Profile.StaminaDrain.Value) : 7f;

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
            Access.Set(sadle, "m_maxStamina", stamina);
            Access.Set(sadle, "m_runStaminaDrain", drain);
            Access.Set(sadle, "m_staminaDrain", drain);
            Access.Set(sadle, "m_staminaDrainPerSec", drain);
            Access.Set(sadle, "m_swimStaminaDrain", drain * 2f);
            Access.Call(sadle, "SetMaxStamina", stamina);
            Access.Call(sadle, "SetStamina", stamina);
        }

        public static void ApplyAll(GameObject go)
        {
            if (go == null) return;
            ApplyMoveSpeed(go.GetComponent<Character>());
            ApplyScale(go);
            ApplyStats(go);
        }

        public static void FitSeat(GameObject go)
        {
            MountGear.Fit(go);
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
            return go.name.StartsWith(PrefabName, StringComparison.Ordinal)
                || go.GetComponent<CentauroControl>() != null;
        }

        public static void Register(ZNetScene scene)
        {
            if (scene == null) return;
            if (scene.m_prefabs != null && scene.m_prefabs.Exists(p => p != null && p.name == PrefabName))
            {
                if (Prefab == null)
                    Prefab = scene.m_prefabs.Find(p => p != null && p.name == PrefabName);
                ApplyMoveSpeed(Prefab != null ? Prefab.GetComponent<Character>() : null);
                ApplyStats(Prefab);
                return;
            }

            if (Prefab != null)
            {
                Access.AddScenePrefab(scene, Prefab);
                return;
            }

            var source = FindPrefab(scene, "Lox") ?? FindPrefab(scene, "Asksvin") ?? FindPrefab(scene, "Boar");
            if (source == null)
            {
                Plugin.Log.LogError("ValheimMontarias: no Lox/Asksvin/Boar prefab for centaur");
                return;
            }

            var clone = UnityEngine.Object.Instantiate(source, Hidden);
            clone.name = PrefabName;

            var nview = clone.GetComponent<ZNetView>();
            if (nview != null)
                nview.m_persistent = true;

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
                ApplyMoveSpeed(character);
                Access.Set(character, "m_jumpStaminaUsage", 0f);
            }

            var procreation = clone.GetComponent<Procreation>();
            if (procreation != null)
                procreation.enabled = false;

            foreach (var old in clone.GetComponentsInChildren<Sadle>(true))
            {
                if (old == null) continue;
                if (old.gameObject == clone)
                    UnityEngine.Object.DestroyImmediate(old);
                else
                    UnityEngine.Object.DestroyImmediate(old.gameObject);
            }

            AttachSaddle(scene, clone, tame, character);
            ApplyStats(clone);
            clone.AddComponent<CentauroControl>();

            Prefab = clone;
            Access.AddScenePrefab(scene, clone);
            Plugin.Log.LogInfo("ValheimMontarias: centaur mount prefab registered");
        }

        public static void Finish(ZNetScene scene)
        {
            Access.EnsureNamedPrefab(scene, Prefab);
        }

        private static void AttachSaddle(ZNetScene scene, GameObject body, Tameable tame, Character character)
        {
            var source = FindPrefab(scene, "Lox") ?? FindPrefab(scene, "Asksvin");
            var sadleSrc = source != null ? source.GetComponentInChildren<Sadle>(true) : null;
            GameObject saddleGo;
            Sadle sadle;
            if (sadleSrc != null)
            {
                MountGear.Capture(source);
                saddleGo = UnityEngine.Object.Instantiate(sadleSrc.gameObject, body.transform);
                saddleGo.name = "Saddle";
                saddleGo.transform.localPosition = Vector3.zero;
                saddleGo.transform.localRotation = Quaternion.identity;
                saddleGo.transform.localScale = Vector3.one;
                sadle = saddleGo.GetComponent<Sadle>();
            }
            else
            {
                saddleGo = new GameObject("Saddle");
                saddleGo.transform.SetParent(body.transform, false);
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
                if (extraView != null && extraView.gameObject != body)
                    UnityEngine.Object.DestroyImmediate(extraView);
            }

            var attach = new GameObject("AttachPoint").transform;
            attach.SetParent(body.transform, false);
            attach.localPosition = new Vector3(0f, 1.2f, -0.2f);
            attach.localRotation = Quaternion.identity;

            Access.Set(sadle, "m_character", character);
            Access.Set(sadle, "m_tameable", tame);
            Access.Set(sadle, "m_attachPoint", attach);
            Access.Set(sadle, "m_attachAnimation", "attach_chair");
            Access.Set(sadle, "m_maxUseRange", 5.5f);
            Access.Set(sadle, "m_dropSaddleOnDeath", false);
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
