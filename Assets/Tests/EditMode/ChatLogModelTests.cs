using MMO.UI;
using NUnit.Framework;

namespace MMO.Tests
{
    public sealed class ChatLogModelTests
    {
        [Test]
        public void Append_KeepsOnlyTheNewestLines()
        {
            var model = new ChatLogModel(2);

            model.Append("one");
            model.Append("two");
            model.Append("three");

            Assert.AreEqual("two\nthree", model.Text);
        }

        [Test]
        public void Append_IgnoresEmptyLines()
        {
            var model = new ChatLogModel(2);
            model.Append("one");
            model.Append(null);
            model.Append(string.Empty);

            Assert.AreEqual("one", model.Text);
        }
    }
}
