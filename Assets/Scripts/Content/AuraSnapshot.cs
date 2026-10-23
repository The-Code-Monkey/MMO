using System;

namespace MMO.Content
{
    /// <summary>
    /// The replicated buff-bar entry for one active aura. Public fields because FishNet's
    /// serializer reads public fields only.
    /// </summary>
    [Serializable]
    public struct AuraSnapshot
    {
        public string AuraId;
        public AuraKind Kind;
        public int Stacks;

        public AuraSnapshot(string auraId, AuraKind kind, int stacks)
        {
            AuraId = auraId;
            Kind = kind;
            Stacks = stacks;
        }
    }
}
