using MMO.Inventory;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// The data half of a harvestable resource: what kind of thing it is, what it yields, how
    /// much of it a node holds, what tool and what tier are needed to take it, and how long it
    /// takes to come back. Created with <c>Assets > Create > MMO > Resource Definition</c>.
    ///
    /// <para>
    /// This is the resource system's answer to <see cref="MMO.Crafting.RecipeData"/>: a recipe
    /// says what a station makes, a resource definition says what a node gives, and both are
    /// assets a designer authors rather than fields a programmer tunes in a prefab. The
    /// <see cref="ResourceNode"/> component stays generic — one component, one prefab, every
    /// kind of resource — and this card is what makes one node an iron vein and the next an oak.
    /// </para>
    ///
    /// <para>
    /// Like every other data asset in the project it carries no networking: the server and the
    /// client read the same asset through the same prefab, so there is nothing to replicate and
    /// nothing a client could lie about. Balance lives in the Inspector.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "New Resource Definition", menuName = "MMO/Resource Definition")]
    public sealed class ResourceDefinition : ScriptableObject
    {
        /// <summary>
        /// The <c>Resources</c> subfolder the shipped resource definitions live under — the
        /// definition above is <c>Resources/ResourceDefinitions/Iron_Ore.asset</c>.
        /// </summary>
        public const string ResourcesFolder = "ResourceDefinitions";

        /// <summary>
        /// The name of the definition the shipped resource node prefab carries: the iron vein a
        /// brand-new character gathers their first ore from.
        /// </summary>
        public const string DefaultDefinitionName = "Iron_Ore";

        /// <summary>Units a node holds when nothing else is configured.</summary>
        public const int DefaultDropAmount = 1;

        /// <summary>Seconds an emptied node spends empty when nothing else is configured.</summary>
        public const float DefaultResetTime = 30f;

        /// <summary>Seconds between two harvests when nothing else is configured.</summary>
        public const float DefaultGatherIntervalSeconds = 1.2f;

        /// <summary>Metres a gatherer may stand from a node when nothing else is configured.</summary>
        public const float DefaultInteractRange = 3f;

        [SerializeField] private ResourceType _resourceType = ResourceType.None;
        [SerializeField] private ItemDefinition _drop;
        [SerializeField, Min(1)] private int _dropAmount = DefaultDropAmount;
        [SerializeField, Min(0)] private int _gatheringTier;
        [SerializeField] private ToolKind _requiredTool = ToolKind.None;
        [SerializeField, Min(0f)] private float _resetTime = DefaultResetTime;
        [SerializeField, Min(0f)] private float _gatherIntervalSeconds = DefaultGatherIntervalSeconds;
        [SerializeField, Min(0f)] private float _interactRange = DefaultInteractRange;

        /// <summary>What kind of resource this is — ore, lumber, herb, fish.</summary>
        public ResourceType Type => _resourceType;

        /// <summary>The item a harvest awards into the gatherer's bag.</summary>
        public ItemDefinition Drop => _drop;

        /// <summary>
        /// How many units one node holds in total. A harvest takes one of them; when the last one
        /// is taken the node is empty and the server regrows it after <see cref="ResetTime"/>.
        /// </summary>
        public int DropAmount => Mathf.Max(1, _dropAmount);

        /// <summary>
        /// The tier of tool the gatherer must bring. Zero means the resource asks for no tier at
        /// all — bare hands will do. A tier above zero means the player must hold a tool of at
        /// least that tier, which is what stops a T1 pickaxe from emptying a T2 vein.
        /// </summary>
        public int GatheringTier => Mathf.Max(0, _gatheringTier);

        /// <summary>
        /// The kind of tool this resource needs, or <see cref="ToolKind.None"/> when it can be
        /// taken by hand. A named kind always has to be satisfied; the tier is the extra
        /// condition on top of it.
        /// </summary>
        public ToolKind RequiredTool => _requiredTool;

        /// <summary>Seconds an emptied node spends empty before the server regrows it.</summary>
        public float ResetTime => Mathf.Max(0f, _resetTime);

        /// <summary>Seconds between two harvests of this resource.</summary>
        public float GatherIntervalSeconds => Mathf.Max(0f, _gatherIntervalSeconds);

        /// <summary>Metres a gatherer may stand from this node to harvest it.</summary>
        public float InteractRange => Mathf.Max(0f, _interactRange);

        /// <summary>
        /// Loads the definition the shipped resource node prefab carries. Returns null when the
        /// asset is missing, which leaves the node inert rather than broken — a node with no
        /// definition yields nothing and says so in the log.
        /// </summary>
        public static ResourceDefinition LoadDefault()
        {
            return Resources.Load<ResourceDefinition>($"{ResourcesFolder}/{DefaultDefinitionName}");
        }
    }
}
