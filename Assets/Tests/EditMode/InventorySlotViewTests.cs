using System.Collections.Generic;
using System.Reflection;
using MMO.Inventory;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MMO.Tests
{
    /// <summary>
    /// The right-click-to-equip input path at the view layer. The spy subclass counts how often
    /// uGUI's pointer dispatch actually reaches the handler, which is the only way to prove the
    /// button filter without a live network round trip — the equip itself is PlayMode's job.
    /// </summary>
    public sealed class InventorySlotViewTests
    {
        private sealed class SpyInventorySlotView : InventorySlotView
        {
            public int RightClicksHandled { get; private set; }

            public override void HandleRightClick() => RightClicksHandled++;
        }

        private readonly List<GameObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        [Test]
        public void InventorySlotView_ListensForPointerClicks()
        {
            Assert.IsTrue(typeof(IPointerClickHandler).IsAssignableFrom(typeof(InventorySlotView)),
                "uGUI only dispatches clicks to components implementing this interface.");
        }

        [Test]
        public void InventorySlotView_CarriesTheComponentMenuAndSingleInstanceGuards()
        {
            Assert.IsNotNull(typeof(InventorySlotView).GetCustomAttribute<DisallowMultipleComponent>(),
                "One view per cell; a second would double-send every click.");
            Assert.IsNotNull(typeof(InventorySlotView).GetCustomAttribute<AddComponentMenu>());
        }

        [Test]
        public void RightClick_ReachesTheHandler()
        {
            SpyInventorySlotView view = CreateSpy();

            view.OnPointerClick(Click(PointerEventData.InputButton.Right));

            Assert.AreEqual(1, view.RightClicksHandled,
                "A right-click is the issue's 'equip' gesture and must reach the request path.");
        }

        [Test]
        public void LeftClick_IsIgnored()
        {
            SpyInventorySlotView view = CreateSpy();

            view.OnPointerClick(Click(PointerEventData.InputButton.Left));

            Assert.AreEqual(0, view.RightClicksHandled,
                "Left-click stays free for selection or drag; only the right button equips.");
        }

        [Test]
        public void NullPointerEvent_DoesNotThrow()
        {
            // uGUI can hand a null event in synthetic dispatch; a view that NREs would kill the
            // whole click pipeline for the screen.
            SpyInventorySlotView view = CreateSpy();

            Assert.DoesNotThrow(() => view.OnPointerClick(null));
            Assert.AreEqual(0, view.RightClicksHandled);
        }

        [Test]
        public void Setters_RoundTripThroughTheProperties()
        {
            InventorySlotView view = Create<InventorySlotView>();
            var inventoryGo = new GameObject("InventoryUnderTest");
            _created.Add(inventoryGo);
            PlayerInventory inventory = inventoryGo.AddComponent<PlayerInventory>();

            view.SetInventory(inventory);
            view.SetCell(3, 4);

            Assert.AreSame(inventory, view.Inventory);
            Assert.AreEqual(3, view.GridX);
            Assert.AreEqual(4, view.GridY);
        }

        [Test]
        public void Handler_WithoutAnInventory_IsANoOp()
        {
            // The view can legitimately exist before its screen wires it (pooling, a late
            // bootstrap); an unwired click must not throw.
            InventorySlotView view = Create<InventorySlotView>();

            Assert.DoesNotThrow(view.HandleRightClick);
        }

        private SpyInventorySlotView CreateSpy() => Create<SpyInventorySlotView>();

        private T Create<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            _created.Add(go);
            return go.AddComponent<T>();
        }

        private static PointerEventData Click(PointerEventData.InputButton button)
        {
            return new PointerEventData(null) { button = button };
        }
    }
}
