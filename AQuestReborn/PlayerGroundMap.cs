using System.Collections.Generic;
using System.Numerics;

namespace AQuestReborn
{
    /// <summary>
    /// Records the player's ground positions over time to build a height map.
    /// NPCs can query this to get a ground-accurate Y value at any XZ position
    /// the player has previously walked through.
    /// </summary>
    public class PlayerGroundMap
    {
        // Grid resolution in game units. 0.5 = 2 samples per unit.
        private const float GridResolution = 0.5f;

        // Cached height maps per territory ID.
        private readonly Dictionary<uint, Dictionary<long, float>> _territoryMaps = new Dictionary<uint, Dictionary<long, float>>();

        // The currently active territory map.
        private Dictionary<long, float> _activeMap = new Dictionary<long, float>();

        /// <summary>
        /// Switch to the height map for the given territory. Creates a new one if first visit.
        /// </summary>
        public void SetTerritory(uint territoryId)
        {
            if (!_territoryMaps.TryGetValue(territoryId, out var map))
            {
                map = new Dictionary<long, float>();
                _territoryMaps[territoryId] = map;
            }
            _activeMap = map;
        }

        // Last recorded position to avoid redundant writes.
        private Vector3 _lastRecordedPosition = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        /// <summary>
        /// Record the player's current position into the active height map.
        /// Skips if the player hasn't moved far enough from the last recorded point.
        /// </summary>
        public void RecordPosition(Vector3 position)
        {
            float dx = position.X - _lastRecordedPosition.X;
            float dz = position.Z - _lastRecordedPosition.Z;
            if (dx * dx + dz * dz < GridResolution * GridResolution)
            {
                return;
            }
            long key = QuantizeKey(position.X, position.Z);
            _activeMap[key] = position.Y;
            _lastRecordedPosition = position;
        }

        /// <summary>
        /// Get the ground Y height at the given XZ position.
        /// Searches the exact grid cell first, then expands outward to find the
        /// closest recorded point. Covers up to ~5 units from the query position
        /// to account for NPC follow offsets from the player's trail.
        /// Returns the fallback Y if no recorded data is available.
        /// </summary>
        public float GetGroundY(float x, float z, float fallbackY)
        {
            long key = QuantizeKey(x, z);
            if (_activeMap.TryGetValue(key, out float y))
            {
                return y;
            }

            // Search expanding rings up to 10 cells out (5 units at 0.5 resolution)
            float closestY = fallbackY;
            float closestDistSq = float.MaxValue;
            int gx = Quantize(x);
            int gz = Quantize(z);
            const int maxRadius = 10;
            for (int r = 1; r <= maxRadius; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        // Only check the outer ring of each radius to avoid redundant checks
                        if (System.Math.Abs(dx) != r && System.Math.Abs(dz) != r) continue;

                        long neighborKey = PackKey(gx + dx, gz + dz);
                        if (_activeMap.TryGetValue(neighborKey, out float ny))
                        {
                            float nx = (gx + dx) * GridResolution;
                            float nz = (gz + dz) * GridResolution;
                            float distSq = (nx - x) * (nx - x) + (nz - z) * (nz - z);
                            if (distSq < closestDistSq)
                            {
                                closestDistSq = distSq;
                                closestY = ny;
                            }
                        }
                    }
                }
                // If we found something in this ring, no need to go further
                if (closestDistSq < float.MaxValue) break;
            }
            return closestY;
        }

        private int Quantize(float value)
        {
            return (int)System.Math.Floor(value / GridResolution);
        }

        private long QuantizeKey(float x, float z)
        {
            return PackKey(Quantize(x), Quantize(z));
        }

        private long PackKey(int gx, int gz)
        {
            return ((long)gx << 32) | (uint)gz;
        }
    }
}
