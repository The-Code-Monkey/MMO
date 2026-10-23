using System.Collections.Generic;
using MMO.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.Tests
{
    /// <summary>
    /// The bag/equipment windows start collapsed to their icon and the icon toggles them, so the
    /// player's screen is not permanently covered by containers.
    /// </summary>
    public sealed class CollapsiblePanelViewTests
    {
        private readonly List<GameObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _created.Clear();
        }

        [Test]
        public void StartsCollapsed_AndTheIconTogglesTheWindow()
        {
            GameObject panel = New("Window");
            Button icon = NewButton("Icon");
            CollapsiblePanelView window = icon.gameObject.AddComponent<CollapsiblePanelView>();

            window.Configure(panel, icon);

            Assert.IsFalse(window.IsExpanded, "A window must not cover the screen until it is asked to.");
            Assert.IsFalse(panel.activeSelf);

            icon.onClick.Invoke();
            Assert.IsTrue(window.IsExpanded);
            Assert.IsTrue(panel.activeSelf);

            icon.onClick.Invoke();
            Assert.IsFalse(window.IsExpanded);
            Assert.IsFalse(panel.activeSelf);
        }

        private GameObject New(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            _created.Add(go);
            return go;
        }

        private Button NewButton(string name)
        {
            GameObject go = New(name);
            go.AddComponent<Image>();
            return go.AddComponent<Button>();
        }
    }
}
