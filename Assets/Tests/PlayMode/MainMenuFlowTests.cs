using System.Collections;
using FishNet.Managing;
using MMO.Bootstrap;
using MMO.Net;
using MMO.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// The Main Menu scene's glue, end to end: a bootstrap wired to the real (local) transport, a
    /// real code-built view, and a fake backend. Typing into the fields and pressing Sign In must
    /// pull the fuse: authenticate, aim the transport at the zone, connect, record it, and paint
    /// the status line — with the form locked while it runs and unlocked when it's over.
    /// </summary>
    public sealed class MainMenuFlowTests
    {
        private NetworkManager _manager;
        private GameObject _bootstrapGo;
        private LoginView _view;

        [UnitySetUp]
        public IEnumerator EnsureManagerReady()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            // The view is a canvas root of its own; the bootstrap only references it.
            if (_bootstrapGo != null)
                Object.Destroy(_bootstrapGo);
            if (_view != null)
                Object.Destroy(_view.gameObject);

            // The manager is shared suite-wide; leave the join client token-free for the next test.
            _manager?.gameObject.GetComponent<JoinRequestClient>()?.SetAuthToken(null);
            NetworkManagerTestHelper.StopIfListening(_manager);
        }

        [UnityTearDown]
        public IEnumerator DrainTeardown()
        {
            yield return null;
        }

        private MainMenuBootstrap NewBootstrap(FakeApiGateway gateway)
        {
            _bootstrapGo = new GameObject("MenuBootstrapUnderTest");
            MainMenuBootstrap bootstrap = _bootstrapGo.AddComponent<MainMenuBootstrap>();
            bootstrap.SetNetworkManager(_manager);
            bootstrap.SetDefaultZone("127.0.0.1", NetworkManagerTestHelper.TestPort);
            _view = MainMenuFactory.Create(createEventSystem: false);
            bootstrap.Initialize(_view, gateway);
            return bootstrap;
        }

        [UnityTest]
        public IEnumerator SignIn_LogsInConnectsAndPaintsTheStatusLine()
        {
            Assert.IsTrue(_manager.ServerManager.StartConnection(), "Zone server failed to start.");
            yield return null;

            var gateway = new FakeApiGateway();
            MainMenuBootstrap bootstrap = NewBootstrap(gateway);
            LoginView view = bootstrap.View;

            view.UsernameField.text = "Bob";
            view.PasswordField.text = "password123";
            view.Submit();

            yield return new WaitUntil(() => view.StatusText.text.StartsWith("Connected to"));

            Assert.AreEqual("Bob", gateway.LastLoginUsername);
            Assert.AreEqual(1, gateway.SaveCalls);
            Assert.AreEqual(NetworkManagerTestHelper.TestPort, gateway.SavedZone.Port);
            Assert.IsTrue(_manager.IsClientStarted, "The bootstrapped flow must admit the client.");
            Assert.IsTrue(view.LoginButton.interactable, "A finished login must unlock the form.");
        }

        [UnityTest]
        public IEnumerator SignIn_WithNoZoneServer_ShowsTheUnreachableMessage()
        {
            var gateway = new FakeApiGateway();
            MainMenuBootstrap bootstrap = NewBootstrap(gateway);
            LoginView view = bootstrap.View;

            view.UsernameField.text = "Bob";
            view.PasswordField.text = "password123";
            view.Submit();

            yield return new WaitUntil(() => view.StatusText.text.StartsWith("Could not reach"));

            Assert.AreEqual("Could not reach the zone server. Try again.", view.StatusText.text);
            Assert.AreEqual(0, gateway.SaveCalls);
            Assert.IsTrue(view.LoginButton.interactable);
        }

        [UnityTest]
        public IEnumerator SignIn_LocksTheFormWhileRunning_AndUnlocksItWhenDone()
        {
            Assert.IsTrue(_manager.ServerManager.StartConnection());
            yield return null;

            var gateway = new FakeApiGateway();
            MainMenuBootstrap bootstrap = NewBootstrap(gateway);
            LoginView view = bootstrap.View;

            view.UsernameField.text = "Bob";
            view.PasswordField.text = "password123";
            view.Submit();

            yield return new WaitUntil(() => view.StatusText.text == "Signing in...");
            Assert.IsFalse(view.LoginButton.interactable, "The form must lock while the flow runs.");

            yield return new WaitUntil(() => view.StatusText.text.StartsWith("Connected to"));
            Assert.IsTrue(view.LoginButton.interactable, "A finished login must unlock the form.");
            Assert.IsTrue(_manager.IsClientStarted);
        }

        [UnityTest]
        public IEnumerator SignIn_WithBadCredentials_StaysOfflineWithTheError()
        {
            var gateway = new FakeApiGateway();
            gateway.OnLogin = (_, _, _) => FakeApiGateway.Failure("Invalid credentials.");

            MainMenuBootstrap bootstrap = NewBootstrap(gateway);
            LoginView view = bootstrap.View;

            view.UsernameField.text = "Bob";
            view.PasswordField.text = "password123";
            view.Submit();

            yield return new WaitUntil(() => !string.IsNullOrEmpty(view.StatusText.text));

            Assert.AreEqual("Invalid credentials.", view.StatusText.text);
            Assert.IsTrue(_manager.IsOffline, "A failed sign-in must never open a connection.");
            Assert.IsTrue(view.LoginButton.interactable);
        }
    }
}