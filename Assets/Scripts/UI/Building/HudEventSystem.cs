using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>Ensures the scene has a single click-routing EventSystem.</summary>
    public static class HudEventSystem
    {
        public static EventSystem Ensure()
        {
            EventSystem existing = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (existing != null)
                return existing;

            GameObject eventSystemObject = new GameObject(NetworkHudFactory.EventSystemName);
            EventSystem eventSystem = eventSystemObject.AddComponent<EventSystem>();

            // This assembly references neither input backend by name, so the choice is made at
            // runtime: the Input System package's module when present, legacy otherwise.
            Type moduleType = FindInputSystemModule() ?? typeof(StandaloneInputModule);
            eventSystemObject.AddComponent(moduleType);

            return eventSystem;
        }

        private static Type FindInputSystemModule()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "Unity.InputSystem")
                    continue;

                return assembly.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule");
            }

            return null;
        }
    }
}
