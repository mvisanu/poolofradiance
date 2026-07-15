using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Build-time provenance for generated environment art. The travel self-test
    /// uses it to prove that licensed packs are represented without coupling runtime code
    /// to editor-only asset discovery.</summary>
    public sealed class EnvironmentArtTag : MonoBehaviour
    {
        public string SourcePack;
        public string Role;
        public string ZoneId;
    }
}
