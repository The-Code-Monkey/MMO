using System.Linq;
using System.Reflection;
using MMO.Content;
using NUnit.Framework;

namespace MMO.Tests
{
    /// <summary>
    /// The dialogue wire shape: only what the client renders, with public fields for FishNet.
    /// </summary>
    public sealed class DialoguePayloadTests
    {
        [Test]
        public void Payload_ContainsTheRenderableFields()
        {
            string[] names = typeof(DialoguePayload)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(field => field.Name)
                .ToArray();

            CollectionAssert.AreEquivalent(new[] { "Speaker", "Text", "ChoiceTexts" }, names);
        }

        [Test]
        public void Payload_ChoiceTextsMayBeNullForANodeWithNoChoices()
        {
            var payload = new DialoguePayload
            {
                Speaker = "Speaker",
                Text = "Text",
                ChoiceTexts = null,
            };

            Assert.IsNull(payload.ChoiceTexts);
        }
    }
}
