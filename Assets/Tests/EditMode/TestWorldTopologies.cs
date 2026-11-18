using System;
using System.Collections.Generic;
using MMO.Content;
using MMO.Gameplay;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// Builds code-only topologies through <see cref="SerializedObject"/> — the same path the
    /// Inspector uses — so the tests prove the serialized field names as well as the rules. Every
    /// created asset is tracked and destroyed by <see cref="Cleanup"/>.
    /// </summary>
    internal static class TestWorldTopologies
    {
        private static readonly List<Object> Created = new();

        public sealed class NodeSpec
        {
            public string Id;
            public ZoneRiskTier Tier = ZoneRiskTier.Yellow;
            public bool TierAssigned = true;
            public string SceneName = string.Empty;
            public bool IsChokePoint;
            public string[] Neighbours = Array.Empty<string>();
        }

        public static NodeSpec Node(
            string id,
            ZoneRiskTier tier,
            bool tierAssigned = true,
            string sceneName = "",
            bool isChokePoint = false,
            params string[] neighbours)
        {
            return new NodeSpec
            {
                Id = id,
                Tier = tier,
                TierAssigned = tierAssigned,
                SceneName = sceneName,
                IsChokePoint = isChokePoint,
                Neighbours = neighbours,
            };
        }

        public static WorldTopology Topology(params NodeSpec[] nodes)
        {
            var world = Track(ScriptableObject.CreateInstance<WorldTopology>());
            var inspector = new SerializedObject(world);
            var nodesProperty = inspector.FindProperty("_nodes");
            nodesProperty.arraySize = nodes.Length;

            for (int i = 0; i < nodes.Length; i++)
            {
                var element = nodesProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_zoneId").stringValue = nodes[i].Id;
                element.FindPropertyRelative("_sceneName").stringValue = nodes[i].SceneName;
                element.FindPropertyRelative("_tier").enumValueIndex = (int)nodes[i].Tier;
                element.FindPropertyRelative("_tierAssigned").boolValue = nodes[i].TierAssigned;
                element.FindPropertyRelative("_isChokePoint").boolValue = nodes[i].IsChokePoint;

                var neighbours = element.FindPropertyRelative("_neighbours");
                neighbours.arraySize = nodes[i].Neighbours.Length;
                for (int n = 0; n < nodes[i].Neighbours.Length; n++)
                    neighbours.GetArrayElementAtIndex(n).stringValue = nodes[i].Neighbours[n];
            }

            inspector.ApplyModifiedPropertiesWithoutUndo();
            return world;
        }

        public static void Cleanup()
        {
            foreach (var created in Created)
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }

            Created.Clear();
        }

        private static T Track<T>(T value) where T : Object
        {
            Created.Add(value);
            return value;
        }
    }
}
