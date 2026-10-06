using System.Collections.Generic;
using System.Reflection;
using MMO.Inventory;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MMO.Tests
{
    /// <summary>
    /// The right-click-to-unequip input path on the paper-doll side, mirrored against the bag
    /// view's tests: button filter, null safety, and wiring — with the actual unequip left to
    /// the PlayMode suite where a server can answer.
    /// </summary>
    public sealed class EquipmentSlotViewTests
    {
        private sealed class SpyEquipmentSlotView : EquipmentSlotView
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
        public void EquipmentSlotView_ListensForPointerClicks()
        {
            Assert.IsTrue(typeof(IPointerClickHandler).IsAssignableFrom(typeof(EquipmentSlotView)),
                "uGUI only dispatches clicks to components implementing this interface.");
        }

        [Test]
        public void EquipmentSlotView_CarriesTheComponentMenuAndSingleInstanceGuards()
        {
            Assert.IsNotNull(typeof(EquipmentSlotView).GetCustomAttribute<DisallowMultipleComponent>(),
                "One view per slot; a second would double-send every click.");
            Assert.IsNotNull(typeof(EquipmentSlotView).GetCustomAttribute<AddComponentMenu>());
        }

        [Test]
        public void RightClick_ReachesTheHandler()
        {
            SpyEquipmentSlotView view = CreateSpy();

            view.OnPointerClick(Click(PointerEventData.InputButton.Right));

            Assert.AreEqual(1, view.RightClicksHandled,
                "A right-click on a worn item is the issue's 'unequip' gesture.");
        }

        [Test]
        public void LeftClick_IsIgnored()
        {
            SpyEquipmentSlotView view = CreateSpy();

            view.OnPointerClick(Click(PointerEventData.InputButton.Left));

            Assert.AreEqual(0, view.RightClicksHandled);
        }

        [Test]
        public void NullPointerEvent_DoesNotThrow()
        {
            SpyEquipmentSlotView view = CreateSpy();

            Assert.DoesNotThrow(() => view.OnPointerClick(null));
            Assert.AreEqual(0, view.RightClicksHandled);
        }

        [Test]
        public void Setters_RoundTripThroughTheProperties()
        {
            EquipmentSlotView view = Create<EquipmentSlotView>();
            var inventoryGo = new GameObject("InventoryUnderTest");
            _created.Add(inventoryGo);
            PlayerInventory inventory = inventoryGo.AddComponent<PlayerInventory>();

            view.SetInventory(inventory);
            view.SetSlot(EquipmentSlot.Feet);

            Assert.AreSame(inventory, view.Inventory);
            Assert.AreEqual(EquipmentSlot.Feet, view.Slot);
        }

        [Test]
        public void Handler_WithoutAnInventory_IsANoOp()
        {
            EquipmentSlotView view = Create<EquipmentSlotView>();

            Assert.DoesNotThrow(view.HandleRightClick);
        }

        [Test]
        public void DefaultSlot_IsMainHand()
        {
            // A freshly added view starts on the first slot; nothing silently unequips slot 99.
            EquipmentSlotView view = Create<EquipmentSlotView>();

            Assert.AreEqual(EquipmentSlot.MainHand, view.Slot);
        }

        private SpyEquipmentSlotView CreateSpy() => Create<SpyEquipmentSlotView>();

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
