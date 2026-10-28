using UnityEngine;

namespace MMO.Editor
{
    /// <summary>Small scene-authoring primitives shared by the zone bake steps.</summary>
    public static class ZoneSceneScaffold
    {
        public static GameObject CreateChild(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
