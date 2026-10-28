using System;

namespace MMO.Social
{
    /// <summary>Cadence for the hub's periodic broadcasts, driven by the Unity frame clock.</summary>
    internal sealed class SocialTickScheduler
    {
        private readonly float _syncInterval;
        private readonly float _presenceInterval;
        private readonly float _friendRefreshInterval;
        private readonly float _mailSweepInterval;
        private float _nextSyncTime;
        private float _nextPresenceTime;
        private float _nextFriendRefreshTime;
        private float _nextMailSweepTime;
        private float _nextZoneHeartbeatTime;

        public SocialTickScheduler(
            float syncInterval,
            float presenceInterval,
            float friendRefreshInterval,
            float mailSweepInterval)
        {
            _syncInterval = syncInterval;
            _presenceInterval = presenceInterval;
            _friendRefreshInterval = friendRefreshInterval;
            _mailSweepInterval = mailSweepInterval;
        }

        public void Start(float now)
        {
            _nextSyncTime = now + _syncInterval;
            _nextPresenceTime = now + _presenceInterval;
            _nextFriendRefreshTime = now + _friendRefreshInterval;
            _nextMailSweepTime = now + _mailSweepInterval;
            // Register with the zone registry on the first connected tick, not one interval later,
            // so peers can select this zone as soon as it is up (#57).
            _nextZoneHeartbeatTime = 0f;
        }

        public void Tick(
            float now,
            bool hasPlayers,
            bool hasZoneRegistry,
            Action heartbeatZone,
            Action heartbeatPresence,
            Action sync,
            Action refreshFriends,
            Action sweepMail)
        {
            if (hasZoneRegistry && now >= _nextZoneHeartbeatTime)
            {
                _nextZoneHeartbeatTime = now + _presenceInterval;
                heartbeatZone?.Invoke();
            }

            if (!hasPlayers)
                return;

            if (now >= _nextPresenceTime)
            {
                _nextPresenceTime = now + _presenceInterval;
                heartbeatPresence?.Invoke();
            }

            if (now >= _nextSyncTime)
            {
                _nextSyncTime = now + _syncInterval;
                sync?.Invoke();
            }

            // Refresh friend-list online flags on a low cadence so join/transfer/login all see
            // fresh states without hammering the database.
            if (now >= _nextFriendRefreshTime)
            {
                _nextFriendRefreshTime = now + _friendRefreshInterval;
                refreshFriends?.Invoke();
            }

            // Grant claimable mail (auction deliveries, refunds) into local players' bags/wallets.
            if (now >= _nextMailSweepTime)
            {
                _nextMailSweepTime = now + _mailSweepInterval;
                sweepMail?.Invoke();
            }
        }
    }
}
