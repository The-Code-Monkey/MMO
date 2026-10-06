using System.Reflection;
using MMO.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The "you are what you wear" data half: <see cref="WeaponData"/> must stay a pure
    /// <c>ScriptableObject</c> — no <c>MonoBehaviour</c> lifecycle, no FishNet types, no public
    /// setters — so a weapon's numbers are authored as an asset, read by whoever needs them, and
    /// changed only by a re-shipped asset.
    ///
    /// The two shipped assets are asserted directly through the <c>AssetDatabase</c>. That is the
    /// only check which proves the whole chain end to end: the .asset YAML, the GUID in the
    /// script's .meta, and the property names the Inspector writes all have to line up for
    /// <c>LoadAssetAtPath&lt;WeaponData&gt;</c> to hand back a typed object instead of a
    /// missing-script stub.
    /// </summary>
    public sealed class WeaponDataTests
    {
        private const string BroadswordPath = "Assets/Data/Weapons/Broadsword.asset";
        private const string FireStaffPath = "Assets/Data/Weapons/Fire Staff.asset";

        private WeaponData _created;

        [TearDown]
        public void TearDown()
        {
            if (_created != null)
                Object.DestroyImmediate(_created);
        }

        [Test]
        public void WeaponData_IsAScriptableObjectNotAMonobehaviour()
        {
            Assert.AreEqual(typeof(ScriptableObject), typeof(WeaponData).BaseType,
                "WeaponData must inherit ScriptableObject so it can live as a project asset.");
            Assert.IsFalse(typeof(MonoBehaviour).IsAssignableFrom(typeof(WeaponData)),
                "A MonoBehaviour would drag scene/GUID wiring into what is meant to be pure data.");
        }

        [Test]
        public void WeaponData_HoldsNoNetworkedFields()
        {
            // The issue asks for purely data with no networking: nothing on this type may be a
            // FishNet type, or a refactor could quietly turn a balance asset into a networked one.
            foreach (FieldInfo field in typeof(WeaponData).GetFields(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                Assert.IsFalse(field.FieldType.FullName != null && field.FieldType.FullName.StartsWith("FishNet."),
                    $"{field.Name} is a FishNet type; WeaponData is a data-only asset.");
            }
        }

        [Test]
        public void WeaponData_IsOfferedByTheCreateAssetMenu()
        {
            // The issue's workflow is "right-click and create two data files", which only works
            // if the type advertises a menu entry.
            CreateAssetMenuAttribute attribute =
                typeof(WeaponData).GetCustomAttribute<CreateAssetMenuAttribute>();

            Assert.IsNotNull(attribute, "WeaponData needs [CreateAssetMenu] to be creatable from the Project window.");
            Assert.AreEqual("MMO/Weapon Data", attribute.menuName);
        }

        [Test]
        public void Stats_ArePrivateSerializedFields()
        {
            // Private + [SerializeField] is the project convention: the Inspector writes,
            // everything else reads.
            foreach (string name in new[] { "_weaponName", "_autoAttackDamage", "_attackRange" })
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
            foreach (string name in new[] { "WeaponName", "AutoAttackDamage", "AttackRange" })
            {
                PropertyInfo property = typeof(WeaponData).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

                Assert.IsNotNull(property, $"{name} should be readable by combat code.");
                Assert.IsNull(property.SetMethod,
                    $"{name} must have no public setter; balance changes ship as an edited asset, not a runtime write.");
            }
        }

        [Test]
        public void InspectorDefaults_AreBlank()
        {
            // A freshly created asset, before anyone touches the Inspector.
            _created = ScriptableObject.CreateInstance<WeaponData>();

            Assert.IsEmpty(_created.WeaponName);
            Assert.AreEqual(0, _created.AutoAttackDamage);
            Assert.AreEqual(0f, _created.AttackRange);
        }

        [Test]
        public void InspectorWrites_ReadBackThroughTheProperties()
        {
            // Uses SerializedObject, i.e. the exact path the Inspector uses when someone edits
            // the asset, so the getters are proven against Unity's own serialization rather than
            // against a hand-rolled field poke.
            WeaponData weapon = CreateWeapon("Test Blade", 12, 3.5f);

            Assert.AreEqual("Test Blade", weapon.WeaponName);
            Assert.AreEqual(12, weapon.AutoAttackDamage);
            Assert.AreEqual(3.5f, weapon.AttackRange);
        }

        [Test]
        public void Stats_CarryMinGuardsAgainstNegativeNumbers()
        {
            AssertMinOn("_autoAttackDamage");
            AssertMinOn("_attackRange");
        }

        [Test]
        public void BroadswordAsset_ExistsAsAWeaponDataAsset()
        {
            WeaponData broadsword = Load(BroadswordPath);

            Assert.AreEqual("Broadsword", broadsword.WeaponName,
                "The shipped asset's name and its WeaponName field should agree.");
            Assert.Greater(broadsword.AutoAttackDamage, 0,
                "A Broadsword that hits for zero is not the high-damage melee weapon the issue asked for.");
            Assert.Greater(broadsword.AttackRange, 0f, "A Broadsword still needs a (short) reach.");
        }

        [Test]
        public void FireStaffAsset_ExistsAsAWeaponDataAsset()
        {
            WeaponData fireStaff = Load(FireStaffPath);

            Assert.AreEqual("Fire Staff", fireStaff.WeaponName,
                "The shipped asset's name and its WeaponName field should agree.");
            Assert.Greater(fireStaff.AutoAttackDamage, 0,
                "A Fire Staff that hits for zero cannot fight at all; it is simply low damage.");
            Assert.Greater(fireStaff.AttackRange, 0f, "A Fire Staff is the long-range option.");
        }

        [Test]
        public void ShippedWeapons_AreHighDamageLowRangeVersusLowDamageHighRange()
        {
            // The issue's actual balance requirement: Broadsword = high damage / low range,
            // Fire Staff = low damage / high range.
            WeaponData broadsword = Load(BroadswordPath);
            WeaponData fireStaff = Load(FireStaffPath);

            Assert.Greater(broadsword.AutoAttackDamage, fireStaff.AutoAttackDamage,
                "The Broadsword must out-damage the Fire Staff.");
            Assert.Greater(fireStaff.AttackRange, broadsword.AttackRange,
                "The Fire Staff must out-range the Broadsword.");
        }

        private WeaponData CreateWeapon(string weaponName = "", int autoAttackDamage = 0, float attackRange = 0f)
        {
            _created = ScriptableObject.CreateInstance<WeaponData>();

            var inspector = new SerializedObject(_created);
            inspector.FindProperty("_weaponName").stringValue = weaponName;
            inspector.FindProperty("_autoAttackDamage").intValue = autoAttackDamage;
            inspector.FindProperty("_attackRange").floatValue = attackRange;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            return _created;
        }

        private static WeaponData Load(string assetPath)
        {
            WeaponData asset = AssetDatabase.LoadAssetAtPath<WeaponData>(assetPath);

            Assert.IsNotNull(asset,
                $"Expected a WeaponData asset at {assetPath}. A null here means the file is missing or its " +
                "m_Script GUID does not match WeaponData.cs.meta.");

            return asset;
        }

        private static FieldInfo GetStatField(string name)
        {
            return typeof(WeaponData).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static void AssertMinOn(string name)
        {
            FieldInfo field = GetStatField(name);
            Assert.IsNotNull(field, $"Expected a private {name} field.");

            MinAttribute min = field.GetCustomAttribute<MinAttribute>();
            Assert.IsNotNull(min, $"{name} should carry [Min] so the Inspector rejects negatives.");
            Assert.GreaterOrEqual(min.min, 0f, $"{name} must clamp at zero, not below it.");
        }
    }
}
