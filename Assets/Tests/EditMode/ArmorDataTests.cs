using System.Collections.Generic;
using System.Reflection;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The armor half of the equipment-injected framework: <see cref="ArmorData"/> has to be a
    /// pure <c>ScriptableObject</c> that grants abilities into the wearer's slots, exactly like
    /// a weapon does. Both halves being data assets is what lets "you are what you wear" be
    /// authored by dragging skills into an asset instead of wiring them into a prefab.
    /// </summary>
    public sealed class ArmorDataTests
    {
        private const string IronPlatePath = "Assets/Data/Armor/Iron_Plate.asset";

        private ArmorData _created;
        private readonly List<Object> _scratch = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object owned in _scratch)
            {
                if (owned != null)
                    Object.DestroyImmediate(owned);
            }

            _scratch.Clear();

            if (_created != null)
                Object.DestroyImmediate(_created);
        }

        [Test]
        public void ArmorData_IsAScriptableObjectNotAMonobehaviour()
        {
            Assert.AreEqual(typeof(ScriptableObject), typeof(ArmorData).BaseType,
                "ArmorData must inherit ScriptableObject so it can live as a project asset.");
            Assert.IsFalse(typeof(MonoBehaviour).IsAssignableFrom(typeof(ArmorData)),
                "A MonoBehaviour would drag scene/GUID wiring into what is meant to be pure data.");
        }

        [Test]
        public void ArmorData_HoldsNoNetworkedFields()
        {
            foreach (FieldInfo field in typeof(ArmorData).GetFields(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                Assert.IsFalse(field.FieldType.FullName != null && field.FieldType.FullName.StartsWith("FishNet."),
                    $"{field.Name} is a FishNet type; ArmorData is a data-only asset.");
            }
        }

        [Test]
        public void ArmorData_IsOfferedByTheCreateAssetMenu()
        {
            CreateAssetMenuAttribute attribute =
                typeof(ArmorData).GetCustomAttribute<CreateAssetMenuAttribute>();

            Assert.IsNotNull(attribute, "ArmorData needs [CreateAssetMenu] to be creatable from the Project window.");
            Assert.AreEqual("MMO/Armor Data", attribute.menuName);
        }

        [Test]
        public void Stats_ArePrivateSerializedFields()
        {
            foreach (string name in new[] { "_armorName", "_armorValue", "_abilities" })
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
            foreach (string name in new[] { "ArmorName", "ArmorValue", "Abilities" })
            {
                PropertyInfo property = typeof(ArmorData).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

                Assert.IsNotNull(property, $"{name} should be readable by equipment code.");
                Assert.IsNull(property.SetMethod,
                    $"{name} must have no public setter; gear grants stats, nothing rewrites the asset.");
            }
        }

        [Test]
        public void InspectorDefaults_AreBlank()
        {
            _created = ScriptableObject.CreateInstance<ArmorData>();

            Assert.IsEmpty(_created.ArmorName);
            Assert.AreEqual(0, _created.ArmorValue);
            Assert.IsEmpty(_created.Abilities,
                "A freshly created piece of armor grants nothing until someone drags skills in.");
        }

        [Test]
        public void InspectorWrites_ReadBackThroughTheProperties()
        {
            ArmorData armor = CreateArmor("Test Plate", 7);

            Assert.AreEqual("Test Plate", armor.ArmorName);
            Assert.AreEqual(7, armor.ArmorValue);
        }

        [Test]
        public void ArmorValue_CarriesAMinGuardAgainstNegativeNumbers()
        {
            FieldInfo field = GetStatField("_armorValue");

            MinAttribute min = field.GetCustomAttribute<MinAttribute>();
            Assert.IsNotNull(min, "_armorValue should carry [Min] so the Inspector rejects negatives.");
            Assert.GreaterOrEqual(min.min, 0f, "_armorValue must clamp at zero, not below it.");
        }

        [Test]
        public void Abilities_CanBeDraggedInThroughTheInspector()
        {
            // The injection mechanism itself, exercised through Unity's own serialization: an
            // armor asset holding an ability array is how a designer grants a skill.
            ArmorData armor = CreateArmor("Test Plate", 1);
            AbilityData granted = ScriptableObject.CreateInstance<AbilityData>();
            granted.name = "Test Granted Ability";
            _scratch.Add(granted);

            var inspector = new SerializedObject(armor);
            SerializedProperty abilities = inspector.FindProperty("_abilities");
            abilities.arraySize = 1;
            abilities.GetArrayElementAtIndex(0).objectReferenceValue = granted;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(1, armor.Abilities.Count);
            Assert.AreSame(granted, armor.Abilities[0]);
        }

        [Test]
        public void IronPlateAsset_ExistsAsAnArmorDataAsset()
        {
            ArmorData ironPlate = Load(IronPlatePath);

            Assert.AreEqual("Iron Plate", ironPlate.ArmorName,
                "The shipped asset's name and its ArmorName field should agree.");
            Assert.Greater(ironPlate.ArmorValue, 0,
                "Armor that blocks nothing is just a vehicle for its skills.");
        }

        [Test]
        public void IronPlateAsset_GrantsItsShippedAbility()
        {
            // The end-to-end promise of the issue: gear referencing AbilityData assets.
            ArmorData ironPlate = Load(IronPlatePath);

            Assert.AreEqual(1, ironPlate.Abilities.Count,
                "Iron Plate is authored as granting exactly one skill.");
            Assert.IsNotNull(ironPlate.Abilities[0],
                "A null entry means the reference in the .asset does not resolve.");
            Assert.AreEqual("Second Wind", ironPlate.Abilities[0].AbilityName);
            Assert.AreEqual(AbilityLogic.SelfHeal, ironPlate.Abilities[0].Logic);
        }

        private ArmorData CreateArmor(string armorName, int armorValue)
        {
            _created = ScriptableObject.CreateInstance<ArmorData>();

            var inspector = new SerializedObject(_created);
            inspector.FindProperty("_armorName").stringValue = armorName;
            inspector.FindProperty("_armorValue").intValue = armorValue;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            return _created;
        }

        private static ArmorData Load(string assetPath)
        {
            ArmorData asset = AssetDatabase.LoadAssetAtPath<ArmorData>(assetPath);

            Assert.IsNotNull(asset,
                $"Expected an ArmorData asset at {assetPath}. A null here means the file is missing or its " +
                "m_Script GUID does not match ArmorData.cs.meta.");

            return asset;
        }

        private static FieldInfo GetStatField(string name)
        {
            return typeof(ArmorData).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }
    }
}
