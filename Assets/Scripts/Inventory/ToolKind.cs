namespace MMO.Inventory
{
    /// <summary>
    /// The kind of tool an item is — the family a resource node asks for by name. A node that
    /// demands a <see cref="Pickaxe"/> is satisfied by any pickaxe the player carries, whatever
    /// its tier; the tier is the separate, numeric half of the requirement
    /// (<see cref="ItemDefinition.ToolTier"/>).
    ///
    /// <para>
    /// Splitting "which tool" from "how good is it" is what lets a world carry a ladder of
    /// tools — a Bronze Pickaxe and an Iron Pickaxe are two items of one kind, at two tiers —
    /// without the resource side having to know their names.
    /// </para>
    /// </summary>
    public enum ToolKind
    {
        /// <summary>Not a tool. Bare hands, or an item that harvests nothing.</summary>
        None = 0,

        /// <summary>A pickaxe: ore veins, stone.</summary>
        Pickaxe = 1,

        /// <summary>An axe: trees and lumber.</summary>
        Axe = 2,

        /// <summary>A shovel: earth, clay, buried things.</summary>
        Shovel = 3,

        /// <summary>A sickle: herbs, reeds, crops.</summary>
        Sickle = 4,

        /// <summary>A knife: hides, carcasses, fish.</summary>
        Knife = 5,
    }
}
