using MMO.UI;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The zone-gate prompt is hidden by default and names the destination exactly once it is shown.
    /// </summary>
    public sealed class ZoneTransferPromptViewTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                Object.DestroyImmediate(_root);
        }

        [Test]
        public void Create_StartsHidden()
        {
            ZoneTransferPromptView view = NewView();

            Assert.IsFalse(view.IsShown);
            Assert.IsNotNull(view.Label);
        }

        [Test]
        public void Show_NamesTheDestinationAndPressKey()
        {
            ZoneTransferPromptView view = NewView();

            view.Show("SafeZone");

            Assert.IsTrue(view.IsShown);
            Assert.AreEqual("To switch to zone SafeZone, press E", view.Label.text);
        }

        [Test]
        public void Hide_TakesThePromptOffScreen()
        {
            ZoneTransferPromptView view = NewView();
            view.Show("Wilderness");

            view.Hide();

            Assert.IsFalse(view.IsShown);
        }

        private ZoneTransferPromptView NewView()
        {
            _root = new GameObject("Canvas");
            return ZoneTransferPromptView.Create(_root.transform);
        }
    }
}
