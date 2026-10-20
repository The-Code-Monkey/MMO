using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// Decides when the click-to-move destination marker should disappear.
    ///
    /// <para>
    /// The marker used to be cleared only when the player came within half a metre of the point
    /// it clicked. That check misses: the server owns the walk and stops on its <em>own</em>
    /// NavMesh-sampled point, and the owner sees an interpolated position, so the client can come
    /// to rest a little short of (or past) the clicked point and leave the marker on screen for
    /// good. A walk that also ends early — blocked, or stopped by the server at its own target —
    /// has the same failure.
    /// </para>
    ///
    /// <para>
    /// So the walk is judged by the player <em>stopping</em> rather than by a distance guess: a
    /// pair of idle frames ends the marker. The idle test only counts once the player has actually
    /// moved, so a marker shown while the click is still round-tripping to the server is not
    /// cleared before the walk begins. Arrival within <see cref="ArrivalRadius"/> still clears it
    /// at once, which is the common case.
    /// </para>
    ///
    /// <para>
    /// This is pure state and arithmetic over positions — no clock, no network, no scene — so an
    /// EditMode test can judge the whole life of a marker in microseconds.
    /// </para>
    /// </summary>
    public sealed class ClickToMoveMarkerTracker
    {
        /// <summary>How close the player must get to the clicked point to count as arrived.</summary>
        public const float ArrivalRadius = 0.5f;

        /// <summary>Movement below this (metres) counts as the player standing still.</summary>
        public const float StoppedEpsilon = 0.001f;

        private Vector3 _target;
        private Vector3 _lastPosition;
        private bool _hasLastPosition;
        private bool _hasMoved;

        /// <summary>True while a marker is on screen and the walk is still being watched.</summary>
        public bool IsTracking { get; private set; }

        /// <summary>The point the player was asked to walk to.</summary>
        public Vector3 Target => _target;

        /// <summary>Starts tracking a new destination, replacing any walk already in progress.</summary>
        public void Begin(Vector3 target)
        {
            _target = target;
            IsTracking = true;
            _hasLastPosition = false;
            _hasMoved = false;
        }

        /// <summary>Stops tracking, without judging anything.</summary>
        public void Cancel()
        {
            IsTracking = false;
            _hasLastPosition = false;
            _hasMoved = false;
        }

        /// <summary>
        /// Reports the player's position for this frame and says whether the marker should be
        /// cleared. Calling it again after it returns true is harmless: the tracker has stopped.
        /// </summary>
        public bool ShouldClear(Vector3 position)
        {
            if (!IsTracking)
                return false;

            if ((position - _target).sqrMagnitude < ArrivalRadius * ArrivalRadius)
            {
                Cancel();
                return true;
            }

            if (_hasLastPosition)
            {
                bool moved = (position - _lastPosition).sqrMagnitude >= StoppedEpsilon * StoppedEpsilon;
                if (moved)
                    _hasMoved = true;
                else if (_hasMoved)
                {
                    // The walk is over — arrived, blocked, or stopped short — so the marker has
                    // outlived its purpose either way.
                    Cancel();
                    return true;
                }
            }

            _lastPosition = position;
            _hasLastPosition = true;
            return false;
        }
    }
}
