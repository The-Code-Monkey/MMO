using UnityEngine;

namespace MMO.Crafting
{
    public static class CraftingXpTable
    {
        public static int XpForLevel(int level)
        {
            int l = Mathf.Max(1, level);
            return 100 * (l - 1) * (l - 1);
        }

        public static int LevelForXp(int xp)
        {
            int x = Mathf.Max(0, xp);
            return Mathf.FloorToInt(Mathf.Sqrt(x / 100f)) + 1;
        }
    }
}
