using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ValheimMontarias
{
    /// <summary>
    /// Publicized DLLs compile field access, but the live Valheim assembly still has those
    /// fields non-public. Reading them in IL throws FieldAccessException. Go through
    /// reflection for Character / Tameable / Sadle internals.
    /// </summary>
    internal static class Access
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>();
        private static readonly Dictionary<string, MethodInfo> Methods = new Dictionary<string, MethodInfo>();

        public static void Set(object target, string field, object value)
        {
            var info = Field(target, field);
            if (info == null) return;
            try
            {
                if (value != null && !info.FieldType.IsInstanceOfType(value))
                    value = Coerce(info.FieldType, value);
                if (value == null && info.FieldType.IsValueType) return;
                info.SetValue(target, value);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"ValheimMontarias: set {target.GetType().Name}.{field} failed: {e.Message}");
            }
        }

        private static object Coerce(Type type, object value)
        {
            if (type == typeof(Vector3) && IsNumeric(value))
            {
                float n = Convert.ToSingle(value);
                return n == 0f ? Vector3.zero : new Vector3(0f, n, 0f);
            }
            if (type == typeof(float) && IsNumeric(value))
                return Convert.ToSingle(value);
            if (type == typeof(int) && IsNumeric(value))
                return Convert.ToInt32(value);
            if (type == typeof(bool) && IsNumeric(value))
                return Convert.ToSingle(value) != 0f;
            return value;
        }

        private static bool IsNumeric(object value)
        {
            return value is float || value is double || value is int || value is long
                || value is short || value is byte || value is uint;
        }

        public static object Get(object target, string field)
        {
            return Field(target, field)?.GetValue(target);
        }

        public static object Call(object target, string method, params object[] args)
        {
            if (target == null) return null;
            var info = Method(target.GetType(), method, args?.Length ?? 0);
            if (info == null) return null;
            try
            {
                return info.Invoke(target, args);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: {target.GetType().Name}.{method} failed: {e.InnerException?.Message ?? e.Message}");
                return null;
            }
        }

        public static void SetDict(object target, string fieldName, int key, object value)
        {
            if (!(Get(target, fieldName) is IDictionary dict) || value == null) return;
            dict[key] = value;
        }

        public static IList GetList(object target, string fieldName)
        {
            return Get(target, fieldName) as IList;
        }

        public static void AddItemPrefab(ObjectDB db, GameObject prefab)
        {
            if (db == null || prefab == null) return;
            if (db.m_items != null && !db.m_items.Contains(prefab))
                db.m_items.Add(prefab);
            SetDict(db, "m_itemByHash", prefab.name.GetStableHashCode(), prefab);
        }

        public static void AddScenePrefab(ZNetScene scene, GameObject prefab)
        {
            if (scene == null || prefab == null || scene.m_prefabs == null) return;
            if (scene.m_prefabs.Exists(p => p != null && p.name == prefab.name))
                return;
            scene.m_prefabs.Add(prefab);
        }

        public static void EnsureNamedPrefab(ZNetScene scene, GameObject prefab)
        {
            if (scene == null || prefab == null) return;
            SetDict(scene, "m_namedPrefabs", prefab.name.GetStableHashCode(), prefab);
        }

        public static void SetSaddle(Tameable tame, bool equipped)
        {
            if (tame == null) return;
            var nview = tame.GetComponent<ZNetView>();
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo != null)
            {
                var hash = typeof(Tameable).GetField("s_haveSaddleHash", Any);
                if (hash?.GetValue(null) is int key)
                    zdo.Set(key, equipped);
                zdo.Set("have_saddle".GetStableHashCode(), equipped);
            }

            Call(tame, "RPC_SetSaddle", 0L, equipped);
            var sadle = tame.GetComponentInChildren<Sadle>(true);
            if (sadle != null)
                Call(sadle, "RPC_SetSaddle", 0L, equipped);
        }

        private static MethodInfo _getButton;
        private static MethodInfo _getButtonDown;
        private static MethodInfo _applyControlls;

        public static bool IsAdmin()
        {
            var znet = ZNet.instance;
            if (znet == null) return false;
            var result = Call(znet, "LocalPlayerIsAdminOrHost");
            if (result is bool admin && admin) return true;
            result = Call(znet, "PlayerIsAdmin");
            if (result is bool fallback && fallback) return true;
            return Plugin.LocalIsServerSyncAdmin;
        }

        public static Rigidbody Body(Character character)
        {
            return character != null ? character.GetComponent<Rigidbody>() : null;
        }

        public static bool ButtonHeld(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            try
            {
                _getButton ??= typeof(ZInput).GetMethod("GetButton", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (_getButton != null)
                    return (bool)_getButton.Invoke(null, new object[] { name });
            }
            catch (Exception)
            {
            }
            return false;
        }

        public static bool ButtonDown(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            try
            {
                _getButtonDown ??= typeof(ZInput).GetMethod("GetButtonDown", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (_getButtonDown != null)
                    return (bool)_getButtonDown.Invoke(null, new object[] { name });
            }
            catch (Exception)
            {
            }
            return false;
        }

        public static bool UsePressed()
        {
            return ButtonDown("Use")
                || ButtonDown("KeyboardUse")
                || ButtonDown("JoyUse")
                || Input.GetKeyDown(KeyCode.E);
        }

        public static bool JumpPressed()
        {
            return ButtonDown("Jump")
                || ButtonDown("JoyJump")
                || Input.GetKeyDown(KeyCode.Space);
        }

        public static bool AttackPressed()
        {
            return ButtonDown("Attack")
                || ButtonDown("JoyAttack")
                || Input.GetMouseButtonDown(0);
        }

        public static bool RunHeld()
        {
            return ButtonHeld("Run") || ButtonHeld("JoyRun") || Input.GetKey(KeyCode.LeftShift);
        }

        public static object GetDoodad(Player player)
        {
            if (player == null) return null;
            return Call(player, "GetDoodadController")
                ?? Get(player, "m_doodadController")
                ?? Get(player, "m_doodadControlls");
        }

        public static bool HaveRider(Sadle sadle)
        {
            if (sadle == null) return false;
            var result = Call(sadle, "HaveRider");
            return result is bool ridden && ridden;
        }

        public static bool DismountPressed()
        {
            return ButtonDown("Use") || ButtonDown("KeyboardUse") || ButtonDown("JoyUse");
        }

        public static void StopDoodad(Player player)
        {
            Call(player, "StopDoodadControl");
        }

        public static Vector3 RideStick()
        {
            float forward = 0f;
            float side = 0f;
            if (ButtonHeld("Forward") || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
                forward = 1f;
            else if (ButtonHeld("Backward") || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
                forward = -1f;
            if (ButtonHeld("Right") || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
                side = 1f;
            else if (ButtonHeld("Left") || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
                side = -1f;
            return new Vector3(side, 0f, forward);
        }

        public static void ApplyDoodad(object controller, Player player)
        {
            if (controller == null || player == null) return;
            Vector3 stick = AdminMenu.IsOpen ? Vector3.zero : RideStick();
            if (stick.z < 0.5f)
            {
                StopMount(controller);
                return;
            }

            Vector3 look = player.GetLookDir();
            look.y = 0f;
            if (look.sqrMagnitude < 0.0001f)
                look = player.transform.forward;
            look.Normalize();
            bool run = RunHeld();
            if (_applyControlls == null)
            {
                MethodInfo best = null;
                foreach (var method in controller.GetType().GetMethods(Any))
                {
                    if (method.Name != "ApplyControlls") continue;
                    if (best == null || method.GetParameters().Length > best.GetParameters().Length)
                        best = method;
                }
                _applyControlls = best;
            }
            if (_applyControlls == null) return;
            var pars = _applyControlls.GetParameters();
            object[] args;
            if (pars.Length == 5 && pars[1].ParameterType == typeof(Vector3))
                args = new object[] { stick, look, run, false, false };
            else if (pars.Length == 5)
                args = new object[] { stick, run, false, false, false };
            else if (pars.Length == 4)
                args = new object[] { stick, run, false, false };
            else if (pars.Length == 3)
                args = new object[] { stick, run, false };
            else if (pars.Length == 1)
                args = new object[] { stick };
            else
                return;
            try
            {
                _applyControlls.Invoke(controller, args);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: ApplyControlls failed: {e.InnerException?.Message ?? e.Message}");
            }

            if (Get(controller, "m_character") is Character character)
                character.SetRun(run);
        }

        /// <summary>
        /// Sadle.ApplyControlls sends Speed.NoChange when stick.z is near 0, which leaves
        /// the last Walk direction running. Stop the AI explicitly when the rider is idle.
        /// </summary>
        public static void StopMount(object controller)
        {
            if (controller == null) return;
            Call(controller, "ResetControlls");
            if (Get(controller, "m_monsterAI") is MonsterAI ai)
                ai.StopMoving();
            if (Get(controller, "m_character") is Character character)
                character.SetRun(false);
        }

        public static void PlayTrigger(Character character, string trigger)
        {
            if (character == null || string.IsNullOrEmpty(trigger)) return;
            var zanim = Call(character, "GetZAnim");
            if (zanim != null)
            {
                if (Call(zanim, "SetTrigger", trigger) != null)
                    return;
                Call(zanim, "SetTrigger", trigger.GetStableHashCode());
            }
            var animator = character.GetComponentInChildren<Animator>();
            animator?.SetTrigger(trigger);
        }

        public static void PlaySfx(string prefabName, Vector3 pos)
        {
            var scene = ZNetScene.instance;
            if (scene == null || string.IsNullOrEmpty(prefabName)) return;
            var prefab = scene.GetPrefab(prefabName);
            if (prefab == null) return;
            UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
        }

        private static FieldInfo Field(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            string key = type.FullName + "." + name;
            if (Fields.TryGetValue(key, out var cached))
                return cached;
            FieldInfo info = null;
            for (var t = type; t != null && info == null; t = t.BaseType)
                info = t.GetField(name, Any);
            Fields[key] = info;
            return info;
        }

        private static MethodInfo Method(Type type, string name, int argc)
        {
            string key = type.FullName + "." + name + "." + argc;
            if (Methods.TryGetValue(key, out var cached))
                return cached;
            MethodInfo match = null;
            const BindingFlags declared = Any | BindingFlags.DeclaredOnly;
            for (var t = type; t != null && match == null; t = t.BaseType)
            {
                foreach (var method in t.GetMethods(declared))
                {
                    if (method.Name != name || method.GetParameters().Length != argc)
                        continue;
                    match = method;
                    break;
                }
            }
            Methods[key] = match;
            return match;
        }
    }
}
