using System.Reflection;
using MMO.Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The item data half: <see cref="ItemDefinition"/> must stay a pure
    /// <c>ScriptableObject</c> — no FishNet types, no public setters — and the two shipped
    /// assets are asserted through the <c>AssetDatabase</c>, the only check that proves the
    /// whole chain (asset YAML, script GUID, property names) lines up end to end.
    /// </summary>
    public sealed class ItemDefinitionTests
    {
        private const string BroadswordPath = "Assets/Resources/Items/Broadsword.asset";
        private const string IronHelmPath = "Assets/Resources/Items/Iron_Helm.asset";

        private ItemDefinition _created;

        [TearDown]
        public void TearDown()
        {
            if (_created != null)
                Object.DestroyImmediate(_created);

            ItemDefinitionRegistry.Clear();
        }

        [Test]
        public void ItemDefinition_IsAScriptableObjectNotAMonobehaviour()
        {
            Assert.AreEqual(typeof(ScriptableObject), typeof(ItemDefinition).BaseType,
                "ItemDefinition must inherit ScriptableObject so it can live as a project asset.");
            Assert.IsFalse(typeof(MonoBehaviour).IsAssignableFrom(typeof(ItemDefinition)),
                "A MonoBehaviour would drag scene/GUID wiring into what is meant to be pure data.");
        }

        [Test]
        public void ItemDefinition_HoldsNoNetworkedFields()
        {
            // Balance numbers are not state: both ends read the same asset, so nothing here may
            // become networked by accident and start replicating what an asset already defines.
            foreach (FieldInfo field in typeof(ItemDefinition).GetFields(
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.DeclaredOnly))
            {
                Assert.IsFalse(field.FieldType.FullName != null && field.FieldType.FullName.StartsWith("FishNet."),
                    $"{field.Name} is a FishNet type; ItemDefinition is data only.");
            }
        }

        [Test]
        public void ItemDefinition_IsOfferedByTheCreateAssetMenu()
        {
            // The issue's workflow is "create data files", which only works if the type
            // advertises a menu entry.
            CreateAssetMenuAttribute attribute =
                typeof(ItemDefinition).GetCustomAttribute<CreateAssetMenuAttribute>();

            Assert.IsNotNull(attribute, "ItemDefinition needs [CreateAssetMenu] to be creatable from the Project window.");
            Assert.AreEqual("MMO/Item Definition", attribute.menuName);
        }

        [Test]
        public void Fields_ArePrivateSerializedWithMinGuards()
        {
            foreach (string name in new[]
                     {
                         "_itemName", "_icon", "_equipSlot", "_requiredLevel",
                         "_gridWidth", "_gridHeight", "_maxHealthBonus", "_maxManaBonus",
                         "_isStackable", "_maxStackSize", "_value",
                     })
            {
                FieldInfo field = typeof(ItemDefinition).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.IsNotNull(field, $"Expected a private {name} field.");
                Assert.IsNotNull(field.GetCustomAttribute<SerializeField>(),
                    $"{name} must carry [SerializeField] or it will not survive into the .asset file.");
            }
        }

        [Test]
        public void Properties_AreReadOnly()
        {
            foreach (string name in new[]
                     {
                         "ItemName", "Icon", "EquipSlot", "RequiredLevel",
                         "GridWidth", "GridHeight", "MaxHealthBonus", "MaxManaBonus",
                         "IsStackable", "MaxStackSize", "Value",
                     })
            {
                PropertyInfo property = typeof(ItemDefinition).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

                Assert.IsNotNull(property, $"{name} should be readable by the inventory system.");
                Assert.IsNull(property.SetMethod,
                    $"{name} must have no public setter; balance changes ship as an edited asset, never a runtime write.");
            }
        }

        [Test]
        public void Counts_CarryMinGuardsAtTheRightFloors()
        {
            // A required level or a bonus may be zero; a footprint may not — zero cells would
            // occupy nothing and let two items stack invisibly.
            AssertMinOn("_requiredLevel", 0f);
            AssertMinOn("_maxHealthBonus", 0f);
            AssertMinOn("_maxManaBonus", 0f);
            AssertMinOn("_gridWidth", 1f);
            AssertMinOn("_gridHeight", 1f);
            AssertMinOn("_maxStackSize", 1f);
            AssertMinOn("_value", 0f);
        }

        [Test]
        public void InspectorDefaults_DescribeAPlainOneCellItem()
        {
            _created = ScriptableObject.CreateInstance<ItemDefinition>();

            Assert.IsEmpty(_created.ItemName);
            Assert.IsNull(_created.Icon, "A new item has no artwork until a designer assigns a sprite.");
            Assert.AreEqual(EquipmentSlot.None, _created.EquipSlot,
                "A fresh asset is a bag item until someone names a wearable slot for it.");
            Assert.AreEqual(0, _created.RequiredLevel);
            Assert.AreEqual(1, _created.GridWidth);
            Assert.AreEqual(1, _created.GridHeight);
            Assert.AreEqual(0, _created.MaxHealthBonus);
            Assert.AreEqual(0, _created.MaxManaBonus);
        }

        [Test]
        public void InspectorWrites_ReadBackThroughTheProperties()
        {
            // SerializedObject is the exact path the Inspector uses when someone edits the asset.
            _created = ScriptableObject.CreateInstance<ItemDefinition>();
            TestItemDefinitions.Write(
                _created, "Test Blade",
                width: 2, height: 3,
                equipSlot: EquipmentSlot.Chest,
                requiredLevel: 7,
                maxHealthBonus: 15,
                maxManaBonus: 5,
                register: false);

            Assert.AreEqual("Test Blade", _created.ItemName);
            Assert.AreEqual(2, _created.GridWidth);
            Assert.AreEqual(3, _created.GridHeight);
            Assert.AreEqual(EquipmentSlot.Chest, _created.EquipSlot);
            Assert.AreEqual(7, _created.RequiredLevel);
            Assert.AreEqual(15, _created.MaxHealthBonus);
            Assert.AreEqual(5, _created.MaxManaBonus);
        }

        [Test]
        public void Footprint_ClampsToAtLeastOneCell()
        {
            // The Inspector's [Min] only guides a human; the property itself must refuse a
            // zero-wide footprint no matter how the asset reached that state.
            _created = ScriptableObject.CreateInstance<ItemDefinition>();
            var inspector = new SerializedObject(_created);
            inspector.FindProperty("_gridWidth").intValue = 0;
            inspector.FindProperty("_gridHeight").intValue = 0;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(1, _created.GridWidth, "A zero-wide item would be invisible to the grid's occupancy checks.");
            Assert.AreEqual(1, _created.GridHeight);
        }

        [Test]
        public void BroadswordAsset_ExistsAsAnItemDefinition()
        {
            ItemDefinition broadsword = Load(BroadswordPath);

            Assert.AreEqual("Broadsword", broadsword.ItemName,
                "The shipped asset's name and its ItemName field should agree — the name is the wire key.");
            Assert.AreEqual(EquipmentSlot.MainHand, broadsword.EquipSlot);
            Assert.GreaterOrEqual(broadsword.RequiredLevel, 0);
            Assert.Greater(broadsword.GridWidth, 0);
            Assert.Greater(broadsword.GridHeight, 0);
        }

        [Test]
        public void IronHelmAsset_ExistsAsAnItemDefinition()
        {
            ItemDefinition helm = Load(IronHelmPath);

            Assert.AreEqual("Iron Helm", helm.ItemName);
            Assert.AreEqual(EquipmentSlot.Head, helm.EquipSlot, "The issue places headgear in the Head slot.");
            Assert.Greater(helm.MaxHealthBonus, 0, "The helm is the shipped example of equipment changing a stat cap.");
        }

        [Test]
        public void ShippedOreItems_HaveIcons()
        {
            foreach (string path in new[]
                     {
                         "Assets/Resources/Items/Iron_Ore.asset",
                         "Assets/Resources/Items/Iron_Ingot.asset",
                         "Assets/Resources/Items/Copper_Ore.asset",
                         "Assets/Resources/Items/Tin_Ore.asset",
                         "Assets/Resources/Items/Cobalt_Ore.asset",
                         "Assets/Resources/Items/Veridium_Ore.asset",
                     })
            {
                ItemDefinition item = Load(path);
                Assert.IsNotNull(item.Icon, $"'{path}' needs its UI icon.");
            }
        }

        [Test]
        public void ShippedItems_ResolveByNameAfterAClear()
        {
            // The registry falls back to Resources, so test ordering never matters: even after
            // every registration is wiped, a shipped item still resolves.
            ItemDefinitionRegistry.Clear();

            Assert.IsNotNull(ItemDefinitionRegistry.Resolve("Broadsword"),
                "Shipped items under a Resources folder must resolve without explicit registration.");
            Assert.IsNotNull(ItemDefinitionRegistry.Resolve("Iron Helm"));
            Assert.IsNull(ItemDefinitionRegistry.Resolve("No Such Item Anywhere"),
                "An unknown key resolves to null so the server can refuse the action.");
        }

        private static ItemDefinition Load(string assetPath)
        {
            ItemDefinition asset = AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);

            Assert.IsNotNull(asset,
                $"Expected an ItemDefinition asset at {assetPath}. A null here means the file is missing or its " +
                "m_Script GUID does not match ItemDefinition.cs.meta.");

            return asset;
        }

        private static void AssertMinOn(string name, float expectedFloor)
        {
            FieldInfo field = typeof(ItemDefinition).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected a private {name} field.");

            MinAttribute min = field.GetCustomAttribute<MinAttribute>();
            Assert.IsNotNull(min, $"{name} should carry [Min] so the Inspector rejects out-of-range values.");
            Assert.AreEqual(expectedFloor, min.min, $"{name} must be floored at {expectedFloor}.");
        }
    }
}
