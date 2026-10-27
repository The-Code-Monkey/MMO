using System.Reflection;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The MonoBehaviour host of the token store: it must hand triggers and the gate a working
    /// store, register for scene objects that cannot be wired directly, and clear itself on the
    /// way out so a second NetworkManager in the same EditMode test does not inherit a stale one.
    /// </summary>
    public sealed class ZoneTransferTokenServiceTests
    {
        private GameObject _go;
        private ZoneTransferTokenService _service;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TokenServiceUnderTest");
            _service = _go.AddComponent<ZoneTransferTokenService>();

            // EditMode never runs MonoBehaviour lifecycle callbacks on AddComponent; invoke the
            // one Unity would call on enable so the test sees what a real scene sees.
            typeof(ZoneTransferTokenService)
                .GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_service, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
                Object.DestroyImmediate(_go);
        }

        [Test]
        public void Awake_CreatesAWorkingStore()
        {
            Assert.IsNotNull(_service.Store);
        }

        [Test]
        public void Awake_RegistersItselfAsTheInstance()
        {
            Assert.AreSame(_service, ZoneTransferTokenService.Instance);
        }

        [Test]
        public void Configure_SwapsInADoubleForTests()
        {
            var double_ = new ZoneTransferTokenStore("test-secret");

            _service.Configure(double_);

            Assert.AreSame(double_, _service.Store);
        }

        [Test]
        public void OnDestroy_ClearsTheInstance()
        {
            // EditMode does not drive the MonoBehaviour lifecycle, so call the callback Unity would.
            typeof(ZoneTransferTokenService)
                .GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_service, null);

            Assert.IsNull(ZoneTransferTokenService.Instance);
        }
    }
}