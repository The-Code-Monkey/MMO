using System.Collections.Generic;
using MMO.Content;
using MMO.Inventory;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// Builds code-only content assets through <see cref="SerializedObject"/> — the same path the
    /// Inspector uses — so the tests prove the serialized field names as well as the rules. Every
    /// created asset is tracked and destroyed by <see cref="Cleanup"/>.
    /// </summary>
    internal static class TestContentData
    {
        private static readonly List<Object> Created = new();

        public static QuestData Quest(
            string questId,
            QuestObjective[] objectives = null,
            string[] prerequisiteQuestIds = null,
            int rewardXp = 0,
            WeightedLootTable rewardTable = null,
            bool register = true)
        {
            var quest = Track(ScriptableObject.CreateInstance<QuestData>());
            quest.name = questId;

            var inspector = new SerializedObject(quest);
            inspector.FindProperty("_questId").stringValue = questId;
            inspector.FindProperty("_title").stringValue = questId;
            WriteObjectives(inspector.FindProperty("_objectives"), objectives);
            WriteStrings(inspector.FindProperty("_prerequisiteQuestIds"), prerequisiteQuestIds);
            inspector.FindProperty("_rewardXp").intValue = rewardXp;
            inspector.FindProperty("_rewardTable").objectReferenceValue = rewardTable;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            if (register)
                QuestRegistry.Register(quest);

            return quest;
        }

        public static QuestObjective Objective(
            QuestObjectiveKind kind,
            string targetName,
            int requiredCount = 1)
        {
            return new QuestObjective
            {
                Kind = kind,
                TargetName = targetName,
                RequiredCount = requiredCount,
            };
        }

        public static WeightedLootTable LootTable(string tableId, params LootEntry[] entries)
        {
            var table = Track(ScriptableObject.CreateInstance<WeightedLootTable>());
            table.name = tableId;

            var inspector = new SerializedObject(table);
            inspector.FindProperty("_tableId").stringValue = tableId;
            SerializedProperty list = inspector.FindProperty("_entries");
            list.arraySize = entries?.Length ?? 0;

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Item").objectReferenceValue = entries[i].Item;
                entry.FindPropertyRelative("Chance").floatValue = entries[i].Chance;
                entry.FindPropertyRelative("MinCount").intValue = entries[i].MinCount;
                entry.FindPropertyRelative("MaxCount").intValue = entries[i].MaxCount;
            }

            inspector.ApplyModifiedPropertiesWithoutUndo();
            LootTableRegistry.Register(table);
            return table;
        }

        public static LootEntry Entry(
            ItemDefinition item,
            float chance,
            int minCount = 1,
            int maxCount = 1)
        {
            return new LootEntry
            {
                Item = item,
                Chance = chance,
                MinCount = minCount,
                MaxCount = maxCount,
            };
        }

        public static AuraData Aura(
            string auraId,
            AuraKind kind,
            AuraStatKind stat = AuraStatKind.None,
            float durationSeconds = 5f,
            float tickIntervalSeconds = 0f,
            int magnitudePerTick = 0,
            int flatStatBonus = 0,
            int maxStacks = 1)
        {
            var aura = Track(ScriptableObject.CreateInstance<AuraData>());
            aura.name = auraId;

            var inspector = new SerializedObject(aura);
            inspector.FindProperty("_auraId").stringValue = auraId;
            inspector.FindProperty("_kind").enumValueIndex = (int)kind;
            inspector.FindProperty("_stat").enumValueIndex = (int)stat;
            inspector.FindProperty("_durationSeconds").floatValue = durationSeconds;
            inspector.FindProperty("_tickIntervalSeconds").floatValue = tickIntervalSeconds;
            inspector.FindProperty("_magnitudePerTick").intValue = magnitudePerTick;
            inspector.FindProperty("_flatStatBonus").intValue = flatStatBonus;
            inspector.FindProperty("_maxStacks").intValue = maxStacks;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            return aura;
        }

        public static DialogueNode Dialogue(string nodeId, params DialogueChoice[] choices)
        {
            var node = Track(ScriptableObject.CreateInstance<DialogueNode>());
            node.name = nodeId;

            var inspector = new SerializedObject(node);
            inspector.FindProperty("_nodeId").stringValue = nodeId;
            inspector.FindProperty("_speaker").stringValue = "Speaker";
            inspector.FindProperty("_text").stringValue = "Text";
            SerializedProperty list = inspector.FindProperty("_choices");
            list.arraySize = choices?.Length ?? 0;

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty choice = list.GetArrayElementAtIndex(i);
                choice.FindPropertyRelative("Text").stringValue = choices[i].Text;
                choice.FindPropertyRelative("Next").objectReferenceValue = choices[i].Next;
                choice.FindPropertyRelative("Action").enumValueIndex = (int)choices[i].Action;
                choice.FindPropertyRelative("Quest").objectReferenceValue = choices[i].Quest;
            }

            inspector.ApplyModifiedPropertiesWithoutUndo();
            return node;
        }

        public static DialogueChoice Choice(
            string text,
            DialogueNode next = null,
            DialogueAction action = DialogueAction.None,
            QuestData quest = null)
        {
            return new DialogueChoice
            {
                Text = text,
                Next = next,
                Action = action,
                Quest = quest,
            };
        }

        public static void Cleanup()
        {
            QuestRegistry.Clear();
            LootTableRegistry.Clear();

            for (int i = 0; i < Created.Count; i++)
            {
                if (Created[i] != null)
                    Object.DestroyImmediate(Created[i]);
            }

            Created.Clear();
        }

        private static T Track<T>(T asset) where T : Object
        {
            Created.Add(asset);
            return asset;
        }

        private static void WriteObjectives(SerializedProperty property, QuestObjective[] objectives)
        {
            property.arraySize = objectives?.Length ?? 0;

            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty entry = property.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Kind").enumValueIndex = (int)objectives[i].Kind;
                entry.FindPropertyRelative("TargetName").stringValue = objectives[i].TargetName;
                entry.FindPropertyRelative("RequiredCount").intValue = objectives[i].RequiredCount;
            }
        }

        private static void WriteStrings(SerializedProperty property, string[] values)
        {
            property.arraySize = values?.Length ?? 0;

            for (int i = 0; i < property.arraySize; i++)
                property.GetArrayElementAtIndex(i).stringValue = values[i];
        }
    }
}
