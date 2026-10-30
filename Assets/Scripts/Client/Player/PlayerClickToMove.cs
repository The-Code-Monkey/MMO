using FishNet.Object;
using MMO.Banking;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace MMO.Client
{
    public class PlayerClickToMove : NetworkBehaviour
    {
        [SerializeField] private LayerMask _walkableLayers = ~0;

        private Camera _mainCam;
        private PlayerMover _mover;
        private PlayerMount _mount;
        private Vector3 _target;
        private GameObject _marker;
        private CraftingStation _pendingStation;
        private ResourceNode _pendingNode;
        private BankTeller _pendingTeller;
        private readonly ClickToMoveMarkerTracker _tracker = new ClickToMoveMarkerTracker();
        private CraftingPanelView _craftingPanel;

        /// <summary>
        /// The crafting panel the local HUD built, or null when there is none. It is found once
        /// and kept: a concealed panel is inactive, so it is only ever found while it is open.
        /// </summary>
        private CraftingPanelView CraftingPanel
        {
            get
            {
                if (_craftingPanel != null) return _craftingPanel;
                _craftingPanel = FindFirstObjectByType<CraftingPanelView>();
                return _craftingPanel;
            }
        }

        /// <summary>
        /// True when the mouse is over a UI element. The world click has to yield to the UI:
        /// without this a click on the crafting panel also sends the player walking.
        /// </summary>
        private static bool IsPointerOverUi()
        {
            EventSystem system = EventSystem.current;
            return system != null && system.IsPointerOverGameObject();
        }

        private void Awake()
        {
            _mover = GetComponent<PlayerMover>();
            _mount = GetComponent<PlayerMount>();
        }

        private void Update()
        {
            if (!IsOwner)
                return;

            // A click on the UI is a click on the UI: the panel swallows it and the player does
            // not walk off. Only a click on the world becomes a destination.
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUi())
                SetDestinationToMouseClick();

            if (UpdateMarker())
            {
                OpenPendingStation();
                GatherPendingNode();
                OpenPendingTeller();
            }
        }

        /// <summary>
        /// Clears the destination marker once the walk is over. The decision lives in
        /// <see cref="ClickToMoveMarkerTracker"/>: a direct arrival clears it at once, and a
        /// player that has stopped moving clears it too — the server owns the walk and stops on
        /// its own sampled point, so a distance check alone would leave the marker stuck.
        ///
        /// <para>
        /// The marker going is the signal that the walk is over, so the station the player was
        /// walking to is opened then — see <see cref="OpenPendingStation"/>.
        /// </para>
        /// </summary>
        /// <returns>True when the walk is over and the marker was cleared.</returns>
        private bool UpdateMarker()
        {
            if (_marker == null)
            {
                _tracker.Cancel();
                return false;
            }

            if (!_tracker.ShouldClear(transform.position))
                return false;

            ClearMarker();
            return true;
        }

        private void ClearMarker()
        {
            if (_marker != null)
                Destroy(_marker);

            _marker = null;
            _tracker.Cancel();
        }

        private void SetDestinationToMouseClick()
        {
            if (_mainCam == null)
            {
                _mainCam = Camera.main;
                if (_mainCam == null) return;
            }

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = _mainCam.ScreenPointToRay(mousePos);

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, _walkableLayers))
            {
                // If we hit a crafting station, walk the player to it and open its panel when the
                // walk is over.
                if (hit.collider.TryGetComponent(out CraftingStation station))
                {
                    BeginStationApproach(station);
                    return;
                }

                // A vein is walked to, not clicked from across the room: stand off it and start
                // gathering once the walk is over.
                if (hit.collider.TryGetComponent(out ResourceNode node))
                {
                    BeginNodeApproach(node);
                    return;
                }

                // A bank teller is walked to exactly like a crafting station; its window opens on
                // arrival.
                if (hit.collider.TryGetComponent(out BankTeller teller))
                {
                    BeginTellerApproach(teller);
                    return;
                }

                // A click on anything else is just a destination: the pending station is dropped,
                // because the walk this click starts is not towards a station, and the panel goes
                // with it — the player is walking away from the station it was showing.
                _pendingStation = null;
                _pendingNode = null;
                _pendingTeller = null;
                CloseCraftingPanel();
                CloseBank();

                // The server accepts a click by sampling the NavMesh within 1 m and walks to the
                // sampled point, so the client must mark and measure against the same point —
                // otherwise the arrival check below can miss (the player stops short of the raw
                // click) and the marker outlives the walk. A flying mount has no NavMesh above it,
                // so the clicked 3D point is the destination directly.
                Vector3 destination = _mount != null && _mount.IsFlying
                    ? hit.point
                    : NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 1f, NavMesh.AllAreas)
                        ? navHit.position
                        : hit.point;

                _target = destination;
                _mover?.RequestMoveTo(destination);
                ShowMarker(destination);
            }
        }

        /// <summary>
        /// Closes the crafting panel, if one is open. A click on the world means the player is
        /// done with the station they were standing at, so the window goes with them.
        /// </summary>
        public void CloseCraftingPanel()
        {
            CraftingPanelView panel = CraftingPanel;
            if (panel != null && panel.IsOpen)
                panel.Conceal();
        }

        /// <summary>
        /// The player clicked a crafting station: stand in front of it and open its panel when the
        /// walk is over.
        ///
        /// <para>
        /// A station already within reach needs no walk, so its panel opens at once. A station
        /// further away is remembered until the walk ends — the server decides what the station
        /// offers and sends it back, and it refuses the request outright when the player is too
        /// far, so asking before the walk ends would only be asking for a refusal.
        /// </para>
        /// </summary>
        public void BeginStationApproach(CraftingStation station)
        {
            _pendingStation = null;
            _pendingNode = null;
            _pendingTeller = null;

            if (station == null)
                return;

            if (PlayerCrafting.IsWithinReach(transform.position, station.transform.position))
            {
                station.RequestOpenFromLocalPlayer();
                return;
            }

            _pendingStation = station;

            // The server samples the destination within 1 m and walks to the point it finds, so
            // the client marks the same point it asked for.
            Vector3 destination = station.StandOffPosition;
            if (NavMesh.SamplePosition(destination, out NavMeshHit navHit, 1f, NavMesh.AllAreas))
                destination = navHit.position;

            _target = destination;
            _mover?.RequestMoveTo(destination);
            ShowMarker(destination);
        }

        /// <summary>
        /// The walk is over: open the station the player walked to, if they are close enough to
        /// use it. A walk that stopped short of the station leaves the panel closed — the server
        /// would refuse the request anyway.
        /// </summary>
        private void OpenPendingStation()
        {
            CraftingStation station = _pendingStation;
            _pendingStation = null;

            if (station == null)
                return;

            if (!PlayerCrafting.IsWithinReach(transform.position, station.transform.position))
                return;

            station.RequestOpenFromLocalPlayer();
        }

        /// <summary>
        /// The player clicked a bank teller: stand in front of it and open its window when the
        /// walk is over. Already within reach, the teller opens at once.
        /// </summary>
        public void BeginTellerApproach(BankTeller teller)
        {
            _pendingTeller = null;
            _pendingStation = null;
            _pendingNode = null;

            if (teller == null)
                return;

            if (BankTeller.IsWithinReach(transform.position, teller.transform.position))
            {
                teller.RequestOpenFromLocalPlayer();
                return;
            }

            _pendingTeller = teller;

            Vector3 destination = teller.StandOffPosition;
            if (NavMesh.SamplePosition(destination, out NavMeshHit navHit, 1f, NavMesh.AllAreas))
                destination = navHit.position;

            _target = destination;
            _mover?.RequestMoveTo(destination);
            ShowMarker(destination);
        }

        /// <summary>The walk is over: open the teller the player walked to, if still in reach.</summary>
        private void OpenPendingTeller()
        {
            BankTeller teller = _pendingTeller;
            _pendingTeller = null;

            if (teller == null)
                return;

            if (!BankTeller.IsWithinReach(transform.position, teller.transform.position))
                return;

            teller.RequestOpenFromLocalPlayer();
        }

        /// <summary>Closes the bank window, if one is open.</summary>
        public void CloseBank()
        {
            BankFrameView frame = FindFirstObjectByType<BankFrameView>();
            if (frame != null && frame.IsOpen)
                frame.Conceal();
        }

        /// <summary>
        /// The player clicked a vein: walk to where they can reach it and start gathering on
        /// arrival. Already within reach, the click gathers at once — the server bounds the
        /// session on its own, so this only saves the walk.
        /// </summary>
        public void BeginNodeApproach(ResourceNode node)
        {
            _pendingNode = null;
            _pendingStation = null;
            _pendingTeller = null;

            if (node == null)
                return;

            if (GatheringRules.IsWithinReach(transform.position, node.transform.position, node.InteractRange))
            {
                node.RequestGatherFromLocalPlayer();
                return;
            }

            _pendingNode = node;

            // Same sampled point the mover is asked for and the server samples: the marker and
            // the arrival check would otherwise disagree by the sampling radius.
            Vector3 destination = node.StandOffPositionFrom(transform.position);
            if (NavMesh.SamplePosition(destination, out NavMeshHit navHit, 1f, NavMesh.AllAreas))
                destination = navHit.position;

            _target = destination;
            _mover?.RequestMoveTo(destination);
            ShowMarker(destination);
        }

        /// <summary>
        /// The walk is over: gather from the vein the player walked to, if it is still within
        /// reach. The server refuses the request outright otherwise, so asking early is pointless.
        /// </summary>
        private void GatherPendingNode()
        {
            ResourceNode node = _pendingNode;
            _pendingNode = null;

            if (node == null)
                return;

            if (!GatheringRules.IsWithinReach(transform.position, node.transform.position, node.InteractRange))
                return;

            node.RequestGatherFromLocalPlayer();
        }

        private void ShowMarker(Vector3 point)
        {
            if (_marker != null)
                Destroy(_marker);

            _marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _marker.transform.position = point + Vector3.up * 0.02f;
            _tracker.Begin(point);
            _marker.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _marker.transform.localScale = Vector3.one * 2f;

            // The marker is a picture of the destination, not a surface: left collidable it
            // would sit in front of the floor and swallow the next click aimed at it.
            var collider = _marker.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = _marker.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = new Color(1f, 1f, 0f, 0.4f);
        }
    }
}