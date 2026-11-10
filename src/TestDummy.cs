using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Test mode only: a local training dummy that stands in for a hider,
    /// so the monster's attack / touch kill can be tested alone.
    /// </summary>
    internal class TestDummy : MonoBehaviour
    {
        public bool Dead { get; private set; }
        public string DummyName;
        public int Hp = 100;
        private Transform _visual;
        private Renderer[] _renderers;
        private float _fall;
        private float _seed;

        public static TestDummy Spawn(Vector3 position, int number)
        {
            var go = new GameObject("HNS_Dummy_" + number);
            go.transform.position = position;
            var d = go.AddComponent<TestDummy>();
            d.DummyName = "Dummy " + number;
            d.Build();
            return d;
        }

        private void Build()
        {
            _seed = Random.value * 10f;
            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);
            Part(PrimitiveType.Capsule, new Vector3(0, 0.75f, 0), new Vector3(0.5f, 0.75f, 0.5f));
            Part(PrimitiveType.Sphere, new Vector3(0, 1.65f, 0), Vector3.one * 0.4f);
            var eye = Part(PrimitiveType.Cube, new Vector3(0, 1.68f, 0.18f), new Vector3(0.28f, 0.08f, 0.06f));
            eye.GetComponent<Renderer>().material.color = Color.black;
            _renderers = _visual.GetComponentsInChildren<Renderer>();
            foreach (var r in _renderers)
                if (r.gameObject != eye) r.material.color = new Color(1f, 0.8f, 0.15f);
        }

        private GameObject Part(PrimitiveType type, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(_visual, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            return go;
        }

        private float _stunUntil, _floatUntil, _lift;
        private bool _stunColor;

        /// <summary>Grab zones: stunned (light blue, no sway) and optionally floating.</summary>
        public void Stun(float seconds, bool zeroG)
        {
            if (Dead) return;
            _stunUntil = Mathf.Max(_stunUntil, Time.time + seconds);
            if (zeroG) _floatUntil = Mathf.Max(_floatUntil, Time.time + seconds);
        }

        public void Kill(Vector3 from)
        {
            if (Dead) return;
            Dead = true;
            foreach (var r in _renderers) r.material.color = new Color(0.7f, 0.05f, 0.05f);
            var away = transform.position - from; away.y = 0;
            if (away.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(-away.normalized);
            Destroy(gameObject, 3f);
        }

        private void Update()
        {
            if (Dead)
            {
                _fall = Mathf.MoveTowards(_fall, 90f, 360f * Time.deltaTime);
                _visual.localEulerAngles = new Vector3(-_fall, 0, 0);
                return;
            }
            bool stunned = Time.time < _stunUntil;
            if (stunned != _stunColor)
            {
                _stunColor = stunned;
                foreach (var r in _renderers)
                    if (r.material.color != Color.black) r.material.color = stunned ? new Color(0.45f, 0.85f, 1f) : new Color(1f, 0.8f, 0.15f);
            }
            _lift = Mathf.MoveTowards(_lift, Time.time < _floatUntil ? 1.5f : 0f, (Time.time < _floatUntil ? 1.2f : 6f) * Time.deltaTime);
            _visual.localPosition = new Vector3(0, _lift, 0);
            if (stunned) { _visual.localEulerAngles = new Vector3(Mathf.Sin(Time.time * 30f) * 3f, 0, 0); return; }
            // idle sway so it's easy to spot
            _visual.localEulerAngles = new Vector3(0, Mathf.Sin(Time.time * 0.7f + _seed) * 40f, Mathf.Sin(Time.time * 2f + _seed) * 4f);
        }
    }
}
