using System.Collections.Generic;
using System.IO;
using MMO.Net;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>
    /// Refuses a build that ships a pin document it cannot honour. A set that says
    /// <c>RequirePinning</c> but lacks a valid primary/backup pair is the classic pinning outage:
    /// every client of that build either trusts nothing or locks itself out on the first rotation.
    /// Catching it at build time is the only moment it is still cheap to fix.
    ///
    /// <para>
    /// The rules live in <see cref="PinRules.FindPinSetViolations"/> so they are EditMode-testable;
    /// this class only finds the shipped documents and turns a violation into a
    /// <see cref="BuildFailedException"/>.
    /// </para>
    /// </summary>
    public sealed class PinningBuildGuard : IPreprocessBuildWithReport
    {
        /// <summary>Folder the release pipeline drops environment pin sets into.</summary>
        public const string PinSetFolder = "Assets/Resources/Net";

        /// <summary>Glob of the documents this guard checks.</summary>
        public const string PinSetPattern = "PinSet*.json";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!Directory.Exists(PinSetFolder))
                return;

            var documents = new List<string>();
            foreach (string path in Directory.GetFiles(PinSetFolder, PinSetPattern))
            {
                TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                documents.Add(asset != null ? asset.text : string.Empty);
            }

            IReadOnlyList<string> violations = PinRules.FindPinSetViolations(documents);
            if (violations.Count > 0)
            {
                throw new BuildFailedException(
                    "The shipped certificate pin set is not usable:\n" + string.Join("\n", violations));
            }
        }
    }
}
