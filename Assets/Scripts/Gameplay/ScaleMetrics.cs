using System;

namespace MMO.Gameplay
{
    /// <summary>
    /// The project's scale contract, in Unity units (1 unit = 1 metre). Every prefab, doorway, camera
    /// offset and range should be expressible from these, so "does a character fit" is arithmetic
    /// rather than an eyeball. Pure so the kit validator and the tests read the same numbers.
    /// </summary>
    public static class ScaleMetrics
    {
        public const float CharacterHeight = 1.8f;
        public const float CharacterRadius = 0.5f;

        public const float DoorWidth = 1.5f;
        public const float DoorHeight = 2.5f;

        public const float MaxStepHeight = 0.25f;

        public const float Unit = 1f;          // 1 unit == 1 metre; combat/ability ranges are in metres
        public const float GridSmall = 1f;
        public const float GridLarge = 2f;

        public const float CharacterClearance = 0.2f;

        /// <summary>A doorway this size admits a standing character with clearance.</summary>
        public static bool AdmitsCharacter(float width, float height)
            => width >= CharacterRadius * 2f && height >= CharacterHeight + CharacterClearance;

        /// <summary>True when a dimension is a whole multiple of the kit grid.</summary>
        public static bool OnGrid(float size, float grid) => grid > 0f && MathF.Abs(size % grid) < 1e-4f;
    }
}