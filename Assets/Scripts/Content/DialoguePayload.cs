using System;

namespace MMO.Content
{
    /// <summary>
    /// The one dialogue node the server sends to the client. Public fields because FishNet
    /// serialises public fields; the node ids and choice actions never cross the wire.
    /// </summary>
    [Serializable]
    public struct DialoguePayload
    {
        public string Speaker;
        public string Text;
        public string[] ChoiceTexts;
    }
}
