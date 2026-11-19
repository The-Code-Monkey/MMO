using System;
using System.Collections.Generic;
using MMO.Crafting;
using MMO.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The queue's save shape: the whole record must survive JsonUtility — the same path Nakama
    /// storage uses — so an order's deadline, state and escrow are intact after a transfer or a
    /// restart.
    /// </summary>
    public sealed class CraftQueuePersistenceTests
    {
        [Test]
        public void PlayerSaveRecord_JsonRoundTripsCraftingXpOrdersAndEscrow()
        {
            var record = new PlayerSaveRecord
            {
                UserId = "u-1",
                CraftingXp = 42,
                CraftOrders =
                {
                    new CraftOrder
                    {
                        OrderId = "order-1",
                        RecipeName = "Iron Ingot",
                        DueUtcTicks = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc).Ticks,
                        State = CraftOrderState.InProgress,
                        Escrow =
                        {
                            new CraftEscrowItem
                            {
                                ItemName = "Iron Ore",
                                InstanceId = "inst-1",
                                Width = 1,
                                Height = 2,
                                StackCount = 3,
                                Durability = 7,
                            },
                        },
                    },
                },
            };

            string json = JsonUtility.ToJson(record);
            PlayerSaveRecord loaded = JsonUtility.FromJson<PlayerSaveRecord>(json);

            Assert.AreEqual(6, loaded.SchemaVersion);
            Assert.AreEqual(42, loaded.CraftingXp);
            Assert.AreEqual(1, loaded.CraftOrders.Count);

            CraftOrder order = loaded.CraftOrders[0];
            Assert.AreEqual("order-1", order.OrderId);
            Assert.AreEqual("Iron Ingot", order.RecipeName);
            Assert.AreEqual(record.CraftOrders[0].DueUtcTicks, order.DueUtcTicks);
            Assert.AreEqual(CraftOrderState.InProgress, order.State);
            Assert.AreEqual(1, order.Escrow.Count);
            Assert.AreEqual("Iron Ore", order.Escrow[0].ItemName);
            Assert.AreEqual("inst-1", order.Escrow[0].InstanceId);
            Assert.AreEqual(2, order.Escrow[0].Height);
            Assert.AreEqual(3, order.Escrow[0].StackCount);
            Assert.AreEqual(7, order.Escrow[0].Durability);
        }

        [Test]
        public void LoadedRecordWithoutCraftOrders_StillLoads()
        {
            const string legacy = "{\"UserId\":\"u-1\",\"SchemaVersion\":5,\"Level\":3}";

            PlayerSaveRecord record = JsonUtility.FromJson<PlayerSaveRecord>(legacy);

            Assert.AreEqual(3, record.Level);
            Assert.IsEmpty(record.CraftOrders);
            Assert.AreEqual(0, record.CraftingXp);
        }
    }
}
