using System.Collections.Generic;
using System.Numerics;

namespace AQuestReborn
{
    /// <summary>
    /// Records the player's ground positions over time to build a height map.
    /// NPCs can query this to get a ground-accurate Y value at any XZ position
    /// the player has previously walked through. Now supports multi-level geometry.
    /// </summary>
    public class PlayerGroundMap
    {
        // Grid resolution in game units. 0.5 = 2 samples per unit.
        private const float GridResolution = 0.5f;
        
        // Vertical tolerance to consider a Y value as the "same floor"
        private const float VerticalTolerance = 1.5f;

        // Cached height maps per territory ID.
        private readonly Dictionary<uint, Dictionary<long, List<float>>> _territoryMaps = new Dictionary<uint, Dictionary<long, List<float>>>();

        // The currently active territory map.
        private Dictionary<long, List<float>> _activeMap = new Dictionary<long, List<float>>();

        /// <summary>
        /// Switch to the height map for the given territory. Creates a new one if first visit.
        /// </summary>
        public void SetTerritory(uint territoryId)
        {
            if (!_territoryMaps.TryGetValue(territoryId, out var map))
            {
                map = new Dictionary<long, List<float>>();
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
            float dy = position.Y - _lastRecordedPosition.Y;
            
            // Incorporate Y into the distance check so stairs record cleanly
            if (dx * dx + dz * dz + dy * dy < GridResolution * GridResolution)
            {
                return;
            }
            
            AddYValue(position);
            _lastRecordedPosition = position;
        }

        /// <summary>
        /// Record a position directly into the map without updating the player's last recorded position.
        /// Useful for recording other entities like enemies.
        /// </summary>
        public void ForceRecordPosition(Vector3 position, bool requireAdjacentGround = false)
        {
            if (requireAdjacentGround && !HasAdjacentGround(position, 2.0f, 3))
            {
                return;
            }
            AddYValue(position);
        }

        private bool HasAdjacentGround(Vector3 position, float maxVerticalDistance, int searchRadiusCells)
        {
            long key = QuantizeKey(position.X, position.Z);
            if (_activeMap.TryGetValue(key, out var exactList) && exactList.Count > 0)
            {
                if (System.Math.Abs(GetClosestY(exactList, position.Y) - position.Y) <= maxVerticalDistance)
                    return true;
            }

            int gx = Quantize(position.X);
            int gz = Quantize(position.Z);
            for (int r = 1; r <= searchRadiusCells; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (CheckAdjacentKey(gx + dx, gz - r, position.Y, maxVerticalDistance)) return true;
                    if (CheckAdjacentKey(gx + dx, gz + r, position.Y, maxVerticalDistance)) return true;
                }
                for (int dz = -r + 1; dz < r; dz++)
                {
                    if (CheckAdjacentKey(gx - r, gz + dz, position.Y, maxVerticalDistance)) return true;
                    if (CheckAdjacentKey(gx + r, gz + dz, position.Y, maxVerticalDistance)) return true;
                }
            }
            return false;
        }

        private bool CheckAdjacentKey(int cx, int cz, float referenceY, float maxVerticalDistance)
        {
            long neighborKey = PackKey(cx, cz);
            if (_activeMap.TryGetValue(neighborKey, out var yList) && yList.Count > 0)
            {
                if (System.Math.Abs(GetClosestY(yList, referenceY) - referenceY) <= maxVerticalDistance)
                    return true;
            }
            return false;
        }

        private void AddYValue(Vector3 position)
        {
            long key = QuantizeKey(position.X, position.Z);
            if (!_activeMap.TryGetValue(key, out var yList))
            {
                yList = new List<float>();
                _activeMap[key] = yList;
            }

            bool found = false;
            for (int i = 0; i < yList.Count; i++)
            {
                if (System.Math.Abs(yList[i] - position.Y) < VerticalTolerance)
                {
                    // Update the existing floor height to the latest reading
                    yList[i] = position.Y;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                yList.Add(position.Y);
            }
        }

        /// <summary>
        /// Get the ground Y height at the given XZ position.
        /// Searches the exact grid cell first, then expands outward to find the
        /// closest recorded point. Covers up to ~5 units from the query position.
        /// </summary>
        public float GetGroundY(float x, float z, float referenceY)
        {
            long key = QuantizeKey(x, z);
            if (_activeMap.TryGetValue(key, out var exactList) && exactList.Count > 0)
            {
                float bestY = GetClosestY(exactList, referenceY);
                // Ensure we don't snap to a completely different floor just because we share XZ
                if (System.Math.Abs(bestY - referenceY) < 5.0f)
                    return bestY;
            }

            // Search expanding rings up to 10 cells out (5 units at 0.5 resolution)
            float closestY = referenceY;
            float closestDistSq = float.MaxValue;
            int gx = Quantize(x);
            int gz = Quantize(z);
            const int maxRadius = 10;
            
            for (int r = 1; r <= maxRadius; r++)
            {
                // Top and bottom edges
                for (int dx = -r; dx <= r; dx++)
                {
                    CheckKey(gx + dx, gz - r, x, z, referenceY, ref closestDistSq, ref closestY);
                    CheckKey(gx + dx, gz + r, x, z, referenceY, ref closestDistSq, ref closestY);
                }
                // Left and right edges (excluding corners)
                for (int dz = -r + 1; dz < r; dz++)
                {
                    CheckKey(gx - r, gz + dz, x, z, referenceY, ref closestDistSq, ref closestY);
                    CheckKey(gx + r, gz + dz, x, z, referenceY, ref closestDistSq, ref closestY);
                }

                // If we found a valid floor in this ring, stop searching outward
                if (closestDistSq < float.MaxValue) break;
            }
            return closestY;
        }

        private float GetClosestY(List<float> yList, float referenceY)
        {
            float closestY = referenceY;
            float minDiff = float.MaxValue;
            foreach (float y in yList)
            {
                float diff = System.Math.Abs(y - referenceY);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    closestY = y;
                }
            }
            return closestY;
        }

        private void CheckKey(int cx, int cz, float originX, float originZ, float referenceY, ref float closestDistSq, ref float closestY)
        {
            long neighborKey = PackKey(cx, cz);
            if (_activeMap.TryGetValue(neighborKey, out var yList) && yList.Count > 0)
            {
                float nx = cx * GridResolution;
                float nz = cz * GridResolution;
                float distSq = (nx - originX) * (nx - originX) + (nz - originZ) * (nz - originZ);
                
                if (distSq < closestDistSq)
                {
                    float bestY = GetClosestY(yList, referenceY);
                    
                    // Reject floors that are excessively high or low compared to where the NPC expects to be
                    if (System.Math.Abs(bestY - referenceY) < 5.0f)
                    {
                        closestDistSq = distSq;
                        closestY = bestY;
                    }
                }
            }
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
