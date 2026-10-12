using System;

namespace MMO.Social
{
    /// <summary>
    /// One flat DTO for everything that rides <c>room.system-social</c>. JsonUtility has no
    /// polymorphism, so chat messages and notify hints share fields with the unused ones left null.
    ///
    /// <para>
    /// The bus carries hints and chat text, never authority: every state change is still a versioned
    /// storage record and an envelope only says "re-read X now". A lost, reordered or duplicated
    /// envelope therefore cannot corrupt state — the reconcile pass repairs it.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class SocialBusEnvelope
    {
        /// <summary>"chat" | "friend" | "party" | "guild" | "presence".</summary>
        public string t;

        /// <summary>Chat only: "global" | "zone" | "party".</summary>
        public string ch;

        /// <summary>Chat: zone id / party id ("" for global). Notify: the affected party/guild id.</summary>
        public string target;

        /// <summary>Chat: sender AccountId, stamped by the originating zone after FishNet auth.</summary>
        public string from;

        /// <summary>Chat: display name, likewise server-stamped — never client text.</summary>
        public string name;

        /// <summary>Chat text.</summary>
        public string body;

        /// <summary>Notify: affected account ids; receiving zones filter on these.</summary>
        public string[] accounts;

        /// <summary>Server-stamped Unix time in seconds.</summary>
        public long ts;
    }
}
