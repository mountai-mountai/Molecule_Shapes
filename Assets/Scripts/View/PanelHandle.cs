// Marker placed on a panel's grab bar. PanelGrabController raycasts for this and repositions PanelRoot
// (the whole panel, including its canvas) when the user grabs the handle.

using UnityEngine;

namespace Molecule_Shapes.View
{
    public class PanelHandle : MonoBehaviour
    {
        [Tooltip("The transform that gets moved when this handle is grabbed (the panel's root object).")]
        public Transform PanelRoot;
    }
}
