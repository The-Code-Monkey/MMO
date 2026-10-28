using System;
using MMO.Gameplay;
using MMO.Social;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>Authors a zone's risk config and territory claim flag.</summary>
    public static class ZoneRiskAuthor
    {
        public static void CreateRiskConfig(Transform zoneRoot, ZoneRiskTier tier)
        {
            var go = ZoneSceneScaffold.CreateChild(ZoneSceneBaker.RiskConfigName, zoneRoot);
            var config = go.AddComponent<ZoneRiskConfig>();
            config.SetTier(tier);
        }

        public static void CreateClaimFlag(Transform zoneRoot)
        {
            var go = ZoneSceneScaffold.CreateChild(ZoneSceneBaker.TerritoryName, zoneRoot);
            go.transform.localPosition = new Vector3(-16f, 0f, 16f);

            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = new Vector3(0f, 1f, 0f);
            collider.size = new Vector3(8f, 2f, 8f);

            var flag = go.AddComponent<ClaimFlag>();
            flag.Configure("wilderness-keep", new VulnerabilityWindow
            {
                Days = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday },
                StartMinuteUtc = 18 * 60,
                EndMinuteUtc = 22 * 60,
            });
        }
    }
}
