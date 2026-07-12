// Small scale-in / scale-out animator for atom and lone-pair views. On spawn it grows from zero to the
// view's natural scale (a satisfying "pop"); on removal, Collapse() shrinks it to zero and then destroys
// the GameObject, so add/remove reads as an animation instead of a hard cut.
//
// Not used for bonds - BondView rewrites its cylinders' scale every frame, which would fight this.

using System.Collections;
using UnityEngine;

namespace Molecule_Shapes.View
{
    public class PopScale : MonoBehaviour
    {
        private bool _collapsing;

        // Grows from zero to the current localScale (captured now as the target).
        public void PopIn(float duration)
        {
            Vector3 target = transform.localScale;
            if (duration <= 0f) return;
            StopAllCoroutines();
            StartCoroutine(Animate(Vector3.zero, target, duration, destroyAtEnd: false));
        }

        // Shrinks to zero, then destroys the GameObject.
        public void Collapse(float duration)
        {
            if (_collapsing) return;
            _collapsing = true;
            if (duration <= 0f) { Destroy(gameObject); return; }
            StopAllCoroutines();
            StartCoroutine(Animate(transform.localScale, Vector3.zero, duration, destroyAtEnd: true));
        }

        private IEnumerator Animate(Vector3 from, Vector3 to, float duration, bool destroyAtEnd)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                transform.localScale = Vector3.Lerp(from, to, k);
                yield return null;
            }
            transform.localScale = to;
            if (destroyAtEnd) Destroy(gameObject);
        }
    }
}
