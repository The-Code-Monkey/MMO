using System;
using System.Collections.Generic;
using MMO.Gameplay;
using MMO.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.Tests
{
    /// <summary>
    /// The centre-screen cast bar: hidden unless a mount or dismount cast is running, filled from
    /// the source's progress, and repainted only when the source says something changed.
    /// </summary>
    public sealed class MountCastBarViewTests
    {
        private sealed class FakeMount : IMountSource
        {
            public MountState State { get; set; }
            public float CastProgress { get; set; }

            public event Action MountChanged;

            public void Raise() => MountChanged?.Invoke();
        }

        private readonly List<GameObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        [Test]
        public void HiddenWhenUnmounted_AndWhileMounted()
        {
            var source = new FakeMount { State = MountState.Unmounted };
            MountCastBarView view = NewView(out _, out _);
            view.Bind(source);

            Assert.IsFalse(view.gameObject.activeSelf, "No cast is running, so there is nothing to show.");

            source.State = MountState.Mounted;
            source.Raise();

            Assert.IsFalse(view.gameObject.activeSelf);
        }

        [Test]
        public void ShowsAndFillsWhileMounting()
        {
            var source = new FakeMount { State = MountState.Mounting, CastProgress = 0.5f };
            MountCastBarView view = NewView(out Image fill, out Text label);
            view.Bind(source);

            Assert.IsTrue(view.gameObject.activeSelf);
            Assert.AreEqual(0.5f, fill.fillAmount, 0.0001f);
            StringAssert.Contains("Mounting", label.text);
        }

        [Test]
        public void ShowsDismountingToo()
        {
            var source = new FakeMount { State = MountState.Dismounting, CastProgress = 0.25f };
            MountCastBarView view = NewView(out Image fill, out Text label);
            view.Bind(source);

            Assert.IsTrue(view.gameObject.activeSelf);
            Assert.AreEqual(0.25f, fill.fillAmount, 0.0001f);
            StringAssert.Contains("Dismounting", label.text);
        }

        [Test]
        public void HidesAgainWhenTheCastFinishes()
        {
            var source = new FakeMount { State = MountState.Mounting, CastProgress = 0.9f };
            MountCastBarView view = NewView(out _, out _);
            view.Bind(source);

            source.State = MountState.Mounted;
            source.CastProgress = 0f;
            source.Raise();

            Assert.IsFalse(view.gameObject.activeSelf);
        }

        private MountCastBarView NewView(out Image fill, out Text label)
        {
            var root = new GameObject("MountCastBar", typeof(RectTransform));
            _created.Add(root);

            var fillObject = new GameObject("Fill", typeof(RectTransform));
            fillObject.transform.SetParent(root.transform, false);
            fill = fillObject.AddComponent<Image>();

            var labelObject = new GameObject("Label", typeof(RectTransform));
            labelObject.transform.SetParent(root.transform, false);
            label = labelObject.AddComponent<Text>();

            MountCastBarView view = root.AddComponent<MountCastBarView>();
            view.SetVisuals(fill, label);
            return view;
        }
    }
}
