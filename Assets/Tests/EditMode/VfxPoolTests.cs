using MMO.Client;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The pool must never allocate while a fight is running: every instance exists after
    /// configuration, and an exhausted pool returns null instead of growing.
    /// </summary>
    public sealed class VfxPoolTests
    {
        private GameObject _root;
        private GameObject _template;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("VfxPoolTest");
            _template = new GameObject("Template");
            _template.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                Object.DestroyImmediate(_root);

            if (_template != null)
                Object.DestroyImmediate(_template);
        }

        [Test]
        public void Configure_PrewarmsEveryInstance()
        {
            VfxPool pool = _root.AddComponent<VfxPool>();

            pool.Configure(_template, capacity: 3);

            Assert.AreEqual(3, pool.Capacity);
            Assert.AreEqual(3, pool.Available);
        }

        [Test]
        public void Rent_ExhaustedPool_ReturnsNullInsteadOfAllocating()
        {
            VfxPool pool = _root.AddComponent<VfxPool>();
            pool.Configure(_template, capacity: 1);

            Assert.IsNotNull(pool.Rent(Vector3.zero, lifetime: 1f));
            Assert.IsNull(pool.Rent(Vector3.one, lifetime: 1f));
            Assert.AreEqual(0, pool.Available);
        }

        [Test]
        public void Return_MakesTheSameInstanceRentableAgain()
        {
            VfxPool pool = _root.AddComponent<VfxPool>();
            pool.Configure(_template, capacity: 1);

            GameObject first = pool.Rent(Vector3.zero, lifetime: 1f);
            pool.Return(first);
            GameObject second = pool.Rent(Vector3.one, lifetime: 1f);

            Assert.AreSame(first, second, "A returned instance must be recycled, not recreated.");
        }

        [Test]
        public void Configure_NullTemplate_LeavesNothingToRent()
        {
            VfxPool pool = _root.AddComponent<VfxPool>();

            pool.Configure(null, capacity: 2);

            Assert.AreEqual(2, pool.Capacity);
            Assert.AreEqual(0, pool.Available);
            Assert.IsNull(pool.Rent(Vector3.zero, lifetime: 1f));
        }

        [Test]
        public void Rent_ActivatesAndPlacesTheInstance()
        {
            VfxPool pool = _root.AddComponent<VfxPool>();
            pool.Configure(_template, capacity: 1);

            GameObject instance = pool.Rent(new Vector3(2f, 3f, 4f), lifetime: 1f);

            Assert.IsTrue(instance.activeSelf);
            Assert.AreEqual(new Vector3(2f, 3f, 4f), instance.transform.position);
        }
    }
}
