using System.Collections.Generic;
using MMO.Editor;
using NUnit.Framework;

namespace MMO.Tests
{
    public sealed class NetworkManagerBuildRulesTests
    {
        [Test]
        public void FindViolations_FlagsEnabledSceneWithoutNetworkManager()
        {
            var scenes = new List<SceneBuildInfo>
            {
                new SceneBuildInfo("Assets/Scenes/A.unity", true, false),
                new SceneBuildInfo("Assets/Scenes/B.unity", true, true),
                new SceneBuildInfo("Assets/Scenes/C.unity", false, false)
            };

            IReadOnlyList<string> violations = NetworkManagerBuildRules.FindViolations(scenes);

            Assert.AreEqual(1, violations.Count);
            Assert.AreEqual("Assets/Scenes/A.unity", violations[0]);
        }

        [Test]
        public void FindViolations_NoViolationsWhenAllEnabledScenesHaveOne()
        {
            var scenes = new List<SceneBuildInfo>
            {
                new SceneBuildInfo("A", true, true),
                new SceneBuildInfo("B", false, false)
            };

            Assert.IsEmpty(NetworkManagerBuildRules.FindViolations(scenes));
        }
    }
}
