using System.Linq;
using MMO.Inventory;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The explicit equipment slots the issue demands — Main Hand, Off Hand, Head, Chest, Feet —
    /// as a countable, checkable list. The enum itself happily accepts any cast
    /// (<c>(EquipmentSlot)99</c>), so this is where "five slots, no others" stops being a
    /// convention and starts being an assertion every server-side path relies on.
    /// </summary>
    public sealed class EquipmentSlotTests
    {
        [Test]
        public void EquipmentSlot_Count_IsFive()
        {
            Assert.AreEqual(5, EquipmentSlots.Count,
                "The issue specifies exactly five slots: Main Hand, Off Hand, Head, Chest, Feet.");
        }

        [Test]
        public void EquipmentSlot_Values_RunFromZeroToFour()
        {
            // Explicit values keep the equipment SyncList indexable across sessions and
            // code versions; inserting a slot in the middle would silently re-map every item.
            Assert.AreEqual(0, (int)EquipmentSlot.MainHand);
            Assert.AreEqual(1, (int)EquipmentSlot.OffHand);
            Assert.AreEqual(2, (int)EquipmentSlot.Head);
            Assert.AreEqual(3, (int)EquipmentSlot.Chest);
            Assert.AreEqual(4, (int)EquipmentSlot.Feet);
        }

        [Test]
        public void EquipmentSlots_All_ListsEverySlotExactlyOnce()
        {
            EquipmentSlot[] expected =
            {
                EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Head,
                EquipmentSlot.Chest, EquipmentSlot.Feet,
            };

            Assert.AreEqual(EquipmentSlots.Count, EquipmentSlots.All.Count);
            CollectionAssert.AreEqual(expected, EquipmentSlots.All.ToArray());
        }

        [Test]
        public void EquipmentSlots_IsValid_AcceptsEveryDeclaredSlot()
        {
            foreach (EquipmentSlot slot in EquipmentSlots.All)
                Assert.IsTrue(EquipmentSlots.IsValid(slot), $"{slot} is a declared slot and must validate.");
        }

        [Test]
        public void EquipmentSlots_IsValid_RejectsOutOfRangeCasts()
        {
            // The classic client payload: an integer cast straight into the enum. Rejected here,
            // it never reaches an index; accepted, it would read past the equipment list.
            Assert.IsFalse(EquipmentSlots.IsValid((EquipmentSlot)5), "One past the last slot must fail.");
            Assert.IsFalse(EquipmentSlots.IsValid((EquipmentSlot)99), "Garbage must fail.");
            Assert.IsFalse(EquipmentSlots.IsValid((EquipmentSlot)(-1)), "Negative casts must fail.");
        }
    }
}
