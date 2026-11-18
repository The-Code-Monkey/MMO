using System.Collections.Generic;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// The corners of a NavMesh route, walked in order.
    ///
    /// <para>
    /// A route is a list of corners: the point the walker starts on, every turn it has to
    /// make, and the destination. This class holds that list and answers the only question the
    /// mover asks of it — <em>where do I walk to next?</em> — stepping over the corners the
    /// walker has already reached.
    /// </para>
    ///
    /// <para>
    /// Pure state and arithmetic over positions — no clock, no network, no scene — so an
    /// EditMode test can judge the whole life of a route in microseconds, exactly like
    /// the route a destination intent plans over the NavMesh.
    /// </para>
    /// </summary>
    public sealed class WalkPath
    {
        /// <summary>How close the walker must be to a corner for that corner to count as reached.</summary>
        public const float CornerEpsilon = 0.01f;

        private readonly List<Vector3> _corners = new();
        private int _index;

        /// <summary>True when there is no route, so the walker heads straight at its target.</summary>
        public bool IsEmpty => _corners.Count == 0;

        /// <summary>
        /// Points the walk at a new route. The first corner is where the route starts — the
        /// walker is already standing on it — so it is skipped.
        /// </summary>
        public void SetCorners(Vector3[] corners)
        {
            _corners.Clear();
            _index = 0;

            if (corners == null)
                return;

            for (int i = 1; i < corners.Length; i++)
                _corners.Add(corners[i]);
        }

        /// <summary>Forgets the route; the walker falls back to walking straight at its target.</summary>
        public void Clear()
        {
            _corners.Clear();
            _index = 0;
        }

        /// <summary>
        /// The next point to walk to. Corners the walker has already reached are stepped over,
        /// so the waypoint returned is always further away than <see cref="CornerEpsilon"/>
        /// unless it is the last one.
        /// </summary>
        public Vector3 NextWaypoint(Vector3 position)
        {
            while (_index < _corners.Count - 1 && PlanarDistance(position, _corners[_index]) <= CornerEpsilon)
                _index++;

            return _corners.Count == 0 ? Vector3.zero : _corners[_index];
        }

        /// <summary>The distance between two points as the walker sees it: on the ground, not in the air.</summary>
        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
