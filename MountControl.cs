using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    /// <summary>
    /// Lives on every mount. A mount works like in WoW: you use the mount item (or H, or the
    /// menu), the cast ends and you are on it; you get off and it is gone. There is never an
    /// empty mount: it is created hidden, shown only once the rider is in the saddle, and
    /// destroyed the moment it has nobody on it. It is not persistent either, so it leaves
    /// with its rider when they log out.
    /// </summary>
    public class MountControl : MonoBehaviour
    {
        public const string OwnerKey = "javali_owner";

        protected virtual void Start()
        {
            InitPet();
            MountPrefab.ApplyInfiniteStamina(gameObject);
            MountPrefab.ApplyAll(gameObject);
        }

        /// <summary>The process that owns the mount's ZDO (the rider) removes it the frame it
        /// is left empty: dismount, the rider dying on it, a leftover from an older version.</summary>
        private void Update()
        {
            if (_pendingMount == gameObject)
            {
                // Start can rebuild the visuals before the rider is on; keep them hidden.
                Hide(gameObject);
                return;
            }

            var nview = GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            if (IsRidden()) return;
            var local = Player.m_localPlayer;
            var sadle = GetComponentInChildren<Sadle>(true);
            Plugin.Log.LogInfo($"ValheimMontarias: {name} has no rider, removed " +
                $"(attached={(local != null ? Access.Get(local, "m_attached") : null)} " +
                $"point={(local != null ? (Access.Get(local, "m_attachPoint") as Transform)?.name : null)} " +
                $"doodad={(local != null ? Access.GetDoodad(local) : null)} " +
                $"user={(sadle != null ? Access.Call(sadle, "GetUser") : null)} valid={(sadle != null ? Access.Call(sadle, "HaveValidUser") : null)} " +
                $"range={(sadle != null ? Access.Get(sadle, "m_maxUseRange") : null)} " +
                $"dist={(local != null ? Vector3.Distance(local.transform.position, transform.position) : -1f):0.0})");
            gameObject.SetActive(false);
            Despawn(gameObject);
        }

        /// <summary>Several ways of asking, because getting this wrong deletes a mount from under
        /// its rider: the local player's saddle controller or attach point belonging to this
        /// mount, the player parented to it, or the saddle's own record of a rider.</summary>
        private bool IsRidden()
        {
            var local = Player.m_localPlayer;
            if (local != null)
            {
                if (local.transform.IsChildOf(transform)) return true;
                if (Access.GetDoodad(local) is Component controller && controller != null &&
                    controller.transform.IsChildOf(transform)) return true;
                if (Access.Get(local, "m_attached") is bool attached && attached &&
                    Access.Get(local, "m_attachPoint") is Transform point && point != null &&
                    point.IsChildOf(transform)) return true;
            }
            return Access.HaveRider(GetComponentInChildren<Sadle>(true));
        }

        protected void InitPet()
        {
            var tame = GetComponent<Tameable>();
            Access.Call(tame, "Tame");
            Access.SetSaddle(tame, true);

            var hunt = GetComponent<BaseAI>();
            if (hunt != null)
            {
                Access.Set(hunt, "m_enableHuntPlayer", false);
                Access.Set(hunt, "m_aggravatable", false);
            }
        }

        public static long OwnerId(GameObject go)
        {
            var nview = go != null ? go.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid()) return 0L;
            return nview.GetZDO().GetLong(OwnerKey.GetStableHashCode(), 0L);
        }

        public static bool IsOwner(GameObject go, Player player)
        {
            if (player == null || go == null) return false;
            long id = OwnerId(go);
            return id == 0L || id == player.GetPlayerID();
        }

        public static void SetOwner(GameObject go, Player player)
        {
            var nview = go != null ? go.GetComponent<ZNetView>() : null;
            if (player == null || nview == null || !nview.IsValid()) return;
            nview.GetZDO().Set(OwnerKey.GetStableHashCode(), player.GetPlayerID());
        }

        public static GameObject FindOwned(Player player)
        {
            return FindOwned(player, MountPrefab.IsOurs);
        }

        public static GameObject FindOwned(Player player, System.Func<GameObject, bool> isOurs)
        {
            if (player == null) return null;
            long id = player.GetPlayerID();
            foreach (var character in Character.GetAllCharacters())
            {
                if (character == null || character.IsDead()) continue;
                if (isOurs != null && !isOurs(character.gameObject)) continue;
                if (OwnerId(character.gameObject) == id)
                    return character.gameObject;
            }
            return null;
        }

        private static int _summonFrame = -1;
        private static GameObject _pendingDespawn;
        private static float _pendingDespawnAt;
        private static GameObject _pendingMount;
        private static Player _pendingMountPlayer;
        private static float _pendingMountUntil;
        private static bool _casting;
        private static float _castStartedAt;
        private static float _castUntil;
        private static string _castPrefab = MountPrefab.PrefabName;
        private static string _castName;
        private static float _castEmoteAt;

        // Hands gesturing while the summon is cast. A one-shot emote, so it is re-fired
        // until the cast ends; emotes go through the ZDO, so everyone nearby sees it.
        private const string CastEmote = "comehere";
        private const float CastEmoteEvery = 1.5f;

        public static bool IsCasting => _casting;

        /// <summary>The mount being summoned, for the cast bar.</summary>
        public static string CastName => _castName;

        public static float CastProgress
        {
            get
            {
                if (!_casting) return 0f;
                float duration = Mathf.Max(0.05f, _castUntil - _castStartedAt);
                return Mathf.Clamp01((Time.unscaledTime - _castStartedAt) / duration);
            }
        }

        public static void Tick(Player player)
        {
            FlushDespawn();
            TryFinishAutoMount();
            TickCast(player);
            if (player == null) return;
            ClearWhistleTargetMode(player);
            ClearOrphanedRide(player);
        }

        private static void ClearWhistleTargetMode(Player player)
        {
            var use = Access.Get(player, "m_useItem") as ItemDrop.ItemData;
            if (!WhistleItem.IsWhistle(use)) return;
            Access.Set(player, "m_useItem", null);
        }

        private static void ClearOrphanedRide(Player player)
        {
            if (Access.Get(player, "m_sleeping") is bool sleeping && sleeping)
                return;

            var doodad = Access.GetDoodad(player);
            if (doodad is UnityEngine.Object doodadObj && !doodadObj)
            {
                ForceUnseat(player);
                return;
            }

            var parent = player.transform.parent;
            if (parent != null && !parent.gameObject)
            {
                ForceUnseat(player);
                return;
            }

            if (parent != null)
            {
                var fromParent = parent.GetComponentInParent<Character>();
                if (fromParent != null && MountHub.IsOurs(fromParent.gameObject)
                    && (fromParent.IsDead() || !fromParent.gameObject.activeInHierarchy))
                {
                    ForceUnseat(player);
                    return;
                }
            }

            var attached = Access.Get(player, "m_attached") is bool flag && flag;
            if (!attached) return;
            if (IsRiding(player)) return;

            var point = Access.Get(player, "m_attachPoint") as Transform;
            if (point == null || !point)
            {
                ForceUnseat(player);
                return;
            }

            var mount = point.GetComponentInParent<Character>();
            if (mount != null && MountHub.IsOurs(mount.gameObject))
                ForceUnseat(player);
        }

        /// <summary>The mount item was used: mount if on foot, dismount if riding.</summary>
        public static void UseMountItem(Player player, ItemDrop.ItemData item)
        {
            if (player == null || player != Player.m_localPlayer) return;
            TrySummonProfile(player, WhistleItem.ProfileOf(item) ?? MountSettings.Default);
        }

        public static void TrySummon(Player player)
        {
            TrySummonProfile(player, MountSettings.Default);
        }

        internal static void TrySummonProfile(Player player, MountProfile profile)
        {
            if (player == null) return;
            if (Time.frameCount == _summonFrame) return;
            _summonFrame = Time.frameCount;
            Access.Set(player, "m_useItem", null);

            if (_casting)
            {
                CancelCast(player, "Invocação interrompida");
                return;
            }

            // Riding already: the same key or item gets you off, and the mount is gone.
            // Always allowed -- in combat too, and even if the skill or the mount was taken
            // away meanwhile.
            if (IsRiding(player) || FindOwned(player, MountHub.IsOurs) != null)
            {
                Dismount(player);
                return;
            }

            if (CombatLock.Block(player)) return;

            if (BusyElsewhere(player))
            {
                player.Message(MessageHud.MessageType.Center, "Solte o que está controlando antes de invocar.");
                return;
            }

            string blocker = MountRoster.Blocker(profile);
            if (blocker != null)
            {
                if (!MountRoster.Known) RiderClient.Request(true);
                player.Message(MessageHud.MessageType.Center, blocker);
                return;
            }

            if (_pendingDespawn != null)
                return;

            _castPrefab = string.IsNullOrEmpty(profile.PrefabName) ? MountPrefab.PrefabName : profile.PrefabName;
            float cast = profile.CastSeconds != null ? Mathf.Max(0.1f, profile.CastSeconds.Value) : 2f;
            cast = Mathf.Max(0.1f, cast * MountRoster.CastMultiplier);
            _castName = profile.Name;
            _casting = true;
            _castStartedAt = Time.unscaledTime;
            _castUntil = Time.unscaledTime + cast;
            _castEmoteAt = 0f;
            TickCastEmote(player);
        }

        private static void TickCastEmote(Player player)
        {
            if (player == null || Time.unscaledTime < _castEmoteAt) return;
            _castEmoteAt = Time.unscaledTime + CastEmoteEvery;
            Access.Call(player, "StartEmote", CastEmote, true);
        }

        private static void StopCastEmote(Player player)
        {
            if (player == null) return;
            Access.Call(player, "StopEmote");
        }

        private static void TickCast(Player player)
        {
            if (!_casting) return;
            if (player == null || player.IsDead() || CombatLock.IsInCombat(player) || CastInterrupted(player))
            {
                CancelCast(player, "Invocação interrompida");
                return;
            }

            if (Time.unscaledTime < _castUntil)
            {
                TickCastEmote(player);
                return;
            }

            _casting = false;
            StopCastEmote(player);
            FinishSummon(player);
        }

        /// <summary>
        /// Steering a ship (or any other doodad), sitting on a chair, in a bed: the player is
        /// held there, so a mount would be ridden by nobody while the camera stays put.
        /// </summary>
        private static bool BusyElsewhere(Player player)
        {
            if (player == null) return false;
            var doodad = Access.GetDoodad(player);
            if (doodad is UnityEngine.Object doodadObj ? doodadObj : doodad != null)
                return true;
            if (Access.Get(player, "m_attached") is bool attached && attached)
                return true;
            return player.IsAttachedToShip();
        }

        private static bool CastInterrupted(Player player)
        {
            if (BusyElsewhere(player))
                return true;
            if (Access.JumpPressed() || Access.AttackPressed() || Input.GetMouseButtonDown(0))
                return true;
            var body = Access.Body(player);
            if (body == null) return false;
            var vel = body.linearVelocity;
            vel.y = 0f;
            return vel.sqrMagnitude > 0.45f;
        }

        private static void CancelCast(Player player, string message)
        {
            _casting = false;
            StopCastEmote(player);
            if (player != null && !string.IsNullOrEmpty(message))
                player.Message(MessageHud.MessageType.Center, message);
        }

        private static void FinishSummon(Player player)
        {
            if (player == null) return;
            var scene = ZNetScene.instance;
            if (scene == null) return;

            string prefabName = string.IsNullOrEmpty(_castPrefab) ? MountPrefab.PrefabName : _castPrefab;
            var prefab = scene.GetPrefab(prefabName)
                ?? (prefabName == MountPrefab.PrefabName ? MountPrefab.Prefab : null);
            if (prefab == null)
            {
                player.Message(MessageHud.MessageType.Center, "A montaria ainda não está pronta.");
                Plugin.Log.LogWarning($"ValheimMontarias: prefab missing at summon ({prefabName})");
                return;
            }

            // Right under the rider, facing where they face. It is born hidden and does not
            // touch the rider, and only shows once they are in the saddle -- normally this same
            // frame. Nobody ever sees it standing empty.
            var rot = Quaternion.LookRotation(Flat(player.transform.forward));
            var go = UnityEngine.Object.Instantiate(prefab, player.transform.position, rot);
            SetOwner(go, player);
            var tame = go.GetComponent<Tameable>();
            Access.Call(tame, "Tame");
            Access.SetSaddle(tame, true);
            MountHub.ApplyAll(go);
            // Aesir 28/30: write the rider's chosen name and skin into the ZDO so every
            // nearby client shows the same customized mount.
            var profile = MountSettings.Find(go) ?? MountSettings.Default;
            MountPrefab.ApplyRiderCosmetics(go, profile);
            IgnoreRider(go, player);
            Hide(go);

            _pendingMount = go;
            _pendingMountPlayer = player;
            _pendingMountUntil = Time.unscaledTime + 0.5f;
            TryFinishAutoMount();
        }

        private static Vector3 Flat(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        /// <summary>Puts the rider in the saddle. If they are not on it by the end of the short
        /// window, the mount -- still hidden -- is destroyed and they are told why.</summary>
        private static void TryFinishAutoMount()
        {
            if (_pendingMount == null) return;
            var go = _pendingMount;
            var player = _pendingMountPlayer != null ? _pendingMountPlayer : Player.m_localPlayer;
            if (!go)
            {
                ClearPendingMount();
                return;
            }

            if (player != null && !player.IsDead() && !CombatLock.IsInCombat(player))
            {
                if (!IsRiding(player)) AttachNow(go, player);
                if (IsRiding(player))
                {
                    ClearPendingMount();
                    Show();
                    return;
                }
                if (Time.unscaledTime <= _pendingMountUntil)
                {
                    Hide(go);
                    return;
                }
            }

            ClearPendingMount();
            _hidden.Clear();
            go.SetActive(false);
            Despawn(go);
            if (player != null && !player.IsDead())
                player.Message(MessageHud.MessageType.Center,
                    CombatLock.IsInCombat(player) ? CombatLock.Message : "Não foi possível montar aqui.");
        }

        /// <summary>
        /// Asks the saddle for control straight away. The summoner owns the new mount, so the
        /// saddle's own request handler can be run here and its answer comes back locally, in
        /// this same frame -- without going throughOkInteract's walk-up checks, which are
        /// about reaching a mount, not about one born under you. Interact stays as the fallback.
        /// </summary>
        private static void AttachNow(GameObject go, Player player)
        {
            var sadle = go.GetComponentInChildren<Sadle>(true);
            if (sadle == null) return;
            try
            {
                if (ZRoutedRpc.instance != null && Access.Get(ZRoutedRpc.instance, "m_id") is long self)
                    Access.Call(sadle, "RPC_RequestControl", self, player.GetPlayerID());
                if (!IsRiding(player))
                    TryMount(go, player);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: could not seat the rider: {e.Message}");
            }
        }

        private static void ClearPendingMount()
        {
            _pendingMount = null;
            _pendingMountPlayer = null;
        }

        private static readonly System.Collections.Generic.List<Renderer> _hidden =
            new System.Collections.Generic.List<Renderer>();

        /// <summary>Turns off every renderer of the mount that is on, remembering which, so that
        /// <see cref="Show"/> brings back exactly those (the vanilla boar meshes the capybara
        /// skin replaces stay off). Safe to call again after the visuals were rebuilt.</summary>
        private static void Hide(GameObject go)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled) continue;
                renderer.enabled = false;
                _hidden.Add(renderer);
            }
        }

        private static void Show()
        {
            foreach (var renderer in _hidden)
                if (renderer != null) renderer.enabled = true;
            _hidden.Clear();
        }

        /// <summary>The mount is born on top of the rider; without this the two capsules would
        /// shove each other apart before the rider is seated.</summary>
        private static void IgnoreRider(GameObject go, Player player)
        {
            var riderCollider = Access.Get(player, "m_collider") as Collider;
            if (riderCollider == null) return;
            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
                if (collider != null) Physics.IgnoreCollision(riderCollider, collider, true);
        }

        /// <summary>Gets the player off their mount, and the mount is gone with it.</summary>
        public static void Dismount(Player player)
        {
            if (player == null) return;
            GameObject mount = null;
            if (TryGetRidden(player, out var ridden, out _, out _) && ridden != null)
                mount = ridden.gameObject;
            if (mount == null)
                mount = FindOwned(player, MountHub.IsOurs);

            ClearPendingMount();
            _hidden.Clear();
            Vector3 riderPos = player.transform.position;
            ForceUnseat(player);
            if (mount != null)
                LandBeside(player, mount.transform, riderPos);
            if (mount == null) return;
            mount.SetActive(false);
            QueueDespawn(mount);
        }

        /// <summary>
        /// Getting off leaves the rider where the saddle's detach offset says. If that put
        /// them somewhere wrong -- far from the mount (a seat or attach point that went bad),
        /// under the ground, or not a number -- they are set down on the ground beside it.
        /// </summary>
        private static void LandBeside(Player player, Transform mount, Vector3 riderPos)
        {
            var pos = player.transform.position;
            Vector3 mountPos = mount.position;
            if (!IsFinite(mountPos)) mountPos = riderPos;
            if (!IsFinite(mountPos)) return;

            var offset = pos - mountPos;
            offset.y = 0f;
            bool far = !IsFinite(pos) || offset.magnitude > 6f;
            float ground = GroundAt(pos);
            bool buried = IsFinite(pos) && !float.IsNaN(ground) && pos.y < ground - 0.5f;
            if (!far && !buried) return;

            var side = mount.right;
            side.y = 0f;
            side = side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.right;
            var landing = mountPos + side * 1.2f;
            float landingGround = GroundAt(landing);
            if (!float.IsNaN(landingGround))
                landing.y = Mathf.Max(landing.y, landingGround) + 0.2f;
            Plugin.Log.LogWarning($"ValheimMontarias: rider left the mount at {pos}, mount at {mountPos}; set down at {landing}");

            player.transform.position = landing;
            var body = Access.Body(player);
            if (body != null)
            {
                body.position = landing;
                body.linearVelocity = Vector3.zero;
            }
        }

        private static float GroundAt(Vector3 pos)
        {
            var zones = ZoneSystem.instance;
            if (zones == null || !IsFinite(pos)) return float.NaN;
            return zones.GetSolidHeight(pos, out float height, 1000) ? height : float.NaN;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
                && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        private static void QueueDespawn(GameObject go)
        {
            if (go == null) return;
            _pendingDespawn = go;
            _pendingDespawnAt = Time.unscaledTime + 0.05f;
        }

        private static void FlushDespawn()
        {
            if (_pendingDespawn == null || Time.unscaledTime < _pendingDespawnAt) return;
            var go = _pendingDespawn;
            _pendingDespawn = null;
            Despawn(go);
        }

        private static void Despawn(GameObject go)
        {
            if (go == null) return;
            var scene = ZNetScene.instance;
            if (scene != null)
            {
                scene.Destroy(go);
                return;
            }
            var nview = go.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
                nview.Destroy();
            else
                UnityEngine.Object.Destroy(go);
        }

        public static void Warp(GameObject go, Vector3 pos, Quaternion rot)
        {
            if (go == null) return;
            go.transform.SetPositionAndRotation(pos, rot);
            var nview = go.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
                nview.GetZDO().SetPosition(pos);
        }

        public static bool IsFollowing(GameObject go, Player player)
        {
            var ai = go != null ? go.GetComponent<BaseAI>() : null;
            var target = Access.Call(ai, "GetFollowTarget") as GameObject;
            return target != null && player != null && target == player.gameObject;
        }

        public static void SetFollow(GameObject go, Player player, bool follow)
        {
            var ai = go != null ? go.GetComponent<BaseAI>() : null;
            if (ai == null) return;
            Access.Call(ai, "SetFollowTarget", follow ? player.gameObject : null);
            if (!follow)
                Access.Call(ai, "ResetPatrolPoint");
        }

        public static bool TryMount(GameObject go, Player player)
        {
            if (go == null || player == null) return false;
            if (CombatLock.Block(player)) return false;
            var tame = go.GetComponent<Tameable>();
            Access.SetSaddle(tame, true);
            var sadle = go.GetComponentInChildren<Sadle>(true);
            if (sadle == null)
            {
                player.Message(MessageHud.MessageType.Center, "Esta montaria ainda não tem sela.");
                return false;
            }

            if (sadle is Interactable interactable)
                return interactable.Interact(player, false, false);
            var result = Access.Call(sadle, "Interact", player, false, false);
            return result is bool ok && ok;
        }

        public static bool TryGetRidden(Player player, out Character mount, out MountControl pet, out object controller)
        {
            mount = null;
            pet = null;
            controller = Access.GetDoodad(player);
            if (player == null) return false;

            foreach (var character in Character.GetAllCharacters())
            {
                if (character == null || character.IsDead()) continue;
                if (!character.gameObject.activeInHierarchy) continue;
                if (!MountHub.IsOurs(character.gameObject)) continue;
                var sadle = character.GetComponentInChildren<Sadle>(true);
                bool ridden = Access.HaveRider(sadle);
                if (!ridden && player.transform.IsChildOf(character.transform))
                    ridden = true;
                if (!ridden) continue;

                mount = character;
                pet = character.GetComponent<MountControl>();
                if (controller == null)
                    controller = sadle;
                return pet != null;
            }

            if (controller == null) return false;
            var fromCtrl = controller as Sadle;
            if (fromCtrl == null && controller is Component component)
                fromCtrl = component.GetComponent<Sadle>() ?? component.GetComponentInParent<Sadle>();
            object source = (object)fromCtrl ?? controller;
            mount = Access.Get(source, "m_character") as Character;
            if (mount == null && controller is Component c)
                mount = c.GetComponentInParent<Character>();
            if (mount == null || !MountHub.IsOurs(mount.gameObject))
            {
                mount = null;
                return false;
            }
            pet = mount.GetComponent<MountControl>();
            return pet != null;
        }

        public static bool IsRiding(Player player)
        {
            return TryGetRidden(player, out _, out _, out _);
        }

        private static bool _wasRiding;
        private static float _rideLockUntil;
        internal static bool AllowUnseat;

        public static void RequestDismount(Player player) => Dismount(player);

        public static void ForceUnseat(Player player)
        {
            if (player == null) return;
            AllowUnseat = true;
            try
            {
                Access.StopDoodad(player);
                Access.Call(player, "AttachStop");
                RestoreAfterAttach(player);
            }
            finally
            {
                AllowUnseat = false;
                _wasRiding = false;
            }
        }

        private static void RestoreAfterAttach(Player player)
        {
            var cols = Access.Get(player, "m_attachColliders") as Collider[];
            var playerCol = Access.Get(player, "m_collider") as Collider;
            if (cols != null && playerCol != null)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null)
                        Physics.IgnoreCollision(playerCol, cols[i], false);
                }
            }

            Access.Set(player, "m_attachColliders", null);
            Access.Set(player, "m_attached", false);
            Access.Set(player, "m_attachPoint", null);
            Access.Set(player, "m_attachPointCamera", null);
            Access.Set(player, "m_doodadController", null);
            Access.Set(player, "m_doodadControlls", null);

            var body = Access.Body(player);
            if (body != null)
            {
                body.useGravity = true;
                body.isKinematic = false;
            }

            if (player.transform.parent != null)
            {
                var mount = player.transform.parent.GetComponentInParent<Character>();
                if (mount != null && MountHub.IsOurs(mount.gameObject))
                    player.transform.SetParent(null);
            }

            var zanim = Access.Call(player, "GetZAnim") ?? Access.Get(player, "m_zanim");
            var anim = Access.Get(player, "m_attachAnimation") as string;
            if (zanim != null && !string.IsNullOrEmpty(anim))
                Access.Call(zanim, "SetBool", anim, false);
        }

        public static bool ShouldBlockUnseat(Player player)
        {
            if (AllowUnseat) return false;
            if (player == null || player.IsDead()) return false;
            if (!TryGetRidden(player, out var mount, out _, out _)) return false;
            if (mount == null || mount.IsDead() || !mount.gameObject.activeInHierarchy)
                return false;
            if (_pendingDespawn != null && mount.gameObject == _pendingDespawn)
                return false;
            return true;
        }

        /// <summary>
        /// While on our boar, skip vanilla doodad (Jump/Attack would unseat).
        /// Mounting itself is still Sadle.Interact.
        /// </summary>
        public static bool TryOverrideRideControls(Player player)
        {
            if (!TryGetRidden(player, out var mount, out _, out _))
            {
                _wasRiding = false;
                return false;
            }
            if (mount.IsDead())
            {
                _wasRiding = false;
                return false;
            }

            if (!_wasRiding)
            {
                _wasRiding = true;
                _rideLockUntil = Time.unscaledTime + 0.55f;
                MountHub.ApplyAll(mount.gameObject);
                MountHub.KeepHumanScale(player);
            }

            MountHub.FitSeat(mount.gameObject);
            MountHub.KeepHumanScale(player);

            // Walking and running are read by the mount's own AI (MountAi_UpdateAI_Patch);
            // here only the keys that act once.
            if (AdminMenu.IsOpen)
                return true;

            // E (Use) does not get you off: only the mount item and the summon key do.

            if (Access.JumpPressed() || Input.GetKeyDown(KeyCode.Space))
            {
                var profile = MountSettings.Find(mount.gameObject);
                if (profile != null && profile.Flies && !mount.IsFlying() && !mount.IsSwimming())
                {
                    // Flying is the fourth level of the skill, whatever the mount itself asks.
                    if (MountRoster.Rank >= MountSettings.FlightRank)
                        TakeOff(mount);
                    else
                        player.Message(MessageHud.MessageType.Center,
                            $"Voar exige a habilidade {RidingRanks.NameOf(MountSettings.FlightRank)}.");
                }
                else if (mount.IsOnGround())
                {
                    mount.Jump();
                    var body = Access.Body(mount);
                    if (body != null)
                    {
                        var vel = body.linearVelocity;
                        if (vel.y < 5f) vel.y = MountHub.JumpOf(mount.gameObject);
                        vel += mount.transform.forward * 1.4f;
                        body.linearVelocity = vel;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// What the mount's AI does in place of its own while someone rides it: walk or run
        /// where the rider looks while forward is held, stand still otherwise. Nothing else --
        /// no wandering, fleeing, following or looking for food, which is what made the
        /// mounts move by themselves. Covers the Deer too, whose AnimalAI never asks the
        /// saddle (only MonsterAI does), so it did not move at all.
        /// </summary>
        public static bool DriveAI(BaseAI ai)
        {
            // BaseAI.m_nview is protected in the live assembly: reading it here throws.
            var nview = ai.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return false;

            WakeUp(ai, nview);
            var character = ai.GetComponent<Character>();
            if (character != null && character.IsFlying())
            {
                Fly(ai, character);
                return true;
            }
            if (RideIntent(ai.gameObject, out var dir, out bool run))
                ai.MoveTowards(dir, run);
            else
                ai.StopMoving();
            return true;
        }

        /// <summary>Until when a mount that just took off keeps climbing on its own. Flying,
        /// the body's velocity is set straight to the wanted one every physics step (Character.
        /// UpdateFlying with our acceleration), so an upward kick would be gone at once.</summary>
        private static float _climbUntil;
        /// <summary>The take-off animation holds the body still (an animator "freeze" tag:
        /// Character.CanMove is false), so the climb starts when it lets go, or gives up.</summary>
        private static float _climbPendingUntil;
        private const float TakeOffClimbSeconds = 0.8f;
        private const float TakeOffAnimationLimit = 4f;

        private static void TakeOff(Character mount)
        {
            _climbUntil = 0f;
            _climbPendingUntil = Time.time + TakeOffAnimationLimit;
            mount.TakeOff();
            var body = Access.Body(mount);
            if (body != null)
            {
                var vel = body.linearVelocity;
                vel.y = Mathf.Max(vel.y, 6f);
                body.linearVelocity = vel;
            }
        }

        /// <summary>
        /// Flying, the Moder goes where the rider looks while forward is held (the camera's
        /// pitch too, WoW style); Space climbs and Ctrl descends; with no key it hovers. It
        /// never goes higher than AlturaMaximaVoo above the ground or the water, and Ctrl close
        /// to dry ground lands it.
        /// </summary>
        private static void Fly(BaseAI ai, Character mount)
        {
            if (Time.time < _climbPendingUntil && mount.CanMove())
            {
                _climbPendingUntil = 0f;
                _climbUntil = Time.time + TakeOffClimbSeconds;
            }
            bool climbing = Time.time < _climbUntil || Time.time < _climbPendingUntil;
            if (!RideInput(ai.gameObject, out var player, out var stick))
            {
                // The take-off climb does not wait for the rider's keys.
                mount.SetMoveDir(climbing ? Vector3.up : Vector3.zero);
                return;
            }

            float vertical = 0f;
            if (Access.ButtonHeld("Jump") || Access.ButtonHeld("JoyJump") || Input.GetKey(KeyCode.Space))
                vertical += 1f;
            if (Access.ButtonHeld("Crouch") || Access.ButtonHeld("JoyCrouch") || Input.GetKey(KeyCode.LeftControl))
                vertical -= 1f;
            if (climbing)
                vertical = 1f;

            var look = player.GetLookDir();
            var flat = new Vector3(look.x, 0f, look.z);
            if (flat.sqrMagnitude < 0.0001f) flat = mount.transform.forward;
            flat.Normalize();

            var move = stick.z > 0.5f ? look.normalized : Vector3.zero;
            float climb = MountSettings.FlyClimbSpeed != null ? Mathf.Clamp(MountSettings.FlyClimbSpeed.Value, 0.1f, 3f) : 1f;
            move.y += vertical * climb;

            Vector3 pos = mount.transform.position;
            float floor = FlightFloor(pos, out bool overWater);
            float ceiling = MountSettings.FlyMaxAltitude != null ? Mathf.Max(5f, MountSettings.FlyMaxAltitude.Value) : 40f;
            float altitude = float.IsNaN(floor) ? 0f : pos.y - floor;
            if (altitude >= ceiling && move.y > 0f) move.y = 0f;
            if (altitude > ceiling + 1f) move.y = -0.5f;
            if (move.sqrMagnitude > 1f) move.Normalize();

            if (vertical < 0f && !overWater && !float.IsNaN(floor) && (altitude < 1.6f || mount.IsOnGround()))
            {
                mount.Land();
                mount.SetMoveDir(Vector3.zero);
                return;
            }

            if (stick.z > 0.5f) ai.LookTowards(flat);
            mount.SetMoveDir(move);
            mount.SetRun(Access.RunHeld());
        }

        /// <summary>What the Moder flies above: the ground, or the sea when that is higher.</summary>
        private static float FlightFloor(Vector3 pos, out bool overWater)
        {
            overWater = false;
            float ground = GroundAt(pos);
            var zones = ZoneSystem.instance;
            if (zones == null) return ground;
            float water = zones.m_waterLevel;
            if (float.IsNaN(ground) || ground < water)
            {
                overWater = true;
                return water;
            }
            return ground;
        }

        /// <summary>
        /// MonsterAI.UpdateSleep is what wakes a sleeping monster, and the AI above replaces
        /// it: one that spawned asleep (Eikthyr, rearing) would stay in that pose forever.
        /// Wakes it without MonsterAI.Wakeup, so no wake-up roar either.
        /// </summary>
        private static void WakeUp(BaseAI ai, ZNetView nview)
        {
            if (!(ai is MonsterAI monster) || !monster.m_sleeping) return;
            monster.m_sleeping = false;
            // BaseAI.m_animator is protected in the live assembly.
            (Access.Get(ai, "m_animator") as ZSyncAnimation)?.SetBool("sleeping", false);
            nview.GetZDO()?.Set(ZDOVars.s_sleeping, false);
        }

        /// <summary>The local player riding this mount and taking input, and their stick.</summary>
        private static bool RideInput(GameObject mount, out Player player, out Vector3 stick)
        {
            stick = Vector3.zero;
            player = Player.m_localPlayer;
            if (player == null || player.IsDead() || AdminMenu.IsOpen) return false;
            // Character.TakeInput is protected in the live assembly; go through reflection.
            if (Access.Call(player, "TakeInput") is bool takeInput && !takeInput) return false;
            if (!TryGetRidden(player, out var ridden, out _, out _) || ridden == null || ridden.gameObject != mount)
                return false;
            stick = Access.RideStick();
            return true;
        }

        private static bool RideIntent(GameObject mount, out Vector3 dir, out bool run)
        {
            dir = Vector3.zero;
            run = false;
            if (!RideInput(mount, out var player, out var stick) || stick.z < 0.5f) return false;

            dir = player.GetLookDir();
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = player.transform.forward;
            dir.Normalize();
            run = Access.RunHeld();
            return true;
        }

        public static string HoverText(GameObject go, Player player)
        {
            if (!IsOwner(go, player))
                return "Montaria de outra pessoa";
            if (CombatLock.IsInCombat(player))
                return "Em combate: não é possível usar a montaria";
            if (Access.IsAdmin())
                return "[<color=yellow><b>E</b></color>] Ajustes da montaria (admin)\n" +
                    $"{MountSettings.MenuKeyName()} abre o menu";
            var profile = MountSettings.Find(go);
            if (profile != null && profile.Flies)
                return "O item da montaria ou H desmontam.\nMontado: Espaço decola e sobe, Ctrl desce e pousa, Shift voa mais rápido";
            return "O item da montaria ou H desmontam.\nMontado: Espaço salta, Shift corre mais rápido";
        }
    }
}
