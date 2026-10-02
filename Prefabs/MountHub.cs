using UnityEngine;

namespace ValheimMontarias.Prefabs
{
    internal static class MountHub
    {
        public static bool IsOurs(GameObject go)
        {
            return BoarPrefab.IsOurs(go);
        }

        public static void ApplyAll(GameObject go)
        {
            if (BoarPrefab.IsOurs(go))
                BoarPrefab.ApplyAll(go);
        }

        public static void FitSeat(GameObject go)
        {
            BoarPrefab.FitSeat(go);
        }

        /// <summary>Re-applies move speed to every mount of ours, after the local rider's skill
        /// level (and with it the speed bonus) changed.</summary>
        public static void ApplySpeedAll()
        {
            foreach (var character in Character.GetAllCharacters())
            {
                if (character != null && BoarPrefab.IsOurs(character.gameObject))
                    BoarPrefab.ApplyMoveSpeed(character);
            }
        }

        public static void KeepHumanScale(Player player)
        {
            BoarPrefab.KeepHumanScale(player);
        }

        public static Transform FindVisual(GameObject go)
        {
            if (go == null) return null;
            var character = go.GetComponent<Character>();
            if (Access.Get(character, "m_visual") is GameObject visual && visual != null)
                return visual.transform;
            return go.transform.Find("Visual");
        }

        public static Transform FindAttach(GameObject go)
        {
            if (go == null) return null;
            var named = go.transform.Find("AttachPoint");
            if (named != null) return named;
            var all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == "AttachPoint"
                    && all[i].GetComponentInParent<Player>() == null)
                    return all[i];
            }
            var sadle = go.GetComponentInChildren<Sadle>(true);
            return Access.Get(sadle, "m_attachPoint") as Transform;
        }

        public static float JumpOf(GameObject go)
        {
            var profile = MountSettings.Find(go);
            if (profile?.JumpHeight != null)
                return profile.JumpHeight.Value;
            return MountSettings.Jump;
        }
    }
}
