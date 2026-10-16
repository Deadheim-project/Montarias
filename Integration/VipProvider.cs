using System;
using System.Linq;
using System.Reflection;

namespace ValheimMontarias.Integration
{
    /// <summary>
    /// Soft integration with Detalhes.VipList (VipList.dll), resolved entirely through
    /// reflection so this mod has no compile- or load-time dependency on it (same pattern as
    /// Velas.Integration.VipProvider and NpcValheim's VipShopShortcut): nome e skin de
    /// montaria (Aesir 28/30) são cosméticos sem vantagem de combate, mas ainda assim
    /// exclusivos de VIP por padrão (MountSettings.MountNameVipOnly/MountSkinVipOnly). Sem
    /// o VipList instalado, todo mundo responde "não é VIP".
    /// </summary>
    internal static class VipProvider
    {
        private const string AssemblyName = "VipList";
        private const string ApiTypeName = "VipList.VipListApi";

        private static readonly BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static bool _resolved;
        private static MethodInfo _isVipByPlatformId;
        private static MethodInfo _isLocalPlayerVip;

        public static bool IsAvailable
        {
            get { Resolve(); return _isVipByPlatformId != null; }
        }

        /// <summary>Whether the player behind a routed RPC sender is on the VIP list. Server
        /// only (needs RiderServer's connection lookup); a remote sender resolves by platform
        /// account id the same way the Deadcoins ledger does, and this process' own
        /// player/host falls back to IsLocalPlayerVip(), mirroring NpcValheim's
        /// VipShopShortcut.SenderIsVip.</summary>
        public static bool IsVip(long sender)
        {
            Resolve();
            if (_isVipByPlatformId == null) return false;

            try
            {
                if (RiderServer.IsLocalSender(sender))
                    return InvokeIsLocalPlayerVip();

                var account = RiderServer.AccountIdOf(sender);
                return !string.IsNullOrEmpty(account) &&
                       (bool)_isVipByPlatformId.Invoke(null, new object[] { account });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: VipList.VipListApi.IsVip failed for sender {sender}: {e.Message}");
                return false;
            }
        }

        /// <summary>Client-side convenience check (for the menu, before asking the server):
        /// whether the local player is VIP. The server re-checks independently on every
        /// name/skin request, so this is a display hint only.</summary>
        public static bool IsLocalPlayerVip()
        {
            Resolve();
            return InvokeIsLocalPlayerVip();
        }

        private static bool InvokeIsLocalPlayerVip()
        {
            if (_isLocalPlayerVip == null) return false;
            try { return (bool)_isLocalPlayerVip.Invoke(null, Array.Empty<object>()); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: VipList.VipListApi.IsLocalPlayerVip failed: {e.Message}");
                return false;
            }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == AssemblyName);
                if (assembly == null)
                {
                    Plugin.Log.LogInfo("ValheimMontarias: mod VipList não instalado -- nome/skin de montaria ficam exclusivos de admin (VipOnly recusa todo o resto).");
                    return;
                }

                var apiType = assembly.GetType(ApiTypeName);
                if (apiType == null)
                {
                    Plugin.Log.LogWarning("ValheimMontarias: assembly VipList encontrada, mas o tipo VipList.VipListApi não existe; integração VIP desativada.");
                    return;
                }

                _isVipByPlatformId = apiType.GetMethods(AnyStatic)
                    .FirstOrDefault(m => m.Name == "IsVip" && m.GetParameters().Length == 1
                                         && m.GetParameters()[0].ParameterType == typeof(string));
                _isLocalPlayerVip = apiType.GetMethod("IsLocalPlayerVip", AnyStatic, null, Type.EmptyTypes, null);

                Plugin.Log.LogInfo($"ValheimMontarias: integração VipList pronta (IsVip={_isVipByPlatformId != null}, IsLocalPlayerVip={_isLocalPlayerVip != null})");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: não foi possível ligar à API do VipList: {e.Message}");
            }
        }
    }
}
