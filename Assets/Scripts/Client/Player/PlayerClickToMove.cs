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

            UpdateMarker();
        }

        /// <summary>
        /// Clears the destination marker once the walk is over. The decision lives in
        /// <see cref="ClickToMoveMarkerTracker"/>: a direct arrival clears it at once, and a
        /// player that has stopped moving clears it too — the server owns the walk and stops on
        /// its own sampled point, so a distance check alone would leave the marker stuck.
        /// </summary>
        private void UpdateMarker()
        {
            if (_marker == null)
            {
                _tracker.Cancel();
                return;
            }

            if (_tracker.ShouldClear(transform.position))
                ClearMarker();
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
                // If we hit a crafting station, path the player to within 3 units of it,
                // then the OnMouseDown() interaction will open the UI.
                if (hit.collider.TryGetComponent(out CraftingStation station))
                {
                    // Find a destination on the NavMesh within 3 units of the station.
                    // We subtract the station forward direction scaled to 3 units so the
                    // player stops in front of the station rather on top of it.
                    Vector3 stationPos = station.transform.position;
                    Vector3 forward = station.transform.forward;
                    Vector3 dest = stationPos - forward * 3f;

                    if (NavMesh.SamplePosition(dest, out var navHitStation, 3f, NavMesh.AllAreas))
                        dest = navHitStation.position;

                    _target = dest;
                    _mover?.RequestMoveTo(dest);
                    ShowMarker(dest);
                    return;
                }

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