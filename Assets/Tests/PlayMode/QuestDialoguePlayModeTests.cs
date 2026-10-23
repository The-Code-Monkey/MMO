using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Object;
using MMO.Content;
using MMO.Gameplay;
using MMO.Inventory;
using MMO.Net;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MMO.Tests
{
    /// <summary>
    /// The quest/dialogue loop over a live host: kill, collect and interact progress, reward
    /// once, and a server-validated dialogue branch.
    /// </summary>
    public sealed class QuestDialoguePlayModeTests
    {
        private static NetworkObject _dialoguePrefab;
        private static NetworkObject _nodePrefab;

        private NetworkManager _manager;
        private NetworkObject _player;
        private PlayerStats _stats;
        private PlayerCombat _combat;
        private PlayerGathering _gathering;
        private PlayerInventory _inventory;
        private PlayerQuestLog _quests;
        private PlayerDialogue _dialogue;
        private WeaponData _weapon;
        private readonly List<Object> _scratch = new();
        private readonly List<NetworkObject> _spawned = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _manager = NetworkManagerTestHelper.GetOrCreate();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            NetworkManagerTestHelper.StopIfListening(_manager);
            yield return null;

            foreach (NetworkObject spawned in _spawned)
            {
                if (spawned != null)
                    Object.Destroy(spawned.gameObject);
            }

            _spawned.Clear();

            foreach (Object owned in _scratch)
            {
                if (owned != null)
                    Object.Destroy(owned);
            }

            _scratch.Clear();
            _player = null;
            _stats = null;
            _combat = null;
            _gathering = null;
            _inventory = null;
            _quests = null;
            _dialogue = null;
            _weapon = null;

            yield return null;
        }

        [UnityTest]
        public IEnumerator KillAdvancesAKillQuestAndRewardsOnce()
        {
            yield return StartHostWithPlayer();

            QuestData quest = CreateQuest(
                "kill_test",
                new[] { Objective(QuestObjectiveKind.Kill, "TargetDummy", 1) },
                rewardXp: 25);

            Assert.IsTrue(_quests.TryStartQuest(quest));

            yield return NetworkManagerTestHelper.WaitForTargetDummy(_manager);
            NPCStats npc = NetworkManagerTestHelper.FindTargetDummies(_manager)[0].GetComponent<NPCStats>();

            yield return ArmAndKill(npc);
            yield return WaitUntil(() => _quests.IsCompleted(quest.QuestId), 300);

            Assert.IsTrue(_quests.IsCompleted(quest.QuestId));
            Assert.AreEqual(25, _stats.Experience);
            Assert.AreEqual(1, ProgressCount(quest.QuestId));

            Assert.IsFalse(_quests.TryComplete(quest), "A completed quest cannot complete twice.");
            Assert.AreEqual(25, _stats.Experience);
        }

        [UnityTest]
        public IEnumerator CollectQuestCountsTheCurrentInventory()
        {
            yield return StartHostWithPlayer();

            ItemDefinition ore = CreateItem("QuestTestOre");
            QuestData quest = CreateQuest(
                "collect_test",
                new[] { Objective(QuestObjectiveKind.Collect, ore.ItemName, 1) },
                rewardXp: 10);

            Assert.IsTrue(_quests.TryStartQuest(quest));
            Assert.IsFalse(_quests.IsCompleted(quest.QuestId));

            Assert.IsTrue(_inventory.TryAddItem(ore), "The player needs room for the collect item.");
            yield return WaitUntil(() => _quests.IsCompleted(quest.QuestId), 300);

            Assert.IsTrue(_quests.IsCompleted(quest.QuestId));
            Assert.AreEqual(10, _stats.Experience);
            Assert.AreEqual(1, ProgressCount(quest.QuestId));
        }

        [UnityTest]
        public IEnumerator GatheringAdvancesAnInteractQuest()
        {
            yield return StartHostWithPlayer();

            QuestData quest = CreateQuest(
                "gather_test",
                new[] { Objective(QuestObjectiveKind.Interact, "Iron_Ore", 1) },
                rewardXp: 5);

            Assert.IsTrue(_quests.TryStartQuest(quest));

            ResourceNode node = SpawnNode(_player.transform.position);
            _gathering.StartGathering(node.NetworkObject);

            yield return WaitUntil(() => _quests.IsCompleted(quest.QuestId), 300);

            Assert.IsTrue(_quests.IsCompleted(quest.QuestId));
            Assert.AreEqual(5, _stats.Experience);
        }

        [UnityTest]
        public IEnumerator DialogueAcceptStartsQuestAndInvalidChoiceIsRefused()
        {
            yield return StartHostWithPlayer();

            QuestData quest = CreateQuest(
                "dialogue_accept",
                new[] { Objective(QuestObjectiveKind.Kill, "Wolf", 1) },
                rewardXp: 0);

            DialogueNode root = CreateDialogue(
                "accept_root",
                Choice("Accept", DialogueAction.OfferQuest, quest));

            DialogueNpc npc = SpawnNpc(root, _player.transform.position + _player.transform.forward * 2f);
            npc.RequestTalkFromLocalPlayer();
            yield return WaitUntil(() => _dialogue.IsOpen, 300);

            Assert.IsTrue(_dialogue.IsOpen);
            Assert.AreEqual("Quest Giver", _dialogue.Current.Speaker);

            _dialogue.RequestChoice(99);
            for (int i = 0; i < 10; i++)
                yield return null;

            Assert.IsFalse(_quests.IsActive(quest.QuestId), "An invalid index must not start the quest.");
            Assert.IsTrue(_dialogue.IsOpen, "An invalid index leaves the conversation where it was.");

            _dialogue.RequestChoice(0);
            yield return WaitUntil(() => _quests.IsActive(quest.QuestId), 300);

            Assert.IsTrue(_quests.IsActive(quest.QuestId));
            Assert.IsFalse(_quests.IsCompleted(quest.QuestId));
            yield return WaitUntil(() => !_dialogue.IsOpen, 300);
        }

        [UnityTest]
        public IEnumerator DialogueTurnInRefusesAnIncompleteQuestAndDoesNotDoubleReward()
        {
            yield return StartHostWithPlayer();

            QuestData quest = CreateQuest(
                "dialogue_turnin",
                new[] { Objective(QuestObjectiveKind.Kill, "Wolf", 1) },
                rewardXp: 15);

            Assert.IsTrue(_quests.TryStartQuest(quest));

            DialogueNode root = CreateDialogue(
                "turnin_root",
                Choice("Turn in", DialogueAction.TurnInQuest, quest));

            DialogueNpc npc = SpawnNpc(root, _player.transform.position + _player.transform.forward * 2f);

            npc.RequestTalkFromLocalPlayer();
            yield return WaitUntil(() => _dialogue.IsOpen, 300);
            _dialogue.RequestChoice(0);
            yield return WaitUntil(() => !_dialogue.IsOpen, 300);

            Assert.IsFalse(_quests.IsCompleted(quest.QuestId),
                "Turn-in must refuse a quest whose objectives are not met.");
            Assert.AreEqual(0, _stats.Experience);

            _quests.NotifyKill("Wolf");
            yield return WaitUntil(() => _quests.IsCompleted(quest.QuestId), 300);
            Assert.AreEqual(15, _stats.Experience);

            npc.RequestTalkFromLocalPlayer();
            yield return WaitUntil(() => _dialogue.IsOpen, 300);
            _dialogue.RequestChoice(0);
            for (int i = 0; i < 10; i++)
                yield return null;

            Assert.AreEqual(15, _stats.Experience,
                "A completed quest cannot grant its reward a second time.");
        }

        private IEnumerator StartHostWithPlayer()
        {
            NetworkManagerTestHelper.StartHost(_manager);
            yield return NetworkManagerTestHelper.WaitForHostAuthenticated(_manager);
            yield return NetworkManagerTestHelper.WaitForPlayerObject(_manager);

            _player = NetworkManagerTestHelper.FindPlayerObject(_manager);
            Assert.IsNotNull(_player);

            _stats = _player.GetComponent<PlayerStats>();
            _combat = _player.GetComponent<PlayerCombat>();
            _gathering = _player.GetComponent<PlayerGathering>();
            _inventory = _player.GetComponent<PlayerInventory>();
            _quests = _player.GetComponent<PlayerQuestLog>();
            _dialogue = _player.GetComponent<PlayerDialogue>();

            Assert.IsNotNull(_stats);
            Assert.IsNotNull(_combat);
            Assert.IsNotNull(_gathering);
            Assert.IsNotNull(_inventory);
            Assert.IsNotNull(_quests, "The player prefab must carry PlayerQuestLog.");
            Assert.IsNotNull(_dialogue, "The player prefab must carry PlayerDialogue.");

            yield return WaitUntil(() => _player.IsClientInitialized, 300);
        }

        private IEnumerator ArmAndKill(NPCStats npc)
        {
            npc.transform.position = _player.transform.position + _player.transform.forward * 2f;
            _combat.Sight = new FakeSight(clear: true);

            _weapon = CreateWeapon(damage: NPCStats.MaxHealth, range: 10f);
            _combat.Equip(_weapon);

            _combat.Attack(npc.NetworkObject);
            yield return WaitUntil(() => !npc.IsAlive, 300);
            yield return null;
        }

        private ResourceNode SpawnNode(Vector3 position)
        {
            if (_nodePrefab == null)
            {
                _nodePrefab = ResourceNodePrefabFactory.Create();
                _nodePrefab.gameObject.SetActive(false);
                ResourceNodePrefabFactory.Register(_manager.SpawnablePrefabs, _nodePrefab, initializeAdded: true);
            }

            GameObject instance = Object.Instantiate(_nodePrefab.gameObject, position, Quaternion.identity);
            instance.name = _nodePrefab.name;
            instance.SetActive(true);
            _manager.ServerManager.Spawn(instance);
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            _spawned.Add(networkObject);
            return instance.GetComponent<ResourceNode>();
        }

        private DialogueNpc SpawnNpc(DialogueNode root, Vector3 position)
        {
            if (_dialoguePrefab == null)
            {
                GameObject prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                prefab.name = "DialogueNpcTest";
                prefab.transform.localScale = Vector3.one * 0.5f;
                _dialoguePrefab = prefab.AddComponent<NetworkObject>();
                prefab.AddComponent<DialogueNpc>();
                prefab.SetActive(false);
                _manager.SpawnablePrefabs.AddObject(_dialoguePrefab, checkForDuplicates: true, initializeAdded: true);
            }

            DialogueNpc template = _dialoguePrefab.GetComponent<DialogueNpc>();
            template.SetRoot(root);
            template.SetInteractRange(10f);

            GameObject instance = Object.Instantiate(_dialoguePrefab.gameObject, position, Quaternion.identity);
            instance.name = _dialoguePrefab.name;
            instance.SetActive(true);
            _manager.ServerManager.Spawn(instance);
            _spawned.Add(instance.GetComponent<NetworkObject>());
            return instance.GetComponent<DialogueNpc>();
        }

        private QuestData CreateQuest(
            string questId,
            QuestObjective[] objectives,
            int rewardXp,
            WeightedLootTable rewardTable = null)
        {
            var quest = ScriptableObject.CreateInstance<QuestData>();
            quest.name = questId;

            var inspector = new SerializedObject(quest);
            inspector.FindProperty("_questId").stringValue = questId;
            inspector.FindProperty("_title").stringValue = questId;
            SerializedProperty objectiveArray = inspector.FindProperty("_objectives");
            objectiveArray.arraySize = objectives.Length;
            for (int i = 0; i < objectives.Length; i++)
            {
                SerializedProperty objective = objectiveArray.GetArrayElementAtIndex(i);
                objective.FindPropertyRelative("Kind").enumValueIndex = (int)objectives[i].Kind;
                objective.FindPropertyRelative("TargetName").stringValue = objectives[i].TargetName;
                objective.FindPropertyRelative("RequiredCount").intValue = objectives[i].RequiredCount;
            }

            inspector.FindProperty("_prerequisiteQuestIds").arraySize = 0;
            inspector.FindProperty("_rewardXp").intValue = rewardXp;
            inspector.FindProperty("_rewardTable").objectReferenceValue = rewardTable;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            QuestRegistry.Register(quest);
            _scratch.Add(quest);
            return quest;
        }

        private DialogueNode CreateDialogue(string nodeId, params DialogueChoice[] choices)
        {
            var node = ScriptableObject.CreateInstance<DialogueNode>();
            node.name = nodeId;

            var inspector = new SerializedObject(node);
            inspector.FindProperty("_nodeId").stringValue = nodeId;
            inspector.FindProperty("_speaker").stringValue = "Quest Giver";
            inspector.FindProperty("_text").stringValue = "What do you need?";
            SerializedProperty choiceArray = inspector.FindProperty("_choices");
            choiceArray.arraySize = choices.Length;
            for (int i = 0; i < choices.Length; i++)
            {
                SerializedProperty choice = choiceArray.GetArrayElementAtIndex(i);
                choice.FindPropertyRelative("Text").stringValue = choices[i].Text;
                choice.FindPropertyRelative("Next").objectReferenceValue = choices[i].Next;
                choice.FindPropertyRelative("Action").enumValueIndex = (int)choices[i].Action;
                choice.FindPropertyRelative("Quest").objectReferenceValue = choices[i].Quest;
            }
            inspector.ApplyModifiedPropertiesWithoutUndo();

            _scratch.Add(node);
            return node;
        }

        private ItemDefinition CreateItem(string name)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.name = name;

            var inspector = new SerializedObject(item);
            inspector.FindProperty("_itemName").stringValue = name;
            inspector.FindProperty("_gridWidth").intValue = 1;
            inspector.FindProperty("_gridHeight").intValue = 1;
            inspector.FindProperty("_maxDurability").intValue = 0;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            ItemDefinitionRegistry.Register(item);
            _scratch.Add(item);
            return item;
        }

        private WeaponData CreateWeapon(int damage, float range)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponData>();
            weapon.name = "Quest Test Weapon";

            var inspector = new SerializedObject(weapon);
            inspector.FindProperty("_weaponName").stringValue = weapon.name;
            inspector.FindProperty("_autoAttackDamage").intValue = damage;
            inspector.FindProperty("_attackRange").floatValue = range;
            inspector.ApplyModifiedPropertiesWithoutUndo();

            _scratch.Add(weapon);
            return weapon;
        }

        private static QuestObjective Objective(
            QuestObjectiveKind kind,
            string targetName,
            int requiredCount)
        {
            return new QuestObjective
            {
                Kind = kind,
                TargetName = targetName,
                RequiredCount = requiredCount,
            };
        }

        private static DialogueChoice Choice(string text, DialogueAction action, QuestData quest)
        {
            return new DialogueChoice
            {
                Text = text,
                Next = null,
                Action = action,
                Quest = quest,
            };
        }

        private int ProgressCount(string questId)
        {
            int count = 0;
            IReadOnlyList<QuestProgressEntry> progress = _quests.Progress;
            for (int i = 0; i < progress.Count; i++)
            {
                if (progress[i].QuestId == questId)
                    count += progress[i].Count;
            }

            return count;
        }

        private static IEnumerator WaitUntil(Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
                yield return null;
        }
    }
}
