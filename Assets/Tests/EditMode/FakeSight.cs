using MMO.Gameplay;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// A scripted <see cref="IServerSight"/> for tests: every line of sight is either clear or
    /// blocked, chosen when it is constructed. This is what lets an EditMode test assert that a
    /// blocked ray vetoes an otherwise valid attack without spawning a wall or running physics.
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
