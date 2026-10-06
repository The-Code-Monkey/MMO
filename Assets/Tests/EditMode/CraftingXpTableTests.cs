using MMO.Crafting;
using MMO.Inventory;
using MMO.Tests;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    public sealed class CraftingXpTableTests
    {
        [Test]
        public void LevelForXp_XpForLevel_RoundTrip()
        {
            for (int l = 1; l <= 20; l++)
            {
                int xp = CraftingXpTable.XpForLevel(l);
                int level = CraftingXpTable.LevelForXp(xp);
                Assert.AreEqual(l, level);
            }
        }

        [Test]
        public void LevelForXp_Monotonic()
        {
            Assert.GreaterOrEqual(CraftingXpTable.LevelForXp(100), CraftingXpTable.LevelForXp(0));
            Assert.GreaterOrEqual(CraftingXpTable.LevelForXp(400), CraftingXpTable.LevelForXp(100));
        }
    }
}
