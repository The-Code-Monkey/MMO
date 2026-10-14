using MMO.Gameplay;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// A scripted <see cref="IServerSight"/> for PlayMode tests: every line of sight is either
    /// clear or blocked, chosen when it is constructed. It lets a host test prove the server
    /// consults the sight on the real RPC path without needing a physical wall in the scene.
    /// </summary>
    public sealed class FakeSight : IServerSight
    {
        private readonly bool _clear;

        /// <param name="clear">True makes every check report a clear line; false hides everything.</param>
        public FakeSight(bool clear)
        {
            _clear = clear;
        }

        /// <summary>How many times the line was asked about, so a test can prove it was consulted.</summary>
        public int Calls { get; private set; }

        /// <inheritdoc />
        public bool HasLineOfSight(Vector3 eyePosition, Vector3 targetPosition, GameObject target)
        {
            Calls++;
            return _clear;
        }
    }
}
