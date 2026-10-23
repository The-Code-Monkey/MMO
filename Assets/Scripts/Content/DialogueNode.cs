using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Content
{
    /// <summary>An optional quest action attached to a dialogue choice.</summary>
    public enum DialogueAction
    {
        None = 0,
        OfferQuest = 1,
        TurnInQuest = 2,
    }

    /// <summary>
    /// One branch in a dialogue graph. The next node is a direct asset reference, so a designer
    /// can wire a conversation in the Inspector without a separate id registry.
    /// </summary>
    [Serializable]
    public struct DialogueChoice
    {
        public string Text;
        public DialogueNode Next;
        public DialogueAction Action;
        public QuestData Quest;
    }

    /// <summary>
    /// A node in an NPC conversation. The server walks these assets and sends the client only the
    /// rendered text and choice labels; the client never receives the node ids it did not ask for.
    /// </summary>
    [CreateAssetMenu(fileName = "New Dialogue Node", menuName = "MMO/Dialogue Node")]
    public sealed class DialogueNode : ScriptableObject
    {
        [SerializeField] private string _nodeId = string.Empty;
        [SerializeField] private string _speaker = string.Empty;
        [SerializeField, TextArea] private string _text = string.Empty;
        [SerializeField] private DialogueChoice[] _choices = Array.Empty<DialogueChoice>();

        public string NodeId => _nodeId;
        public string Speaker => _speaker;
        public string Text => _text;
        public IReadOnlyList<DialogueChoice> Choices => _choices ?? Array.Empty<DialogueChoice>();
    }
}
