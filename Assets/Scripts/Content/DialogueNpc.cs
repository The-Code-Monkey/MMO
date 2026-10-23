using FishNet.Object;
using UnityEngine;

namespace MMO.Content
{
    /// <summary>
    /// A world NPC that opens a dialogue graph when clicked. The root is a shared asset; the
    /// server walks it and the client only renders the payload it is sent. A collider is required
    /// for Unity to deliver <see cref="OnMouseDown"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialogueNpc : NetworkBehaviour
    {
        [SerializeField] private DialogueNode _root;
        [SerializeField, Min(0f)] private float _interactRange = 3f;

        public DialogueNode Root => _root;
        public float InteractRange => Mathf.Max(0f, _interactRange);

        /// <summary>Points the NPC at a conversation root (a prefab author or a test).</summary>
        public void SetRoot(DialogueNode root) => _root = root;

        /// <summary>Reconfigures the talk reach (a prefab author or a test).</summary>
        public void SetInteractRange(float range) => _interactRange = Mathf.Max(0f, range);

        private void OnMouseDown()
        {
            RequestTalkFromLocalPlayer();
        }

        /// <summary>
        /// The click's local effect: resolve the local player's dialogue shell and ask to talk.
        /// Does nothing on a dedicated server or before a local player has spawned.
        /// </summary>
        public void RequestTalkFromLocalPlayer()
        {
            NetworkObject self = NetworkObject;
            if (self == null || self.NetworkManager == null)
                return;

            PlayerDialogue dialogue = PlayerDialogue.FindLocal(self.NetworkManager);
            dialogue?.RequestTalk(self);
        }
    }
}
