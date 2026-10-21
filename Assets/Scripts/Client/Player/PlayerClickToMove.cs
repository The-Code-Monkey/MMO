using FishNet.Object;
using MMO.Crafting;
using MMO.Gameplay;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace MMO.Client
{
    public class PlayerClickToMove : NetworkBehaviour
    {
        [SerializeField] private LayerMask _walkableLayers = ~0;

        private Camera _mainCam;
        private PlayerMover _mover;
        private Vector3 _target;
        private GameObject _marker;
        private CraftingStation _pendingStation;
        private readonly ClickToMoveMarkerTracker _tracker = new ClickToMoveMarkerTracker();

        private void Awake()
        {
            _mover = GetComponent<PlayerMover>();
        }

        private void Update()
        {
            if (!IsOwner)
                return;

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                SetDestinationToMouseClick();
            }

            if (UpdateMarker())
                OpenPendingStation();
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

                // A click on anything else is just a destination: the pending station is dropped,
                // because the walk this click starts is not towards a station.
                _pendingStation = null;

                // The server accepts a click by sampling the NavMesh within 1 m and walks to the
                // sampled point, so the client must mark and measure against the same point —
                // otherwise the arrival check below can miss (the player stops short of the raw
                // click) and the marker outlives the walk.
                Vector3 destination = NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 1f, NavMesh.AllAreas)
                    ? navHit.position
                    : hit.point;

                _target = destination;
                _mover?.RequestMoveTo(destination);
                ShowMarker(destination);
            }
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