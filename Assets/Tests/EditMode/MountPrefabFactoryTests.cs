using System.Collections.Generic;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The generic mount body: one capsule sized by the data, no colliders (the player's mover is
    /// the only physics surface), and the authored model dropped over it when one is set.
    /// </summary>
    public sealed class MountPrefabFactoryTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        [Test]
        public void Create_ScalesTheBodyToSize_AndStripsColliders()
        {
            MountData data = NewMount(size: 2f);

            GameObject mount = MountPrefabFactory.Create(data);
            _created.Add(mount);

            Assert.AreEqual(2f, mount.transform.localScale.x, 0.0001f);
            Assert.AreEqual(0, mount.GetComponentsInChildren<Collider>(true).Length,
                "A cosmetic mount must never collide with the player or carve the NavMesh.");
        }

        [Test]
        public void Create_WithAModel_PutsItOverTheGenericBody()
        {
            var model = new GameObject("AuthoredModel");
            _created.Add(model);

            MountData data = NewMount(size: 1f, model: model);

            GameObject mount = MountPrefabFactory.Create(data);
            _created.Add(mount);

            Transform child = mount.transform.Find("Model");
            Assert.IsNotNull(child, "The authored model must be parented over the generic body.");
            Assert.IsFalse(mount.transform.Find("Body").GetComponent<MeshRenderer>().enabled,
                "With a model present the placeholder capsule must not show through.");
        }

        private MountData NewMount(float size, GameObject model = null)
        {
            MountData data = ScriptableObject.CreateInstance<MountData>();
            _created.Add(data);

            var inspector = new SerializedObject(data);
            inspector.FindProperty("_mountName").stringValue = "Test Mount";
            inspector.FindProperty("_itemName").stringValue = "Test Mount For Prefab Factory";
            inspector.FindProperty("_size").floatValue = size;
            inspector.FindProperty("_modelPrefab").objectReferenceValue = model;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            return data;
        }
    }
}
