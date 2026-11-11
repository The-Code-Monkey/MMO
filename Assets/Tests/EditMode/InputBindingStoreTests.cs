using System.IO;
using MMO.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MMO.Tests
{
    /// <summary>
    /// Bindings survive a restart through the Input System's override JSON. A missing or bad file
    /// must leave the code defaults in place.
    /// </summary>
    public sealed class InputBindingStoreTests
    {
        private string _directory;
        private string _path;
        private InputActionAsset _asset;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "mmo-bindings-tests-" + System.Guid.NewGuid().ToString("N"));
            _path = Path.Combine(_directory, InputBindingStore.FileName);
            _asset = GameInputActions.Create();
            _asset.Enable();
        }

        [TearDown]
        public void TearDown()
        {
            if (_asset != null)
                Object.DestroyImmediate(_asset);

            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        [Test]
        public void Save_ThenLoad_RestoresTheOverride()
        {
            InputAction ability = _asset.FindAction("Ability1");
            ability.ApplyBindingOverride(0, "<Keyboard>/q");

            var store = new InputBindingStore(_path);
            store.Save(_asset);

            InputActionAsset reloaded = GameInputActions.Create();
            try
            {
                store.LoadInto(reloaded);
                reloaded.Enable();

                Assert.AreEqual("<Keyboard>/q", reloaded.FindAction("Ability1").bindings[0].effectivePath);
            }
            finally
            {
                Object.DestroyImmediate(reloaded);
            }
        }

        [Test]
        public void Load_MissingFile_KeepsCodeDefaults()
        {
            var store = new InputBindingStore(_path);

            store.LoadInto(_asset);

            Assert.AreEqual("<Keyboard>/1", _asset.FindAction("Ability1").bindings[0].effectivePath);
        }

        [Test]
        public void Load_CorruptFile_KeepsCodeDefaults()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(_path, "not json");
            var store = new InputBindingStore(_path);

            store.LoadInto(_asset);

            Assert.AreEqual("<Keyboard>/1", _asset.FindAction("Ability1").bindings[0].effectivePath);
        }

        [Test]
        public void LoadJson_EmptyInput_ReturnsFalse()
        {
            Assert.IsFalse(InputBindingStore.LoadJson(_asset, string.Empty));
        }

        [Test]
        public void RoundTrip_DoesNotChangeOtherActions()
        {
            InputAction ability = _asset.FindAction("Ability2");
            ability.ApplyBindingOverride(0, "<Keyboard>/q");
            string json = InputBindingStore.SaveJson(_asset);

            InputActionAsset reloaded = GameInputActions.Create();
            try
            {
                InputBindingStore.LoadJson(reloaded, json);
                reloaded.Enable();

                Assert.AreEqual("<Keyboard>/q", reloaded.FindAction("Ability2").bindings[0].effectivePath);
                Assert.AreEqual("<Keyboard>/1", reloaded.FindAction("Ability1").bindings[0].effectivePath);
            }
            finally
            {
                Object.DestroyImmediate(reloaded);
            }
        }
    }
}
