using System.Collections.Generic;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The decisions behind resolving an accepted cast: where an effect originates, what it
    /// touches, and how hard it hits.
    ///
    /// These are the rules that stop an AreaDamage cleave from hitting its own caster and stop a
    /// hand-edited negative power from healing the enemy, so they are checked as pure geometry
    /// against a couple of stand-in <see cref="PlayerStats"/> capsules — no server required.
    /// </summary>
    public sealed class AbilityEffectsTests
    {
        private readonly List<GameObject> _sceneObjects = new();
        private readonly List<Object> _scratch = new();

        private static readonly Vector3 Caster = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 Target = new Vector3(10f, 0f, 0f);

        [TearDown]
        public void TearDown()
        {
            foreach (Object owned in _scratch)
            {
                if (owned != null)
                    Object.DestroyImmediate(owned);
            }

            foreach (GameObject go in _sceneObjects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _scratch.Clear();
            _sceneObjects.Clear();
        }

        [Test]
        public void ResolveOrigin_TargetedDamage_ExplodesWhereTheCasterAimed()
        {
            Assert.AreEqual(Target,
                AbilityEffects.ResolveOrigin(AbilityLogic.TargetedDamage, Caster, Target),
                "A fireball lands where it was thrown, not on the thrower.");
        }

        [Test]
        public void ResolveOrigin_AreaDamage_SweepsAroundTheCaster()
        {
            // A client picks the target point for every cast, so a cleave must never take its
            // origin from one — that would let a melee swing be aimed across the map.
            Assert.AreEqual(Caster,
                AbilityEffects.ResolveOrigin(AbilityLogic.AreaDamage, Caster, Target));
        }

        [Test]
        public void ResolveOrigin_SelfHeal_IsCastOnTheCaster()
        {
            Assert.AreEqual(Caster,
                AbilityEffects.ResolveOrigin(AbilityLogic.SelfHeal, Caster, Target));
        }

        [Test]
        public void ResolveOrigin_UnknownLogic_FallsBackToTheAimedPoint()
        {
            Assert.AreEqual(Target,
                AbilityEffects.ResolveOrigin((AbilityLogic)99, Caster, Target));
        }

        [Test]
        public void Hits_IsTrueInsideAndOnTheEdgeOfTheRadius()
        {
            Assert.IsTrue(AbilityEffects.Hits(Caster, new Vector3(2f, 0f, 0f), 2f),
                "The edge of an effect counts as being hit.");
            Assert.IsTrue(AbilityEffects.Hits(Caster, Vector3.zero, 2f));
        }

        [Test]
        public void Hits_IsFalseOutsideTheRadius()
        {
            Assert.IsFalse(AbilityEffects.Hits(Caster, new Vector3(2.01f, 0f, 0f), 2f));
            Assert.IsFalse(AbilityEffects.Hits(Caster, Target, 0f),
                "A zero-radius effect only touches its exact origin.");
        }

        [Test]
        public void Hits_TreatsANegativeRadiusAsZero()
        {
            // Negative only arrives through hand-edited YAML; it must not invert the test.
            Assert.IsFalse(AbilityEffects.Hits(Caster, Target, -5f));
            Assert.IsTrue(AbilityEffects.Hits(Caster, Caster, -5f));
        }

        [Test]
        public void ComputePower_PassesTheAuthoredValueThrough()
        {
            AbilityData ability = NewAbility("Test Skill", effectPower: 30);

            Assert.AreEqual(30, AbilityEffects.ComputePower(ability));
        }

        [Test]
        public void ComputePower_ClampsANegativePowerToZero()
        {
            AbilityData ability = NewAbility("Test Skill", effectPower: -10);

            Assert.AreEqual(0, AbilityEffects.ComputePower(ability),
                "A negative power is malformed authoring, not a heal dressed up as damage.");
        }

        [Test]
        public void ComputePower_IsZeroForNoAbilityAtAll()
        {
            Assert.AreEqual(0, AbilityEffects.ComputePower(null));
        }

        [Test]
        public void SelectVictims_HitsEveryoneInsideTheRadius()
        {
            PlayerStats near = SpawnStats("Near", new Vector3(1f, 0f, 0f));
            PlayerStats far = SpawnStats("Far", new Vector3(50f, 0f, 0f));

            List<PlayerStats> victims = AbilityEffects.SelectVictims(
                Caster, 5f, self: null, candidates: new[] { near, far });

            Assert.AreEqual(1, victims.Count);
            Assert.AreSame(near, victims[0]);
        }

        [Test]
        public void SelectVictims_ExcludesTheCastersOwnStats()
        {
            // The rule that makes AreaDamage not a suicide button.
            PlayerStats self = SpawnStats("Self", Caster);
            PlayerStats other = SpawnStats("Other", new Vector3(2f, 0f, 0f));

            List<PlayerStats> victims = AbilityEffects.SelectVictims(
                Caster, 5f, self, new[] { self, other });

            Assert.AreEqual(1, victims.Count);
            Assert.AreSame(other, victims[0]);
        }

        [Test]
        public void SelectVictims_IgnoresNullCandidates()
        {
            PlayerStats survivor = SpawnStats("Survivor", new Vector3(1f, 0f, 0f));

            List<PlayerStats> victims = AbilityEffects.SelectVictims(
                Caster, 5f, null, new PlayerStats[] { null, survivor, null });

            Assert.AreEqual(1, victims.Count);
            Assert.AreSame(survivor, victims[0]);
        }

        [Test]
        public void SelectVictims_ReturnsNobodyForMissingCandidates()
        {
            Assert.IsEmpty(AbilityEffects.SelectVictims(Caster, 5f, null, null),
                "A scan that found no candidates resolves into an effect that hits nothing.");
        }

        [Test]
        public void SelectVictims_PreservesTheCandidateOrder()
        {
            PlayerStats first = SpawnStats("First", new Vector3(1f, 0f, 0f));
            PlayerStats second = SpawnStats("Second", new Vector3(2f, 0f, 0f));

            List<PlayerStats> victims = AbilityEffects.SelectVictims(
                Caster, 5f, null, new[] { first, second });

            Assert.AreEqual(new[] { first, second }, victims);
        }

        [Test]
        public void SelectVisibleVictims_KeepsEveryoneOnAClearLine()
        {
            PlayerStats near = SpawnStats("Near", new Vector3(1f, 0f, 0f));
            PlayerStats far = SpawnStats("Far", new Vector3(2f, 0f, 0f));

            List<PlayerStats> visible = AbilityEffects.SelectVisibleVictims(
                Caster, new[] { near, far }, new FakeSight(clear: true));

            Assert.AreEqual(new[] { near, far }, visible);
        }

        [Test]
        public void SelectVisibleVictims_DropsEveryoneBehindCover()
        {
            // A fireball centred just past a wall must reach nobody the wall is hiding.
            PlayerStats hidden = SpawnStats("Hidden", new Vector3(10f, 0f, 0f));

            List<PlayerStats> visible = AbilityEffects.SelectVisibleVictims(
                Caster, new[] { hidden }, new FakeSight(clear: false));

            Assert.IsEmpty(visible);
        }

        [Test]
        public void SelectVisibleVictims_ASightAsksAboutEveryVictim()
        {
            PlayerStats first = SpawnStats("First", new Vector3(1f, 0f, 0f));
            PlayerStats second = SpawnStats("Second", new Vector3(2f, 0f, 0f));
            var sight = new FakeSight(clear: true);

            AbilityEffects.SelectVisibleVictims(Caster, new[] { first, second }, sight);

            Assert.AreEqual(2, sight.Calls, "The filter must not skip a victim and silently spare them.");
        }

        [Test]
        public void SelectVisibleVictims_IgnoresNullVictims()
        {
            PlayerStats survivor = SpawnStats("Survivor", new Vector3(1f, 0f, 0f));

            List<PlayerStats> visible = AbilityEffects.SelectVisibleVictims(
                Caster, new PlayerStats[] { null, survivor, null }, new FakeSight(clear: true));

            Assert.AreEqual(new[] { survivor }, visible);
        }

        [Test]
        public void SelectVisibleVictims_WithNoSight_KeepsEveryone()
        {
            // A caller wired without a sight must fail open (old behaviour), not silently hit nobody.
            PlayerStats victim = SpawnStats("Victim", new Vector3(1f, 0f, 0f));

            List<PlayerStats> visible = AbilityEffects.SelectVisibleVictims(
                Caster, new[] { victim }, null);

            Assert.AreEqual(new[] { victim }, visible);
        }

        [Test]
        public void SelectVisibleVictims_ReturnsNobodyForMissingVictims()
        {
            Assert.IsEmpty(AbilityEffects.SelectVisibleVictims(Caster, null, new FakeSight(clear: true)));
        }

        [Test]
        public void SelectNpcVictims_HitsEveryoneInsideTheRadius()
        {
            NPCStats near = SpawnNpc("Near", new Vector3(1f, 0f, 0f));
            NPCStats far = SpawnNpc("Far", new Vector3(50f, 0f, 0f));

            List<NPCStats> victims = AbilityEffects.SelectNpcVictims(
                Caster, 5f, new[] { near, far });

            Assert.AreEqual(new[] { near }, victims);
        }

        [Test]
        public void SelectNpcVictims_IgnoresNullCandidatesAndPreservesOrder()
        {
            NPCStats first = SpawnNpc("First", new Vector3(1f, 0f, 0f));
            NPCStats second = SpawnNpc("Second", new Vector3(2f, 0f, 0f));

            List<NPCStats> victims = AbilityEffects.SelectNpcVictims(
                Caster, 5f, new[] { null, first, second });

            Assert.AreEqual(new[] { first, second }, victims);
        }

        [Test]
        public void SelectNpcVictims_ReturnsNobodyForMissingCandidates()
        {
            Assert.IsEmpty(AbilityEffects.SelectNpcVictims(Caster, 5f, null));
        }

        [Test]
        public void SelectVisibleNpcVictims_KeepsEveryoneOnAClearLine()
        {
            NPCStats near = SpawnNpc("Near", new Vector3(1f, 0f, 0f));
            NPCStats far = SpawnNpc("Far", new Vector3(2f, 0f, 0f));

            List<NPCStats> visible = AbilityEffects.SelectVisibleNpcVictims(
                Caster, new[] { near, far }, new FakeSight(clear: true));

            Assert.AreEqual(new[] { near, far }, visible);
        }

        [Test]
        public void SelectVisibleNpcVictims_DropsEveryoneBehindCover()
        {
            NPCStats hidden = SpawnNpc("Hidden", new Vector3(10f, 0f, 0f));

            List<NPCStats> visible = AbilityEffects.SelectVisibleNpcVictims(
                Caster, new[] { hidden }, new FakeSight(clear: false));

            Assert.IsEmpty(visible);
        }

        [Test]
        public void SelectVisibleNpcVictims_WithNoSight_KeepsEveryone()
        {
            NPCStats victim = SpawnNpc("Victim", new Vector3(1f, 0f, 0f));

            List<NPCStats> visible = AbilityEffects.SelectVisibleNpcVictims(
                Caster, new[] { victim }, null);

            Assert.AreEqual(new[] { victim }, visible);
        }

        private PlayerStats SpawnStats(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            _sceneObjects.Add(go);

            return go.AddComponent<PlayerStats>();
        }

        private NPCStats SpawnNpc(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            _sceneObjects.Add(go);

            return go.AddComponent<NPCStats>();
        }

        private AbilityData NewAbility(string abilityName, int effectPower)
        {
            AbilityData ability = ScriptableObject.CreateInstance<AbilityData>();
            ability.name = abilityName;

            var inspector = new UnityEditor.SerializedObject(ability);
            inspector.FindProperty("_abilityName").stringValue = abilityName;
            inspector.FindProperty("_effectPower").intValue = effectPower;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            _scratch.Add(ability);
            return ability;
        }
    }
}
