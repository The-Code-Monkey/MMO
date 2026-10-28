using FishNet.Connection;
using FishNet.Object;
using MMO.Gameplay;
using MMO.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MMO.Client
{
    /// <summary>
    /// Owner-side half of a zone gate. The client finds the gate it stands in, shows the HUD
    /// prompt naming that gate's destination, and turns an E press into an intent. The server
    /// re-checks the player's authoritative position against its own gate list before it runs the
    /// handoff, so the client never chooses a destination or proves its own range.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerZoneTransfer : NetworkBehaviour
    {
        /// <summary>How long the prompt stays quiet after an E press, so one press is one intent.</summary>
        private const float RequestCooldownSeconds = 2f;

        private ZoneTransferTrigger[] _gates;
        private ZoneTransferPromptView _prompt;
        private ZoneTransferTrigger _shownGate;
        private float _nextRequestTime;

        /// <summary>
        /// Owner-side intent: ask the server to transfer this player through the gate it stands
        /// in. The server resolves and validates that gate itself.
        /// </summary>
        public void RequestTransfer()
        {
            NetworkObject self = NetworkObject;
            if (self == null || !self.IsOwner)
                return;

            RpcRequestTransfer();
        }

        [ServerRpc]
        private void RpcRequestTransfer(NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            NetworkObject self = NetworkObject;
            if (self == null)
                return;

            ZoneTransferTrigger gate = ZoneTransferTrigger.FindNearestInRange(transform.position);
            if (gate == null)
                return;

            gate.BeginHandoff(self, sender);
        }

        public override void OnStartClient() => _gates = null;

        private void Update()
        {
            if (!IsOwner || !IsClientStarted)
                return;

            ZoneTransferTrigger gate = FindGate();
            if (gate == null || Time.time < _nextRequestTime)
            {
                HidePrompt();
                return;
            }

            ShowPrompt(gate);

            if (!IsTyping() && Input.GetKeyDown(KeyCode.E))
            {
                _nextRequestTime = Time.time + RequestCooldownSeconds;
                HidePrompt();
                RequestTransfer();
            }
        }

        private ZoneTransferTrigger FindGate()
        {
            if (_gates == null)
                _gates = FindObjectsByType<ZoneTransferTrigger>(FindObjectsSortMode.None);

            ZoneTransferTrigger best = null;
            float bestDistance = float.PositiveInfinity;
            bool stale = false;

            foreach (ZoneTransferTrigger gate in _gates)
            {
                if (gate == null)
                {
                    stale = true;
                    continue;
                }

                if (!gate.IsPositionInRange(transform.position))
                    continue;

                float distance = (gate.transform.position - transform.position).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = gate;
            }

            if (stale)
                _gates = null;

            return best;
        }

        private void ShowPrompt(ZoneTransferTrigger gate)
        {
            if (gate == _shownGate)
                return;

            ZoneTransferPromptView prompt = Prompt;
            if (prompt == null)
                return;

            prompt.Show(gate.TargetZoneName);
            _shownGate = gate;
        }

        private void HidePrompt()
        {
            if (_shownGate == null)
                return;

            Prompt?.Hide();
            _shownGate = null;
        }

        private ZoneTransferPromptView Prompt
        {
            get
            {
                if (_prompt != null)
                    return _prompt;

                LocalPlayerHud hud = GetComponent<LocalPlayerHud>();
                if (hud != null && hud.Hud != null)
                    _prompt = hud.Hud.ZoneTransferPrompt;

                return _prompt;
            }
        }

        /// <summary>
        /// E is also a letter: while a text field owns focus, the press belongs to the field.
        /// </summary>
        private static bool IsTyping()
        {
            GameObject selected = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject
                : null;

            return selected != null && selected.GetComponent<InputField>() != null;
        }
    }
}
