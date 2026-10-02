namespace ValheimMontarias
{
    /// <summary>
    /// Soft link to the combat status of other mods. Deadheim (Detalhes.Deadheim) shows
    /// "DH_Combat" while its CombatStatusIcon is on; the old standalone Detalhes.Combat used
    /// "Combat". Without either mod the status never exists and these checks stay false.
    /// </summary>
    internal static class CombatLock
    {
        private static readonly int[] StatusHashes =
        {
            "DH_Combat".GetStableHashCode(),
            "Combat".GetStableHashCode()
        };

        public const string Message = "Você não pode usar montarias em combate.";

        public static bool IsInCombat(Player player)
        {
            if (player == null) return false;
            var seman = player.GetSEMan();
            if (seman == null) return false;
            for (int i = 0; i < StatusHashes.Length; i++)
            {
                if (seman.HaveStatusEffect(StatusHashes[i]))
                    return true;
            }
            return false;
        }

        public static bool Block(Player player)
        {
            if (!IsInCombat(player)) return false;
            player.Message(MessageHud.MessageType.Center, Message);
            return true;
        }
    }
}
