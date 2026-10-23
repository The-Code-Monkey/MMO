using System.Reflection;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The ability half of the "you are what you wear" framework: <see cref="AbilityData"/> must
    /// stay a pure <c>ScriptableObject</c> holding exactly what the issue asked for — mana cost,
    /// cooldown, cast time and the logic the server runs — with no <c>MonoBehaviour</c> lifecycle,
    /// no FishNet types and no public setters.
    ///
    /// The three shipped skills are asserted through the <c>AssetDatabase</c>, which is the only
    /// check that proves the whole chain: the .asset YAML, the GUID in the script's .meta, and
    /// the property names the Inspector writes all have to agree for a typed load to succeed.
    /// </summary>
    public sealed class AbilityDataTests
    {
        private const string FireballPath = "Assets/Data/Abilities/Fireball.asset";
        private const string CleavePath = "Assets/Data/Abilities/Cleave.asset";
        private const string SecondWindPath = "Assets/Data/Abilities/Second_Wind.asset";

        private static readonly string[] StatFields =
        {
            "_abilityName", "_manaCost", "_cooldown", "_castTime",
            "_castRange", "_effectRadius", "_logic", "_effectPower", "_aura",
        };

        private static readonly string[] NumericFields =
        {
            "_manaCost", "_cooldown", "_castTime", "_castRange", "_effectRadius", "_effectPower",
        };

        private AbilityData _created;

        [TearDown]
        public void TearDown()
        {
            if (_created != null)
                Object.DestroyImmediate(_created);
        }

        [Test]
        public void AbilityData_IsAScriptableObjectNotAMonobehaviour()
        {
            Assert.AreEqual(typeof(ScriptableObject), typeof(AbilityData).BaseType,
                "AbilityData must inherit ScriptableObject so a skill can live as a project asset.");
            Assert.IsFalse(typeof(MonoBehaviour).IsAssignableFrom(typeof(AbilityData)),
                "A MonoBehaviour would drag scene/GUID wiring into what is meant to be pure data.");
        }

        [Test]
        public void AbilityData_HoldsNoNetworkedFields()
        {
            // Balance numbers are not state: if a refactor lets a FishNet type in, the asset
            // would become something a client could try to argue about.
            foreach (FieldInfo field in typeof(AbilityData).GetFields(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                Assert.IsFalse(field.FieldType.FullName != null && field.FieldType.FullName.StartsWith("FishNet."),
                    $"{field.Name} is a FishNet type; AbilityData is a data-only asset.");
            }
        }

        [Test]
        public void AbilityData_IsOfferedByTheCreateAssetMenu()
        {
            CreateAssetMenuAttribute attribute =
                typeof(AbilityData).GetCustomAttribute<CreateAssetMenuAttribute>();

            Assert.IsNotNull(attribute, "AbilityData needs [CreateAssetMenu] to be creatable from the Project window.");
            Assert.AreEqual("MMO/Ability Data", attribute.menuName);
        }

        [Test]
        public void Stats_ArePrivateSerializedFields()
        {
            foreach (string name in StatFields)
            {
                FieldInfo field = GetStatField(name);

                Assert.IsNotNull(field, $"Expected a private {name} field.");
                Assert.IsNotNull(field.GetCustomAttribute<SerializeField>(),
                    $"{name} must carry [SerializeField] or it will not survive into the .asset file.");
            }
        }

        [Test]
        public void Stats_AreExposedThroughReadOnlyProperties()
        {
            foreach (string name in new[]
                     {
                         "AbilityName", "ManaCost", "Cooldown", "CastTime",
                         "CastRange", "EffectRadius", "Logic", "EffectPower", "Aura",
                     })
            {
                PropertyInfo property = typeof(AbilityData).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

                Assert.IsNotNull(property, $"{name} should be readable by combat code.");
                Assert.IsNull(property.SetMethod,
                    $"{name} must have no public setter; balance changes ship as an edited asset, not a runtime write.");
            }
        }

        [Test]
        public void InspectorDefaults_AreBlank()
        {
            _created = ScriptableObject.CreateInstance<AbilityData>();

            Assert.IsEmpty(_created.AbilityName);
            Assert.AreEqual(0, _created.ManaCost);
            Assert.AreEqual(0f, _created.Cooldown);
            Assert.AreEqual(0f, _created.CastTime);
            Assert.AreEqual(0f, _created.CastRange);
            Assert.AreEqual(0f, _created.EffectRadius);
            Assert.AreEqual(AbilityLogic.TargetedDamage, _created.Logic);
            Assert.AreEqual(0, _created.EffectPower);
            Assert.IsNull(_created.Aura);
        }

        [Test]
        public void InspectorWrites_ReadBackThroughTheProperties()
        {
            // SerializedObject is the exact path the Inspector uses, so the getters are proven
            // against Unity's own serialization rather than against a hand-rolled field poke.
            AbilityData ability = CreateAbility(
                "Test Bolt", 12, 4f, 1.5f, 9f, 2f, AbilityLogic.AreaDamage, 30);

            Assert.AreEqual("Test Bolt", ability.AbilityName);
            Assert.AreEqual(12, ability.ManaCost);
            Assert.AreEqual(4f, ability.Cooldown);
            Assert.AreEqual(1.5f, ability.CastTime);
            Assert.AreEqual(9f, ability.CastRange);
            Assert.AreEqual(2f, ability.EffectRadius);
            Assert.AreEqual(AbilityLogic.AreaDamage, ability.Logic);
            Assert.AreEqual(30, ability.EffectPower);
        }

        [Test]
        public void Stats_CarryMinGuardsAgainstNegativeNumbers()
        {
            foreach (string name in NumericFields)
            {
                FieldInfo field = GetStatField(name);
                Assert.IsNotNull(field, $"Expected a private {name} field.");

                MinAttribute min = field.GetCustomAttribute<MinAttribute>();
                Assert.IsNotNull(min, $"{name} should carry [Min] so the Inspector rejects negatives.");
                Assert.GreaterOrEqual(min.min, 0f, $"{name} must clamp at zero, not below it.");
            }
        }

        [Test]
        public void Logic_CoversTheMechanicsTheSkillsNeed()
        {
            // An asset hand-edited to a value outside the enum would silently resolve as
            // "no effect", so the mechanics the shipped skills rely on must exist by name.
            foreach (AbilityLogic logic in new[]
                     {
                         AbilityLogic.TargetedDamage, AbilityLogic.AreaDamage, AbilityLogic.SelfHeal,
                         AbilityLogic.ApplyAura,
                     })
            {
                Assert.IsTrue(System.Enum.IsDefined(typeof(AbilityLogic), logic),
                    $"{logic} is a mechanic the ability system claims to support.");
            }
        }

        [Test]
        public void FireballAsset_ExistsAsAnAbilityDataAsset()
        {
            AbilityData fireball = Load(FireballPath);

            Assert.AreEqual("Fireball", fireball.AbilityName,
                "The shipped asset's name and its AbilityName field should agree.");
            Assert.AreEqual(AbilityLogic.TargetedDamage, fireball.Logic,
                "Fireball is the ranged, aim-it-somewhere skill.");
            Assert.Greater(fireball.ManaCost, 0, "An active skill that costs nothing spams.");
            Assert.Greater(fireball.Cooldown, 0f, "An active skill with no cooldown is an auto-attack.");
            Assert.Greater(fireball.CastTime, 0f, "Fireball is the channelling skill of the pair.");
            Assert.Greater(fireball.CastRange, 0f, "A fireball you can only throw at yourself is a self-harm.");
            Assert.Greater(fireball.EffectPower, 0, "Zero power means the cast resolves into nothing.");
        }

        [Test]
        public void CleaveAsset_ExistsAsAnAbilityDataAsset()
        {
            AbilityData cleave = Load(CleavePath);

            Assert.AreEqual("Cleave", cleave.AbilityName,
                "The shipped asset's name and its AbilityName field should agree.");
            Assert.AreEqual(AbilityLogic.AreaDamage, cleave.Logic,
                "Cleave is the swing-everywhere-around-you skill.");
            Assert.AreEqual(0f, cleave.CastTime, "A melee cleave lands the moment it is pressed.");
            Assert.Greater(cleave.EffectRadius, 0f, "A cleave with no radius only hits the caster's own tile.");
            Assert.Greater(cleave.ManaCost, 0, "Even melee swings draw on the mana pool.");
            Assert.Greater(cleave.EffectPower, 0);
        }

        [Test]
        public void SecondWindAsset_ExistsAsAnAbilityDataAsset()
        {
            AbilityData secondWind = Load(SecondWindPath);

            Assert.AreEqual("Second Wind", secondWind.AbilityName);
            Assert.AreEqual(AbilityLogic.SelfHeal, secondWind.Logic,
                "Second Wind is the self-heal the armor injects.");
            Assert.Greater(secondWind.EffectPower, 0, "A heal that restores nothing is not a skill.");
            Assert.Greater(secondWind.Cooldown, 0f, "A free-to-spam heal breaks the mana economy.");
        }

        [Test]
        public void ShippedRangedSkill_OutRangesTheMeleeSkill()
        {
            // The balance line the two mechanics exist to straddle: one is thrown, one is swung.
            AbilityData fireball = Load(FireballPath);
            AbilityData cleave = Load(CleavePath);

            Assert.Greater(fireball.CastRange, cleave.CastRange,
                "Fireball must reach further than Cleave, or the melee skill is strictly better.");
        }

        private AbilityData CreateAbility(
            string abilityName,
            int manaCost,
            float cooldown,
            float castTime,
            float castRange,
            float effectRadius,
            AbilityLogic logic,
            int effectPower)
        {
            _created = ScriptableObject.CreateInstance<AbilityData>();

            var inspector = new SerializedObject(_created);
            inspector.FindProperty("_abilityName").stringValue = abilityName;
            inspector.FindProperty("_manaCost").intValue = manaCost;
            inspector.FindProperty("_cooldown").floatValue = cooldown;
            inspector.FindProperty("_castTime").floatValue = castTime;
            inspector.FindProperty("_castRange").floatValue = castRange;
            inspector.FindProperty("_effectRadius").floatValue = effectRadius;
            inspector.FindProperty("_logic").enumValueIndex = (int)logic;
            inspector.FindProperty("_effectPower").intValue = effectPower;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            return _created;
        }

        private static AbilityData Load(string assetPath)
        {
            AbilityData asset = AssetDatabase.LoadAssetAtPath<AbilityData>(assetPath);

            Assert.IsNotNull(asset,
                $"Expected an AbilityData asset at {assetPath}. A null here means the file is missing or its " +
                "m_Script GUID does not match AbilityData.cs.meta.");

            return asset;
        }

        private static FieldInfo GetStatField(string name)
        {
            return typeof(AbilityData).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }
    }
}
