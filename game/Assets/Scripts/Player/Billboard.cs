using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Keeps a world-space label facing the camera. This attachable component
    /// has its own file so Unity can serialize a stable script GUID into generated scenes.</summary>
    public class Billboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            transform.rotation = Quaternion.LookRotation(
                transform.position - cam.transform.position);
        }
    }
}
