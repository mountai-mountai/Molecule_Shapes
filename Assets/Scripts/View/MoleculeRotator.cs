using UnityEngine;
using UnityEngine.InputSystem;

namespace Molecule_Shapes.View
{
    // Drag with the left mouse button to spin the molecule. Rotates this GameObject's transform in
    // world space (premultiply) so the drag axes stay fixed to the screen regardless of how the
    // molecule is currently oriented - the familiar "grab and turn" feel.
    //
    // Uses the new Input System (this project's Active Input Handling is "Input System Package").
    public class MoleculeRotator : MonoBehaviour
    {
        [Tooltip("Degrees of rotation per pixel of mouse drag.")]
        [SerializeField] private float sensitivity = 0.3f;

        [Tooltip("Camera whose right axis is used for vertical (pitch) drags. Defaults to Camera.main.")]
        [SerializeField] private Camera viewCamera;

        private void Awake()
        {
            if (viewCamera == null) viewCamera = Camera.main;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.isPressed) return;

            Vector2 delta = mouse.delta.ReadValue();
            if (delta.sqrMagnitude < 1e-6f) return;

            Vector3 right = viewCamera != null ? viewCamera.transform.right : Vector3.right;

            // Horizontal drag -> yaw about world up; vertical drag -> pitch about the camera's right axis.
            Quaternion yaw = Quaternion.AngleAxis(-delta.x * sensitivity, Vector3.up);
            Quaternion pitch = Quaternion.AngleAxis(delta.y * sensitivity, right);

            // Premultiply so rotation happens in world/screen space, not the object's local space.
            transform.rotation = yaw * pitch * transform.rotation;
        }
    }
}
