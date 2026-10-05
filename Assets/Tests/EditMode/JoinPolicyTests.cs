using MMO.Networking;
using NUnit.Framework;

namespace MMO.Tests
{
    public sealed class JoinPolicyTests
    {
        [Test]
        public void Approve_AcceptsNormalName()
        {
            Assert.IsTrue(new JoinPolicy().Approve("HeroName"));
        }

        [Test]
        public void Approve_RejectsNull()
        {
            Assert.IsFalse(new JoinPolicy().Approve(null));
        }

        [Test]
        public void Approve_RejectsEmpty()
        {
            Assert.IsFalse(new JoinPolicy().Approve(""));
        }

        [Test]
        public void Approve_RejectsWhitespace()
        {
            Assert.IsFalse(new JoinPolicy().Approve("   "));
        }

        [Test]
        public void Approve_RejectsTooLong()
        {
            Assert.IsFalse(new JoinPolicy().Approve(new string('a', JoinPolicy.MaxNameLength + 1)));
        }

        [Test]
        public void Approve_AcceptsAtMaxLength()
        {
            Assert.IsTrue(new JoinPolicy().Approve(new string('a', JoinPolicy.MaxNameLength)));
        }
    }
}
