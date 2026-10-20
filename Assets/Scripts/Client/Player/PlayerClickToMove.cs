using FishNet.Object;
using MMO.Gameplay;
using UnityEngine;
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
                _target = hit.point;
                _mover?.RequestMoveTo(hit.point);
                ShowMarker(hit.point);
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