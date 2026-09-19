using System.Collections.Generic;
using UnityEngine;

namespace BarrowsEnd.Spike
{
    /// <summary>THROWAWAY (Task 2.0b): hand-rolled billboard + rock + y-sort, to judge the look before Tasks 2.1/2.2 build the real ones.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class SpikeCharacter : MonoBehaviour
    {
        public Transform card;            // billboarded
        public Transform rockPivot;       // at the feet
        public List<SpriteRenderer> layers = new();
        public float walkRange = 0f;      // 0 = stationary
        public float speed = 2f;
        public float peakDegrees = 6f;
        public float strideLength = 1f;
        public float hopHeight = 0f;
        public float phase;
        private float amplitude, dir = 1f, startX;
        private Vector3 lastPos;
        private float velocity;

        private void Start() { startX = transform.position.x; lastPos = transform.position; }

        private void Update()
        {
            if (walkRange > 0f)
            {
                var p = transform.position;
                p.x += dir * speed * Time.deltaTime;
                if (p.x > startX + walkRange) dir = -1f; else if (p.x < startX - walkRange) dir = 1f;
                transform.position = p;
            }
            var dt = Mathf.Max(Time.deltaTime, 1e-5f);
            velocity = (transform.position - lastPos).magnitude / dt;
            lastPos = transform.position;
            var moving = velocity > 0.1f;
            amplitude = Mathf.MoveTowards(amplitude, moving ? 1f : 0f, Time.deltaTime * (moving ? 8f : 6f));
            if (moving) phase += velocity * dt / strideLength;
            ApplyPose();
        }

        public void ApplyPose()
        {
            var tilt = Mathf.Sin(2f * Mathf.PI * phase) * peakDegrees * amplitude;
            rockPivot.localRotation = Quaternion.Euler(0f, 0f, tilt);
            var hop = Mathf.Abs(Mathf.Sin(Mathf.PI * phase)) * hopHeight * amplitude;
            rockPivot.localPosition = new Vector3(0f, -0.5f + hop, 0f);
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) card.rotation = cam.transform.rotation;
            ApplySort();
        }

        public void ApplySort()
        {
            var b = -Mathf.RoundToInt(transform.position.z * 8f) * 16; // CharacterRoot Z, never the card's
            b = Mathf.Clamp(b, -32000, 32000 - layers.Count);
            for (var i = 0; i < layers.Count; i++) layers[i].sortingOrder = b + i;
        }
    }
}
