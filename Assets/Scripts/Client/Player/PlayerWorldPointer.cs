using FishNet.Object;
using MMO.Banking;
using MMO.Content;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace MMO.Client
{
    /// <summary>
    /// The owner-side pointer: a press and release that did not become a drag activates whatever is
    /// under it — a crafting station, bank teller, resource node, dialogue NPC, loot chest or target
    /// dummy — through the intent entry points those objects already expose.
    ///
    /// <para>
    /// It is deliberately not a movement driver: there is no auto-walk and no destination. A click
    /// beyond the target's reach still sends its intent, but first tells the player through the chat
    /// log that they are out of range; the server remains the only authority on range, so a stale
    /// client position can never block a legitimate interaction.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerWorldPointer : NetworkBehaviour
    {
        private Camera _camera;
        private bool _pressed;
        private float _draggedPixels;

        private void Update()
        {
            // Only the owner points at the world on this screen; a dedicated server has no pointer.
            if (!IsOwner || (IsServerStarted && !IsClientStarted))
                return;

            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
            {
                _pressed = true;
                _draggedPixels = 0f;
                return;
            }

            if (!_pressed)
                return;

            // Delta, not position: once the cursor is captured the pointer stops moving on screen
            // while a drag is still a drag.
            _draggedPixels += mouse.delta.ReadValue().magnitude;

            bool released = mouse.leftButton.wasReleasedThisFrame || mouse.rightButton.wasReleasedThisFrame;
            if (!released)
                return;

            _pressed = false;
            if (!WorldClickRules.IsClick(_draggedPixels))
                return;

            HandleWorldClick(mouse.position.ReadValue());
        }

        /// <summary>
        /// The world point under the pointer, used as the cast target for ability clicks and
        /// hotkeys. Falls back to a point ahead of the player when there is no camera or hit.
        /// </summary>
        public Vector3 AimPoint()
        {
            if (_camera == null)
                _camera = Camera.main;

            if (_camera == null)
                return transform.position + transform.forward * 5f;

            Vector2 mousePosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Ray ray = _camera.ScreenPointToRay(mousePosition);
            return Physics.Raycast(ray, out RaycastHit hit, 1000f)
                ? hit.point
                : ray.GetPoint(20f);
        }

        /// <summary>Closes the crafting panel, if one is open. A click on the world ends the visit.</summary>
        public void CloseCraftingPanel()
        {
            CraftingPanelView panel = FindFirstObjectByType<CraftingPanelView>();
            if (panel != null && panel.IsOpen)
                panel.Conceal();
        }

        /// <summary>Closes the bank window, if one is open.</summary>
        public void CloseBank()
        {
            BankFrameView frame = FindFirstObjectByType<BankFrameView>();
            if (frame != null && frame.IsOpen)
                frame.Conceal();
        }

        private void HandleWorldClick(Vector2 screenPosition)
        {
            // A click on a panel is the panel's, not the world's.
            if (IsPointerOverUi())
                return;

            if (_camera == null)
                _camera = Camera.main;

            if (_camera == null)
                return;

            Ray ray = _camera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                CloseCraftingPanel();
                CloseBank();
                return;
            }

            Collider collider = hit.collider;

            if (collider.TryGetComponent(out CraftingStation station))
            {
                AdviseOutOfRange(PlayerCrafting.IsWithinReach(transform.position, station.transform.position));
                CloseCraftingPanel();
                CloseBank();
                station.RequestOpenFromLocalPlayer();
                return;
            }

            if (collider.TryGetComponent(out ResourceNode node))
            {
                AdviseOutOfRange(GatheringRules.IsWithinReach(
                    transform.position, node.transform.position, node.InteractRange));
                CloseCraftingPanel();
                CloseBank();
                node.RequestGatherFromLocalPlayer();
                return;
            }

            if (collider.TryGetComponent(out BankTeller teller))
            {
                AdviseOutOfRange(BankTeller.IsWithinReach(transform.position, teller.transform.position));
                CloseCraftingPanel();
                CloseBank();
                teller.RequestOpenFromLocalPlayer();
                return;
            }

            if (collider.TryGetComponent(out DialogueNpc npc))
            {
                AdviseOutOfRange(LootBag.IsWithinRange(
                    transform.position, npc.transform.position, npc.InteractRange));
                CloseCraftingPanel();
                CloseBank();
                npc.RequestTalkFromLocalPlayer();
                return;
            }

            if (collider.TryGetComponent(out LootChest chest))
            {
                AdviseOutOfRange(LootBag.IsWithinRange(
                    transform.position, chest.transform.position, chest.InteractRange));
                CloseCraftingPanel();
                CloseBank();
                chest.RequestOpenFromLocalPlayer();
                return;
            }

            if (collider.TryGetComponent(out TargetDummy dummy))
            {
                CloseCraftingPanel();
                CloseBank();
                dummy.RequestAttackFromLocalPlayer();
                return;
            }

            // A click on the world itself is still a click: it closes the window it covered.
            CloseCraftingPanel();
            CloseBank();
        }

        private static void AdviseOutOfRange(bool withinReach)
        {
            if (!withinReach)
                ChatFeed.PushSystem("Out of range.");
        }

        private static bool IsPointerOverUi()
        {
            EventSystem system = EventSystem.current;
            return system != null && system.IsPointerOverGameObject();
        }
    }
}
