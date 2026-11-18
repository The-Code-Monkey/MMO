using System.Collections.Generic;
using MMO.Client;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// Who may take the camera away. Every client holds every avatar, so only the copy that bound
    /// the rig may clear it; a remote avatar's teardown must leave the local player's view alone.
    /// </summary>
    public sealed class LocalPlayerCameraTargetTests
    {
        private readonly List<GameObject> _objects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _objects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _objects.Clear();
        }

        [Test]
        public void ClearLocalTarget_IgnoresACallerThatDoesNotOwnTheBinding()
        {
            ThirdPersonCameraRig rig = BuildRig();
            Transform local = BuildObject("LocalPlayer");
            Transform remote = BuildObject("RemotePlayer");

            rig.SetLocalTarget(local);

            Assert.IsFalse(rig.ClearLocalTarget(remote),
                "A stranger must not be able to clear the binding.");
            Assert.AreSame(local, rig.Target, "The local player keeps the camera.");

            Assert.IsTrue(rig.ClearLocalTarget(local));
            Assert.IsNull(rig.Target, "The owner can still let go of it.");
        }

        [Test]
        public void RemoteTeardown_DoesNotClearTheLocalBinding()
        {
            ThirdPersonCameraRig rig = BuildRig();
            Transform local = BuildObject("LocalPlayer");
            GameObject remote = BuildObject("RemotePlayer").gameObject;

            rig.SetLocalTarget(local);

            // The prefab carries this on every copy; the remote's teardown runs on this client too.
            LocalPlayerCameraTarget target = remote.AddComponent<LocalPlayerCameraTarget>();
            target.OnStopNetwork();

            Assert.AreSame(local, rig.Target,
                "A remote avatar leaving the zone must not take the local camera with it.");
        }

        private ThirdPersonCameraRig BuildRig()
        {
            var go = new GameObject("CameraRig");
            _objects.Add(go);
            return go.AddComponent<ThirdPersonCameraRig>();
        }

        private Transform BuildObject(string name)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go.transform;
        }
    }
}
