using System.Linq;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>A Pool-of-Radiance "block": walk into the volume, fight the listed
    /// monsters. Server-only logic; consumed after victory so cleared zones stay
    /// pacified. Placed by the bootstrap from content/zones JSON.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public class EncounterTrigger : MonoBehaviour
    {
        public string EncounterId = "";
        public string ZoneId = "";
        public string DisplayName = "";
        public bool RequiredForClear = true;
        public string[] MonsterIds = System.Array.Empty<string>();
        public bool Consumed { get; private set; }

        private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

        private void OnTriggerEnter(Collider other)
        {
            var combat = CombatManager.Instance;
            if (combat == null || !combat.IsServerStarted) return;   // server decides
            if (Consumed || combat.InCombat.Value) return;
            if (other.GetComponentInParent<PlayerCharacterHolder>() == null) return;
            if (MonsterIds.Length == 0) return;

            combat.StartEncounter(this);
        }

        /// <summary>Server marks the encounter cleared; collider off = permanently pacified.</summary>
        public void Consume()
        {
            Consumed = true;
            GetComponent<BoxCollider>().enabled = false;
        }
    }
}
