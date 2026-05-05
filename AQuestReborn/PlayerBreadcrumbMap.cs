using System.Collections.Generic;
using System.Numerics;

namespace AQuestReborn
{
    /// <summary>
    /// Records the chronological path the player takes in a given territory.
    /// Used by NPCs to pathfind over long distances by retracing the player's steps.
    /// </summary>
    public class PlayerBreadcrumbMap
    {
        private readonly Dictionary<uint, List<Vector3>> _territoryMaps = new Dictionary<uint, List<Vector3>>();
        private List<Vector3> _activeBreadcrumbs = new List<Vector3>();

        // Distance squared between breadcrumbs (1.5 units)
        private const float MinDistanceSq = 1.5f * 1.5f;

        public void SetTerritory(uint territoryId)
        {
            if (!_territoryMaps.TryGetValue(territoryId, out var map))
            {
                map = new List<Vector3>();
                _territoryMaps[territoryId] = map;
            }
            _activeBreadcrumbs = map;
        }

        public void RecordPosition(Vector3 position)
        {
            if (_activeBreadcrumbs.Count == 0)
            {
                _activeBreadcrumbs.Add(position);
                return;
            }

            Vector3 lastPos = _activeBreadcrumbs[_activeBreadcrumbs.Count - 1];
            if (Vector3.DistanceSquared(lastPos, position) >= MinDistanceSq)
            {
                // To avoid massive memory leaks in long sessions, cap the breadcrumb trail.
                if (_activeBreadcrumbs.Count > 10000)
                {
                    _activeBreadcrumbs.RemoveAt(0);
                }
                _activeBreadcrumbs.Add(position);
            }
        }

        /// <summary>
        /// Get an optimized path through the breadcrumbs from the given start to the given end position.
        /// Start is usually the NPC, End is usually the Player.
        /// </summary>
        public List<Vector3> GetPath(Vector3 start, Vector3 end, out int startIndex)
        {
            startIndex = -1;
            if (_activeBreadcrumbs.Count < 2) return new List<Vector3>();

            int closestToStart = FindClosestIndex(start);
            int closestToEnd = FindClosestIndex(end);

            if (closestToStart == -1 || closestToEnd == -1) return new List<Vector3>();

            List<Vector3> path = new List<Vector3>();
            
            // If the start is chronologically after the end, walk backwards!
            if (closestToStart <= closestToEnd)
            {
                for (int i = closestToStart; i <= closestToEnd; i++)
                {
                    path.Add(_activeBreadcrumbs[i]);
                }
            }
            else
            {
                for (int i = closestToStart; i >= closestToEnd; i--)
                {
                    path.Add(_activeBreadcrumbs[i]);
                }
            }
            
            // Basic Shortcut/Loop Optimization:
            // If a node later in the path is physically very close to an earlier node, skip the middle nodes.
            // This prevents NPCs from running in circles if the player mined in a circle.
            return OptimizePath(path);
        }

        private List<Vector3> OptimizePath(List<Vector3> originalPath)
        {
            if (originalPath.Count <= 2) return originalPath;

            List<Vector3> optimized = new List<Vector3>();
            optimized.Add(originalPath[0]);

            int currentIndex = 0;
            while (currentIndex < originalPath.Count - 1)
            {
                int bestJumpIndex = currentIndex + 1;
                
                // Look ahead for shortcuts. Limit lookahead to prevent massive spikes in CPU usage.
                int lookAheadLimit = System.Math.Min(originalPath.Count - 1, currentIndex + 100);
                for (int lookAhead = lookAheadLimit; lookAhead > currentIndex + 1; lookAhead--)
                {
                    float distSq = Vector3.DistanceSquared(originalPath[currentIndex], originalPath[lookAhead]);
                    // If a node way ahead is within 3.0 units, take the shortcut!
                    if (distSq < 3.0f * 3.0f)
                    {
                        bestJumpIndex = lookAhead;
                        break;
                    }
                }
                
                optimized.Add(originalPath[bestJumpIndex]);
                currentIndex = bestJumpIndex;
            }

            return optimized;
        }

        private int FindClosestIndex(Vector3 pos)
        {
            int bestIndex = -1;
            float minDistSq = float.MaxValue;

            for (int i = 0; i < _activeBreadcrumbs.Count; i++)
            {
                float distSq = Vector3.DistanceSquared(pos, _activeBreadcrumbs[i]);
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    bestIndex = i;
                }
            }
            
            // If the closest point is absurdly far away, don't use the breadcrumb system
            // (e.g. NPC was told to stay, player teleported away, then told to follow)
            if (minDistSq > 20.0f * 20.0f) return -1;

            return bestIndex;
        }
    }
}
