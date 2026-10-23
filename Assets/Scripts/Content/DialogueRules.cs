namespace MMO.Content
{
    /// <summary>
    /// Pure dialogue graph rules. The server validates a client's choice index through these
    /// before it touches a quest or advances the conversation.
    /// </summary>
    public static class DialogueRules
    {
        public static int ChoiceCount(DialogueNode node)
        {
            return node != null ? node.Choices.Count : 0;
        }

        public static bool CanChoose(DialogueNode node, int index)
        {
            return node != null && index >= 0 && index < node.Choices.Count;
        }

        /// <summary>
        /// Resolves a choice by index. An invalid index is a refused request, never an exception.
        /// </summary>
        public static bool TryChoose(DialogueNode node, int index, out DialogueChoice choice)
        {
            if (!CanChoose(node, index))
            {
                choice = default;
                return false;
            }

            choice = node.Choices[index];
            return true;
        }

        /// <summary>The node a valid choice leads to, or null for an invalid choice or a dead end.</summary>
        public static DialogueNode Next(DialogueNode node, int index)
        {
            return TryChoose(node, index, out DialogueChoice choice) ? choice.Next : null;
        }
    }
}
