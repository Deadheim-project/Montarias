using HarmonyLib;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    internal static class ZNetScene_Awake_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(ZNetScene __instance)
        {
            Try("register items", () =>
            {
                if (ObjectDB.instance != null)
                    WhistleItem.RegisterItem(ObjectDB.instance);
            });
            Try("register capybara", () => BoarPrefab.Register(__instance));
            Try("register whistles", () => WhistleItem.RegisterScene(__instance));
        }

        [HarmonyPostfix]
        private static void Postfix(ZNetScene __instance)
        {
            Try("finish capybara", () => BoarPrefab.Finish(__instance));
            Try("finish whistles", () => WhistleItem.FinishScene(__instance));
        }

        private static void Try(string what, System.Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"ValheimMontarias: {what} failed: {e}");
            }
        }
    }
}
