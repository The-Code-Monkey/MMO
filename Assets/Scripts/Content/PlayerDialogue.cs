using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using MMO.Gameplay;
using UnityEngine;

namespace MMO.Content
{
    /// <summary>
    /// A player's server-walked dialogue session. The client sends a talk request and choice
    /// indexes; the server owns the current node, checks the index, applies quest actions and
    /// sends back only the text and labels to render.
    /// </summary>
    public sealed class PlayerDialogue : NetworkBehaviour
    {
        private DialogueNode _node;
        private PlayerQuestLog _quests;

        public bool IsOpen { get; private set; }
        public DialoguePayload Current { get; private set; }

        public event Action<DialoguePayload> NodeShown;
        public event Action Closed;

        private PlayerQuestLog Quests => _quests != null ? _quests : _quests = GetComponent<PlayerQuestLog>();

        /// <summary>Owner-side intent: talk to <paramref name="npc"/>.</summary>
        public void RequestTalk(NetworkObject npc)
        {
            if (!IsOwner || npc == null)
                return;

            RpcRequestTalk(npc);
        }

        [ServerRpc]
        private void RpcRequestTalk(NetworkObject npcObject, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender) || npcObject == null)
                return;

            if (!npcObject.TryGetComponent(out DialogueNpc npc) || npc.Root == null)
                return;

            if (!LootBag.IsWithinRange(transform.position, npc.transform.position, npc.InteractRange))
                return;

            ShowNode(npc.Root);
        }

        /// <summary>Owner-side intent: choose a branch by index.</summary>
        public void RequestChoice(int index)
        {
            if (!IsOwner)
                return;

            RpcRequestChoice(index);
        }

        [ServerRpc]
        private void RpcRequestChoice(int index, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            if (!DialogueRules.TryChoose(_node, index, out DialogueChoice choice))
                return;

            switch (choice.Action)
            {
                case DialogueAction.OfferQuest:
                    Quests?.TryStartQuest(choice.Quest);
                    break;
                case DialogueAction.TurnInQuest:
                    Quests?.TryComplete(choice.Quest);
                    break;
            }

            if (choice.Next != null)
                ShowNode(choice.Next);
            else
                CloseDialogue();
        }

        /// <summary>
        /// Finds this process's player object — the spawned object owned by the local client —
        /// and returns its dialogue shell, or null when there is none yet.
        /// </summary>
        public static PlayerDialogue FindLocal(NetworkManager networkManager)
        {
            if (networkManager == null || networkManager.ServerManager == null || networkManager.ClientManager == null)
                return null;

            IReadOnlyDictionary<int, NetworkObject> spawned = networkManager.IsHostStarted
                ? networkManager.ServerManager.Objects.Spawned
                : networkManager.ClientManager.Objects.Spawned;

            foreach (NetworkObject nob in spawned.Values)
            {
                if (nob != null && nob.IsSpawned && nob.IsOwner && nob.TryGetComponent(out PlayerDialogue dialogue))
                    return dialogue;
            }

            return null;
        }

        private void ShowNode(DialogueNode node)
        {
            if (node == null)
            {
                CloseDialogue();
                return;
            }

            _node = node;
            Quests?.NotifyInteract(node.NodeId);

            IReadOnlyList<DialogueChoice> choices = node.Choices;
            var payload = new DialoguePayload
            {
                Speaker = node.Speaker,
                Text = node.Text,
                ChoiceTexts = new string[choices.Count],
            };

            for (int i = 0; i < choices.Count; i++)
                payload.ChoiceTexts[i] = choices[i].Text;

            if (Owner != null)
                RpcShowNode(Owner, payload);
        }

        private void CloseDialogue()
        {
            _node = null;

            if (Owner != null)
                RpcCloseNode(Owner);
        }

        [TargetRpc]
        private void RpcShowNode(NetworkConnection target, DialoguePayload payload)
        {
            if (!IsOwner)
                return;

            Current = payload;
            IsOpen = true;
            NodeShown?.Invoke(payload);
        }

        [TargetRpc]
        private void RpcCloseNode(NetworkConnection target)
        {
            if (!IsOwner)
                return;

            Current = default;
            IsOpen = false;
            Closed?.Invoke();
        }
    }
}
