namespace ValheimMontarias
{
    /// <summary>
    /// Combat comes from Deadheim, which absorbed the old Detalhes.Combat. Any of these counts:
    /// the "DH_Combat" buff (fighting a player or a monster), the Combat bit Deadheim publishes on
    /// the player's ZDO (dh_pvpFlags, present even with the buff turned off in its cfg) and the
    /// legacy "Combat" status. Without Deadheim none of them exist and the checks stay false.
    /// </summary>
    internal static class CombatLock
    {
        private static readonly int LegacyStatusHash = "Combat".GetStableHashCode();
        private static readonly int DeadheimStatusHash = "DH_Combat".GetStableHashCode();
        private static readonly int DeadheimFlagsKey = "dh_pvpFlags".GetStableHashCode();
        // PvpFlags.Combat in Deadheim (Pvp/PvpState.cs).
        private const int DeadheimFlagCombat = 16;
        public const string Message = "Você não pode usar montarias em combate.";

        public static bool IsInCombat(Player player)
        {
            if (player == null) return false;
            var seman = player.GetSEMan();
            if (seman != null && (seman.HaveStatusEffect(DeadheimStatusHash) || seman.HaveStatusEffect(LegacyStatusHash)))
                return true;
            var nview = player.GetComponent<ZNetView>();
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            return zdo != null && (zdo.GetInt(DeadheimFlagsKey, 0) & DeadheimFlagCombat) != 0;
        }

        public static bool Block(Player player)
        {
            if (!IsInCombat(player)) return false;
            player.Message(MessageHud.MessageType.Center, Message);
            return true;
        }
    }
}
