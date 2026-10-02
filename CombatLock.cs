namespace ValheimMontarias
{
    /// <summary>
    /// Soft link to Detalhes.Combat. If that mod is not installed, the status never
    /// exists and these checks stay false.
    /// </summary>
    internal static class CombatLock
    {
        public static readonly int StatusHash = "Combat".GetStableHashCode();
        public const string Message = "Você não pode usar montarias em combate.";

        public static bool IsInCombat(Player player)
        {
            if (player == null) return false;
            var seman = player.GetSEMan();
            return seman != null && seman.HaveStatusEffect(StatusHash);
        }

        public static bool Block(Player player)
        {
            if (!IsInCombat(player)) return false;
            player.Message(MessageHud.MessageType.Center, Message);
            return true;
        }
    }
}
