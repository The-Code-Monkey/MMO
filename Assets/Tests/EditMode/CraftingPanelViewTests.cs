using System;
using System.Collections.Generic;
using MMO.Crafting;
using MMO.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The unified crafting window as a pure view: it shows whatever offer payload the source
    /// handed it, greys out what the payload flagged as unaffordable, forwards clicks as craft
    /// intents, and does nothing at all without an open station.
    ///
    /// <para>
    /// A tiny in-memory <see cref="ICraftingSource"/> stands in for the player, so none of this
    /// touches a network. The point being proven is the boundary the issue is about: the panel
    /// never consults <c>RecipeRegistry</c>, it renders the payload.
    /// </para>
    /// </summary>
    public sealed class CraftingPanelViewTests
    {
        private GameObject _panelObject;
        private CraftingPanelView _view;
        private GameObject _countingObject;
        private FakeCraftingSource _source;
        private GameObject _stationObject;
        private CraftingStation _station;

        [SetUp]
        public void SetUp()
        {
            _source = new FakeCraftingSource();

            _stationObject = new GameObject("Anvil");
            _stationObject.AddComponent<FishNet.Object.NetworkObject>();
            _station = _stationObject.AddComponent<CraftingStation>();

            _panelObject = new GameObject("CraftingPanelUnderTest");
            _view = _panelObject.AddComponent<CraftingPanelView>();
            _view.SetContainer(CreateChild(_panelObject, "Rows"));
            _view.SetTitle(CreateChild(_panelObject, "Title").gameObject.AddComponent<Text>());
            _panelObject.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_countingObject != null)
                Object.DestroyImmediate(_countingObject);
            if (_panelObject != null)
                Object.DestroyImmediate(_panelObject);
            if (_stationObject != null)
                Object.DestroyImmediate(_stationObject);
        }

        private static RectTransform CreateChild(GameObject parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent.transform, false);
            return (RectTransform)child.transform;
        }

        [Test]
        public void Bind_WithoutAnOpenStation_StaysClosed()
        {
            _view.Bind(_source);

            Assert.IsFalse(_view.IsOpen, "Nothing is open until the server sends a payload.");
            Assert.IsEmpty(_view.Rows);
        }

        [Test]
        public void StationOpened_RevealsThePanelAndBuildsOneRowPerOffer()
        {
            _view.Bind(_source);

            _source.OpenStation(_station, Offer("Iron Sword"), Offer("Iron Plate"));

            Assert.IsTrue(_view.IsOpen);
            Assert.AreEqual(2, _view.Rows.Count);
            Assert.AreEqual("Iron Sword", _view.Rows[0].NameLabel.text);
            Assert.AreEqual("Iron Plate", _view.Rows[1].NameLabel.text);
        }

        [Test]
        public void Rows_ShowTheOutputCountAndDisableTheButtonWhenUnaffordable()
        {
            _view.Bind(_source);

            _source.OpenStation(
                _station,
                Offer("Iron Bars", outputCount: 3, ingredientsMet: true),
                Offer("Iron Plate", ingredientsMet: false));

            Assert.AreEqual("x3", _view.Rows[0].CountLabel.text);
            Assert.IsTrue(_view.Rows[0].CraftButton.interactable);
            Assert.IsFalse(_view.Rows[1].CraftButton.interactable,
                "An unaffordable row is shown but its craft button is dead.");
        }

        [Test]
        public void ClickingAnAffordableRow_ForwardsExactlyThatRecipeAndStation()
        {
            _view.Bind(_source);
            _source.OpenStation(_station, Offer("Iron Sword", ingredientsMet: true));

            _view.Rows[0].CraftButton.onClick.Invoke();

            Assert.AreEqual(1, _source.Crafts.Count);
            Assert.AreSame(_station, _source.Crafts[0].Station);
            Assert.AreEqual("Iron Sword", _source.Crafts[0].RecipeName);
        }

        [Test]
        public void ClickingAnUnaffordableRow_SendsNothing()
        {
            _view.Bind(_source);
            _source.OpenStation(_station, Offer("Iron Plate", ingredientsMet: false));

            _view.HandleRowClicked(0);

            Assert.IsEmpty(_source.Crafts, "The grey-out is a courtesy; nothing is sent.");
        }

        [Test]
        public void StationOpened_ThenANewPayload_RepaintsFromTheSourcesPayload()
        {
            _countingObject = new GameObject("CountingPanel");
            CountingPanel counting = _countingObject.AddComponent<CountingPanel>();
            counting.SetContainer(_view.RowContainer);
            counting.Bind(_source);

            _source.OpenStation(_station, Offer("Iron Sword"));
            int afterOpen = counting.Repaints;

            _source.OpenStation(_station, Offer("Iron Sword"), Offer("Healing Potion"));

            Assert.Greater(counting.Repaints, afterOpen, "A new payload must trigger a repaint.");
            Assert.AreEqual(2, counting.Rows.Count);
        }

        [Test]
        public void RecipesChanged_TriggersARepaint()
        {
            _countingObject = new GameObject("CountingPanel");
            CountingPanel counting = _countingObject.AddComponent<CountingPanel>();
            counting.Bind(_source);
            _source.OpenStation(_station, Offer("Iron Sword"));
            int before = counting.Repaints;

            _source.RaiseRecipesChanged();

            Assert.Greater(counting.Repaints, before,
                "A level change invalidates what the panel shows without a new payload.");
        }

        [Test]
        public void EmptyPayload_ShowsNoRows()
        {
            _view.Bind(_source);

            _source.OpenStation(_station);

            Assert.IsTrue(_view.IsOpen);
            Assert.IsEmpty(_view.Rows, "A station with nothing to offer still opens — empty.");
        }

        [Test]
        public void Conceal_HidesThePanelAndForgetsTheStation()
        {
            _view.Bind(_source);
            _source.OpenStation(_station, Offer("Iron Sword"));

            _view.Conceal();

            Assert.IsFalse(_view.IsOpen);
            Assert.IsEmpty(_view.Rows);
            Assert.IsNull(_view.Station);
        }

        [Test]
        public void Unbind_StopsRenderingFurtherPayloads()
        {
            _view.Bind(_source);
            _source.OpenStation(_station, Offer("Iron Sword"));

            _view.Unbind();
            _source.OpenStation(_station, Offer("Iron Sword"), Offer("Healing Potion"));

            Assert.AreEqual(1, _view.Rows.Count, "A detached panel must not repaint.");
        }

        [Test]
        public void Bind_ToASourceThatAlreadyHasAnOpenStation_ShowsIt()
        {
            _source.OpenStation(_station, Offer("Iron Sword"));

            _view.Bind(_source);

            Assert.IsTrue(_view.IsOpen, "Re-binding a live source must restore what it holds.");
            Assert.AreEqual(1, _view.Rows.Count);
        }

        [Test]
        public void Title_FollowsTheStationType()
        {
            _view.Bind(_source);

            _source.OpenStation(_station, Offer("Iron Sword"));

            Assert.AreEqual(CraftingStationType.None.ToString(), _view.TitleLabel.text,
                "The header is the station actually open, not the factory's placeholder.");
        }

        [Test]
        public void Factory_BuildsAConcealedPanelWithACloseButton()
        {
            var root = new GameObject("Hud", typeof(RectTransform));
            try
            {
                CraftingPanelView panel = CraftingPanelFactory.Create(root.transform);

                Assert.IsFalse(panel.IsOpen, "A fresh panel has no station and must not show.");
                Assert.IsNotNull(panel.RowContainer, "The factory hands over the scroll content.");
                Assert.IsNotNull(panel.TitleLabel);

                Button close = panel.transform.Find("Close")?.GetComponent<Button>();
                Assert.IsNotNull(close, "The window needs a way out.");
                close.onClick.Invoke();
                Assert.IsFalse(panel.gameObject.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static RecipeOffer Offer(string recipeName, int outputCount = 1, bool ingredientsMet = true)
            => new RecipeOffer
            {
                RecipeName = recipeName,
                OutputCount = outputCount,
                IngredientsMet = ingredientsMet,
                LevelMet = true,
            };

        private sealed class FakeCraftingSource : ICraftingSource
        {
            public readonly List<(CraftingStation Station, string RecipeName)> Crafts = new();

            public int CraftingXp => 0;
            public int CraftingLevel => 1;
            public CraftingStation OpenedStation { get; private set; }
            public IReadOnlyList<RecipeOffer> AvailableRecipes { get; private set; } = Array.Empty<RecipeOffer>();

            public event Action RecipesChanged;
            public event Action<CraftingStation, IReadOnlyList<RecipeOffer>> StationOpened;

            public void OpenStation(CraftingStation station, params RecipeOffer[] offers)
            {
                OpenedStation = station;
                AvailableRecipes = offers ?? Array.Empty<RecipeOffer>();
                StationOpened?.Invoke(station, AvailableRecipes);
            }

            public void RaiseRecipesChanged() => RecipesChanged?.Invoke();

            public void RequestCraft(CraftingStation station, string recipeName)
                => Crafts.Add((station, recipeName));
        }

        /// <summary>Counts repaints so a test can prove they only follow bind/change events.</summary>
        private sealed class CountingPanel : CraftingPanelView
        {
            public int Repaints { get; private set; }

            public override void Repaint()
            {
                Repaints++;
                base.Repaint();
            }
        }
    }
}
