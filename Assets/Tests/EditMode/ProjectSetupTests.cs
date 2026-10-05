using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    public sealed class ProjectSetupTests
    {
        [Test]
        public void Manifest_DeclaresFishNetPinnedGitUrl()
        {
            var manifest = ReadProjectFile(Path.Combine("Packages", "manifest.json"));

            StringAssert.Contains("com.firstgeargames.fishnet", manifest,
                "manifest.json must declare the FishNet package.");
            StringAssert.Contains("#4.7.3", manifest,
                "manifest.json must pin FishNet to tag 4.7.3.");
        }

        [Test]
        public void Manifest_FishNetIsTestable()
        {
            var manifest = ReadProjectFile(Path.Combine("Packages", "manifest.json"));

            StringAssert.Contains("\"testables\"", manifest, "Manifest must declare a testables array.");
            var fishnetIndex = manifest.IndexOf("com.firstgeargames.fishnet");
            var testablesIndex = manifest.IndexOf("\"testables\"");
            Assert.IsTrue(testablesIndex > fishnetIndex && manifest.LastIndexOf("com.firstgeargames.fishnet") > testablesIndex,
                "FishNet must be listed in testables.");
        }

        [Test]
        public void ProjectVersion_PinsUnityLts()
        {
            var versionFile = ReadProjectFile(Path.Combine("ProjectSettings", "ProjectVersion.txt"));

            StringAssert.Contains("m_EditorVersion: 6000.3.25f1", versionFile,
                "ProjectVersion.txt must pin Unity 6000.3.25f1.");
        }

        [Test]
        public void RuntimeAsmdef_ReferencesFishNet()
        {
            var asmdef = ReadProjectFile(Path.Combine("Assets", "Scripts", "MMO.Runtime.asmdef"));

            StringAssert.Contains("\"FishNet.Runtime\"", asmdef,
                "MMO.Runtime must reference the FishNet.Runtime assembly.");
        }

        [Test]
        public void PlayerStats_ExposesServerAuthoritativeHealth()
        {
            var type = typeof(MMO.Gameplay.PlayerStats);

            Assert.IsNotNull(type.GetProperty("Health"), "PlayerStats must expose Health.");
            Assert.IsNotNull(type.GetMethod("RequestDamage"), "PlayerStats must expose RequestDamage.");
        }

        [Test]
        public void ConnectionButtons_ExposesButtonHandlers()
        {
            var type = typeof(MMO.Bootstrap.ConnectionButtons);

            Assert.IsNotNull(type.GetMethod("OnHostClicked"), "ConnectionButtons must expose OnHostClicked.");
            Assert.IsNotNull(type.GetMethod("OnClientClicked"), "ConnectionButtons must expose OnClientClicked.");
            Assert.IsNotNull(type.GetMethod("OnStopClicked"), "ConnectionButtons must expose OnStopClicked.");
        }

        private static string ReadProjectFile(string relativePath)
        {
            var path = Path.Combine(Application.dataPath, "..", relativePath);
            Assert.IsTrue(File.Exists(path), $"Expected file: {path}");
            return File.ReadAllText(path);
        }
    }
}
