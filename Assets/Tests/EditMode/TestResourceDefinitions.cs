using MMO.Gameplay;
using MMO.Inventory;
using UnityEditor;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// Shared factory for code-built <see cref="ResourceDefinition"/> fixtures. Writes every
    /// field through <see cref="SerializedObject"/> — the exact path the Inspector uses — so the
    /// properties are proven against Unity's own serialization instead of a hand-rolled field
    /// poke.
    /// </summary>
    public static class TestResourceDefinitions
    {
        /// <summary>The name of the fixture every test builds its vein from.</summary>
        public const string IronVein = "TestIronVein";

        /// <summary>
        /// Writes <paramref name="definition"/> in place and returns it.
        /// </summary>
        public static ResourceDefinition Write(
            ResourceDefinition definition,
            string name,
            ResourceType type = ResourceType.Ore,
            ItemDefinition drop = null,
            int dropAmount = 1,
            int gatheringTier = 0,
            ToolKind requiredTool = ToolKind.None,
            float resetTime = 30f,
            float gatherIntervalSeconds = 1.2f,
            float interactRange = 3f)
        {
            var inspector = new SerializedObject(definition);
            inspector.FindProperty("_resourceType").intValue = (int)type;
            inspector.FindProperty("_drop").objectReferenceValue = drop;
            inspector.FindProperty("_dropAmount").intValue = dropAmount;
            inspector.FindProperty("_gatheringTier").intValue = gatheringTier;
            inspector.FindProperty("_requiredTool").intValue = (int)requiredTool;
            inspector.FindProperty("_resetTime").floatValue = resetTime;
            inspector.FindProperty("_gatherIntervalSeconds").floatValue = gatherIntervalSeconds;
            inspector.FindProperty("_interactRange").floatValue = interactRange;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            definition.name = name;
            return definition;
        }

        /// <summary>Creates a fresh definition and writes it.</summary>
        public static ResourceDefinition Create(
            string name = IronVein,
            ResourceType type = ResourceType.Ore,
            ItemDefinition drop = null,
            int dropAmount = 1,
            int gatheringTier = 0,
            ToolKind requiredTool = ToolKind.None,
            float resetTime = 30f,
            float gatherIntervalSeconds = 1.2f,
            float interactRange = 3f)
        {
            return Write(
                ScriptableObject.CreateInstance<ResourceDefinition>(),
                name,
                type,
                drop,
                dropAmount,
                gatheringTier,
                requiredTool,
                resetTime,
                gatherIntervalSeconds,
                interactRange);
        }
    }
}
