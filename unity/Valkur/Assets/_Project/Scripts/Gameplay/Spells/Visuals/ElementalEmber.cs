using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Trailing ember/spark/shard with parameterized drag and buoyancy. Drives all
    /// element trails (fiery embers rise, ice shards fall, lightning sparks scatter).
    /// </summary>
    internal class ElementalEmber : MonoBehaviour
    {
        private Vector2 _vel;
        private float _life, _age, _scale, _drag, _buoyancy;
        private SpriteRenderer _sr;

        public void Init(Vector2 velocity, float lifetime, float scale, float drag, float buoyancy)
        {
            _vel = velocity;
            _life = Mathf.Max(0.05f, lifetime);
            _scale = scale;
            _drag = drag;
            _buoyancy = buoyancy;
            transform.localScale = Vector3.one * _scale;
            _sr = GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            float t = _age / _life;
            if (t >= 1f) { Destroy(gameObject); return; }

            _vel *= 1f - _drag * dt;
            _vel.y += _buoyancy * dt;
            transform.position += (Vector3)(_vel * dt);

            float scaleT = 1f - t * 0.6f;
            transform.localScale = Vector3.one * _scale * scaleT;
            if (_sr != null)
            {
                var c = _sr.color;
                c.a = (1f - t) * (1f - t);
                _sr.color = c;
            }
        }
    }
}
