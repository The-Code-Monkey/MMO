using System.Linq;
using MMO.Inventory;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The explicit equipment slots — Main Hand, Off Hand, Head, Chest, Feet and Mount — as a
    /// countable, checkable list. The enum itself happily accepts any cast
    /// (<c>(EquipmentSlot)99</c>), so this is where "these are the slots" stops being a convention
    /// and starts being an assertion every server-side path relies on.
    /// </summary>
    public sealed class EquipmentSlotTests
    {
        [Test]
        public void EquipmentSlot_Count_IsSix()
        {
            Assert.AreEqual(6, EquipmentSlots.Count,
                "The five gear slots plus the mount slot the donkey is worn in.");
        }

        [Test]
        public void EquipmentSlot_Values_RunFromZeroToFive()
        {
            // Explicit values keep the equipment SyncList indexable across sessions and
            // code versions; inserting a slot in the middle would silently re-map every item.
            Assert.AreEqual(0, (int)EquipmentSlot.MainHand);
            Assert.AreEqual(1, (int)EquipmentSlot.OffHand);
            Assert.AreEqual(2, (int)EquipmentSlot.Head);
            Assert.AreEqual(3, (int)EquipmentSlot.Chest);
            Assert.AreEqual(4, (int)EquipmentSlot.Feet);
            Assert.AreEqual(5, (int)EquipmentSlot.Mount);
        }

        [Test]
        public void EquipmentSlots_All_ListsEverySlotExactlyOnce()
        {
            EquipmentSlot[] expected =
            {
                EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Head,
                EquipmentSlot.Chest, EquipmentSlot.Feet, EquipmentSlot.Mount,
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
        public void EquipmentSlots_IsValid_RejectsNoneAndOutOfRangeCasts()
        {
            // The classic client payload: an integer cast straight into the enum. Rejected here,
            // it never reaches an index; accepted, it would read past the equipment list.
            Assert.IsFalse(EquipmentSlots.IsValid(EquipmentSlot.None), "None is not a wearable slot.");
            Assert.IsFalse(EquipmentSlots.IsValid((EquipmentSlot)6), "One past the last slot must fail.");
            Assert.IsFalse(EquipmentSlots.IsValid((EquipmentSlot)99), "Garbage must fail.");
            Assert.IsFalse(EquipmentSlots.IsValid((EquipmentSlot)(-1)), "Negative casts must fail.");
        }
    }
}
