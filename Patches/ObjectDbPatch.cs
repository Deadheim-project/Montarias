using HarmonyLib;
using ValheimMontarias.Prefabs;

namespace ValheimMontarias.Patches
{
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    internal static class ObjectDB_Awake_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ObjectDB __instance)
        {
            WhistleItem.RegisterItem(__instance);
            MountGear.CaptureFromDb();
        }
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class ObjectDB_CopyOtherDB_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ObjectDB __instance)
        {
            WhistleItem.RegisterItem(__instance);
            MountGear.CaptureFromDb();
        }
    }
}
