using FishNet.Object;
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

            if (_marker != null && Vector3.Distance(transform.position, _target) < 0.5f)
            {
                Destroy(_marker);
                _marker = null;
            }
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
            _marker.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _marker.transform.localScale = Vector3.one * 2f;

            var renderer = _marker.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = new Color(1f, 1f, 0f, 0.4f);
        }
    }
}