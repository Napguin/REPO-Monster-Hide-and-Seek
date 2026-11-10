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
            // idle sway so it's easy to spot
            _visual.localEulerAngles = new Vector3(0, Mathf.Sin(Time.time * 0.7f + _seed) * 40f, Mathf.Sin(Time.time * 2f + _seed) * 4f);
        }
    }
}
