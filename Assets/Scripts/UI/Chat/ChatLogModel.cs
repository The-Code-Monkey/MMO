using System.Collections.Generic;

namespace MMO.UI
{
    /// <summary>Bounded chat transcript behind the chat panel.</summary>
    public sealed class ChatLogModel
    {
        private readonly List<string> _lines = new List<string>();
        private readonly int _maxLines;

        public ChatLogModel(int maxLines)
        {
            _maxLines = maxLines;
        }

        public void Append(string line)
        {
            if (string.IsNullOrEmpty(line))
                return;

            _lines.Add(line);
            while (_lines.Count > _maxLines)
                _lines.RemoveAt(0);
        }

        public string Text => string.Join("\n", _lines);
    }
}
