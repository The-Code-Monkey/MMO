using System;
using System.Collections.Generic;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The mount resolver both ends use: a registered key answers, an unknown key is null (so the
    /// server refuses the request rather than throwing), and clearing forgets everything.
    /// </summary>
    public sealed class MountDataRegistryTests
    {
        private readonly List<MountData> _assets = new();

        [SetUp]
        public void SetUp() => MountDataRegistry.Clear();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _assets.Count; i++)
            {
                if (_assets[i] != null)
                    UnityEngine.Object.DestroyImmediate(_assets[i]);
            }

            _assets.Clear();
            MountDataRegistry.Clear();
        }

        [Test]
        public void Resolve_ReturnsTheRegisteredMount()
        {
            MountData data = NewMount("Test Horse");
            MountDataRegistry.Register(data);

            Assert.AreSame(data, MountDataRegistry.Resolve("Test Horse"));
        }

        [Test]
        public void Resolve_UnknownKey_IsNull()
        {
            Assert.IsNull(MountDataRegistry.Resolve("No Such Mount"));
        }

        [Test]
        public void Register_WithoutAnItemName_Throws()
        {
            MountData data = NewMount(string.Empty);

            Assert.Throws<ArgumentException>(() => MountDataRegistry.Register(data));
        }

        [Test]
        public void Clear_ForgetsTheRegistration()
        {
            MountData data = NewMount("Test Horse");
            MountDataRegistry.Register(data);

            MountDataRegistry.Clear();

            Assert.IsNull(MountDataRegistry.Resolve("Test Horse"));
        }

        private MountData NewMount(string itemName)
        {
            MountData data = ScriptableObject.CreateInstance<MountData>();
            _assets.Add(data);

            var inspector = new SerializedObject(data);
            inspector.FindProperty("_mountName").stringValue = "Test Mount";
            inspector.FindProperty("_itemName").stringValue = itemName;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            return data;
        }
    }
}
