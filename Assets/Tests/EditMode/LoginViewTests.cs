using MMO.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.Tests
{
    /// <summary>
    /// The code-built login form. The view's whole job is one decision point: whatever submits the
    /// form (the button, a test, an enter key later) must reach <see cref="LoginView.LoginSubmitted"/>
    /// exactly once with the raw field values, and the busy/status calls must paint the controls
    /// the factory gave it.
    /// </summary>
    public sealed class LoginViewTests
    {
        private GameObject _root;
        private LoginView _view;
        private int _submits;
        private string _lastUsername;
        private string _lastPassword;

        [SetUp]
        public void SetUp()
        {
            _submits = 0;
            _lastUsername = null;
            _lastPassword = null;

            _view = MainMenuFactory.Create(createEventSystem: false);
            _root = _view.gameObject;
            _view.LoginSubmitted += OnLoginSubmitted;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                Object.DestroyImmediate(_root);
        }

        private void OnLoginSubmitted(string username, string password)
        {
            _submits++;
            _lastUsername = username;
            _lastPassword = password;
        }

        [Test]
        public void Create_WiresEveryControl()
        {
            Assert.IsNotNull(_view.UsernameField);
            Assert.IsNotNull(_view.PasswordField);
            Assert.IsNotNull(_view.LoginButton);
            Assert.IsNotNull(_view.StatusText);
        }

        [Test]
        public void Create_StartsWithAnEmptyStatusLine()
        {
            Assert.IsNotNull(_view.StatusText);
            Assert.AreEqual(string.Empty, _view.StatusText.text);
        }

        [Test]
        public void Submit_RaisesLoginSubmittedWithTheRawEnteredValues()
        {
            _view.UsernameField.text = "Bob";
            _view.PasswordField.text = "hunter2hunter2";

            _view.Submit();

            Assert.AreEqual(1, _submits);
            Assert.AreEqual("Bob", _lastUsername);
            Assert.AreEqual("hunter2hunter2", _lastPassword);
        }

        [Test]
        public void LoginButton_DrivesTheSameSubmitPath()
        {
            _view.UsernameField.text = "Carol";

            _view.LoginButton.onClick.Invoke();

            Assert.AreEqual(1, _submits, "The button must go through Submit, not a parallel path.");
            Assert.AreEqual("Carol", _lastUsername);
            Assert.AreEqual(string.Empty, _lastPassword);
        }

        [Test]
        public void SetFields_CalledTwice_DoesNotStackDuplicateSubmits()
        {
            _view.SetFields(
                _view.UsernameField, _view.PasswordField, _view.LoginButton, _view.StatusText);

            _view.LoginButton.onClick.Invoke();

            Assert.AreEqual(1, _submits, "Re-wiring must replace the listener, never duplicate it.");
        }

        [Test]
        public void SetStatus_PaintsTheStatusLine()
        {
            _view.SetStatus("Connecting to 127.0.0.1:7770…");

            Assert.AreEqual("Connecting to 127.0.0.1:7770…", _view.StatusText.text);
        }

        [Test]
        public void SetStatus_NullClearsTheStatusLine()
        {
            _view.SetStatus("working");
            _view.SetStatus(null);

            Assert.AreEqual(string.Empty, _view.StatusText.text);
        }

        [Test]
        public void SetBusy_LocksAndUnlocksTheButton()
        {
            _view.SetBusy(true);
            Assert.IsFalse(_view.LoginButton.interactable);

            _view.SetBusy(false);
            Assert.IsTrue(_view.LoginButton.interactable);
        }

        [Test]
        public void Submit_WithNoListener_DoesNotThrow()
        {
            var fresh = MainMenuFactory.Create(createEventSystem: false);
            try
            {
                fresh.UsernameField.text = "Bob";
                fresh.Submit();
            }
            finally
            {
                Object.DestroyImmediate(fresh.gameObject);
            }
        }

        [Test]
        public void DestroyedView_RemovesItsListenerFromTheButton()
        {
            // A button that outlives the view (the factory's is destroyed with it, so re-wire onto
            // a standalone one): after the view dies, pressing it must not call back into it.
            var buttonGo = new GameObject("StandaloneButton");
            try
            {
                Button button = buttonGo.AddComponent<Button>();
                _view.SetFields(
                    _view.UsernameField, _view.PasswordField, button, _view.StatusText);

                Object.DestroyImmediate(_root);
                _root = null;

                button.onClick.Invoke();

                Assert.AreEqual(0, _submits, "A destroyed view must not stay wired to the button.");
            }
            finally
            {
                Object.DestroyImmediate(buttonGo);
            }
        }
    }
}