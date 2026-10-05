using UnityEngine;

namespace Fomoxa.Unity
{
    [DisallowMultipleComponent]
    public sealed class ObserverRange : MonoBehaviour
    {
        [SerializeField] private float radius = 50f;

        public float Radius
        {
            get => radius;
            set => radius = Mathf.Max(0f, value);
        }
    }
}
