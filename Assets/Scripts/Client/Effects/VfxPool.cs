using System.Collections.Generic;
using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// A fixed local pool for one-shot combat bursts. Every instance is built during
    /// <see cref="Configure"/>; a fight only flips objects active and inactive.
    ///
    /// <para>
    /// When every instance is in use, <see cref="Rent"/> returns null instead of allocating. The
    /// caller drops that effect: presentation degrades under a ZvZ burst, but the server and the
    /// client never see a mid-combat allocation storm.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VfxPool : MonoBehaviour
    {
        private readonly Stack<GameObject> _idle = new();
        private GameObject _template;

        /// <summary>Instances prewarmed, or zero before <see cref="Configure"/>.</summary>
        public int Capacity { get; private set; }

        /// <summary>Instances currently waiting to be rented.</summary>
        public int Available => _idle.Count;

        /// <summary>
        /// Prewarms <paramref name="capacity"/> inactive copies of <paramref name="template"/>.
        /// Reconfiguring replaces the previous pool.
        /// </summary>
        public void Configure(GameObject template, int capacity)
        {
            if (_template != null)
                Clear();

            _template = template;
            Capacity = Mathf.Max(1, capacity);

            if (_template == null)
                return;

            for (int i = 0; i < Capacity; i++)
                _idle.Push(CreateInstance());
        }

        /// <summary>
        /// Activates an idle instance at <paramref name="position"/>. Null means the pool is
        /// exhausted; no allocation happens.
        /// </summary>
        public GameObject Rent(Vector3 position, float lifetime)
        {
            if (_idle.Count == 0)
                return null;

            GameObject instance = _idle.Pop();
            instance.transform.position = position;
            instance.SetActive(true);

            PooledVfx pooled = instance.GetComponent<PooledVfx>();
            if (pooled != null)
                pooled.Play(lifetime);

            return instance;
        }

        /// <summary>Deactivates an instance and puts it back at the front of the queue.</summary>
        public void Return(GameObject instance)
        {
            if (instance == null)
                return;

            instance.SetActive(false);
            _idle.Push(instance);
        }

        private GameObject CreateInstance()
        {
            GameObject instance = Instantiate(_template, transform);
            instance.name = "PooledVfx";
            instance.SetActive(false);

            PooledVfx pooled = instance.GetComponent<PooledVfx>();
            if (pooled == null)
                pooled = instance.AddComponent<PooledVfx>();

            pooled.SetPool(this);
            return instance;
        }

        private void Clear()
        {
            while (_idle.Count > 0)
            {
                GameObject instance = _idle.Pop();
                if (instance != null)
                    Destroy(instance);
            }

            Capacity = 0;
        }

        private void OnDestroy() => Clear();
    }
}
