using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Server lookup anchor for one campaign location. The whole campaign can
    /// live in spatially separated, cheaply rendered site cells while the party travels
    /// through a single authoritative path.</summary>
    public class CampaignDestination : MonoBehaviour
    {
        public int ZoneIndex = -1;   // -1 = Council Quarter
    }
}
