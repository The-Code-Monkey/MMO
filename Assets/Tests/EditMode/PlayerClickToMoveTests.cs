using System;
using System.Collections.Generic;
using FishNet.Object;
using MMO.Client;
using MMO.Crafting;
using MMO.UI;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The click-to-move driver's guard on the crafting panel.
    ///
    /// <para>
    /// The panel is a screen-space window over the world, and the driver turns a click into a
    /// destination with a <em>physics</em> raycast — so a click that lands on the panel would
    /// also send the player walking. The panel swallows its own clicks, and a click on the world
    /// closes the panel the player was standing in front of.
    /// </para>
    /// </summary>
    public sealed class PlayerClickToMoveTests
    {
        private readonly List<GameObject> _objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _objects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _objects.Clear();
        }

        [Test]
        public void CloseCraftingPanel_HidesTheOpenPanel()
        {
            CraftingPanelView panel = BuildOpenPanel();
            PlayerClickToMove clickToMove = BuildClickToMove();

            Assert.IsTrue(panel.IsOpen, "The server opened a station, so the panel is on screen.");

            clickToMove.CloseCraftingPanel();

            Assert.IsFalse(panel.IsOpen,
                "A click on the world closes the panel it was covering: the player is walking away.");
        }

        [Test]
        public void CloseCraftingPanel_WithoutAnOpenPanel_LeavesThePanelAlone()
        {
            CraftingPanelView panel = BuildOpenPanel();
            PlayerClickToMove clickToMove = BuildClickToMove();

            panel.Conceal();

            Assert.DoesNotThrow(() => clickToMove.CloseCraftingPanel(),
                "Closing a panel that is already closed is a no-op, not a null reference.");
        }

        private PlayerClickToMove BuildClickToMove()
        {
            var go = new GameObject("ClickToMoveUnderTest");
            _objects.Add(go);
            return go.AddComponent<PlayerClickToMove>();
        }

        /// <summary>
        /// A panel the way the HUD builds it: the factory's window, bound to a source that
        /// already holds a station, which is what makes it visible.
        /// </summary>
        private CraftingPanelView BuildOpenPanel()
        {
            var stationObject = new GameObject("Anvil");
            _objects.Add(stationObject);
            stationObject.AddComponent<NetworkObject>();
            CraftingStation station = stationObject.AddComponent<CraftingStation>();

            var root = new GameObject("Hud", typeof(RectTransform));
            _objects.Add(root);

            CraftingPanelView panel = CraftingPanelFactory.Create(root.transform);
            panel.Bind(new FakeCraftingSource(station));
            return panel;
        }

        private sealed class FakeCraftingSource : ICraftingSource
        {
            public FakeCraftingSource(CraftingStation station) => OpenedStation = station;

            public int CraftingXp => 0;
            public int CraftingLevel => 1;
            public CraftingStation OpenedStation { get; }
            public IReadOnlyList<RecipeOffer> AvailableRecipes => Array.Empty<RecipeOffer>();

            public event Action RecipesChanged;
            public event Action<CraftingStation, IReadOnlyList<RecipeOffer>> StationOpened;

            public void RequestCraft(CraftingStation station, string recipeName)
            {
            }
        }
    }
}
