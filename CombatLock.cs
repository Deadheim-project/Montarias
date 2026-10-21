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
        private static readonly int[] StatusHashes =
        {
            "DH_Combat".GetStableHashCode(),
            "Combat".GetStableHashCode()
        };
        private static readonly int DeadheimFlagsKey = "dh_pvpFlags".GetStableHashCode();
        // PvpFlags.Combat in Deadheim (Pvp/PvpState.cs).
        private const int DeadheimFlagCombat = 16;

        public const string Message = "Você não pode usar montarias em combate.";

        public static bool IsInCombat(Player player)
        {
            if (player == null) return false;
            var seman = player.GetSEMan();
            if (seman != null)
            {
                for (int i = 0; i < StatusHashes.Length; i++)
                {
                    if (seman.HaveStatusEffect(StatusHashes[i]))
                        return true;
                }
            }
            // GetComponent, not Character.m_nview: without AllowUnsafeBlocks reading the field
            // threw FieldAccessException in game (0.1.67).
            var nview = player.GetComponent<ZNetView>();
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            return zdo != null && (zdo.GetInt(DeadheimFlagsKey, 0) & DeadheimFlagCombat) != 0;
        }

        public static bool Block(Player player)
        {
            if (!IsInCombat(player))
                return BlockHeavy(player);
            player.Message(MessageHud.MessageType.Center, Message);
            return true;
        }

        public const string HeavyMessage = "Você está pesado demais para montar.";

        /// <summary>Over the carry limit (the game's own Encumbered state): no mounting.</summary>
        public static bool BlockHeavy(Player player)
        {
            if (player == null || !player.IsEncumbered()) return false;
            player.Message(MessageHud.MessageType.Center, HeavyMessage);
            return true;
        }
    }
}
