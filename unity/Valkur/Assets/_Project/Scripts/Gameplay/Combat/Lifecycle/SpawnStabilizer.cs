using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Resolves overlapping entities by nudging them apart.
    /// Maps to Python's SpawnStabilizationSystem.
    /// Runs for a few frames after spawn to prevent stacking.
    /// </summary>
    public class SpawnStabilizer : MonoBehaviour
    {
        [SerializeField, Tooltip("Number of FixedUpdate frames to run stabilization.")]
        private int framesToStabilize = 5;

        [SerializeField, Tooltip("Separation force applied per overlap.")]
        private float separationForce = 2f;

        [SerializeField, Tooltip("Detection radius for overlap checks.")]
        private float detectionRadius = 1f;

        private int _framesRemaining;
        private static readonly Collider2D[] _overlapBuffer = new Collider2D[16];

        private void OnEnable()
        {
            _framesRemaining = framesToStabilize;
        }

        private void FixedUpdate()
        {
            if (_framesRemaining <= 0)
            {
                Destroy(this);
                return;
            }

            _framesRemaining--;

            int count = Physics2D.OverlapCircleNonAlloc(
                transform.position, detectionRadius, _overlapBuffer);

            Vector2 pushDir = Vector2.zero;
            int neighbors = 0;

            for (int i = 0; i < count; i++)
            {
                var other = _overlapBuffer[i];
                if (other == null || other.gameObject == gameObject) continue;
                if (other.isTrigger) continue;

                Vector2 diff = (Vector2)transform.position - (Vector2)other.transform.position;
                float dist = diff.magnitude;
                if (dist < 0.01f) diff = Random.insideUnitCircle.normalized;
                else diff = diff.normalized;

                pushDir += diff / Mathf.Max(dist, 0.1f);
                neighbors++;
            }

            if (neighbors > 0)
            {
                pushDir /= neighbors;
                var rb = GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.MovePosition(rb.position + pushDir * separationForce * Time.fixedDeltaTime);
                }
                else
                {
                    transform.position += (Vector3)(pushDir * separationForce * Time.fixedDeltaTime);
                }
            }
        }
    }
}
