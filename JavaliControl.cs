using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    /// <summary>
    /// Lives on the cloned boar. Makes sure it is tamed and saddled after spawn / world load.
    /// Follow / stay / mount are handled by Tameable patches + whistle summon.
    /// </summary>
    public class JavaliControl : MonoBehaviour
    {
        public const string OwnerKey = "javali_owner";

        protected virtual void Start()
        {
            InitPet();
            BoarPrefab.ApplyAll(gameObject);
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

        protected virtual string DashSfx => "sfx_boar_attack";

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
            return FindOwned(player, BoarPrefab.IsOurs);
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
        private static string _castPrefab = BoarPrefab.PrefabName;

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

        public static void TrySummon(Player player)
        {
            TrySummonProfile(player, MountSettings.Javali);
        }

        internal static void TrySummonProfile(Player player, MountProfile profile)
        {
            if (player == null) return;
            if (profile == null)
            {
                player.Message(MessageHud.MessageType.Center, "Você ainda não possui nenhuma montaria.");
                return;
            }
            if (!MountRoster.Owns(player, profile))
            {
                player.Message(MessageHud.MessageType.Center, "Você ainda não tem essa montaria.");
                return;
            }
            if (CombatLock.Block(player)) return;
            if (Time.frameCount == _summonFrame) return;
            _summonFrame = Time.frameCount;
            Access.Set(player, "m_useItem", null);

            string prefabName = string.IsNullOrEmpty(profile.PrefabName) ? BoarPrefab.PrefabName : profile.PrefabName;
            System.Func<GameObject, bool> isOurs = profile.IsInstance;
            float cast = profile.CastSeconds != null ? Mathf.Max(0.1f, profile.CastSeconds.Value) : 2f;

            if (_casting)
            {
                CancelCast(player, "Invocação interrompida");
                return;
            }

            var existing = FindOwned(player, isOurs);
            if (existing != null)
            {
                _pendingMount = null;
                ForceUnseat(player);
                existing.SetActive(false);
                QueueDespawn(existing);
                player.Message(MessageHud.MessageType.Center, "Seu aliado foi recolhido");
                return;
            }

            if (_pendingDespawn != null)
                return;

            _castPrefab = prefabName;
            _casting = true;
            _castStartedAt = Time.unscaledTime;
            _castUntil = Time.unscaledTime + cast;
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
                return;

            _casting = false;
            FinishSummon(player);
        }

        private static bool CastInterrupted(Player player)
        {
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
            if (player != null && !string.IsNullOrEmpty(message))
                player.Message(MessageHud.MessageType.Center, message);
        }

        private static void FinishSummon(Player player)
        {
            if (player == null) return;
            var scene = ZNetScene.instance;
            if (scene == null) return;

            string prefabName = string.IsNullOrEmpty(_castPrefab) ? BoarPrefab.PrefabName : _castPrefab;
            var prefab = scene.GetPrefab(prefabName)
                ?? (prefabName == BoarPrefab.PrefabName ? BoarPrefab.Prefab : null);
            if (prefab == null)
            {
                player.Message(MessageHud.MessageType.Center, "A montaria ainda não está pronta.");
                Plugin.Log.LogWarning($"ValheimMontarias: prefab missing at summon ({prefabName})");
                return;
            }

            var rot = Quaternion.LookRotation(player.transform.forward);
            var go = UnityEngine.Object.Instantiate(prefab, player.transform.position, rot);
            SetOwner(go, player);
            var tame = go.GetComponent<Tameable>();
            Access.Call(tame, "Tame");
            Access.SetSaddle(tame, true);
            SetFollow(go, player, true);
            MountHub.ApplyAll(go);
            QueueAutoMount(go, player);
            TryFinishAutoMount();
            player.Message(MessageHud.MessageType.Center, "Seu aliado foi invocado");
        }

        private static void QueueAutoMount(GameObject go, Player player)
        {
            _pendingMount = go;
            _pendingMountPlayer = player;
            _pendingMountUntil = Time.unscaledTime + 1.2f;
        }

        private static void TryFinishAutoMount()
        {
            if (_pendingMount == null) return;
            if (!_pendingMount || Time.unscaledTime > _pendingMountUntil)
            {
                _pendingMount = null;
                _pendingMountPlayer = null;
                return;
            }

            var player = _pendingMountPlayer != null ? _pendingMountPlayer : Player.m_localPlayer;
            if (player == null || CombatLock.IsInCombat(player))
                return;
            if (IsRiding(player))
            {
                _pendingMount = null;
                _pendingMountPlayer = null;
                return;
            }

            if (TryMount(_pendingMount, player))
            {
                _pendingMount = null;
                _pendingMountPlayer = null;
            }
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

        public static bool TryGetRidden(Player player, out Character mount, out JavaliControl pet, out object controller)
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
                pet = character.GetComponent<JavaliControl>();
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
            pet = mount.GetComponent<JavaliControl>();
            return pet != null;
        }

        public static bool IsRiding(Player player)
        {
            return TryGetRidden(player, out _, out _, out _);
        }

        private static bool _wasRiding;
        private static float _rideLockUntil;
        internal static bool AllowUnseat;

        public static void RequestDismount(Player player)
        {
            ForceUnseat(player);
        }

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
            if (!TryGetRidden(player, out var mount, out var pet, out var controller))
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

            if (controller == null)
                controller = mount.GetComponentInChildren<Sadle>(true);

            if (AdminMenu.IsOpen)
            {
                Access.ApplyDoodad(controller, player);
                return true;
            }

            if (pet.IsDashing)
            {
                pet.TickDash(player);
                return true;
            }

            if (Access.AttackPressed() || Input.GetMouseButtonDown(0))
                pet.TryDash(player);
            if (pet.IsDashing)
            {
                pet.TickDash(player);
                return true;
            }

            if (Access.JumpPressed() || Input.GetKeyDown(KeyCode.Space))
            {
                if (mount.IsOnGround())
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
                Access.ApplyDoodad(controller, player);
                return true;
            }

            Access.ApplyDoodad(controller, player);
            return true;
        }

        private const float DashDuration = 0.42f;
        private const float DashCooldown = 5f;
        private const float DashSpeed = 21f;
        private const float DashStamina = 12f;
        private const float DashDamage = 22f;
        private const float DashPush = 40f;

        private float _dashUntil;
        private float _dashReadyAt;
        private readonly System.Collections.Generic.HashSet<int> _dashHits = new System.Collections.Generic.HashSet<int>();

        public bool IsDashing => Time.time < _dashUntil;

        public void TryDash(Player player)
        {
            if (player == null) return;
            if (Time.time < _dashReadyAt) return;
            if (!player.HaveStamina(DashStamina))
            {
                player.Message(MessageHud.MessageType.Center, "Sem Energia! Descanse um pouco.");
                return;
            }

            player.UseStamina(DashStamina);
            _dashHits.Clear();
            _dashUntil = Time.time + DashDuration;
            _dashReadyAt = Time.time + DashCooldown;

            var character = GetComponent<Character>();
            Access.PlayTrigger(character, "attack");
            Access.PlaySfx(DashSfx, transform.position);
            Access.PlaySfx("sfx_unarmed_hit", transform.position);
        }

        public void TickDash(Player player)
        {
            var mount = GetComponent<Character>();
            var body = Access.Body(mount);
            Vector3 look = player != null ? player.GetLookDir() : transform.forward;
            look.y = 0f;
            if (look.sqrMagnitude < 0.0001f)
                look = transform.forward;
            look.Normalize();
            transform.rotation = Quaternion.LookRotation(look);

            if (body != null)
            {
                var vel = look * DashSpeed;
                vel.y = body.linearVelocity.y;
                body.linearVelocity = vel;
            }

            HitDashTargets(mount, look);
        }

        private void HitDashTargets(Character mount, Vector3 dir)
        {
            if (mount == null) return;
            Vector3 origin = mount.GetCenterPoint() + dir * 0.7f;
            var hits = Physics.OverlapSphere(origin, 1.35f, -1, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                var other = hits[i] != null ? hits[i].GetComponentInParent<Character>() : null;
                if (other == null || other == mount || other.IsPlayer() || other.IsDead())
                    continue;
                int id = other.GetInstanceID();
                if (!_dashHits.Add(id)) continue;

                var hit = new HitData();
                hit.m_damage.m_blunt = DashDamage;
                hit.m_pushForce = DashPush;
                hit.m_point = other.GetCenterPoint();
                hit.m_dir = dir;
                hit.m_hitType = HitData.HitType.EnemyHit;
                other.Damage(hit);
            }
        }

        public static string HoverText(GameObject go, Player player)
        {
            if (!IsOwner(go, player))
                return "Montaria de outra pessoa";
            if (CombatLock.IsInCombat(player))
                return "Em combate: não é possível usar a montaria";
            if (Access.IsAdmin())
                return "[<color=yellow><b>E</b></color>] Ajustes da montaria (admin)\nU abre o menu  ·  H invoca/recolhe";
            return "Use o apito para recolher a montaria.\nMontado: Espaço salta, clique investida";
        }
    }
}
