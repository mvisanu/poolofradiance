using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>A physical barrier into a zone that opens when that zone's quest becomes
    /// Active (i.e. the previous zone was turned in). Reads replicated state, so host and
    /// clients open in sync; the collider keeps players out until then.</summary>
    public class ZoneGate : MonoBehaviour
    {
        public int ZoneIndex;

        private bool _opened;

        private void Update()
        {
            if (_opened) return;
            var director = GameDirector.Instance;
            if (director == null) return;
            if (director.GetZoneState(ZoneIndex) == QuestState.Locked) return;

            _opened = true;
            // Sink the gate rather than destroying it: cheap "the way is open" read.
            transform.position += Vector3.down * 2.6f;
            var collider = GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
        }
    }
}
