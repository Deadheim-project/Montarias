using HarmonyLib;
using UnityEngine;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    /// <summary>
    /// Aesir 28: garante que o nome customizado (gravado no ZDO pelo dono ao invocar) seja
    /// exibido no EnemyHud de qualquer cliente que veja a montaria.
    ///
    /// O Valheim usa Character.GetHoverName() para preencher o rótulo acima da cabeça.
    /// O método retorna Character.m_name -- que já é corretamente setado pelo
    /// MountPrefab.ApplyName quando a montaria é instanciada localmente. Mas quando a
    /// montaria já está no mundo (invocada por outro cliente) e ainda não passou por
    /// ApplyAll, m_name pode ter ficado com o nome padrão do prefab.
    ///
    /// O postfix abaixo lê diretamente do ZDO, que é sempre a fonte de verdade (o ZDO é
    /// replicado pelo Valheim a todos os clientes assim que o dono o escreve), e sobrescreve
    /// o resultado apenas se o ZDO tiver um nome customizado não-vazio.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
    internal static class Character_GetHoverName_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Character), nameof(Character.GetHoverName)) != null;

        [HarmonyPostfix]
        private static void Postfix(Character __instance, ref string __result)
        {
            if (__instance == null) return;
            if (!MountPrefab.IsMountPrefab(__instance.gameObject)) return;
            if (!(MountSettings.MountNameActive?.Value ?? false)) return;

            string custom = MountPrefab.ReadZdoString(__instance.gameObject, MountPrefab.RiderNameHash);
            if (!string.IsNullOrEmpty(custom))
                __result = custom;
        }
    }

    [HarmonyPatch(typeof(Hud), "UpdateMount")]
    internal static class Hud_UpdateMount_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Hud), "UpdateMount") != null;

        [HarmonyPrefix]
        private static bool Prefix(Hud __instance)
        {
            var player = Player.m_localPlayer;
            if (player == null || !MountControl.IsRiding(player)) return true;

            var panel = Access.Get(__instance, "m_mountPanel") as GameObject;
            if (panel != null)
                panel.SetActive(false);
            return false;
        }
    }

    /// <summary>
    /// The summon cast uses the game's own action bar -- the one of "equipping" or "eating" --
    /// so it looks and sits exactly like Valheim's.
    /// </summary>
    [HarmonyPatch(typeof(Hud), "UpdateActionProgress")]
    internal static class Hud_UpdateActionProgress_Patch
    {
        private static bool Prepare() => AccessTools.Method(typeof(Hud), "UpdateActionProgress") != null;

        [HarmonyPostfix]
        private static void Postfix(Hud __instance)
        {
            if (!MountControl.IsCasting) return;
            if (__instance.m_actionBarRoot == null || __instance.m_actionProgress == null) return;

            __instance.m_actionBarRoot.SetActive(true);
            __instance.m_actionProgress.SetValue(MountControl.CastProgress);
            if (__instance.m_actionName != null)
                __instance.m_actionName.text = string.IsNullOrEmpty(MountControl.CastName)
                    ? "Invocando montaria"
                    : $"Invocando {MountControl.CastName}";
        }
    }
}