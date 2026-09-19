using UnityEngine;

namespace RDA
{
    /// <summary>
    /// Isolates Physics.Raycast so terrain scan can soft-fail if the module is missing.
    /// </summary>
    internal static class PhysicsRaycastHelper
    {
        private static bool _unavailable;

        internal static bool Try(Vector3 origin, Vector3 direction, float maxDistance, out float hitDistance)
        {
            hitDistance = 0f;
            if (_unavailable)
            {
                return false;
            }

            try
            {
                if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance))
                {
                    hitDistance = hit.distance;
                    return hitDistance > 0.01f;
                }

                return false;
            }
            catch (System.Exception)
            {
                _unavailable = true;
                return false;
            }
        }
    }
}
