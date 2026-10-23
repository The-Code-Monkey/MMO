using System.Collections;
using FishNet.Managing;
using FishNet.Object;
using MMO.Client;
using MMO.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The HUD is built by the player prefab, not the scene: nothing is committed that references
    /// the HUD, so the only thing that can bring one into being is the owning client acting on the
    /// <see cref="LocalPlayerHud"/> script it received with its player.
    ///
    /// <para>
    /// Every player object exists on every client, so the only thing separating "my screen" from
    /// "somebody else's" is FishNet's <c>IsOwner</c>. These tests run against a real host going
    /// through a real handshake and assert the state of the actual <see cref="NetworkHud"/>, because
    /// a screen built for the wrong player silently shows the wrong stats.
    /// </para>
    ///
    /// <para>
    /// FishNet allows one NetworkManager per process, so a genuine second client cannot join this
    /// test run. "A player this client does not own" is staged the way a remote player's copy of the
    /// prefab actually looks here: spawned and alive, with ownership handed away.
    /// </para>
    /// </summary>
    public sealed class LocalPlayerHudTests
    {
        private const int MaxWaitFrames = 300;

        private NetworkManager _manager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();

            // A HUD from a previous test may have survived a lesion in teardown. Start from a scene
            // with none so "a HUD was built" means this test caused it.
            DestroyAnyHud();

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);
            DestroyAnyHud();

            yield return null;
        }

        private static void DestroyAnyHud()
        {
            foreach (NetworkHud hud in Object.FindObjectsByType<NetworkHud>(FindObjectsSortMode.None))
            {
                if (hud != null)
                    Object.Destroy(hud.gameObject);
            }
        }

        /// <summary>Waits until <paramref name="condition"/> holds, so a frame count is not the test.</summary>
        private static IEnumerator WaitUntil(System.Func<bool> condition)
        {
            for (int i = 0; i < MaxWaitFrames && !condition(); i++)
                yield return null;
        }

        [UnityTest]
        public IEnumerator ConnectingClient_ReceivesTheScriptThatBuildsTheHud()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player, "A connecting client must receive a player object.");

            Assert.IsNotNull(player.GetComponent<LocalPlayerHud>(),
                "The HUD script travels with the prefab, so a remote client's copy carries it too.");
        }

        [UnityTest]
        public IEnumerator OwnedPlayer_BuildsTheHudWithoutAnySceneSetup()
        {
            // No scene has been baked: nothing but the prefab itself can bring a HUD into being.
            Assert.IsNull(Object.FindFirstObjectByType<NetworkHud>(),
                "Precondition: the scene starts with no HUD.");

            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);
            yield return WaitUntil(() => Object.FindFirstObjectByType<NetworkHud>() != null);

            Assert.IsNotNull(Object.FindFirstObjectByType<NetworkHud>(),
                "The client needs a screen, and nothing else is going to build it.");
        }

        [UnityTest]
        public IEnumerator OwnedPlayer_BindsTheFullHudAndItsCraftingPanel()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            LocalPlayerHud behaviour = player.GetComponent<LocalPlayerHud>();
            Assert.IsNotNull(behaviour);
            yield return WaitUntil(() => behaviour.Hud != null);

            NetworkHud hud = behaviour.Hud;
            Assert.IsNotNull(hud, "We own this player, so the HUD is ours to build.");

            Assert.IsNotNull(hud.PlayerHud, "The vitals bars are bound to our own PlayerStats.");
            Assert.IsNotNull(hud.InventoryGrid, "The bag grid is bound to our own PlayerInventory.");
            Assert.IsNotNull(hud.ActionBar, "The action bar is bound to our own PlayerAbilities.");
            Assert.IsNotNull(hud.EquipmentPanel, "The paper doll ships with the HUD.");
            Assert.IsNotNull(hud.InventoryWindow, "The bag window collapses to its icon.");
            Assert.IsNotNull(hud.EquipmentWindow, "The equipment window collapses to its icon.");
            Assert.IsNotNull(hud.MountCastBar, "The mount progress bar ships with the HUD.");
            Assert.IsFalse(hud.MountCastBar.gameObject.activeSelf,
                "The mount bar stays hidden until a cast starts.");
            Assert.IsFalse(hud.InventoryWindow.IsExpanded, "The bag starts collapsed.");
            Assert.IsFalse(hud.EquipmentWindow.IsExpanded, "The equipment window starts collapsed.");
            Assert.IsNotNull(hud.CraftingPanel, "The crafting window ships with the HUD, concealed.");
            Assert.IsFalse(hud.CraftingPanel.IsOpen,
                "The crafting panel stays hidden until the server sends this player an offer.");
            Assert.IsNotNull(hud.DialogueView, "The dialogue panel ships with the HUD, concealed.");
            Assert.IsFalse(hud.DialogueView.IsOpen,
                "The dialogue panel stays hidden until the server sends this player a node.");
        }

        [UnityTest]
        public IEnumerator APlayerThisClientDoesNotOwn_NeverBuildsAHud()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);

            LocalPlayerHud behaviour = player.GetComponent<LocalPlayerHud>();
            Assert.IsNotNull(behaviour);
            Assert.IsNotNull(behaviour.TryBuildHud(), "Precondition: an owned player does build a HUD.");

            // Hand ownership away on the server. This is the state a remote player's copy of the
            // prefab is in on this client: spawned, alive, and not ours.
            player.RemoveOwnership();
            yield return WaitUntil(() => !behaviour.IsOwner);
            Assert.IsFalse(behaviour.IsOwner, "Precondition: this player is no longer ours.");

            yield return WaitUntil(() => Object.FindFirstObjectByType<NetworkHud>() == null);

            Assert.IsNull(behaviour.TryBuildHud(),
                "The guard is the whole feature: a player this client does not own must never build a HUD.");
            Assert.IsNull(Object.FindFirstObjectByType<NetworkHud>(),
                "Losing ownership has to take the HUD down with it.");
        }

        [UnityTest]
        public IEnumerator DespawningTheOwnedPlayer_DestroysTheHud()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            NetworkObject player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(player);
            Assert.IsNotNull(Object.FindFirstObjectByType<NetworkHud>(),
                "Precondition: we own this player, so a HUD is live.");

            NetworkManagerTestHelper.StopIfListening(_manager);
            yield return WaitUntil(() => Object.FindFirstObjectByType<NetworkHud>() == null);

            Assert.IsNull(Object.FindFirstObjectByType<NetworkHud>(),
                "A disconnected client must not keep a stale HUD bound to destroyed components.");
            Assert.IsTrue(player == null, "Unity's == is true for a destroyed object; the player is gone.");
        }
    }
}
