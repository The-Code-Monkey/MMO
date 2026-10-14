using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMO.Inventory;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The structural half of dupe protection, proven under concurrency. The sequential contract
    /// is pinned in <see cref="ItemOwnershipRegistryTests"/>; this suite fires a storm of
    /// simultaneous pickups, trades, and loots at the same instance id and asserts the invariant
    /// the rest of the server relies on: <em>a single physical item can never be held by two
    /// holders at once</em>, no matter how the operations interleave.
    ///
    /// Everything here is plain C# on real threads, so it runs in EditMode and is deterministic:
    /// the registry lock makes "exactly one winner per id" hold under every scheduling order, not
    /// merely a likely one. The cross-process half — two zone servers racing one item — is the
    /// backend's compare-and-swap / single transaction (#59) and is exercised against a live
    /// Nakama profile, not here.
    /// </summary>
    public sealed class ItemOwnershipRegistryStressTests
    {
        /// <summary>How many contenders race for one item in the single-item storms.</summary>
        private const int Contenders = 16;

        /// <summary>How many distinct items are thrown into the multi-item double-spend storm.</summary>
        private const int StormItems = 32;

        private ItemOwnershipRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new ItemOwnershipRegistry();
        }

        /// <summary>
        /// A loot/pickup storm: everyone grabs the same dropped id at once. Exactly one player may
        /// pick it up; every other grab is the duplicate the server must refuse.
        /// </summary>
        [Test]
        public void ConcurrentPickups_OfOneInstance_ExactlyOneHolderWins()
        {
            const string id = "loot-sword";
            string[] looters = Holders("looter", Contenders);

            OwnershipMove[] outcomes = Race(looters, looter => _registry.Grant(id, looter));

            int winners = AssertOneWinner(id, outcomes, looters, OwnershipMove.DuplicateDetected);
            Assert.AreEqual(1, _registry.Count, "Exactly one item should be tracked.");
            StringAssert.StartsWith("looter-", looters[winners]);
        }

        /// <summary>
        /// The same pickup storm, but over ids minted by the real P2 identity path
        /// (<see cref="ItemInstance.NewInstanceId"/>) rather than hand-written strings. Every
        /// minted id is unique, so each item resolves to exactly one holder under contention.
        /// </summary>
        [Test]
        public void ConcurrentPickups_OfRealInstanceIds_EachIdPickedUpOnce()
        {
            string[] ids = Enumerable.Range(0, StormItems)
                .Select(_ => ItemInstance.NewInstanceId())
                .ToArray();
            Assert.AreEqual(ids.Length, ids.Distinct().Count(), "Minted instance ids must be unique.");

            var bags = new ConcurrentDictionary<string, ConcurrentBag<string>>();

            foreach (string id in ids)
            {
                string[] looters = Holders("looter", Contenders);
                OwnershipMove[] outcomes = Race(looters, looter => _registry.Grant(id, looter));

                int winner = AssertOneWinner(id, outcomes, looters, OwnershipMove.DuplicateDetected);
                bags.GetOrAdd(looters[winner], _ => new ConcurrentBag<string>()).Add(id);
            }

            Assert.AreEqual(ids.Length, _registry.Count, "Every dropped item must be tracked exactly once.");

            // The issue's check: no instance id may appear in two holders' records.
            foreach (string id in ids)
            {
                int owners = bags.Values.Count(bag => bag.Contains(id));
                Assert.AreEqual(1, owners, $"{id} appeared in {owners} looters' bags; it must be picked up once.");
            }
        }

        /// <summary>
        /// A trade storm: many buyers accept the same seller's offer at once. Exactly one trade
        /// settles; the rest are refused because the seller no longer holds the item — the dupe.
        /// </summary>
        [Test]
        public void ConcurrentTrades_OfOneInstance_ExactlyOneBuyerWins()
        {
            const string id = "trade-sword";

            for (int round = 0; round < 100; round++)
            {
                _registry.Clear();
                _registry.Grant(id, "seller");
                string[] buyers = Holders("buyer", Contenders);

                OwnershipMove[] outcomes = Race(buyers, buyer => _registry.Transfer(id, "seller", buyer));

                AssertOneWinner(id, outcomes, buyers, OwnershipMove.NotHeld);
                Assert.AreEqual(1, _registry.Count, $"Round {round}: exactly one item should be tracked.");
            }
        }

        /// <summary>
        /// A double-spend across many listings at once: two buyers race for each of
        /// <see cref="StormItems"/> items simultaneously. Afterwards no instance id may appear in
        /// two buyers' inventories, and the registry's owner must agree with the winner's record.
        /// </summary>
        [Test]
        public void ConcurrentDoubleSpend_NoInstanceAppearsInTwoInventories()
        {
            string[] items = Enumerable.Range(0, StormItems).Select(i => $"listing-{i}").ToArray();
            foreach (string item in items)
                _registry.Grant(item, Seller(item));

            var bought = new ConcurrentDictionary<string, ConcurrentBag<string>>();
            var winners = new ConcurrentDictionary<string, string>();

            int participants = items.Length * 2;
            using var start = new Barrier(participants);

            var tasks = new Task[participants];
            for (int p = 0; p < participants; p++)
            {
                int index = p;
                tasks[p] = Task.Factory.StartNew(() =>
                {
                    string item = items[index / 2];
                    string buyer = $"buyer-{index % 2}-of-{item}";

                    start.SignalAndWait();

                    if (_registry.Transfer(item, Seller(item), buyer) == OwnershipMove.Ok)
                    {
                        bought.GetOrAdd(buyer, _ => new ConcurrentBag<string>()).Add(item);
                        winners[item] = buyer;
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }

            Task.WaitAll(tasks);

            foreach (string item in items)
            {
                Assert.IsTrue(winners.TryGetValue(item, out string winner),
                    $"{item} was not sold; exactly one buyer must win the race.");
                Assert.IsTrue(_registry.TryGetOwner(item, out string owner), $"{item} lost its owner.");
                Assert.AreEqual(winner, owner, $"{item}'s registry owner must be the winning buyer.");
                Assert.IsTrue(_registry.IsHeldBy(item, winner));

                int appearances = bought.Values.Count(bag => bag.Contains(item));
                Assert.AreEqual(1, appearances,
                    $"{item} appeared in {appearances} inventories; an instance id must be held once.");
            }
        }

        /// <summary>
        /// Only the current holder may release an item. A crowd of non-holders cannot erase it, and
        /// the moment the true owner releases it the count drops — a non-owner never can.
        /// </summary>
        [Test]
        public void ConcurrentReleases_OnlyTheOwnerCanEraseTheItem()
        {
            const string id = "bank-sword";
            _registry.Grant(id, "alice");
            string[] mallorys = Holders("mallory", Contenders);

            OwnershipMove[] outcomes = Race(mallorys, mallory => _registry.Release(id, mallory));

            Assert.IsTrue(outcomes.All(o => o == OwnershipMove.NotHeld),
                "A non-holder must never be able to release someone else's item.");
            Assert.IsTrue(_registry.IsHeldBy(id, "alice"));

            Assert.AreEqual(OwnershipMove.Ok, _registry.Release(id, "alice"));
            Assert.AreEqual(0, _registry.Count);
        }

        /// <summary>
        /// A mixed soak: threads interleave grants, transfers, and releases over a small shared id
        /// space. No operation may throw, and once the smoke clears the registry must still be
        /// internally consistent — every tracked id has exactly one corroborated owner.
        /// </summary>
        [Test]
        public void MixedWorkload_LeavesTheRegistryConsistent()
        {
            const int idSpace = 16;
            const int holders = 8;
            const int operations = 20000;

            string[] ids = Enumerable.Range(0, idSpace).Select(i => $"mix-{i}").ToArray();

            Parallel.For(0, operations, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                string id = ids[i % idSpace];
                string holder = $"holder-{i % holders}";

                switch (i % 4)
                {
                    case 0:
                    case 1:
                        _registry.Grant(id, holder);
                        break;
                    case 2:
                        _registry.Transfer(id, $"holder-{(i + 1) % holders}", holder);
                        break;
                    default:
                        _registry.Release(id, holder);
                        break;
                }
            });

            int tracked = 0;
            foreach (string id in ids)
            {
                if (!_registry.TryGetOwner(id, out string owner))
                    continue;

                tracked++;
                Assert.IsTrue(_registry.IsHeldBy(id, owner), $"{id}'s reported owner {owner} must hold it.");

                int claimants = Enumerable.Range(0, holders)
                    .Count(h => _registry.IsHeldBy(id, $"holder-{h}"));
                Assert.AreEqual(1, claimants, $"{id} must have exactly one holder.");
            }

            Assert.AreEqual(tracked, _registry.Count, "Count must equal the number of tracked ids.");
        }

        /// <summary>
        /// Releases every contender onto its own operation at the same instant and returns each
        /// contender's outcome, aligned with <paramref name="contenders"/> by index.
        /// </summary>
        private static OwnershipMove[] Race(string[] contenders, Func<string, OwnershipMove> operation)
        {
            var outcomes = new OwnershipMove[contenders.Length];
            using var start = new Barrier(contenders.Length);

            var tasks = new Task[contenders.Length];
            for (int c = 0; c < contenders.Length; c++)
            {
                int index = c;
                tasks[c] = Task.Factory.StartNew(() =>
                {
                    start.SignalAndWait();
                    outcomes[index] = operation(contenders[index]);
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }

            Task.WaitAll(tasks);
            return outcomes;
        }

        /// <summary>
        /// Asserts exactly one contender won (the sole <see cref="OwnershipMove.Ok"/>), everyone
        /// else was refused with <paramref name="loserOutcome"/>, and only the winner holds the id.
        /// Returns the winning index.
        /// </summary>
        private int AssertOneWinner(string id, OwnershipMove[] outcomes, string[] contenders, OwnershipMove loserOutcome)
        {
            Assert.AreEqual(1, outcomes.Count(o => o == OwnershipMove.Ok),
                $"{id}: exactly one contender may win.");

            int winner = Array.IndexOf(outcomes, OwnershipMove.Ok);
            for (int i = 0; i < outcomes.Length; i++)
            {
                if (i == winner)
                    continue;

                Assert.AreEqual(loserOutcome, outcomes[i], $"{id}: contender {i} was refused unexpectedly.");
                Assert.IsFalse(_registry.IsHeldBy(id, contenders[i]),
                    $"{id}: losing contender {contenders[i]} must not hold the item.");
            }

            Assert.IsTrue(_registry.IsHeldBy(id, contenders[winner]),
                $"{id}: the winner {contenders[winner]} must hold the item.");
            return winner;
        }

        private static string[] Holders(string prefix, int count)
            => Enumerable.Range(0, count).Select(i => $"{prefix}-{i}").ToArray();

        private static string Seller(string item) => $"seller-of-{item}";
    }
}
