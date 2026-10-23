using System;
using System.Collections.Generic;
using MMO.Inventory;
using MMO.UI;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The paper-doll window: one row per declared slot, live labels from the equipment source,
    /// and a right-click that reaches the unequip intent. A plain fake source keeps this offline.
    /// </summary>
    public sealed class EquipmentPanelViewTests
    {
        private sealed class FakeEquipment : IEquipmentSource
        {
            public readonly Dictionary<EquipmentSlot, ItemInstance> Worn = new();
            public int UnequipRequests { get; private set; }
            public EquipmentSlot LastRequestedSlot { get; private set; }

            public event Action ContainersChanged;

            public ItemInstance GetEquipped(EquipmentSlot slot)
                => Worn.TryGetValue(slot, out ItemInstance item) ? item : ItemInstance.Empty;

            public void RequestUnequip(EquipmentSlot slot)
            {
                UnequipRequests++;
                LastRequestedSlot = slot;
            }

            public void Raise() => ContainersChanged?.Invoke();
        }

        private readonly List<GameObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        [Test]
        public void Bind_BuildsOneRowPerDeclaredSlot()
        {
            EquipmentPanelView panel = NewPanel();

            panel.Bind(new FakeEquipment());

            Assert.AreEqual(EquipmentSlots.Count, panel.Rows.Count,
                "Every wearable slot must be visible on the paper doll.");
        }

        [Test]
        public void Repaint_ShowsWornItems_WhenTheSourceChanges()
        {
            var source = new FakeEquipment();
            EquipmentPanelView panel = NewPanel();
            panel.Bind(source);

            source.Worn[EquipmentSlot.Mount] = new ItemInstance { ItemName = "Donkey" };
            source.Raise();

            EquipmentPanelView.Row mount = FindRow(panel, EquipmentSlot.Mount);
            StringAssert.Contains("Donkey", mount.Label.text);
            StringAssert.Contains("Mount", mount.Label.text);
        }

        [Test]
        public void RightClickingARow_RequestsUnequipForThatSlot()
        {
            var source = new FakeEquipment();
            EquipmentPanelView panel = NewPanel();
            panel.Bind(source);

            FindRow(panel, EquipmentSlot.Mount).View.HandleRightClick();

            Assert.AreEqual(1, source.UnequipRequests);
            Assert.AreEqual(EquipmentSlot.Mount, source.LastRequestedSlot);
        }

        [Test]
        public void Unbind_StopsListeningToTheSource()
        {
            var source = new FakeEquipment();
            EquipmentPanelView panel = NewPanel();
            panel.Bind(source);

            panel.Unbind();
            source.Worn[EquipmentSlot.Mount] = new ItemInstance { ItemName = "Donkey" };

            Assert.DoesNotThrow(source.Raise, "An unbound panel must not repaint from a dead source.");
            Assert.IsNull(panel.Source);
        }

        private EquipmentPanelView NewPanel()
        {
            var go = new GameObject("EquipmentPanel", typeof(RectTransform));
            _created.Add(go);
            EquipmentPanelView panel = go.AddComponent<EquipmentPanelView>();
            panel.SetContainer((RectTransform)go.transform);
            return panel;
        }

        private static EquipmentPanelView.Row FindRow(EquipmentPanelView panel, EquipmentSlot slot)
        {
            for (int i = 0; i < panel.Rows.Count; i++)
            {
                if (panel.Rows[i].Slot == slot)
                    return panel.Rows[i];
            }

            Assert.Fail($"No row was built for {slot}.");
            return null;
        }
    }
}
