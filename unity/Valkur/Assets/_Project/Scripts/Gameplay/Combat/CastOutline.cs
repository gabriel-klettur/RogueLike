using UnityEngine;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// Renders a cast telegraph circle on the ground before a spell lands.
    /// Mirrors Python's CastOutlineRenderSystem.
    /// Uses a LineRenderer to draw an expanding circle at the target position.
    /// </summary>
    public class CastOutline : MonoBehaviour
    {
        [SerializeField, Tooltip("Radius of the telegraph circle in world units.")]
        private float radius = 1.5f;

        [SerializeField, Tooltip("Line width.")]
        private float lineWidth = 0.05f;

        [SerializeField, Tooltip("Number of segments in the circle.")]
        private int segments = 32;

        [SerializeField, Tooltip("Line color.")]
        private Color color = new Color(1f, 0.3f, 0.3f, 0.6f);

        [SerializeField, Tooltip("Duration before auto-destroy in seconds. 0 = manual.")]
        private float duration;

        private LineRenderer _lr;
        private float _spawnTime;

        public void Initialize(Vector2 center, float radius, float duration, Color? color = null)
        {
            transform.position = (Vector3)center;
            this.radius = radius;
            this.duration = duration;
            if (color.HasValue) this.color = color.Value;
            _spawnTime = Time.time;
            BuildCircle();
        }

        private void Awake()
        {
            _lr = gameObject.AddComponent<LineRenderer>();
            _lr.useWorldSpace = false;
            _lr.loop = true;
            _lr.startWidth = lineWidth;
            _lr.endWidth = lineWidth;
            _lr.material = new Material(Shader.Find("Sprites/Default"));
            _lr.startColor = color;
            _lr.endColor = color;
            _lr.sortingLayerName = "VFX";
            _lr.sortingOrder = 0;
            _spawnTime = Time.time;
        }

        private void Start()
        {
            if (_lr.positionCount == 0)
                BuildCircle();
        }

        private void Update()
        {
            if (duration > 0f && Time.time - _spawnTime >= duration)
            {
                Destroy(gameObject);
                return;
            }

            // Pulse alpha
            float t = Mathf.PingPong(Time.time * 2f, 1f);
            Color c = color;
            c.a = Mathf.Lerp(0.3f, color.a, t);
            _lr.startColor = c;
            _lr.endColor = c;
        }

        private void BuildCircle()
        {
            _lr.positionCount = segments;
            float step = 360f / segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.Deg2Rad * step * i;
                _lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
        }

        private void OnDestroy()
        {
            // Cleanup runtime material
            if (_lr != null && _lr.material != null)
                Destroy(_lr.material);
        }
    }
}
