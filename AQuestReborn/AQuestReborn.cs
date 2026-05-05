using Anamnesis.GameData;
using Brio;
using Brio.Capabilities.Actor;
using Brio.Capabilities.Posing;
using Brio.Entities;
using Brio.Game.Actor;
using Brio.IPC;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Config;
using Dalamud.Game.Gui.Toast;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Common.Lua;
using Lumina.Excel.Sheets;
using McdfDataImporter;
using RoleplayingMediaCore;
using RoleplayingQuestCore;
using RoleplayingVoiceDalamud.Glamourer;
using RoleplayingVoiceDalamudWrapper;
using SamplePlugin;
using Swan;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AQuestReborn.CustomNpc;
using static RoleplayingQuestCore.QuestEvent;
using Utf8String = FFXIVClientStructs.FFXIV.Client.System.String.Utf8String;

namespace AQuestReborn
{
    internal class AQuestReborn
    {
        public Plugin Plugin { get; }
        public Dictionary<string, Dictionary<string, ICharacter>> SpawnedNPCs { get => _spawnedNpcsDictionary; set => _spawnedNpcsDictionary = value; }
        public string Discriminator { get => _discriminator; set => _discriminator = value; }
        public Dictionary<string, InteractiveNpc> InteractiveNpcDictionary { get => _interactiveNpcDictionary; set => _interactiveNpcDictionary = value; }
        public PlayerGroundMap GroundMap { get; } = new PlayerGroundMap();
        public bool WaitingForMcdfLoad { get => _waitingForAppearanceLoad; set => _waitingForAppearanceLoad = value; }
        public static MediaGameObject PlayerObject { get => _playerObject; set => _playerObject = value; }
        public static nint PlayerAddress { get => _playerAddress; set => _playerAddress = value; }
        public static CharacterCustomization PlayerAppearanceData { get; internal set; }
        public static string PlayerClassJob { get; set; }
        public Stopwatch CheckCooldownTimer { get => _checkCooldownTimer; set => _checkCooldownTimer = value; }
        internal CutsceneCamera CutsceneCamera { get => _cutsceneCamera; set => _cutsceneCamera = value; }
        public InteractiveNpc CutscenePlayer { get => _cutscenePlayer; set => _cutscenePlayer = value; }
        public Dictionary<string, ICharacter> CustomNpcCharacters => _customNpcCharacters;
        public Dictionary<string, NPCConversationManager> CustomNpcConversationManagers => _customNpcConversationManagers;

        /// <summary>
        /// Generates a composite key for quest NPCs so that two quests can have
        /// NPCs with the same name without colliding in _interactiveNpcDictionary.
        /// </summary>
        public static string QuestNpcKey(string questId, string npcName) => $"{questId}::{npcName}";

        private Stopwatch _pollingTimer;
        private Stopwatch _inputCooldown;
        private Stopwatch _mcdfRefreshTimer = new Stopwatch();
        private Stopwatch _actorSpawnRefreshTimer = new Stopwatch();
        private Stopwatch _mapRefreshTimer = new Stopwatch();
        private Stopwatch _passiveObjectiveRefreshTimer = new Stopwatch();
        private Stopwatch _checkCooldownTimer = new Stopwatch();
        private bool _screenButtonClicked;
        private Dictionary<string, Dictionary<string, ICharacter>> _spawnedNpcsDictionary = new Dictionary<string, Dictionary<string, ICharacter>>();
        private Dictionary<string, InteractiveNpc> _interactiveNpcDictionary = new Dictionary<string, InteractiveNpc>();
        private Dictionary<string, Tuple<int, Stopwatch>> _objectiveTimers = new Dictionary<string, Tuple<int, Stopwatch>>();
        private bool _triggerRefresh;
        private bool _waitingForSelectionRelease;
        Queue<Tuple<string, AppearanceSwapType, ICharacter>> _appearanceApplicationQueue = new Queue<Tuple<string, AppearanceSwapType, ICharacter>>();
        Queue<Tuple<Transform, string, string, Dictionary<string, ICharacter>, bool, RoleplayingQuest, bool>> _npcActorSpawnQueue = new Queue<Tuple<Transform, string, string, Dictionary<string, ICharacter>, bool, RoleplayingQuest, bool>>();
        private ActorSpawnService _actorSpawnService;
        private static MediaGameObject _playerObject;
        private unsafe Camera* _camera;
        private MediaCameraObject _playerCamera;
        private List<Tuple<int, QuestObjective, RoleplayingQuest>> _activeQuestChainObjectives;
        private bool alreadyProcessingRespawns;
        private bool _waitingForAppearanceLoad;
        Stopwatch zoneChangeCooldown = new Stopwatch();
        private bool _isInitialized;
        private bool _initializationStarted;
        private bool _refreshingNPCQuests;
        private string _discriminator;
        private bool _gotZoneDiscriminator;
        private bool _checkForPartyMembers;
        private InteractiveNpc _cutscenePlayer;
        private bool _cutsceneNpcSpawned;
        private bool _cutsceneNpcSpawnScheduled;
        private bool _hasCheckedForPlayerAppearance;
        private bool _disposed;
        private static nint _playerAddress;
        private CutsceneCamera _cutsceneCamera;
        private bool _dummyNpcSpawned;
        // Icon ID for custom NPC markers on the map/minimap. Change to any valid icon ID from /xldata.
        private const uint NPC_MAP_ICON = 61483;
        private Dictionary<string, InteractiveNpc> _customNpcDictionary = new Dictionary<string, InteractiveNpc>();
        private Dictionary<string, ICharacter> _customNpcCharacters = new Dictionary<string, ICharacter>();
        private Dictionary<string, NPCConversationManager> _customNpcConversationManagers = new Dictionary<string, NPCConversationManager>();
        // Hidden pool: dismissed NPCs are buried underground instead of destroyed, for instant re-summon
        private Dictionary<string, (InteractiveNpc Npc, ICharacter Character)> _hiddenNpcPool = new Dictionary<string, (InteractiveNpc, ICharacter)>();

        // Tail objective state
        private bool _tailObjectiveActive;
        public bool IsTailObjectiveActive => _tailObjectiveActive;
        private string _tailObjectiveNpcName;
        private QuestObjective _tailObjectiveRef;
        private Stopwatch _tailDetectionCooldown = new Stopwatch();
        public DateTime LastNpcChatTime { get; set; } = DateTime.MinValue;

        public AQuestReborn(Plugin plugin)
        {
            Plugin = plugin;
            plugin.RoleplayingQuestManager.LoadMainQuestGameObject(new QuestGameObject(plugin.ObjectTable, plugin.ClientState));
            Plugin.DialogueBackgroundWindow.ButtonClicked += DialogueBackgroundWindow_buttonClicked;
            Plugin.ObjectiveWindow.OnSelectionAttempt += DialogueBackgroundWindow_buttonClicked;
            Plugin.QuestAcceptanceWindow.OnQuestAccepted += QuestAcceptanceWindow_OnQuestAccepted;
            plugin.RoleplayingQuestManager.OnQuestTextTriggered += _roleplayingQuestManager_OnQuestTextTriggered;
            plugin.RoleplayingQuestManager.OnQuestStarted += _roleplayingQuestManager_OnQuestStarted;
            plugin.RoleplayingQuestManager.OnQuestCompleted += _roleplayingQuestManager_OnQuestCompleted;
            plugin.RoleplayingQuestManager.OnObjectiveCompleted += _roleplayingQuestManager_OnObjectiveCompleted;
            plugin.RoleplayingQuestManager.OnQuestAcceptancePopup += _roleplayingQuestManager_OnQuestAcceptancePopup;
            plugin.RewardWindow.OnRewardClosed += RewardWindow_OnRewardClosed;
            Plugin.Framework.Update += _framework_Update;
            Plugin.ClientState.Login += _clientState_Login;
            Plugin.ClientState.TerritoryChanged += _clientState_TerritoryChanged;
            Plugin.ClientState.Logout += ClientState_Logout;
            Plugin.ChatGui.ChatMessage += ChatGui_ChatMessage;
            Plugin.NamePlateGui.OnNamePlateUpdate += NamePlateGui_OnNamePlateUpdate;
            Plugin.EmoteReaderHook.OnEmote += (instigator, emoteId) => OnEmote(instigator as ICharacter, emoteId);
            Translator.LoadCache(Path.Combine(Plugin.Configuration.QuestInstallFolder, "languageCache.json"));
            Translator.UiLanguage = Plugin.Configuration.QuestLanguage;
            Translator.OnError += Translator_OnError;
            Translator.OnTranslationEvent += Translator_OnTranslationEvent;
            try
            {
                _cutsceneCamera = new CutsceneCamera(plugin);
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Warning(ex, "CutsceneCamera initialization failed - cutscene camera features will be unavailable");
            }
        }

        private void Translator_OnTranslationEvent(object? sender, string e)
        {
            Plugin.PluginLog.Verbose(e);
        }

        private void NamePlateGui_OnNamePlateUpdate(Dalamud.Game.Gui.NamePlate.INamePlateUpdateContext context, System.Collections.Generic.IReadOnlyList<Dalamud.Game.Gui.NamePlate.INamePlateUpdateHandler> handlers)
        {
            foreach (var handler in handlers)
            {
                if (handler.GameObject == null) continue;

                // Check Custom NPCs
                foreach (var kvp in _customNpcCharacters)
                {
                    if (kvp.Value != null && kvp.Value.Address == handler.GameObject.Address)
                    {
                        handler.NameParts.Text = kvp.Key;
                        break;
                    }
                }

                // Check Spawned Quest NPCs
                foreach (var questKvp in _spawnedNpcsDictionary)
                {
                    foreach (var npcKvp in questKvp.Value)
                    {
                        if (npcKvp.Value != null && npcKvp.Value.Address == handler.GameObject.Address)
                        {
                            handler.NameParts.Text = npcKvp.Key;
                            break;
                        }
                    }
                }
            }
        }

        private void Translator_OnError(object? sender, string e)
        {
            Plugin.PluginLog.Warning(e);
        }

        private void ClientState_Logout(int type, int code)
        {
            // Invalidate all native character references — they point at freed memory now
            _customNpcCharacters.Clear();
            _customNpcDictionary.Clear();
            _hiddenNpcPool.Clear();
            _nameplateForcedActors.Clear();
            _spawnedNpcsDictionary.Clear();
            _interactiveNpcDictionary.Clear();
            _customNpcConversationManagers.Clear();
            // Reset initialization flags so CheckInitialization re-runs on next login
            _isInitialized = false;
            _initializationStarted = false;
            CleanupCache();
        }

        private void CleanupCache()
        {
            try
            {
                if (Directory.Exists(AppearanceAccessUtils.CacheLocation))
                {
                    foreach (var file in Directory.EnumerateFiles(AppearanceAccessUtils.CacheLocation, "*.tmp"))
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Warning(e, e.Message);
            }
        }

        public void TestActionSheet()
        {
            var props = typeof(Lumina.Excel.Sheets.Action).GetProperties();
            foreach (var p in props)
            {
                Plugin.PluginLog.Information("Action Prop: " + p.Name);
            }
        }

        private unsafe void ChatGui_ChatMessage(Dalamud.Game.Chat.IChatMessage chatMessage)
        {
            try
            {
                if (chatMessage.Message.ToString().ToLower() == "/rerollmodels")
                {
                    foreach (var npc in Plugin.Configuration.CustomNpcCharacters)
                    {
                        npc.ModelChoice = "";
                    }
                    Plugin.Configuration.Save();
                    Plugin.PluginLog.Information("All Custom NPC models have been reset for re-rolling.");
                    return;
                }

                Plugin.PluginLog.Debug((int)chatMessage.LogKind + " " + chatMessage.Message);
                var messageAsString = chatMessage.Message.ToString();
                var chatType = (Dalamud.Game.Text.XivChatType)chatMessage.LogKind;
                if (chatType == Dalamud.Game.Text.XivChatType.NPCDialogue || 
                    chatType == Dalamud.Game.Text.XivChatType.NPCDialogueAnnouncements)
                {
                    lock (CustomNpc.NPCConversationManager.RecentGameDialogue)
                    {
                        string senderName = chatMessage.Sender?.ToString() ?? "Unknown";
                        string formattedLine = $"{senderName}: \"{messageAsString}\"";
                        CustomNpc.NPCConversationManager.RecentGameDialogue.Add(formattedLine);
                        if (CustomNpc.NPCConversationManager.RecentGameDialogue.Count > 2)
                        {
                            CustomNpc.NPCConversationManager.RecentGameDialogue.RemoveAt(0);
                        }
                    }
                }
                else if ((int)chatType == 41 || (int)chatType == 42 || (int)chatType == 43 || (int)chatType == 58)
                {
                    lock (CustomNpc.NPCConversationManager.RecentCombatEvents)
                    {
                        CustomNpc.NPCConversationManager.RecentCombatEvents.Add(messageAsString);
                        if (CustomNpc.NPCConversationManager.RecentCombatEvents.Count > 3)
                        {
                            CustomNpc.NPCConversationManager.RecentCombatEvents.RemoveAt(0);
                        }
                    }
                }

                switch ((int)chatMessage.LogKind)
                {
                    case 2874:
                        Task.Run(() =>
                        {
                            while (Conditions.Instance()->InCombat)
                            {
                                Thread.Sleep(1000);
                            }
                            Plugin.RoleplayingQuestManager.AttemptProgressingQuestObjective(QuestObjective.ObjectiveTriggerType.KillEnemy, messageAsString, true, GetMonsterIndex(messageAsString));
                        });
                        break;
                    case 4922:
                        if (Conditions.Instance()->BoundByDuty)
                        {
                            Task.Run(() =>
                            {
                                while (Conditions.Instance()->InCombat)
                                {
                                    Thread.Sleep(1000);
                                }
                                Plugin.RoleplayingQuestManager.AttemptProgressingQuestObjective(QuestObjective.ObjectiveTriggerType.KillEnemy, messageAsString, true, GetMonsterIndex(messageAsString));
                            });
                        }
                        break;

                }
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Warning(e, e.Message);
            }
        }
        public uint GetMonsterIndex(string value)
        {
            var loweredText = value.ToLower();
            foreach (var item in Plugin.DataManager.GetExcelSheet<BNpcName>())
            {
                try
                {
                    var monster = item.Singular.ExtractText().ToLower();
                    if (!string.IsNullOrWhiteSpace(monster))
                    {
                        if (loweredText.Contains(monster))
                        {
                            Plugin.PluginLog.Debug(loweredText + " compared to " + monster);
                            return item.RowId;
                        }
                    }
                }
                catch { }
                try
                {
                    var monster = item.Plural.ExtractText().ToLower();
                    if (!string.IsNullOrWhiteSpace(monster))
                    {
                        if (loweredText.Contains(monster))
                        {
                            Plugin.PluginLog.Debug(loweredText + " compared to " + monster);
                            return item.RowId;
                        }
                    }
                }
                catch { }
            }
            return 0;
        }
        private void RewardWindow_OnRewardClosed(object? sender, RoleplayingQuest e)
        {
            QuestToastOptions questToastOptions = new QuestToastOptions();
            string path = Path.Combine(e.FoundPath, e.QuestEndTitleCard);
            string soundPath = Path.Combine(e.FoundPath, e.QuestEndTitleSound);
            Plugin.TitleCardWindow.DisplayCard(path, soundPath, true);
            Plugin.Configuration.Save();
        }

        private void OnEmote(ICharacter character, ushort emoteId)
        {
            try
            {
                if (!Plugin.EventWindow.IsOpen && !Plugin.ChoiceWindow.IsOpen)
                {
                    Plugin.RoleplayingQuestManager.AttemptProgressingQuestObjective(QuestObjective.ObjectiveTriggerType.DoEmote, emoteId.ToString());
                }
                if (Plugin.EditorWindow.IsOpen)
                {
                    Emote emote = Plugin.DataManager.GetExcelSheet<Emote>().GetRow(emoteId);
                    Plugin.ChatGui.Print("Emote Id: " + emoteId);
                    Plugin.ChatGui.Print("Body Animation Id: " + emote.ActionTimeline[0].Value.RowId);
                }

                // Emote mirroring for custom NPCs - only react to the local player
                if (character != null && Plugin.ObjectTable.LocalPlayer != null
                    && character.Address == Plugin.ObjectTable.LocalPlayer.Address)
                {
                    const float EMOTE_REACT_RANGE = 15f;
                    const ushort BECKON_EMOTE_ID = 8;
                    const ushort GOODBYE_EMOTE_ID = 15;

                    // Only iterate custom NPCs - never quest NPCs
                    foreach (var kvp in _customNpcDictionary.ToList())
                    {
                        var npc = kvp.Value;
                        if (npc == null || npc.Character == null) continue;

                        float distance = Vector3.Distance(npc.CurrentPosition, Plugin.ObjectTable.LocalPlayer.Position);
                        if (distance > EMOTE_REACT_RANGE) continue;

                        if (emoteId == BECKON_EMOTE_ID)
                        {
                            // Beckon: NPC starts following
                            npc.FollowPlayer(2);
                            // Update config state
                            var npcConfig = Plugin.Configuration.CustomNpcCharacters?.Find(n => n.NpcName == kvp.Key);
                            if (npcConfig != null)
                            {
                                npcConfig.IsFollowingPlayer = true;
                                npcConfig.IsStaying = false;
                                Plugin.Configuration.Save();
                            }
                        }
                        else if (emoteId == GOODBYE_EMOTE_ID)
                        {
                            // Goodbye: following NPCs stop and stay
                            var pos = npc.CurrentPosition;
                            var rot = npc.CurrentRotation;
                            npc.StopFollowingPlayer();
                            npc.SetDefaults(pos, rot);
                            npc.SetDefaultRotation(rot);
                            // Save stay state
                            var npcConfig = Plugin.Configuration.CustomNpcCharacters?.Find(n => n.NpcName == kvp.Key);
                            if (npcConfig != null)
                            {
                                npcConfig.IsStaying = true;
                                npcConfig.IsFollowingPlayer = false;
                                npcConfig.StayTerritoryId = Plugin.ClientState.TerritoryType;
                                npcConfig.StayPositionX = pos.X;
                                npcConfig.StayPositionY = pos.Y;
                                npcConfig.StayPositionZ = pos.Z;
                                npcConfig.StayRotationX = rot.X;
                                npcConfig.StayRotationY = rot.Y;
                                npcConfig.StayRotationZ = rot.Z;
                                Plugin.Configuration.Save();
                            }
                        }
                        else if (npc.IsStationary)
                        {
                            // Sit emotes: NPCs settle into their idle sooner
                            if (emoteId == 50 || emoteId == 52)
                            {
                                npc.TriggerIdleSoon();
                            }
                            else
                            {
                                // Mirror the emote if the NPC is standing still
                                npc.ReactToEmote(emoteId);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Warning(e, e.Message);
            }
        }

        private void _clientState_TerritoryChanged(uint territory)
        {
            try
            {
                PenumbraAndGlamourerIpcWrapper.Instance.SetCollectionForObject.Invoke((int)201, Guid.Empty);
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Warning(e, e.Message);
            }
            CleanupTailObjective();
            ClearNPCs(territory);
        }

        private void ClearNPCs(uint territory)
        {
            try
            {
                _cutsceneNpcSpawnScheduled = false;
                _pollingTimer = new Stopwatch();
                _pollingTimer.Start();
                _inputCooldown = new Stopwatch();
                _inputCooldown.Start();
                _actorSpawnRefreshTimer.Start();
                _mapRefreshTimer.Start();
                _appearanceApplicationQueue.Clear();
                _npcActorSpawnQueue.Clear();
                zoneChangeCooldown.Reset();
                _spawnedNpcsDictionary.Clear();
                _mcdfRefreshTimer.Reset();
                _interactiveNpcDictionary.Clear();
                _nameplateForcedActors.Clear();
                _hasCheckedForPlayerAppearance = false;
                GroundMap.SetTerritory(territory);

                // Stamp last-seen for all custom NPCs before cleanup (zone departure)
                try
                {
                    string playerName = Plugin.ObjectTable.LocalPlayer?.Name?.TextValue ?? "Adventurer";
                    var activeNpcNames = new List<string>(_customNpcCharacters.Keys);
                    foreach (var npcData in Plugin.Configuration.CustomNpcCharacters)
                    {
                        if (activeNpcNames.Contains(npcData.NpcName))
                        {
                            npcData.UpdateLastSeen(playerName);
                            // Also stamp NPC-to-NPC last-seen for all co-present NPCs
                            foreach (var otherName in activeNpcNames)
                            {
                                if (otherName != npcData.NpcName)
                                    npcData.UpdateLastSeen(otherName);
                            }

                            // Staying NPCs are being left behind; following NPCs are travelling with you
                            if (npcData.IsStaying && !npcData.IsFollowingPlayer)
                                npcData.WasLeftBehind = true;
                            else
                                npcData.WasLeftBehind = false;
                        }
                    }
                    Plugin.Configuration.Save();
                }
                catch { }

                // Flush conversation summaries to disk before cleanup
                foreach (var convMgr in _customNpcConversationManagers.Values)
                {
                    try
                    {
                        _ = Task.Run(async () =>
                        {
                            try { await convMgr.FlushSummaries(); }
                            catch { }
                        });
                    }
                    catch { }
                }

                // Clean up custom NPC references (Brio actors are destroyed on zone change)
                foreach (var kvp in _customNpcDictionary)
                {
                    try { kvp.Value.Dispose(); } catch { }
                }
                _customNpcDictionary.Clear();
                _customNpcCharacters.Clear();
                _customNpcConversationManagers.Clear();

                // Also clear the hidden pool — Brio actors don't survive zone changes
                foreach (var kvp in _hiddenNpcPool)
                {
                    try { kvp.Value.Npc.Dispose(); } catch { }
                }
                _hiddenNpcPool.Clear();

                Task.Run(() =>
                {
                    try
                    {
                        bool stillLoading = true;
                        while (stillLoading)
                        {
                            if (!Plugin.ClientState.IsLoggedIn)
                            {
                                // Logged out while waiting — abort respawn
                                return;
                            }
                            unsafe { stillLoading = _actorSpawnService == null || Conditions.Instance()->BetweenAreas; }
                            if (stillLoading) Thread.Sleep(3000);
                        }
                        _triggerRefresh = true;
                        _gotZoneDiscriminator = false;
                        _checkForPartyMembers = true;
                        _cutsceneNpcSpawned = false;
                        _dummyNpcSpawned = false;

                        // Respawn custom NPCs that were active before zone change
                        RespawnActiveCustomNpcs();
                    }
                    catch (Exception e)
                    {
                        Plugin.PluginLog.Warning(e, e.Message);
                    }
                });
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Warning(e, e.Message);
            }
        }

        private void RespawnActiveCustomNpcs()
        {
            if (Plugin.Configuration.CustomNpcCharacters == null)
            {
                Plugin.PluginLog.Information("[Custom NPC] No CustomNpcCharacters in config.");
                return;
            }
            // Don't spawn custom NPCs during duties with other real players
            unsafe
            {
                if (Conditions.Instance()->BoundByDuty)
                {
                    // Check if there are real players in the party (not solo/Trust)
                    var groupManager = GroupManager.Instance();
                    int memberCount = groupManager != null ? groupManager->MainGroup.MemberCount : 0;
                    if (memberCount > 1)
                    {
                        Plugin.PluginLog.Information("[Custom NPC] Skipping respawn — in a duty with other players.");
                        return;
                    }
                }
            }
            // Wait for zone to be fully loaded before spawning
            Thread.Sleep(3000);

            // Wait for the cutscene player slot to be reserved.
            // The cutscene player is now always spawned to reserve Brio slot 0.
            int waitedMs = 0;
            while (!_cutsceneNpcSpawned && waitedMs < 15000)
            {
                if (!Plugin.ClientState.IsLoggedIn) return;
                Thread.Sleep(500);
                waitedMs += 500;
            }
            uint currentTerritory = Plugin.ClientState.TerritoryType;
            Plugin.PluginLog.Information("[Custom NPC] Checking " + Plugin.Configuration.CustomNpcCharacters.Count + " NPCs for respawn in territory " + currentTerritory);
            int spawned = 0;
            foreach (var npcData in Plugin.Configuration.CustomNpcCharacters)
            {
                if (spawned >= MAX_CUSTOM_NPCS) break;

                if (npcData.IsFollowingPlayer && !npcData.IsStaying)
                {
                    // Following NPCs spawn in any zone
                    Plugin.PluginLog.Information("[Custom NPC] Respawning follower: " + npcData.NpcName);
                    Thread.Sleep(1000);
                    SummonCustomNpc(npcData);
                    spawned++;
                }
                else if (npcData.IsStaying && npcData.StayTerritoryId == currentTerritory)
                {
                    // Staying NPCs only spawn if we're in their saved territory
                    Plugin.PluginLog.Information("[Custom NPC] Respawning at stay location: " + npcData.NpcName);
                    Thread.Sleep(1000);
                    SummonCustomNpcAtPosition(npcData,
                        new System.Numerics.Vector3(npcData.StayPositionX, npcData.StayPositionY, npcData.StayPositionZ),
                        new System.Numerics.Vector3(npcData.StayRotationX, npcData.StayRotationY, npcData.StayRotationZ));
                    spawned++;
                }
            }
        }

        public unsafe void RefreshMapMarkers()
        {
            try
            {
                if (Plugin.ClientState.IsLoggedIn && !Conditions.Instance()->BetweenAreas)
                {
                    _activeQuestChainObjectives = Plugin.RoleplayingQuestManager.GetActiveQuestChainObjectivesInZone((int)Plugin.ClientState.TerritoryType, _discriminator);
                    unsafe
                    {
                        AgentMap.Instance()->ResetMapMarkers();
                        AgentMap.Instance()->ResetMiniMapMarkers();
                        foreach (var item in _activeQuestChainObjectives)
                        {
                            if (!item.Item2.DontShowOnMap && !item.Item2.ObjectiveCompleted)
                            {
                                {
                                    var map = Plugin.DataManager.GetExcelSheet<TerritoryType>().GetRow((ushort)Plugin.ClientState.TerritoryType).Map.Value;
                                    var scaleFactor = map.SizeFactor;

                                    Utf8String* stringBuffer = Utf8String.CreateEmpty();
                                    stringBuffer->SetString(item.Item3.QuestName);

                                    uint icon = (item.Item1 == 0 ? (uint)230604 : (uint)230605);
                                    var offset = new Vector3(map.OffsetX, 0, map.OffsetY);

                                    AgentMap.Instance()->AddMapMarker(item.Item2.Coordinates + offset, icon, 0, stringBuffer->StringPtr);
                                    AgentMap.Instance()->AddMiniMapMarker(item.Item2.Coordinates + offset, icon);
                                }
                            }
                        }

                        // Add custom NPC markers to the map/minimap
                        foreach (var kvp in _customNpcCharacters)
                        {
                            try
                            {
                                if (kvp.Value == null || !kvp.Value.IsValid()) continue;
                                // Skip NPCs in the hidden pool (underground)
                                if (_hiddenNpcPool.ContainsKey(kvp.Key)) continue;

                                var map = Plugin.DataManager.GetExcelSheet<TerritoryType>().GetRow((ushort)Plugin.ClientState.TerritoryType).Map.Value;
                                var offset = new Vector3(map.OffsetX, 0, map.OffsetY);
                                var npcWorldPos = kvp.Value.Position;
                                var npcMapPos = new Vector3(npcWorldPos.X, 0, npcWorldPos.Z) + offset;

                                Utf8String* npcNameBuffer = Utf8String.CreateEmpty();
                                npcNameBuffer->SetString(kvp.Key);

                                AgentMap.Instance()->AddMapMarker(npcMapPos, NPC_MAP_ICON, 0, npcNameBuffer->StringPtr);
                                AgentMap.Instance()->AddMiniMapMarker(npcMapPos, NPC_MAP_ICON);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Warning(e, e.Message);
            }
        }
        private void _clientState_Login()
        {
            if (Plugin.ClientState.IsLoggedIn)
            {
                InitializeMediaManager();
                _checkForPartyMembers = true;
                RefreshNpcs((ushort)Plugin.ClientState.TerritoryType);
                _gotZoneDiscriminator = false;
            }
        }

        public unsafe void InitializeMediaManager()
        {
            try
            {
                if (_playerObject == null)
                {
                    _playerObject = new MediaGameObject(Plugin.ObjectTable.LocalPlayer);
                }

                if (_playerCamera == null)
                {
                    _camera = CameraManager.Instance()->GetActiveCamera();
                    _playerCamera = new MediaCameraObject(_camera);
                }

                Plugin.MediaManager = new MediaManager(_playerObject, _playerCamera,
                Path.GetDirectoryName(Plugin.DalamudPluginInterface.AssemblyLocation.FullName));
                Plugin.DialogueBackgroundWindow.MediaManager = Plugin.MediaManager;
                Plugin.MediaManager.LowPerformanceMode = true;
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Warning(e, e.Message);
            }
        }

        public void RefreshPlaceHolderCutscenePlayer()
        {
            try
            {
                Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    if (_cutscenePlayer != null && _cutscenePlayer.Character.Name.TextValue == "Cutscene Player")
                    {
                        var collection = PenumbraAndGlamourerIpcWrapper.Instance.GetCollectionForObject.Invoke((int)Plugin.ObjectTable.LocalPlayer.ObjectIndex);
                        PenumbraAndGlamourerIpcWrapper.Instance.SetCollectionForObject.Invoke((int)_cutscenePlayer.Character.ObjectIndex, collection.EffectiveCollection.Id);
                        PenumbraAndGlamourerIpcWrapper.Instance.RedrawObject.Invoke((int)_cutscenePlayer.Character.ObjectIndex, Penumbra.Api.Enums.RedrawType.Redraw);
                        var design = PenumbraAndGlamourerIpcWrapper.Instance.GetStateBase64.Invoke(Plugin.ObjectTable.LocalPlayer.ObjectIndex);
                        AppearanceAccessUtils.AppearanceManager.LoadAppearance(design.Item2, _cutscenePlayer.Character, 0);
                    }
                });
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Warning(e, e.Message);
            }
        }
        private void ScheduleCutsceneNpcSpawn()
        {
            _cutsceneNpcSpawnScheduled = true;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        try
                        {
                            // Here we spawn an NPC for the purposes of acting as the player character in simulated cutscenes.
                            if (_actorSpawnService.CreateCharacter(out ICharacter character, SpawnFlags.DefinePosition, true,
                            (new Vector3(0, float.MaxValue, 0) / 8), CoordinateUtility.ConvertDegreesToRadians(0))
                            && character != null)
                            {
                                _cutscenePlayer = new InteractiveNpc(Plugin, character);
                                _cutscenePlayer.SetDefaults((new Vector3(0, float.MaxValue, 0) / 8), Quaternion.Identity.QuaternionToEuler());
                                _cutscenePlayer.HideNPC();
                                _cutsceneNpcSpawned = true;
                            }
                            else
                            {
                                _cutsceneNpcSpawnScheduled = false;
                            }
                        }
                        catch (Exception e)
                        {
                            Plugin.PluginLog.Warning(e, e.Message);
                            _cutsceneNpcSpawnScheduled = false;
                        }
                    });
                }
                catch (Exception e)
                {
                    Plugin.PluginLog.Warning(e, e.Message);
                    _cutsceneNpcSpawnScheduled = false;
                }
            });
        }

        private HashSet<nint> _nameplateForcedActors = new HashSet<nint>();
        private Stopwatch _penumbraCollectionVerifyTimer = Stopwatch.StartNew();
        // Cache the expected collection Guid per NPC name, set at apply time
        private Dictionary<string, Guid> _expectedNpcCollections = new Dictionary<string, Guid>();
        // Per-NPC cooldown to avoid spamming redraws when DrawObject disappears
        private Dictionary<string, long> _npcRedrawCooldowns = new Dictionary<string, long>();
        // Track each NPC's DrawObject pointer to detect engine recreation (camera-clip culling)
        private Dictionary<string, nint> _npcLastDrawObjectPtr = new Dictionary<string, nint>();
        // Track when the camera clips into an NPC so we can redraw when it exits
        private Dictionary<string, bool> _npcCameraClipped = new Dictionary<string, bool>();
        // Periodic safety redraw: catch invisible mesh states we can't detect
        private Stopwatch _safetyRedrawTimer = Stopwatch.StartNew();
        // Track per-NPC redraw retry count for escalation to nuclear Glamourer re-apply
        private Dictionary<string, int> _npcRedrawRetries = new Dictionary<string, int>();
        // Track when each NPC's DrawObject first became broken for time-based respawn
        private Dictionary<string, long> _npcBrokenTimestamp = new Dictionary<string, long>();
        private unsafe void _framework_Update(IFramework framework)
        {
            // Don't touch game objects during logout/loading/dispose — memory may be freed
            if (_disposed || !Plugin.ClientState.IsLoggedIn) return;

            bool requestedRedraw = false;
            foreach (var kvp in _customNpcCharacters)
            {
                if (kvp.Value != null && kvp.Value.Address != 0)
                {
                    var characterStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)kvp.Value.Address;
                    characterStruct->NamePlateIconId = 71201; // Force Friendly NPC icon

                    // --- Visibility protection ---
                    // First: determine if camera is currently clipped into this NPC.
                    // If so, the engine INTENTIONALLY hides the DrawObject — do NOT fight it.
                    bool cameraIsClipped = false;
                    bool cameraJustExited = false;
                    try
                    {
                        var camera = FFXIVClientStructs.FFXIV.Client.Game.Control.CameraManager.Instance()->GetActiveCamera();
                        if (camera != null)
                        {
                            var cameraPos = camera->CameraBase.SceneCamera.Position;
                            var npcPos = characterStruct->GameObject.Position;
                            float camDist = Vector3.Distance(
                                new Vector3(cameraPos.X, cameraPos.Y, cameraPos.Z),
                                new Vector3(npcPos.X, npcPos.Y, npcPos.Z));

                            _npcCameraClipped.TryGetValue(kvp.Key, out bool wasClipped);
                            cameraIsClipped = camDist < 3.5f;
                            cameraJustExited = wasClipped && !cameraIsClipped;
                            _npcCameraClipped[kvp.Key] = cameraIsClipped;
                        }
                    }
                    catch { }

                    // Check if DrawObject is broken (regardless of camera distance)
                    bool drawObjectMissing = characterStruct->GameObject.DrawObject == null;
                    bool drawObjectHidden = !drawObjectMissing && (characterStruct->GameObject.DrawObject->Flags & 0x10) != 0;
                    bool renderFlagsSet = !drawObjectMissing && characterStruct->GameObject.RenderFlags != 0;
                    // Check if model mesh is gone — DrawObject exists but skeleton is null
                    bool modelBroken = false;
                    if (!drawObjectMissing)
                    {
                        var charBase = (FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CharacterBase*)characterStruct->GameObject.DrawObject;
                        modelBroken = charBase->Skeleton == null;
                    }

                    // Track DrawObject pointer changes
                    if (!drawObjectMissing)
                    {
                        nint currentDrawPtr = (nint)characterStruct->GameObject.DrawObject;
                        _npcLastDrawObjectPtr[kvp.Key] = currentDrawPtr;
                    }

                    if (drawObjectMissing || drawObjectHidden || renderFlagsSet || modelBroken)
                    {
                        // Track when the DrawObject first became broken
                        if (!_npcBrokenTimestamp.ContainsKey(kvp.Key))
                            _npcBrokenTimestamp[kvp.Key] = Environment.TickCount64;

                        long brokenDuration = Environment.TickCount64 - _npcBrokenTimestamp[kvp.Key];

                        // Respawn fallback — fires regardless of camera distance
                        if (brokenDuration > 10000)
                        {
                            _npcBrokenTimestamp.Remove(kvp.Key);
                            _npcRedrawCooldowns.Remove(kvp.Key);
                            _npcLastDrawObjectPtr.Remove(kvp.Key);
                            _npcCameraClipped.Remove(kvp.Key);
                            var npcConfig = Plugin.Configuration.CustomNpcCharacters.FirstOrDefault(n => n.NpcName == kvp.Key);
                            if (npcConfig != null)
                            {
                                Plugin.PluginLog.Warning($"[NPC Visibility] '{kvp.Key}' DrawObject broken for {brokenDuration}ms — destroying and respawning.");
                                try { _actorSpawnService?.DestroyObject(kvp.Value); } catch { }
                                _customNpcCharacters.Remove(kvp.Key);
                                _customNpcDictionary.Remove(kvp.Key);
                                _interactiveNpcDictionary.Remove(kvp.Key);
                                FreshSpawnCustomNpc(npcConfig);
                            }
                            break; // Dictionary modified — exit foreach
                        }

                        // Penumbra recovery — only when camera is NOT clipped (proven to work better)
                        if (!cameraIsClipped)
                        {
                            long now = Environment.TickCount64;
                            _npcRedrawCooldowns.TryGetValue(kvp.Key, out long lastRedraw);
                            if (now - lastRedraw > 3000)
                            {
                                _npcRedrawCooldowns[kvp.Key] = now;

                                if (drawObjectHidden)
                                    characterStruct->GameObject.DrawObject->Flags &= unchecked((byte)~0x10);
                                if (renderFlagsSet)
                                    characterStruct->GameObject.RenderFlags = 0;
                                characterStruct->GameObject.EnableDraw();

                                try
                                {
                                    PenumbraAndGlamourerIpcWrapper.Instance.RedrawObject.Invoke(kvp.Value.ObjectIndex, Penumbra.Api.Enums.RedrawType.Redraw);
                                    Plugin.PluginLog.Information($"[NPC Visibility] '{kvp.Key}' broken for {brokenDuration}ms — Penumbra redraw attempt.");
                                }
                                catch { }
                            }
                        }
                    }
                    else
                    {
                        // Visible and healthy — reset broken timestamp
                        _npcBrokenTimestamp.Remove(kvp.Key);
                    }

                    // Case 6: Position corruption — NPC yeeted far from player
                    if (Plugin.ObjectTable.LocalPlayer != null)
                    {
                        var npcPos = characterStruct->GameObject.Position;
                        var playerPos = Plugin.ObjectTable.LocalPlayer.Position;
                        float distToPlayer = Vector3.Distance(
                            new Vector3(npcPos.X, npcPos.Y, npcPos.Z),
                            playerPos);

                        if (distToPlayer > 100f)
                        {
                            // NPC is way too far — position got corrupted, snap back to player
                            Plugin.PluginLog.Warning($"[NPC Visibility] '{kvp.Key}' is {distToPlayer:F0}y from player — position corrupted, snapping back.");
                            characterStruct->GameObject.SetPosition(playerPos.X, playerPos.Y, playerPos.Z);

                            // Also update the InteractiveNpc's tracked position if it exists
                            if (_interactiveNpcDictionary.TryGetValue(kvp.Key, out var interactiveNpc))
                            {
                                interactiveNpc.TeleportTo(playerPos);
                            }

                            // Redraw after repositioning
                            try
                            {
                                PenumbraAndGlamourerIpcWrapper.Instance.RedrawObject.Invoke(kvp.Value.ObjectIndex, Penumbra.Api.Enums.RedrawType.Redraw);
                            }
                            catch { }
                        }
                    }
                    
                    if (!_nameplateForcedActors.Contains(kvp.Value.Address))
                    {
                        _nameplateForcedActors.Add(kvp.Value.Address);
                        requestedRedraw = true;
                    }
                }
            }
            foreach (var questKvp in _spawnedNpcsDictionary)
            {
                foreach (var npcKvp in questKvp.Value)
                {
                    if (npcKvp.Value != null && npcKvp.Value.Address != 0)
                    {
                        var characterStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)npcKvp.Value.Address;
                        characterStruct->NamePlateIconId = 71201;
                        // RenderFlags left to engine — clearing every frame causes DrawObject corruption
                        
                        if (!_nameplateForcedActors.Contains(npcKvp.Value.Address))
                        {
                            _nameplateForcedActors.Add(npcKvp.Value.Address);
                            requestedRedraw = true;
                        }
                    }
                }
            }

            if (requestedRedraw)
            {
                Plugin.NamePlateGui.RequestRedraw();
            }

            // Periodically verify Penumbra collections are actually applied to custom NPCs
            if (_penumbraCollectionVerifyTimer.ElapsedMilliseconds > 2000 && _customNpcCharacters.Count > 0)
            {
                _penumbraCollectionVerifyTimer.Restart();

                // --- Dead actor detection: re-spawn if the game destroyed our actors ---
                try
                {
                    var deadNpcs = new List<string>();
                    foreach (var kvp in _customNpcCharacters)
                    {
                        bool isDead = false;
                        if (kvp.Value == null || kvp.Value.Address == 0)
                        {
                            isDead = true;
                        }
                        else
                        {
                            // Verify the actor still exists in the object table
                            bool foundInTable = false;
                            foreach (var obj in Plugin.ObjectTable)
                            {
                                if (obj != null && obj.Address == kvp.Value.Address)
                                {
                                    foundInTable = true;
                                    break;
                                }
                            }
                            if (!foundInTable)
                            {
                                isDead = true;
                            }
                            else
                            {
                                // Actor exists but check if model is irrecoverably broken
                                var cs = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)kvp.Value.Address;
                                if (cs->GameObject.DrawObject == null)
                                {
                                    // DrawObject gone — might recover, but if it persists it's broken
                                    isDead = true;
                                    Plugin.PluginLog.Warning($"[NPC Respawn] '{kvp.Key}' has null DrawObject — marking for respawn.");
                                }
                                else
                                {
                                    // Check if the CharacterBase skeleton is null (model destroyed)
                                    var charBase = (FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CharacterBase*)cs->GameObject.DrawObject;
                                    if (charBase->Skeleton == null)
                                    {
                                        isDead = true;
                                        Plugin.PluginLog.Warning($"[NPC Respawn] '{kvp.Key}' has null Skeleton — marking for respawn.");
                                    }
                                    else
                                    {
                                        // Check if head bone position is valid (bone 6 = head)
                                        // If it returns Zero, the model's bone data is corrupt (nameplate detaches from head)
                                        try
                                        {
                                            var headPos = Hypostasis.Game.Common.GetBoneWorldPosition(&cs->GameObject, 6);
                                            if (headPos == Vector3.Zero)
                                            {
                                                isDead = true;
                                                Plugin.PluginLog.Warning($"[NPC Respawn] '{kvp.Key}' head bone returned Zero — model corrupt, marking for respawn.");
                                            }
                                        }
                                        catch
                                        {
                                            isDead = true;
                                            Plugin.PluginLog.Warning($"[NPC Respawn] '{kvp.Key}' bone access threw — model corrupt, marking for respawn.");
                                        }
                                    }
                                }
                            }
                        }
                        if (isDead) deadNpcs.Add(kvp.Key);
                    }

                    if (deadNpcs.Count > 0)
                    {
                        Plugin.PluginLog.Warning($"[NPC Respawn] Detected {deadNpcs.Count} destroyed NPC(s): {string.Join(", ", deadNpcs)}. Scheduling re-spawn.");
                        // Clean up stale tracking and destroy broken actors
                        foreach (var name in deadNpcs)
                        {
                            // Try to destroy the broken actor so the object table slot is freed
                            if (_customNpcCharacters.TryGetValue(name, out var brokenChar) && brokenChar != null)
                            {
                                try { _actorSpawnService?.DestroyObject(brokenChar); }
                                catch { }
                            }
                            _customNpcCharacters.Remove(name);
                            _customNpcDictionary.Remove(name);
                            _interactiveNpcDictionary.Remove(name);
                            _npcRedrawCooldowns.Remove(name);
                            _npcLastDrawObjectPtr.Remove(name);
                            _npcCameraClipped.Remove(name);
                        }
                        // Schedule re-spawns
                        Task.Run(() =>
                        {
                            try
                            {
                                Thread.Sleep(2000);
                                foreach (var name in deadNpcs)
                                {
                                    var npcData = Plugin.Configuration.CustomNpcCharacters.FirstOrDefault(n => n.NpcName == name);
                                    if (npcData != null && npcData.IsFollowingPlayer && !npcData.IsStaying)
                                    {
                                        Plugin.Framework.RunOnFrameworkThread(() => SummonCustomNpc(npcData));
                                        Thread.Sleep(500);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Plugin.PluginLog.Warning(ex, "[NPC Respawn] Failed to re-spawn destroyed NPCs.");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Warning(ex, "[NPC Respawn] Failed dead-actor check.");
                }

                // --- Penumbra collection verification ---
                try
                {
                    if (Brio.Brio.TryGetService<Brio.IPC.PenumbraService>(out var penumbraService))
                    {
                        var collections = penumbraService.GetCollections();
                        foreach (var kvp in _customNpcCharacters)
                        {
                            if (kvp.Value == null || kvp.Value.Address == 0) continue;

                            // Find the NPC config
                            var npcData = Plugin.Configuration.CustomNpcCharacters.FirstOrDefault(n => n.NpcName == kvp.Key);
                            if (npcData == null || !npcData.UsePenumbraCollection || string.IsNullOrEmpty(npcData.PenumbraCollection)) continue;

                            // Resolve the expected Guid
                            var expectedGuid = collections.FirstOrDefault(x => x.Value == npcData.PenumbraCollection).Key;
                            if (expectedGuid == Guid.Empty) continue;

                            // Query what Penumbra currently has assigned
                            try
                            {
                                var currentCollection = PenumbraAndGlamourerIpcWrapper.Instance.GetCollectionForObject.Invoke(kvp.Value.ObjectIndex);
                                var currentGuid = currentCollection.EffectiveCollection.Id;

                                if (currentGuid != expectedGuid)
                                {
                                    Plugin.PluginLog.Information($"Penumbra collection mismatch on {kvp.Key}: expected {npcData.PenumbraCollection}, reapplying.");
                                    PenumbraAndGlamourerIpcWrapper.Instance.SetCollectionForObject.Invoke(kvp.Value.ObjectIndex, expectedGuid, true, true);
                                    PenumbraAndGlamourerIpcWrapper.Instance.RedrawObject.Invoke(kvp.Value.ObjectIndex, Penumbra.Api.Enums.RedrawType.Redraw);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Warning(ex, "Failed to verify Penumbra collections.");
                }
            }
            if (!_disposed)
            {
                try
                {
                    if (!Plugin.ClientState.IsGPosing && !Plugin.ClientState.IsPvPExcludingDen && !Conditions.Instance()->BetweenAreas && !Conditions.Instance()->WatchingCutscene
                        && !Conditions.Instance()->Occupied && !Conditions.Instance()->InCombat && Plugin.ClientState.IsLoggedIn)
                    {
                        // Record player position for NPC ground height map
                        if (Plugin.ObjectTable.LocalPlayer != null)
                        {
                            GroundMap.RecordPosition(Plugin.ObjectTable.LocalPlayer.Position);

                            foreach (var obj in Plugin.ObjectTable)
                            {
                                if (obj != null && obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc)
                                {
                                    // Use memory address comparison to reliably filter out our Custom NPCs regardless of Name overrides
                                    bool isCustomNpc = false;
                                    foreach (var customNpc in _customNpcCharacters.Values)
                                    {
                                        if (customNpc.Address == obj.Address)
                                        {
                                            isCustomNpc = true;
                                            break;
                                        }
                                    }
                                    
                                    if (isCustomNpc) continue;

                                    GroundMap.ForceRecordPosition(obj.Position);
                                }
                            }
                        }
                        // Hopefully waiting prevents crashing on zone changes?
                        if (zoneChangeCooldown.ElapsedMilliseconds > 500)
                        {
                            if (!_isInitialized)
                            {
                                CheckInitialization();
                            }
                            else
                            {
                                bool hasActiveQuests = Plugin.RoleplayingQuestManager.GetActiveQuestChainObjectivesInZone((int)Plugin.ClientState.TerritoryType, _discriminator).Count > 0;
                                bool hasActiveSummons = Plugin.Configuration.CustomNpcCharacters.Any(n => n.IsFollowingPlayer || (n.IsStaying && n.StayTerritoryId == Plugin.ClientState.TerritoryType));
                                if (!_cutsceneNpcSpawned && !_cutsceneNpcSpawnScheduled && (hasActiveQuests || hasActiveSummons))
                                {
                                    ScheduleCutsceneNpcSpawn();
                                }
                                else if (_cutsceneNpcSpawned)
                                {
                                    CheckForPassiveQuestProgression();
                                    CheckForNewAppearanceLoad();
                                    QuestInputCheck();
                                    CheckForNewPlayerCreationLoad();
                                    CheckForNPCRefresh();
                                    CheckForMapRefresh();
                                    if (_checkCooldownTimer.ElapsedMilliseconds > 500)
                                    {
                                        CheckZoneDiscriminator();
                                        CheckForPlayerAppearance();
                                        _checkCooldownTimer.Restart();
                                    }
                                }
                            }
                        }
                        if (!zoneChangeCooldown.IsRunning)
                        {
                            zoneChangeCooldown.Start();
                        }
                        // Ensure ObjectiveWindow is open whenever custom NPCs exist,
                        // even if no quest cutscene NPC has been spawned in this zone.
                        if (_customNpcCharacters.Count > 0 || _spawnedNpcsDictionary.Count > 0)
                        {
                            Plugin.ObjectiveWindow.IsOpen = true;
                        }
                        // Custom NPC click-to-chat detection
                        CustomNpcChatCheck();
                        // Ambient NPC speech bubbles
                        Plugin.SpeechBubbleManager?.Update();
                    }
                    else
                    {
                        if (Plugin.ClientState.IsGPosing)
                        {
                            if (_cutsceneNpcSpawned || _spawnedNpcsDictionary.Count > 0)
                            {
                                foreach (var item in _interactiveNpcDictionary)
                                {
                                    item.Value?.Dispose();
                                }
                                _interactiveNpcDictionary?.Clear();
                                ClearNPCs(Plugin.ClientState.TerritoryType);
                                _actorSpawnService?.ClearAll();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Warning(ex, ex.Message);
                }
            }
        }

        private void CheckForPassiveQuestProgression()
        {
            if (_passiveObjectiveRefreshTimer.ElapsedMilliseconds > 100 && !Plugin.EventWindow.IsOpen && !Plugin.ChoiceWindow.IsOpen)
            {
                Plugin.RoleplayingQuestManager.AttemptProgressingQuestObjective(QuestObjective.ObjectiveTriggerType.SubObjectivesFinished, "", true);
                Plugin.RoleplayingQuestManager.AttemptProgressingQuestObjective(QuestObjective.ObjectiveTriggerType.BoundingTrigger, "", true);
                CheckForTailObjective();
                _passiveObjectiveRefreshTimer.Restart();
            }
            if (!_passiveObjectiveRefreshTimer.IsRunning)
            {
                _passiveObjectiveRefreshTimer.Start();
            }
        }

        /// <summary>
        /// Check if there's a TailNpc objective active in the current zone, and start playback if needed.
        /// </summary>
        private void CheckForTailObjective()
        {
            if (_tailObjectiveActive) return; // Already running

            if (_activeQuestChainObjectives == null) return;

            foreach (var item in _activeQuestChainObjectives)
            {
                var objective = item.Item2;
                if (objective.TypeOfObjectiveTrigger == QuestObjective.ObjectiveTriggerType.TailNpc
                    && !objective.ObjectiveCompleted
                    && objective.TailData != null
                    && Plugin.RoleplayingQuestManager.IsTailObjectiveActivated(objective.Id)
                    && !objective.TailData.PathCompleted
                    && objective.TailData.Waypoints.Count >= 2
                    && !string.IsNullOrEmpty(objective.TailData.NpcName))
                {
                    StartTailObjective(objective, item.Item3);
                    break;
                }
            }
        }

        private void StartTailObjective(QuestObjective objective, RoleplayingQuest quest)
        {
            var tailData = objective.TailData;
            string npcName = tailData.NpcName;
            string npcKey = QuestNpcKey(quest.QuestId, npcName);

            // Find the NPC in the interactive dictionary
            if (_interactiveNpcDictionary.ContainsKey(npcKey))
            {
                var npc = _interactiveNpcDictionary[npcKey];
                _tailObjectiveActive = true;
                _tailObjectiveNpcName = npcKey; // Store the composite key for later lookups
                _tailObjectiveRef = objective;

                // Subscribe to detection and completion events
                npc.OnPlayerDetected += TailObjective_OnPlayerDetected;
                npc.OnTailPathCompleted += TailObjective_OnPathCompleted;

                // Start the playback
                npc.StartTailPlayback(tailData);

                Plugin.PluginLog.Information($"[TailObjective] Started tail playback for NPC '{npcName}' (key={npcKey}) with {tailData.Waypoints.Count} waypoints.");
            }
        }

        private void TailObjective_OnPlayerDetected(object sender, InteractiveNpc.TailFailureEventArgs e)
        {
            // Prevent spam — only trigger once per 3 seconds
            if (_tailDetectionCooldown.IsRunning && _tailDetectionCooldown.ElapsedMilliseconds < 3000) return;
            _tailDetectionCooldown.Restart();

            Plugin.PluginLog.Information($"[TailObjective] Tail failed! Reason: {e.Reason}");

            // Show fail toast
            try
            {
                string message = e.Reason switch
                {
                    InteractiveNpc.TailFailureReason.TooClose => "You got too close to the target! Objective failed.",
                    InteractiveNpc.TailFailureReason.TooFar => "You lost sight of the target! Objective failed.",
                    InteractiveNpc.TailFailureReason.Spotted => "You've been spotted! The NPC is returning to the start.",
                    _ => "Objective failed."
                };
                Plugin.ToastGui.ShowError(message);
            }
            catch { }

            // Reset the NPC to the start of the path
            if (_interactiveNpcDictionary.TryGetValue(_tailObjectiveNpcName, out var npc))
            {
                // Pause and show reaction
                npc.ShowTailFailure(e.Reason);

                Task.Run(async () =>
                {
                    // Delay reset if they were spotted or player got too close
                    if (e.Reason == InteractiveNpc.TailFailureReason.Spotted || e.Reason == InteractiveNpc.TailFailureReason.TooClose)
                    {
                        await Task.Delay(3000);
                    }

                    Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        npc.ResetTailToStart();
                        
                        // Clear the active state so the player has to click the bubble again
                        if (_tailObjectiveRef != null)
                        {
                            Plugin.RoleplayingQuestManager.DeactivateTailObjective(_tailObjectiveRef.Id);
                        }
                        CleanupTailObjective();
                    });
                });
            }
        }

        private void TailObjective_OnPathCompleted(object sender, EventArgs e)
        {
            Plugin.PluginLog.Information("[TailObjective] NPC reached destination! Player can now interact.");

            // Mark path as completed so the objective can now be satisfied by NormalInteraction
            if (_tailObjectiveRef != null)
            {
                _tailObjectiveRef.TailData.PathCompleted = true;

                // Move the objective marker to the NPC's final position so the player knows where to go
                var lastWaypoint = _tailObjectiveRef.TailData.Waypoints[_tailObjectiveRef.TailData.Waypoints.Count - 1];
                _tailObjectiveRef.Coordinates = lastWaypoint.Position;
            }

            // Show success toast
            try
            {
                string nameToDisplay = _tailObjectiveRef?.TailData?.NpcName ?? "The NPC";
                Plugin.ToastGui.ShowNormal($"{nameToDisplay} has arrived to their destination. Approach and speak to them.");
            }
            catch { }

            // Stop tail playback — NPC idles at destination
            if (_interactiveNpcDictionary.ContainsKey(_tailObjectiveNpcName))
            {
                var npc = _interactiveNpcDictionary[_tailObjectiveNpcName];
                npc.StopTailPlayback(true);
                npc.OnPlayerDetected -= TailObjective_OnPlayerDetected;
                npc.OnTailPathCompleted -= TailObjective_OnPathCompleted;
            }

            _tailObjectiveActive = false;

            // The objective now waits for the player to walk up and interact via NormalInteraction.
            // The TailNpc trigger type in the quest manager already handles this via SubObjectivesComplete.
        }

        private void CleanupTailObjective()
        {
            if (_tailObjectiveActive && _interactiveNpcDictionary.ContainsKey(_tailObjectiveNpcName))
            {
                var npc = _interactiveNpcDictionary[_tailObjectiveNpcName];
                npc.StopTailPlayback();
                npc.OnPlayerDetected -= TailObjective_OnPlayerDetected;
                npc.OnTailPathCompleted -= TailObjective_OnPathCompleted;
            }
            _tailObjectiveActive = false;
            _tailObjectiveNpcName = null;
            _tailObjectiveRef = null;
        }

        private void CheckForPlayerAppearance()
        {
            PlayerAppearanceData = AppearanceHelper.GetCustomization(Plugin.ObjectTable.LocalPlayer);
            PlayerClassJob = Plugin.ObjectTable.LocalPlayer.ClassJob.Value.Abbreviation.Data.ToString();
            Plugin.ObjectTable.LocalPlayer.ClassJob.Value.Abbreviation.Data.ToString();
            if (!_waitingForAppearanceLoad && (AppearanceAccessUtils.AppearanceManager == null || !AppearanceAccessUtils.AppearanceManager.IsWorking()) && !_hasCheckedForPlayerAppearance)
            {
                _hasCheckedForPlayerAppearance = true;
                var appearance = Plugin.RoleplayingQuestManager.GetPlayerAppearanceForZone((int)Plugin.ClientState.TerritoryType, _discriminator);
                if (appearance != null)
                {
                    Plugin.SetAutomationGlobalState(false);
                    LoadAppearance(appearance.AppearanceData, appearance.AppearanceSwapType, Plugin.ObjectTable.LocalPlayer);
                    Plugin.ToastGui.ShowNormal("A quest in this zone is affecting your characters appearance.");
                }
                else
                {
                    AppearanceAccessUtils.AppearanceManager.RemoveTemporaryCollection(Plugin.ObjectTable.LocalPlayer.Name.TextValue);
                    Plugin.SetAutomationGlobalState(true);
                }
            }
        }

        private void CheckZoneDiscriminator()
        {
            if (!_gotZoneDiscriminator)
            {
                try
                {
                    _discriminator = DiscriminatorGenerator.GetDiscriminator(Plugin.ObjectTable);
                    _gotZoneDiscriminator = true;
                }
                catch (Exception e)
                {
                    Plugin.PluginLog.Warning(e, e.Message);
                }
            }
        }

        private void CheckInitialization()
        {
            if (!_initializationStarted)
            {
                _initializationStarted = true;
                _playerAddress = Plugin.ObjectTable.LocalPlayer.Address;
                Task.Run(() =>
                {
                    try
                    {
                        while (!Plugin.ClientState.IsLoggedIn)
                        {
                            Thread.Sleep(500);
                        }
                        if (Plugin.ClientState.IsLoggedIn)
                        {
                            while (Brio.Brio._services == null)
                            {
                                Thread.Sleep(100);
                            }

                            var result = Brio.Brio.TryGetService<ActorSpawnService>(out _actorSpawnService);

                            InitializeMediaManager();
                            _clientState_TerritoryChanged(Plugin.ClientState.TerritoryType);
                            _isInitialized = true;

                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.PluginLog.Warning(ex, ex.Message);
                    }
                });
            }
        }

        private void CheckForNewPlayerCreationLoad()
        {
            if (_npcActorSpawnQueue != null)
            {
                if (_actorSpawnRefreshTimer.ElapsedMilliseconds > 200)
                {
                    if (_npcActorSpawnQueue.Count > 0)
                    {
                        if (!_waitingForAppearanceLoad && (AppearanceAccessUtils.AppearanceManager == null || !AppearanceAccessUtils.AppearanceManager.IsWorking()))
                        {
                            var value = _npcActorSpawnQueue.Dequeue();
                            bool newNPC = !value.Item5;
                            if (!string.IsNullOrEmpty(value.Item3) && !value.Item3.Contains("none"))
                            {
                                ICharacter character = null;
                                string npcKey = QuestNpcKey(value.Item6.QuestId, value.Item2);
                                if (newNPC)
                                {
                                    if (!_interactiveNpcDictionary.ContainsKey(npcKey))
                                    {
                                        if (_actorSpawnService.CreateCharacter(out character, SpawnFlags.DefinePosition, true,
                                    value.Item1.Position + new Vector3(0, -20, 0), CoordinateUtility.ConvertDegreesToRadians(value.Item1.EulerRotation.Y))
                                        && character != null)
                                        {
                                            value.Item4[value.Item2] = character;
                                            var npc = new InteractiveNpc(Plugin, character);
                                            _interactiveNpcDictionary.Add(npcKey, npc);
                                        }
                                    }
                                }
                                else
                                {
                                    character = value.Item4[value.Item2];
                                }
                                if (_interactiveNpcDictionary.ContainsKey(npcKey))
                                {
                                    _interactiveNpcDictionary[npcKey].SetDefaults(value.Item1.Position, value.Item1.EulerRotation);
                                    _interactiveNpcDictionary[npcKey].SetScale(value.Item1.TransformScale, 2);
                                    if (character != null)
                                    {
                                        if (_interactiveNpcDictionary[npcKey].LastAppearance != value.Item3
                                        || Plugin.RoleplayingQuestManager.QuestProgression[value.Item6.QuestId] == 0)
                                        {
                                            LoadAppearance(value.Item3, AppearanceSwapType.EntireAppearance, character);
                                            _interactiveNpcDictionary[npcKey].LastAppearance = value.Item3;
                                        }
                                        Plugin.AnamcoreManager.SetVoice(character, 0);
                                        Plugin.AnamcoreManager.TriggerEmote(character.Address, (ushort)value.Item1.DefaultAnimationId);
                                    }
                                    if (value.Item7)
                                    {
                                        _interactiveNpcDictionary[npcKey].FollowPlayer(2, true);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        public void LoadAppearance(string appearanceData, AppearanceSwapType appearanceSwapType, ICharacter character)
        {
            _waitingForAppearanceLoad = true;
            Task.Run(() =>
            {
                lock (_npcActorSpawnQueue)
                {
                    Thread.Sleep(200);
                    while (_appearanceApplicationQueue.Count > 0)
                    {
                        Thread.Sleep(200);
                    }
                    _appearanceApplicationQueue.Enqueue(new Tuple<string, AppearanceSwapType, ICharacter>(appearanceData, appearanceSwapType, character));
                }
            });
        }
        private void CheckForMapRefresh()
        {
            // Refresh faster when custom NPCs are active so their minimap positions stay current
            int refreshMs = _customNpcCharacters.Count > 0 ? 2000 : 10000;
            if (_mapRefreshTimer.ElapsedMilliseconds > refreshMs)
            {
                RefreshMapMarkers();
                _mapRefreshTimer.Restart();
            }
        }

        private void CheckVolumeLevels()
        {
            uint voiceVolume = 0;
            uint masterVolume = 0;
            uint soundEffectVolume = 0;
            uint soundMicPos = 0;
            try
            {
                if (Plugin.GameConfig.TryGet(SystemConfigOption.SoundVoice, out voiceVolume))
                {
                    if (Plugin.GameConfig.TryGet(SystemConfigOption.SoundMaster, out masterVolume))
                    {
                        if (Plugin.GameConfig.TryGet(SystemConfigOption.SoundMicpos, out soundMicPos))
                            Plugin.MediaManager.NpcVolume = ((float)voiceVolume / 100f) * ((float)masterVolume / 100f) * 1.15f;
                        Plugin.MediaManager.CameraAndPlayerPositionSlider = (float)soundMicPos / 100f;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.PluginLog?.Warning(e, e.Message);
            }
        }
        private async void CheckForNewAppearanceLoad()
        {
            if (_appearanceApplicationQueue.Count > 0)
            {
                if (_waitingForAppearanceLoad && _mcdfRefreshTimer.ElapsedMilliseconds > 500 && (AppearanceAccessUtils.AppearanceManager == null || !AppearanceAccessUtils.AppearanceManager.IsWorking()))
                {
                    var item = _appearanceApplicationQueue.Dequeue();
                    if (item.Item3 != null)
                    {
                        var appearanceDataItems = item.Item1.StringToArray();
                        bool charaAlreadyLoaded = false;
                        bool mcdfAlreadyLoaded = false;
                        EventHandler charaLoad = null;
                        EventHandler mcdfLoad = null;
                        foreach (var appearanceDataItem in appearanceDataItems)
                        {
                            if (appearanceDataItem.EndsWith(".chara") && !charaAlreadyLoaded)
                            {
                                BrioAccessUtils.EntityManager.SetSelectedEntity(item.Item3);
                                BrioAccessUtils.EntityManager.TryGetCapabilityFromSelectedEntity<ActorAppearanceCapability>(out var appearance);
                                charaLoad = delegate
                                {
                                    appearance.ImportAppearance(appearanceDataItem, Brio.Game.Actor.Appearance.AppearanceImportOptions.All);
                                };
                                charaAlreadyLoaded = true;
                            }
                            else if (!mcdfAlreadyLoaded)
                            {
                                AppearanceSwapType appearanceSwapType = AppearanceSwapType.EntireAppearance;
                                if (charaAlreadyLoaded)
                                {
                                    appearanceSwapType = AppearanceSwapType.OnlyModData;
                                }
                                mcdfLoad = delegate
                                {
                                    AppearanceAccessUtils.AppearanceManager?.LoadAppearance(appearanceDataItem, item.Item3, (int)appearanceSwapType);
                                };
                                mcdfAlreadyLoaded = true;
                            }
                        }
                        try
                        {
                            charaLoad?.Invoke(this, EventArgs.Empty);
                        }
                        catch
                        {

                        }
                        Task.Run(() =>
                        {
                            if (charaAlreadyLoaded)
                            {
                                Thread.Sleep(100);
                            }
                            mcdfLoad?.Invoke(this, EventArgs.Empty);
                        });
                        _waitingForAppearanceLoad = false;
                    }
                    _mcdfRefreshTimer.Restart();
                }
                else
                {
                    if (!_mcdfRefreshTimer.IsRunning)
                    {
                        _mcdfRefreshTimer.Start();
                    }
                }
            }
        }


        private void CheckForNPCRefresh()
        {
            if (_triggerRefresh && (AppearanceAccessUtils.AppearanceManager == null || !AppearanceAccessUtils.AppearanceManager.IsWorking()))
            {

                RefreshMapMarkers();
                RefreshNpcs((ushort)Plugin.ClientState.TerritoryType);
                _triggerRefresh = false;
            }
        }

        private unsafe void QuestInputCheck()
        {
            if (_pollingTimer.ElapsedMilliseconds > 20)
            {
                Plugin.ObjectiveWindow.IsOpen = true;
                if (((Plugin.GamepadState.Raw(GamepadButtons.South) == 1) || _screenButtonClicked))
                {
                    _screenButtonClicked = false;
                    if (!_waitingForSelectionRelease)
                    {
                        if (Plugin.QuestAcceptanceWindow.TimeSinceLastQuestAccepted.ElapsedMilliseconds > 300
                            && Plugin.ChoiceWindow.TimeSinceLastChoiceMade.ElapsedMilliseconds > 300)
                        {
                            _inputCooldown.Restart();
                            if (!Plugin.EventWindow.IsOpen && !Plugin.ChoiceWindow.IsOpen)
                            {
                                Plugin.RoleplayingQuestManager.AttemptProgressingQuestObjective();
                            }
                            else
                            {
                                Plugin.EventWindow.NextEvent();
                            }
                        }
                        _waitingForSelectionRelease = true;
                    }
                }
                else
                {
                    _waitingForSelectionRelease = false;
                }
                _pollingTimer.Restart();
            }
        }

        public void DestroyAllNpcsInQuestId(string questId)
        {
            try
            {
                if (_spawnedNpcsDictionary.ContainsKey(questId))
                {
                    int sleepTime = 100;
                    foreach (var item in _spawnedNpcsDictionary[questId])
                    {
                        if (item.Value != null)
                        {
                            try
                            {
                                _actorSpawnService.DestroyObject(item.Value);
                            }
                            catch
                            {

                            }
                        }
                        // Clean up the interactive NPC entry for this quest+name combo
                        _interactiveNpcDictionary.Remove(QuestNpcKey(questId, item.Key));
                    }
                    _spawnedNpcsDictionary[questId].Clear();
                }
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Warning(ex, ex.Message);
            }
        }
        public void RefreshNpcs(ushort territoryId, string questId = "", bool softRefresh = false)
        {

            if (!_refreshingNPCQuests && _npcActorSpawnQueue.Count == 0)
            {
                try
                {
                    _refreshingNPCQuests = true;
                    if (_checkForPartyMembers)
                    {
                        RefreshPartyMembers(territoryId, _discriminator);
                    }
                    if (_actorSpawnService != null)
                    {
                        var questChains = Plugin.RoleplayingQuestManager.GetActiveQuestChainObjectivesInZone(territoryId, _discriminator);
                        var playerPos = Plugin.ObjectTable?.LocalPlayer?.Position ?? Vector3.Zero;

                        // Collect all NPC spawn candidates, keyed by (questId, npcName).
                        // When multiple objectives want the same NPC, the closest objective to the player wins.
                        var npcCandidates = new Dictionary<string, (float Distance, Tuple<Transform, string, string, Dictionary<string, ICharacter>, bool, RoleplayingQuest, bool> SpawnData)>();

                        foreach (var item in questChains)
                        {

                            if (item.Item3.QuestId == questId || string.IsNullOrEmpty(questId))
                            {
                                string foundPath = item.Item3.FoundPath;
                                foreach (var npcAppearance in item.Item3.NpcCustomizations)
                                {

                                    // For TailNpc objectives, auto-inject the NPC starting position from the first waypoint
                                    if (item.Item2.TypeOfObjectiveTrigger == QuestObjective.ObjectiveTriggerType.TailNpc
                                        && item.Item2.TailData != null
                                        && item.Item2.TailData.Waypoints.Count >= 2
                                        && !string.IsNullOrEmpty(item.Item2.TailData.NpcName)
                                        && npcAppearance.Value.NpcName == item.Item2.TailData.NpcName
                                        && !item.Item2.NpcStartingPositions.ContainsKey(npcAppearance.Value.NpcName))
                                    {
                                        // If the objective was already completed, spawn the NPC at the end of the path.
                                        // Otherwise, spawn them at the start of the path.
                                        var targetWp = item.Item2.ObjectiveCompleted 
                                            ? item.Item2.TailData.Waypoints[item.Item2.TailData.Waypoints.Count - 1] 
                                            : item.Item2.TailData.Waypoints[0];
                                            
                                        item.Item2.NpcStartingPositions[npcAppearance.Value.NpcName] = new Transform
                                        {
                                            Position = targetWp.Position,
                                            EulerRotation = targetWp.Rotation
                                        };
                                        Plugin.PluginLog.Information($"[TailNpc] Auto-injected position for '{npcAppearance.Value.NpcName}' at {targetWp.Position} (Completed: {item.Item2.ObjectiveCompleted})");
                                    }
                                    else if (item.Item2.TypeOfObjectiveTrigger == QuestObjective.ObjectiveTriggerType.TailNpc)
                                    {
                                        Plugin.PluginLog.Information($"[TailNpc] Auto-inject SKIPPED for '{npcAppearance.Value.NpcName}': " +
                                            $"TailData={item.Item2.TailData != null}, " +
                                            $"WpCount={item.Item2.TailData?.Waypoints?.Count ?? 0}, " +
                                            $"TailNpcName='{item.Item2.TailData?.NpcName}', " +
                                            $"NpcAppName='{npcAppearance.Value.NpcName}', " +
                                            $"AlreadyHasPosition={item.Item2.NpcStartingPositions.ContainsKey(npcAppearance.Value.NpcName)}");
                                    }

                                    if (item.Item2.NpcStartingPositions.ContainsKey(npcAppearance.Value.NpcName))
                                    {
                                        if (!_spawnedNpcsDictionary.ContainsKey(item.Item3.QuestId))
                                        {
                                            _spawnedNpcsDictionary[item.Item3.QuestId] = new Dictionary<string, ICharacter>();
                                        }
                                        var spawnedNpcsList = _spawnedNpcsDictionary[item.Item3.QuestId];
                                        var startingInfo = item.Item2.NpcStartingPositions[npcAppearance.Value.NpcName];
                                        bool foundExistingNPC = false;
                                        if (spawnedNpcsList.ContainsKey(npcAppearance.Value.NpcName))
                                        {
                                            var npc = spawnedNpcsList[npcAppearance.Value.NpcName];
                                            if (npc != null)
                                            {
                                                try
                                                {
                                                    foundExistingNPC = true;
                                                }
                                                catch (Exception e)
                                                {
                                                    Plugin.PluginLog.Warning(e, e.Message);
                                                }
                                            }
                                        }
                                        string[] appearanceItems = npcAppearance.Value.AppearanceData.StringToArray();
                                        for (int i = 0; i < appearanceItems.Length; i++)
                                        {
                                            if (appearanceItems[i].Contains(".chara") || appearanceItems[i].Contains(".mcdf"))
                                            {
                                                appearanceItems[i] = Path.Combine(foundPath, appearanceItems[i].Trim());
                                            }
                                        }
                                        string customNpcAppearancePath = appearanceItems.ArrayToString();
                                        var spawnValue = new Tuple<Transform, string, string, Dictionary<string, ICharacter>, bool, RoleplayingQuest, bool>
                                        (startingInfo, npcAppearance.Value.NpcName, Path.Combine(foundPath, customNpcAppearancePath), spawnedNpcsList, foundExistingNPC, item.Item3, false);

                                        // Use composite key so same NPC from different quests are independent
                                        string candidateKey = QuestNpcKey(item.Item3.QuestId, npcAppearance.Value.NpcName);
                                        float distToObjective = Vector3.Distance(playerPos, item.Item2.Coordinates);

                                        // Keep only the closest objective's position for each NPC
                                        if (!npcCandidates.ContainsKey(candidateKey) || distToObjective < npcCandidates[candidateKey].Distance)
                                        {
                                            npcCandidates[candidateKey] = (distToObjective, spawnValue);
                                        }
                                    }
                                }
                            }
                        }

                        // Enqueue only the winning candidate per NPC
                        foreach (var candidate in npcCandidates.Values)
                        {
                            _npcActorSpawnQueue.Enqueue(candidate.SpawnData);
                        }
                    }
                    else
                    {

                    }
                }
                catch (Exception e)
                {
                    Plugin.PluginLog.Warning(e, e.Message);
                }
                _refreshingNPCQuests = false;
            }
            else
            {

            }
        }
        private void RefreshPartyMembers(ushort territoryType, string discriminator)
        {
            var members = Plugin.RoleplayingQuestManager.GetPartyMembersForZone(territoryType, discriminator);
            foreach (var member in members)
            {
                if (Plugin.RoleplayingQuestManager.QuestChains.ContainsKey(member.QuestId))
                {
                    var transform = new Transform() { Name = member.NpcName, Position = Plugin.ObjectTable.LocalPlayer.Position, TransformScale = new Vector3(1, 1, 1) };
                    if (!_spawnedNpcsDictionary.ContainsKey(member.QuestId))
                    {
                        _spawnedNpcsDictionary[member.QuestId] = new Dictionary<string, ICharacter>();
                    }
                    var spawnedNpcList = _spawnedNpcsDictionary[member.QuestId];
                    var foundExistingNpc = _spawnedNpcsDictionary.ContainsKey(member.NpcName);
                    var customization = Plugin.RoleplayingQuestManager.GetNpcInformation(member.QuestId, member.NpcName);
                    var quest = Plugin.RoleplayingQuestManager.QuestChains[member.QuestId];

                    string[] appearanceItems = customization.AppearanceData.StringToArray();
                    for (int i = 0; i < appearanceItems.Length; i++)
                    {
                        if (appearanceItems[i].Contains(".chara") || appearanceItems[i].Contains(".mcdf"))
                        {
                            appearanceItems[i] = Path.Combine(quest.FoundPath, appearanceItems[i].Trim());
                        }
                    }
                    string customNpcAppearancePath = appearanceItems.ArrayToString();
                    var value = new Tuple<Transform, string, string, Dictionary<string, ICharacter>, bool, RoleplayingQuest, bool>(
                    transform, member.NpcName, customNpcAppearancePath, spawnedNpcList, foundExistingNpc, quest, true);
                    _npcActorSpawnQueue.Enqueue(value);
                }
            }
            _checkForPartyMembers = false;
        }
        public void UpdateNPCAppearance(ushort territoryId, string questId, string npcName, string appearancePath)
        {
            try
            {
                LoadAppearance(appearancePath, AppearanceSwapType.EntireAppearance, _spawnedNpcsDictionary[questId][npcName]);
            }
            catch
            {
                try
                {
                    RefreshNpcs(territoryId, questId, true);
                }
                catch (Exception e)
                {
                    Plugin.PluginLog.Warning(e, e.Message);
                }
            }
        }
        private void _roleplayingQuestManager_OnQuestAcceptancePopup(object? sender, RoleplayingQuest e)
        {
            Plugin.QuestAcceptanceWindow.PromptQuest(e);
        }

        private void QuestAcceptanceWindow_OnQuestAccepted(object? sender, EventArgs e)
        {
            Plugin.RoleplayingQuestManager.AttemptProgressingQuestObjective();
        }

        private void _roleplayingQuestManager_OnObjectiveCompleted(object? sender, Tuple<QuestObjective, RoleplayingQuest> e)
        {
            Task.Run(async () =>
            {
                var toast = await Translator.LocalizeText(e.Item1.Objective, Plugin.Configuration.QuestLanguage, e.Item2.QuestLanguage);
                Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    Plugin.ToastGui.ShowQuest(toast,
    new Dalamud.Game.Gui.Toast.QuestToastOptions()
    {
        DisplayCheckmark = e.Item1.ObjectiveStatus == QuestObjective.ObjectiveStatusType.Complete,
        PlaySound = e.Item1.ObjectiveStatus == QuestObjective.ObjectiveStatusType.Complete
    });
                });
            });
        }

        private void _roleplayingQuestManager_OnQuestCompleted(object? sender, RoleplayingQuest e)
        {
            Plugin.RewardWindow.PromptReward(e);
        }

        private void _roleplayingQuestManager_OnQuestStarted(object? sender, RoleplayingQuest e)
        {
            string foundPath = string.IsNullOrWhiteSpace(e.FoundPath) ? Path.Combine(Plugin.Configuration.QuestInstallFolder, e.QuestName) : e.FoundPath;
            string path = Path.Combine(foundPath, e.QuestStartTitleCard);
            string soundPath = Path.Combine(foundPath, e.QuestStartTitleSound);
            Plugin.TitleCardWindow.DisplayCard(path, soundPath);
        }

        private void DialogueBackgroundWindow_buttonClicked(object? sender, EventArgs e)
        {
            _screenButtonClicked = true;
        }

        private void _roleplayingQuestManager_OnQuestTextTriggered(object? sender, QuestDisplayObject e)
        {
            if (e.QuestObjective.QuestText.Count > 0)
            {
                Plugin.EventWindow.IsOpen = true;
                Plugin.EventWindow.NewText(e);
            }
            else
            {
                e.QuestEvents.Invoke(this, EventArgs.Empty);
            }
        }
        public void Dispose()
        {
            _disposed = true;
            PenumbraAndGlamourerIpcWrapper.Instance.SetCollectionForObject.Invoke((int)201, Guid.Empty);
            AppearanceAccessUtils.AppearanceManager.RemoveAllTemporaryCollections();
            CutsceneCamera.Dispose();
            Plugin.DialogueBackgroundWindow.ButtonClicked -= DialogueBackgroundWindow_buttonClicked;
            Plugin.ObjectiveWindow.OnSelectionAttempt -= DialogueBackgroundWindow_buttonClicked;
            Plugin.QuestAcceptanceWindow.OnQuestAccepted -= QuestAcceptanceWindow_OnQuestAccepted;
            Plugin.RoleplayingQuestManager.OnQuestTextTriggered -= _roleplayingQuestManager_OnQuestTextTriggered;
            Plugin.RoleplayingQuestManager.OnQuestStarted -= _roleplayingQuestManager_OnQuestStarted;
            Plugin.RoleplayingQuestManager.OnQuestCompleted -= _roleplayingQuestManager_OnQuestCompleted;
            Plugin.RoleplayingQuestManager.OnObjectiveCompleted -= _roleplayingQuestManager_OnObjectiveCompleted;
            Plugin.RoleplayingQuestManager.OnQuestAcceptancePopup -= _roleplayingQuestManager_OnQuestAcceptancePopup;
            Plugin.RewardWindow.OnRewardClosed -= RewardWindow_OnRewardClosed;
            Plugin.Framework.Update -= _framework_Update;
            Plugin.ClientState.Login -= _clientState_Login;
            Plugin.ClientState.TerritoryChanged -= _clientState_TerritoryChanged;
            Plugin.ChatGui.ChatMessage -= ChatGui_ChatMessage;
            Plugin.EmoteReaderHook.OnEmote -= (instigator, emoteId) => OnEmote(instigator as ICharacter, emoteId);
            Plugin.ClientState.Logout -= ClientState_Logout;
            // Save custom NPC state before shutdown
            try
            {
                foreach (var kvp in _customNpcDictionary)
                {
                    var npcConfig = Plugin.Configuration.CustomNpcCharacters?.Find(n => n.NpcName == kvp.Key);
                    if (npcConfig != null)
                    {
                        var npc = kvp.Value;
                        if (npc != null)
                        {
                            // If NPC was staying, update position
                            if (npcConfig.IsStaying)
                            {
                                npcConfig.StayPositionX = npc.CurrentPosition.X;
                                npcConfig.StayPositionY = npc.CurrentPosition.Y;
                                npcConfig.StayPositionZ = npc.CurrentPosition.Z;
                            }
                        }
                    }
                }
                Plugin.Configuration.Save();
            }
            catch { }

            // MUST DESTROY ACTORS TO PREVENT CRASHES ON GAME CLOSE/RELOAD!
            try
            {
                if (_actorSpawnService != null)
                {
                    foreach (var kvp in _customNpcCharacters)
                    {
                        try
                        {
                            if (kvp.Value != null)
                            {
                                PenumbraAndGlamourerIpcWrapper.Instance.SetCollectionForObject.Invoke((int)kvp.Value.ObjectIndex, Guid.Empty, true, true);
                                PenumbraAndGlamourerIpcWrapper.Instance.RevertState.Invoke(kvp.Value.ObjectIndex);
                                _actorSpawnService.DestroyObject(kvp.Value);
                            }
                        }
                        catch { }
                    }
                    _customNpcCharacters.Clear();

                    // Also destroy hidden pool actors
                    foreach (var kvp in _hiddenNpcPool)
                    {
                        try
                        {
                            if (kvp.Value.Character != null)
                            {
                                _actorSpawnService.DestroyObject(kvp.Value.Character);
                            }
                            kvp.Value.Npc.Dispose();
                        }
                        catch { }
                    }
                    _hiddenNpcPool.Clear();

                    foreach (var questDict in _spawnedNpcsDictionary.Values)
                    {
                        foreach (var kvp in questDict)
                        {
                            try
                            {
                                if (kvp.Value != null)
                                {
                                    _actorSpawnService.DestroyObject(kvp.Value);
                                }
                            }
                            catch { }
                        }
                    }
                    _spawnedNpcsDictionary.Clear();
                }
            }
            catch { }

            CleanupCache();
        }

        public void StartObjectiveTimer(int timer, string questId)
        {
            if (_objectiveTimers.ContainsKey(questId))
            {
                _objectiveTimers[questId].Item2.Reset();
            }
            _objectiveTimers[questId] = new Tuple<int, Stopwatch>(timer, Stopwatch.StartNew());
        }

        public bool FailedTimeLimit(string questId)
        {
            if (_objectiveTimers.ContainsKey(questId))
            {
                return _objectiveTimers[questId].Item2.ElapsedMilliseconds > _objectiveTimers[questId].Item1;
            }
            else
            {
                return false;
            }
        }
        public void RemoveTimer(string questId)
        {
            if (_objectiveTimers.ContainsKey(questId))
            {
                _objectiveTimers[questId].Item2.Stop();
                _objectiveTimers.Remove(questId);
            }
        }

        #region Custom NPC Management
        private const int MAX_CUSTOM_NPCS = 3;

        /// <summary>
        /// Records encounters for a newly spawned/returned NPC:
        /// - The NPC meets the player
        /// - The NPC meets all other currently summoned NPCs (bidirectional)
        /// </summary>
        public void RecordNpcEncounters(CustomNpcCharacter npcData)
        {
            try
            {
                // Record NPC meeting the player
                string playerName = Plugin.ObjectTable.LocalPlayer?.Name?.TextValue ?? "Adventurer";
                npcData.RecordEncounter(playerName);

                // They're no longer left behind — the player has returned
                npcData.WasLeftBehind = false;

                // Start the "chatty first minute" phase for this NPC
                Plugin.SpeechBubbleManager?.NotifyNpcSummoned(npcData.NpcName);

                // Record this zone as a visited location
                try
                {
                    var territory = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()?.GetRow(Plugin.ClientState.TerritoryType);
                    string placeName = territory?.PlaceName.Value.Name.ToString();
                    if (!string.IsNullOrWhiteSpace(placeName))
                        npcData.RecordVisit(placeName);
                }
                catch { }

                // Ensure birth/home location is in the visited list
                if (!string.IsNullOrWhiteSpace(npcData.NpcBirthLocation))
                    npcData.RecordVisit(npcData.NpcBirthLocation);

                // Record NPC meeting all other co-summoned NPCs (bidirectional)
                foreach (var otherNpc in Plugin.Configuration.CustomNpcCharacters)
                {
                    if (otherNpc.NpcName != npcData.NpcName && _customNpcCharacters.ContainsKey(otherNpc.NpcName))
                    {
                        npcData.RecordEncounter(otherNpc.NpcName);
                        otherNpc.RecordEncounter(npcData.NpcName);
                    }
                }

                Plugin.Configuration.Save();
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Warning(ex, "Failed to record NPC encounters");
            }
        }

        public void SummonCustomNpc(CustomNpcCharacter npcData)
        {
            if (_actorSpawnService == null || !Plugin.ClientState.IsLoggedIn) return;
            // Don't spawn custom NPCs during duties with other real players
            unsafe
            {
                if (Conditions.Instance()->BoundByDuty)
                {
                    var groupManager = GroupManager.Instance();
                    if (groupManager != null && groupManager->MainGroup.MemberCount > 1) return;
                }
            }
            if (_customNpcDictionary.ContainsKey(npcData.NpcName))
            {
                // Already summoned, dismiss instead
                DismissCustomNpc(npcData.NpcName);
                return;
            }

            // Check hidden pool first — instant re-summon without Brio create/destroy
            if (_hiddenNpcPool.ContainsKey(npcData.NpcName))
            {
                Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    try
                    {
                        var pooled = _hiddenNpcPool[npcData.NpcName];
                        _hiddenNpcPool.Remove(npcData.NpcName);

                        if (pooled.Character != null && pooled.Character.IsValid())
                        {
                            var playerPos = Plugin.ObjectTable.LocalPlayer.Position;
                            float spawnX = playerPos.X + 2;
                            float spawnZ = playerPos.Z + 2;
                            float spawnY = GroundMap.GetGroundY(spawnX, spawnZ, playerPos.Y);
                            var spawnPos = new Vector3(spawnX, spawnY, spawnZ);

                            pooled.Npc.TeleportTo(spawnPos);
                            pooled.Npc.ShowNPC();

                            _customNpcCharacters[npcData.NpcName] = pooled.Character;
                            _customNpcDictionary[npcData.NpcName] = pooled.Npc;
                            _interactiveNpcDictionary[npcData.NpcName] = pooled.Npc;

                            pooled.Npc.TargetClassJobId = npcData.NpcClassJobId;
                            pooled.Npc.TargetWeaponItemId = npcData.NpcEquippedWeaponItemId;
                            pooled.Npc.ClassWeaponApplied = false;

                            // Restore follow/stay state
                            if (npcData.IsFollowingPlayer)
                            {
                                pooled.Npc.FollowPlayer(2);
                            }
                            else if (npcData.IsStaying && npcData.StayTerritoryId == Plugin.ClientState.TerritoryType)
                            {
                                var stayPos = new Vector3(npcData.StayPositionX, npcData.StayPositionY, npcData.StayPositionZ);
                                var stayRot = new Vector3(npcData.StayRotationX, npcData.StayRotationY, npcData.StayRotationZ);
                                pooled.Npc.SetDefaults(stayPos, stayRot);
                                pooled.Npc.SetDefaultRotation(stayRot);
                            }
                            pooled.Npc.IdleEmoteId = npcData.IdleEmoteId;
                            if (npcData.RandomIdleEmotes != null) pooled.Npc.RandomIdleEmotes = npcData.RandomIdleEmotes.ToList();
                            pooled.Npc.VictoryPoseEmoteId = npcData.VictoryPoseEmoteId;

                            RecordNpcEncounters(npcData);

                            // Re-create conversation manager
                            string baseDir = Plugin.Configuration.QuestInstallFolder ?? Path.GetTempPath();
                            string npcMemoryDir = Path.Combine(baseDir, "CustomNpcMemories");
                            Directory.CreateDirectory(npcMemoryDir);
                            var conversationManager = new NPCConversationManager(
                                npcData.NpcName, npcMemoryDir, Plugin, pooled.Character);
                            _customNpcConversationManagers[npcData.NpcName] = conversationManager;

                            Plugin.ChatGui.Print("[A Quest Reborn] " + npcData.NpcName + " has been summoned!");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.PluginLog.Warning(ex, "Failed to re-summon pooled NPC, falling back to fresh spawn.");
                    }

                    // Pooled actor was invalid — fall through to fresh create
                    FreshSpawnCustomNpc(npcData);
                });
                return;
            }

            if (_customNpcDictionary.Count >= MAX_CUSTOM_NPCS)
            {
                Plugin.ToastGui.ShowError("Custom NPC limit reached! Maximum of " + MAX_CUSTOM_NPCS + " NPCs allowed.");
                return;
            }

            FreshSpawnCustomNpc(npcData);
        }

        private void FreshSpawnCustomNpc(CustomNpcCharacter npcData)
        {
            Task.Run(() =>
            {
                try
                {
                    Thread.Sleep(500);
                    Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        try
                        {
                            var playerPos = Plugin.ObjectTable.LocalPlayer.Position;
                            float spawnX = playerPos.X + 2;
                            float spawnZ = playerPos.Z + 2;
                            float spawnY = GroundMap.GetGroundY(spawnX, spawnZ, playerPos.Y);
                            var spawnPos = new Vector3(spawnX, spawnY, spawnZ);
                            ICharacter character = null;
                            if (_actorSpawnService.CreateCharacter(out character, SpawnFlags.DefinePosition, true,
                                spawnPos, 0, customName: npcData.UseMcdfAppearance ? null : npcData.NpcName.Split(' ')[0] + " Cnpc") && character != null)
                            {
                                _customNpcCharacters[npcData.NpcName] = character;
                                var npc = new InteractiveNpc(Plugin, character);
                                _customNpcDictionary[npcData.NpcName] = npc;
                                _interactiveNpcDictionary[npcData.NpcName] = npc;

                                // Apply appearance
                                if (!npcData.UseMcdfAppearance)
                                {
                                    AppearanceAccessUtils.AppearanceManager?.RemoveTemporaryCollection(character.Name.TextValue);
                                }

                                // Apply Penumbra Collection separately
                                if (!npcData.UseMcdfAppearance && npcData.UsePenumbraCollection && !string.IsNullOrEmpty(npcData.PenumbraCollection))
                                {
                                    if (Brio.Brio.TryGetService<Brio.IPC.PenumbraService>(out var penumbraService))
                                    {
                                        var collections = penumbraService.GetCollections();
                                        var collectionGuid = collections.FirstOrDefault(x => x.Value == npcData.PenumbraCollection).Key;
                                        if (collectionGuid != Guid.Empty)
                                        {
                                            PenumbraAndGlamourerIpcWrapper.Instance.SetCollectionForObject.Invoke(character.ObjectIndex, collectionGuid, true, true);
                                            PenumbraAndGlamourerIpcWrapper.Instance.RedrawObject.Invoke(character.ObjectIndex, Penumbra.Api.Enums.RedrawType.Redraw);
                                        }
                                        else
                                        {
                                            Plugin.PluginLog.Warning($"Could not find Penumbra collection: {npcData.PenumbraCollection}");
                                        }
                                    }
                                }

                                if (npcData.UseMonsterModel)
                                {
                                    unsafe
                                    {
                                        var native = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)character.Address;
                                        native->ModelContainer.ModelCharaId = (int)npcData.MonsterModelId;
                                    }
                                    PenumbraAndGlamourerIpcWrapper.Instance.RedrawObject.Invoke(character.ObjectIndex, Penumbra.Api.Enums.RedrawType.Redraw);
                                }
                                else if (npcData.UseMcdfAppearance && !string.IsNullOrEmpty(npcData.McdfFilePath))
                                {
                                    try
                                    {
                                        AppearanceAccessUtils.AppearanceManager?.LoadAppearance(
                                            npcData.McdfFilePath, character, (int)AppearanceSwapType.EntireAppearance);
                                    }
                                    catch (Exception ex)
                                    {
                                        Plugin.PluginLog.Warning(ex, "Failed to load MCDF appearance: " + npcData.McdfFilePath);
                                    }
                                }
                                else if (!string.IsNullOrEmpty(npcData.NpcGlamourerAppearanceString))
                                {
                                    if (Guid.TryParse(npcData.NpcGlamourerAppearanceString, out var designGuid))
                                    {
                                        PenumbraAndGlamourerIpcWrapper.Instance.ApplyDesign.Invoke(designGuid, character.ObjectIndex);
                                    }
                                }

                                Plugin.AnamcoreManager.SetVoice(character, 0);

                                npc.TargetClassJobId = npcData.NpcClassJobId;
                                npc.TargetWeaponItemId = npcData.NpcEquippedWeaponItemId;
                                npc.ClassWeaponApplied = false;

                                RecordNpcEncounters(npcData);

                                if (npcData.IsFollowingPlayer)
                                {
                                    npc.FollowPlayer(2);
                                }
                                else if (npcData.IsStaying && npcData.StayTerritoryId == Plugin.ClientState.TerritoryType)
                                {
                                    var stayPos = new Vector3(npcData.StayPositionX, npcData.StayPositionY, npcData.StayPositionZ);
                                    var stayRot = new Vector3(npcData.StayRotationX, npcData.StayRotationY, npcData.StayRotationZ);
                                    npc.SetDefaults(stayPos, stayRot);
                                    npc.SetDefaultRotation(stayRot);
                                }
                                npc.IdleEmoteId = npcData.IdleEmoteId;
                                if (npcData.RandomIdleEmotes != null) npc.RandomIdleEmotes = npcData.RandomIdleEmotes.ToList();
                                npc.VictoryPoseEmoteId = npcData.VictoryPoseEmoteId;

                                if (!npcData.IsFollowingPlayer)
                                {
                                    ushort initialEmoteId = npcData.IdleEmoteId;
                                    if (npcData.RandomIdleEmotes != null && npcData.RandomIdleEmotes.Count > 0)
                                    {
                                        initialEmoteId = npcData.RandomIdleEmotes[new System.Random().Next(npcData.RandomIdleEmotes.Count)];
                                    }
                                    if (initialEmoteId > 0)
                                    {
                                        try
                                        {
                                            var emote = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>().GetRow(initialEmoteId);
                                            Plugin.AnamcoreManager.TriggerEmote(character.Address, (ushort)emote.ActionTimeline[0].Value.RowId);
                                        }
                                        catch { }
                                    }
                                }

                                string baseDir = Plugin.Configuration.QuestInstallFolder ?? Path.GetTempPath();
                                string npcMemoryDir = Path.Combine(baseDir, "CustomNpcMemories");
                                Directory.CreateDirectory(npcMemoryDir);
                                var conversationManager = new NPCConversationManager(
                                    npcData.NpcName, npcMemoryDir, Plugin, character);
                                _customNpcConversationManagers[npcData.NpcName] = conversationManager;

                                Plugin.ChatGui.Print("[A Quest Reborn] " + npcData.NpcName + " has been summoned!");
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.PluginLog.Warning(ex, "Failed to summon custom NPC: " + ex.Message);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Warning(ex, "Failed to summon custom NPC: " + ex.Message);
                }
            });
        }

        public void SummonCustomNpcAtPosition(CustomNpcCharacter npcData, System.Numerics.Vector3 position, System.Numerics.Vector3 rotation)
        {
            if (_actorSpawnService == null || Plugin.ObjectTable.LocalPlayer == null) return;
            // Don't spawn custom NPCs during duties with other real players
            unsafe
            {
                if (Conditions.Instance()->BoundByDuty)
                {
                    var groupManager = GroupManager.Instance();
                    if (groupManager != null && groupManager->MainGroup.MemberCount > 1) return;
                }
            }
            if (_customNpcDictionary.ContainsKey(npcData.NpcName)) return;
            if (_customNpcDictionary.Count >= MAX_CUSTOM_NPCS) return;

            Task.Run(() =>
            {
                try
                {
                    Thread.Sleep(500);
                    Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        try
                        {
                            ICharacter character = null;
                            if (_actorSpawnService.CreateCharacter(out character, SpawnFlags.DefinePosition, true,
                                position, 0, customName: npcData.UseMcdfAppearance ? null : npcData.NpcName.Split(' ')[0] + " Cnpc") && character != null)
                            {
                                _customNpcCharacters[npcData.NpcName] = character;
                                var npc = new InteractiveNpc(Plugin, character);
                                _customNpcDictionary[npcData.NpcName] = npc;
                                _interactiveNpcDictionary[npcData.NpcName] = npc;

                                        // Apply appearance
                                        if (!npcData.UseMcdfAppearance)
                                        {
                                            AppearanceAccessUtils.AppearanceManager?.RemoveTemporaryCollection(character.Name.TextValue);
                                        }

                                        // Apply Penumbra Collection separately
                                        if (!npcData.UseMcdfAppearance && npcData.UsePenumbraCollection && !string.IsNullOrEmpty(npcData.PenumbraCollection))
                                        {
                                            if (Brio.Brio.TryGetService<Brio.IPC.PenumbraService>(out var penumbraService))
                                            {
                                                var collections = penumbraService.GetCollections();
                                                var collectionGuid = collections.FirstOrDefault(x => x.Value == npcData.PenumbraCollection).Key;
                                                if (collectionGuid != Guid.Empty)
                                                {
                                                    PenumbraAndGlamourerIpcWrapper.Instance.SetCollectionForObject.Invoke(character.ObjectIndex, collectionGuid, true, true);
                                                    PenumbraAndGlamourerIpcWrapper.Instance.RedrawObject.Invoke(character.ObjectIndex, Penumbra.Api.Enums.RedrawType.Redraw);
                                                }
                                                else
                                                {
                                                    Plugin.PluginLog.Warning($"Could not find Penumbra collection: {npcData.PenumbraCollection}");
                                                }
                                            }
                                        }

                                        if (npcData.UseMonsterModel)
                                        {
                                            unsafe
                                            {
                                                var native = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)character.Address;
                                                native->ModelContainer.ModelCharaId = (int)npcData.MonsterModelId;
                                            }
                                            PenumbraAndGlamourerIpcWrapper.Instance.RedrawObject.Invoke(character.ObjectIndex, Penumbra.Api.Enums.RedrawType.Redraw);
                                        }
                                        else if (npcData.UseMcdfAppearance && !string.IsNullOrEmpty(npcData.McdfFilePath))
                                        {
                                            try
                                            {
                                                AppearanceAccessUtils.AppearanceManager?.LoadAppearance(
                                                    npcData.McdfFilePath, character, (int)AppearanceSwapType.EntireAppearance);
                                            }
                                            catch (Exception ex)
                                            {
                                                Plugin.PluginLog.Warning(ex, "Failed to load MCDF appearance: " + npcData.McdfFilePath);
                                            }
                                        }
                                        else if (!string.IsNullOrEmpty(npcData.NpcGlamourerAppearanceString))
                                        {
                                            if (Guid.TryParse(npcData.NpcGlamourerAppearanceString, out var designGuid))
                                            {
                                                PenumbraAndGlamourerIpcWrapper.Instance.ApplyDesign.Invoke(designGuid, character.ObjectIndex);
                                            }
                                        }

                                        Plugin.AnamcoreManager.SetVoice(character, 0);

                                        npc.TargetClassJobId = npcData.NpcClassJobId;
                                        npc.TargetWeaponItemId = npcData.NpcEquippedWeaponItemId;
                                        npc.ClassWeaponApplied = false;

                                        // Record encounters (player returning to zone with NPC)
                                        RecordNpcEncounters(npcData);

                                // Set to stay at the saved position/rotation
                                npc.SetDefaults(position, rotation);
                                npc.SetDefaultRotation(rotation);
                                npc.IdleEmoteId = npcData.IdleEmoteId;
                                if (npcData.RandomIdleEmotes != null) npc.RandomIdleEmotes = npcData.RandomIdleEmotes.ToList();
                                npc.VictoryPoseEmoteId = npcData.VictoryPoseEmoteId;

                                // Trigger idle emote immediately for staying NPCs
                                ushort initialEmoteId = npcData.IdleEmoteId;
                                if (npcData.RandomIdleEmotes != null && npcData.RandomIdleEmotes.Count > 0)
                                {
                                    initialEmoteId = npcData.RandomIdleEmotes[new System.Random().Next(npcData.RandomIdleEmotes.Count)];
                                }

                                if (initialEmoteId > 0)
                                {
                                    try
                                    {
                                        var emote = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>().GetRow(initialEmoteId);
                                        Plugin.AnamcoreManager.TriggerEmote(character.Address, (ushort)emote.ActionTimeline[0].Value.RowId);
                                    }
                                    catch { }
                                }

                                // Create conversation manager
                                string baseDir = Plugin.Configuration.QuestInstallFolder ?? Path.GetTempPath();
                                string npcMemoryDir = Path.Combine(baseDir, "CustomNpcMemories");
                                Directory.CreateDirectory(npcMemoryDir);
                                var conversationManager = new NPCConversationManager(
                                    npcData.NpcName, npcMemoryDir, Plugin, character);
                                _customNpcConversationManagers[npcData.NpcName] = conversationManager;

                                Plugin.ChatGui.Print("[A Quest Reborn] " + npcData.NpcName + " is waiting where you left them!");
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.PluginLog.Warning(ex, "Failed to summon custom NPC at position: " + ex.Message);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Warning(ex, "Failed to summon custom NPC at position: " + ex.Message);
                }
            });
        }

        public void DismissCustomNpc(string npcName)
        {
            Plugin.Framework.RunOnFrameworkThread(() =>
            {
                if (_customNpcDictionary.ContainsKey(npcName))
                {
                    var npc = _customNpcDictionary[npcName];
                    npc.StopFollowingPlayer();

                    // Hide the NPC and bury underground instead of destroying
                    npc.HideNPC();
                    npc.SetDefaults(new Vector3(0, -5000f, 0), Vector3.Zero);

                    _customNpcDictionary.Remove(npcName);
                    _interactiveNpcDictionary.Remove(npcName);

                    // Move the actor into the hidden pool for re-use
                    if (_customNpcCharacters.ContainsKey(npcName))
                    {
                        var character = _customNpcCharacters[npcName];
                        _customNpcCharacters.Remove(npcName);
                        _hiddenNpcPool[npcName] = (npc, character);
                    }
                }
                if (_customNpcConversationManagers.ContainsKey(npcName))
                {
                    // Flush conversation summaries before removing
                    var convMgr = _customNpcConversationManagers[npcName];
                    _ = Task.Run(async () =>
                    {
                        try { await convMgr.FlushSummaries(); }
                        catch { }
                    });
                    _customNpcConversationManagers.Remove(npcName);
                }
                // Update the config state
                foreach (var npc in Plugin.Configuration.CustomNpcCharacters)
                {
                    if (npc.NpcName == npcName)
                    {
                        npc.IsFollowingPlayer = false;
                        break;
                    }
                }
                Plugin.ChatGui.Print("[A Quest Reborn] " + npcName + " has been dismissed.");
                Plugin.SpeechBubbleManager?.NotifyNpcDismissed(npcName);

                // Stamp last-seen for this NPC (player and other co-summoned NPCs)
                try
                {
                    foreach (var npc in Plugin.Configuration.CustomNpcCharacters)
                    {
                        if (npc.NpcName == npcName)
                        {
                            string playerName = Plugin.ObjectTable.LocalPlayer?.Name?.TextValue ?? "Adventurer";
                            npc.UpdateLastSeen(playerName);

                            // Update last-seen with remaining NPCs
                            foreach (var otherNpc in Plugin.Configuration.CustomNpcCharacters)
                            {
                                if (otherNpc.NpcName != npcName && _customNpcCharacters.ContainsKey(otherNpc.NpcName))
                                {
                                    npc.UpdateLastSeen(otherNpc.NpcName);
                                    otherNpc.UpdateLastSeen(npcName);
                                }
                            }
                            break;
                        }
                    }
                    Plugin.Configuration.Save();
                }
                catch { }
            });
        }

        public void HandleCustomNpcChat(IPlayerCharacter sender, string message)
        {
            LastNpcChatTime = DateTime.Now;

            if (_customNpcCharacters.Count == 0)
            {
                Plugin.ChatGui.PrintError("[A Quest Reborn] No custom NPCs are currently summoned.");
                return;
            }

            // If the player is dead, NPCs only hear murmurs
            string npcHearsMessage = message;
            if (sender.CurrentHp == 0)
            {
                string[] murmurs = new string[]
                {
                    "*barely audible, weak murmuring*",
                    "*faint, incoherent groaning*",
                    "*a pained whisper, too quiet to make out*",
                    "*lips move slightly but no words come out*",
                    "*a barely perceptible sigh escapes*",
                };
                npcHearsMessage = murmurs[new Random().Next(murmurs.Length)];
            }

            // Print the player's message (show what they typed)
            Plugin.SpeechBubbleManager.ShowBubble(sender, sender.Name.TextValue, message);

            Random random = new Random();

            foreach (var kvp in _customNpcCharacters)
            {
                string targetNpcName = kvp.Key;
                var npcCharacter = kvp.Value;

                if (!_customNpcConversationManagers.ContainsKey(targetNpcName)) continue;
                var conversationManager = _customNpcConversationManagers[targetNpcName];

                CustomNpcCharacter npcData = null;
                foreach (var npc in Plugin.Configuration.CustomNpcCharacters)
                {
                    if (npc.NpcName == targetNpcName)
                    {
                        npcData = npc;
                        break;
                    }
                }
                if (npcData == null) continue;

                // Analyze the player's message for sentiment toward this NPC
                npcData.RecordSentiment(sender.Name.TextValue, message);

                // If relationship is completely broken, NPC refuses to engage
                if (npcData.ShouldRefuseConversation(sender.Name.TextValue))
                {
                    var refusalNpc = npcCharacter; // Capture for lambda
                    var refusalName = npcData.NpcName;
                    Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        Plugin.SpeechBubbleManager?.ShowBubble(refusalNpc, refusalName,
                            "*turns away and refuses to speak*");
                    });
                    continue;
                }

                // Build lore with mood context injected
                string npcLore = npcData.GetFullLore() + npcData.GetMoodContext(sender.Name.TextValue);

                Task.Run(async () =>
                {
                    try
                    {
                        // Add a random delay so they don't all reply on the exact same frame
                        await Task.Delay(random.Next(500, 3000));

                        string response = await conversationManager.SendMessage(
                            sender, npcCharacter,
                            npcData.NpcName,
                            npcData.NPCGreeting,
                            npcHearsMessage,
                            Plugin.GetEnvironmentContext(npcCharacter),
                            npcLore);

                        if (!string.IsNullOrEmpty(response))
                        {
                            string cleanResponse = response.Trim();
                            
                            // Fix LLM hallucinating chat logs
                            string aiNamePrefix = npcData.NpcName.Split(" ")[0] + ":";
                            string senderNamePrefix = sender.Name.TextValue.Split(" ")[0] + ":";
                            
                            if (cleanResponse.StartsWith(senderNamePrefix))
                            {
                                int aiIndex = cleanResponse.IndexOf(aiNamePrefix);
                                if (aiIndex != -1)
                                {
                                    cleanResponse = cleanResponse.Substring(aiIndex);
                                }
                            }
                            if (cleanResponse.StartsWith(aiNamePrefix))
                            {
                                cleanResponse = cleanResponse.Substring(aiNamePrefix.Length).Trim();
                            }
                            int nextSenderIndex = cleanResponse.IndexOf(senderNamePrefix);
                            if (nextSenderIndex != -1)
                            {
                                cleanResponse = cleanResponse.Substring(0, nextSenderIndex).Trim();
                            }
                            int nextAiIndex = cleanResponse.IndexOf(aiNamePrefix);
                            if (nextAiIndex != -1)
                            {
                                cleanResponse = cleanResponse.Substring(0, nextAiIndex).Trim();
                            }

                            // Handle [glamour:Outfit] commands
                            try
                            {
                                var glamourMatch = System.Text.RegularExpressions.Regex.Match(cleanResponse, @"\[glamour:(.*?)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (glamourMatch.Success && _interactiveNpcDictionary.ContainsKey(npcData.NpcName))
                                {
                                    string requestedOutfit = glamourMatch.Groups[1].Value.Trim().ToLower();
                                    
                                    // Remove the command from the final output
                                    cleanResponse = System.Text.RegularExpressions.Regex.Replace(cleanResponse, @"\[glamour:.*?\]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

                                    var allDesigns = PenumbraAndGlamourerHelpers.PenumbraAndGlamourerHelperFunctions.GetGlamourerDesigns();
                                    foreach (var design in allDesigns)
                                    {
                                        if (design.Value.ToLower().Contains(requestedOutfit))
                                        {
                                            var customNpc = _interactiveNpcDictionary[npcData.NpcName];
                                            PenumbraAndGlamourerIpcWrapper.Instance.ApplyDesign.Invoke(design.Key, customNpc.Character.ObjectIndex);
                                            break;
                                        }
                                    }
                                }
                                else if (_interactiveNpcDictionary.ContainsKey(npcData.NpcName))
                                {
                                    // Fallback: If AI didn't use the explicit command, parse conversational agreement
                                    string userMessageLower = message.ToLower();
                                    if (userMessageLower.Contains("change") || userMessageLower.Contains("wear") || userMessageLower.Contains("put on") || userMessageLower.Contains("outfit") || userMessageLower.Contains("clothes"))
                                    {
                                        var allDesigns = PenumbraAndGlamourerHelpers.PenumbraAndGlamourerHelperFunctions.GetGlamourerDesigns();
                                        foreach (var design in allDesigns)
                                        {
                                            if (userMessageLower.Contains(design.Value.ToLower()))
                                            {
                                                string aiResponseLower = cleanResponse.ToLower();
                                                bool agreed = aiResponseLower.Contains("sure") || aiResponseLower.Contains("okay") || aiResponseLower.Contains("of course") || aiResponseLower.Contains("right away") || aiResponseLower.Contains("*nods*") || aiResponseLower.Contains("*smiles*") || aiResponseLower.Contains("i will") || aiResponseLower.Contains("alright") || aiResponseLower.Contains("certainly") || aiResponseLower.Contains("give me a moment") || aiResponseLower.Contains("changing");
                                                bool disagreed = aiResponseLower.Contains("no ") || aiResponseLower.Contains("don't") || aiResponseLower.Contains("won't") || aiResponseLower.Contains("*shakes head*") || aiResponseLower.Contains("cannot") || aiResponseLower.Contains("can't") || aiResponseLower.Contains("not right now");
                                                
                                                if (agreed && !disagreed)
                                                {
                                                    var customNpc = _interactiveNpcDictionary[npcData.NpcName];
                                                    PenumbraAndGlamourerIpcWrapper.Instance.ApplyDesign.Invoke(design.Key, customNpc.Character.ObjectIndex);
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Plugin.PluginLog.Warning("Failed to apply glamour command: " + ex.Message);
                            }

                            // Trigger emotes if the LLM generated an action
                            try
                            {
                                var emoteMatches = System.Text.RegularExpressions.Regex.Matches(cleanResponse, @"\*(.*?)\*");
                                if (emoteMatches.Count > 0 && _interactiveNpcDictionary.ContainsKey(npcData.NpcName))
                                {
                                    string actionText = emoteMatches[0].Groups[1].Value.ToLower();
                                    var emoteSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>();
                                    var emote = emoteSheet.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name.ToString()) && actionText.Contains(e.Name.ToString().ToLower()));
                                    
                                    if (emote.RowId == 0)
                                    {
                                        if (actionText.Contains("smiles") || actionText.Contains("grins")) emote = emoteSheet.GetRow(50);
                                        else if (actionText.Contains("laughs") || actionText.Contains("chuckles") || actionText.Contains("giggles")) emote = emoteSheet.GetRow(51);
                                        else if (actionText.Contains("nods") || actionText.Contains("agrees")) emote = emoteSheet.GetRow(52);
                                        else if (actionText.Contains("waves") || actionText.Contains("greets")) emote = emoteSheet.GetRow(53);
                                        else if (actionText.Contains("bows")) emote = emoteSheet.GetRow(54);
                                        else if (actionText.Contains("shrugs")) emote = emoteSheet.GetRow(64);
                                        else if (actionText.Contains("cries") || actionText.Contains("weeps")) emote = emoteSheet.GetRow(56);
                                        else if (actionText.Contains("points")) emote = emoteSheet.GetRow(62);
                                        else if (actionText.Contains("thinks") || actionText.Contains("ponders")) emote = emoteSheet.GetRow(76);
                                        else if (actionText.Contains("frowns") || actionText.Contains("angry")) emote = emoteSheet.GetRow(63);
                                        else if (actionText.Contains("cheers") || actionText.Contains("celebrates")) emote = emoteSheet.GetRow(58);
                                        else if (actionText.Contains("claps") || actionText.Contains("applauds")) emote = emoteSheet.GetRow(59);
                                    }

                                    if (emote.RowId > 0 && emote.ActionTimeline[0].Value.RowId > 0)
                                    {
                                        var customNpc = _interactiveNpcDictionary[npcData.NpcName];
                                        Plugin.AnamcoreManager.TriggerEmote(customNpc.Character.Address, (ushort)emote.ActionTimeline[0].Value.RowId);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Plugin.PluginLog.Warning("Failed to trigger emote: " + ex.Message);
                            }

                            // Strip actions for the chat log, similar to speech bubbles
                            foreach (var prefix in new[] { "says, ", "asks, ", "exclaims, " })
                            {
                                if (cleanResponse.StartsWith(prefix))
                                {
                                    cleanResponse = cleanResponse.Substring(prefix.Length);
                                    break;
                                }
                            }

                            int quoteCount = cleanResponse.Split(new[] { '"', '“', '”' }).Length - 1;
                            if (quoteCount % 2 != 0)
                            {
                                cleanResponse += "\"";
                            }

                            var quoteMatches = System.Text.RegularExpressions.Regex.Matches(cleanResponse, "[\"“]([^\"”]+)[\"”]");
                            if (quoteMatches.Count > 0)
                            {
                                string dialogueOnly = "";
                                foreach (System.Text.RegularExpressions.Match m in quoteMatches)
                                {
                                    dialogueOnly += m.Groups[1].Value.Trim() + " ";
                                }
                                cleanResponse = dialogueOnly.Trim();
                            }
                            else
                            {
                                cleanResponse = System.Text.RegularExpressions.Regex.Replace(cleanResponse, @"\*[^*]+\*", "").Trim();
                                cleanResponse = System.Text.RegularExpressions.Regex.Replace(cleanResponse, @"\[[^\]]+\]", "").Trim();
                                if (cleanResponse.StartsWith("\"") && cleanResponse.EndsWith("\"") && cleanResponse.Length > 2)
                                    cleanResponse = cleanResponse.Substring(1, cleanResponse.Length - 2);
                                cleanResponse = cleanResponse.TrimEnd('"').Trim();
                            }

                            // Anti-parroting filter: If the AI just echoed the user's message, reject it.
                            string cleanUserMsg = message.Trim().ToLower();
                            string finalCleanResp = cleanResponse.Trim().ToLower();
                            if (finalCleanResp == cleanUserMsg || (cleanUserMsg.Length > 15 && finalCleanResp.Contains(cleanUserMsg)))
                            {
                                cleanResponse = "...";
                            }

                            if (string.IsNullOrWhiteSpace(cleanResponse)) cleanResponse = "...";

                            Plugin.Framework.RunOnFrameworkThread(() =>
                            {
                                Plugin.SpeechBubbleManager.ShowBubble(npcCharacter, npcData.NpcName, cleanResponse);
                            });

                            // Trigger lip sync on the NPC
                            if (npcCharacter != null)
                            {
                                Plugin.AnamcoreManager.TriggerLipSync(npcCharacter, 0);
                                await Task.Delay(3000);
                                Plugin.AnamcoreManager.StopLipSync(npcCharacter);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.PluginLog.Warning(ex, "NPC Chat Error: " + ex.Message);
                    }
                });
            }
        }
        public void ReapplyCustomNpcAppearance(string npcName, Guid designGuid)
        {
            if (_customNpcCharacters.ContainsKey(npcName))
            {
                var character = _customNpcCharacters[npcName];
                Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    try
                    {
                        PenumbraAndGlamourerIpcWrapper.Instance.ApplyDesign.Invoke(designGuid, character.ObjectIndex);
                    }
                    catch (Exception ex)
                    {
                        Plugin.PluginLog.Warning(ex, "Failed to reapply appearance: " + ex.Message);
                    }
                });
            }
        }
        public void ReapplyCustomNpcPenumbraAppearance(string npcName, string penumbraCollection)
        {
            if (_customNpcCharacters.ContainsKey(npcName))
            {
                var character = _customNpcCharacters[npcName];
                try
                {
                    if (Brio.Brio.TryGetService<Brio.IPC.PenumbraService>(out var penumbraService))
                    {
                        var collections = penumbraService.GetCollections();
                        var collectionGuid = collections.FirstOrDefault(x => x.Value == penumbraCollection).Key;
                        if (collectionGuid != Guid.Empty)
                        {
                            PenumbraAndGlamourerIpcWrapper.Instance.SetCollectionForObject.Invoke(character.ObjectIndex, collectionGuid, true, true);
                            PenumbraAndGlamourerIpcWrapper.Instance.RedrawObject.Invoke(character.ObjectIndex, Penumbra.Api.Enums.RedrawType.Redraw);
                        }
                        else
                        {
                            Plugin.PluginLog.Warning($"Could not find Penumbra collection: {penumbraCollection}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Warning(ex, $"Failed to reapply Penumbra Collection '{penumbraCollection}' to NPC {npcName}");
                }
            }
        }

        public void ReapplyCustomNpcMcdfAppearance(string npcName, string mcdfPath)
        {
            if (_customNpcCharacters.ContainsKey(npcName))
            {
                var character = _customNpcCharacters[npcName];
                try
                {
                    AppearanceAccessUtils.AppearanceManager?.LoadAppearance(
                        mcdfPath, character, (int)AppearanceSwapType.EntireAppearance);
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Warning(ex, "Failed to reapply MCDF appearance: " + ex.Message);
                }
            }
        }

        public unsafe void ReapplyCustomNpcMonsterAppearance(string npcName, uint monsterModelId)
        {
            if (_customNpcCharacters.ContainsKey(npcName))
            {
                var character = _customNpcCharacters[npcName];
                Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    try
                    {
                        var native = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)character.Address;
                        if (native != null)
                        {
                            native->ModelContainer.ModelCharaId = (int)monsterModelId;
                            PenumbraAndGlamourerIpcWrapper.Instance.RedrawObject.Invoke(character.ObjectIndex, Penumbra.Api.Enums.RedrawType.Redraw);
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.PluginLog.Warning(ex, "Failed to reapply Monster appearance: " + ex.Message);
                    }
                });
            }
        }
        public void ToggleCustomNpcFollow(string npcName, bool shouldFollow)
        {
            if (_customNpcDictionary.ContainsKey(npcName))
            {
                var npc = _customNpcDictionary[npcName];
                // Find the config data for this NPC
                var npcConfig = Plugin.Configuration.CustomNpcCharacters?.Find(n => n.NpcName == npcName);
                if (shouldFollow)
                {
                    npc.FollowPlayer(2);
                    // Clear stay data
                    if (npcConfig != null)
                    {
                        npcConfig.IsStaying = false;
                        npcConfig.StayTerritoryId = 0;
                        Plugin.Configuration.Save();
                    }
                }
                else
                {
                    // Capture the Brio transform position and rotation before stopping
                    var pos = npc.CurrentPosition;
                    var rot = npc.CurrentRotation;
                    npc.StopFollowingPlayer();
                    npc.SetDefaults(pos, rot);
                    npc.SetDefaultRotation(rot);

                    // Save stay location to config
                    if (npcConfig != null)
                    {
                        npcConfig.IsStaying = true;
                        npcConfig.StayTerritoryId = Plugin.ClientState.TerritoryType;
                        npcConfig.StayPositionX = pos.X;
                        npcConfig.StayPositionY = pos.Y;
                        npcConfig.StayPositionZ = pos.Z;
                        npcConfig.StayRotationX = rot.X;
                        npcConfig.StayRotationY = rot.Y;
                        npcConfig.StayRotationZ = rot.Z;
                        Plugin.Configuration.Save();
                    }
                }
            }
        }
        private Stopwatch _npcChatCooldown = new Stopwatch();
        private bool _npcChatConfirmHeld;

        /// <summary>
        /// Detects when the player targets a custom NPC and presses confirm to open the chat window.
        /// </summary>
        private unsafe void CustomNpcChatCheck()
        {
            if (Conditions.Instance()->InCombat) return;
            if (Plugin.ObjectTable.LocalPlayer != null && Plugin.ObjectTable.LocalPlayer.CurrentHp == 0) return; // Can't talk while dead
            if (Plugin.NpcChatWindow.IsConversationActive) return;
            if (Plugin.EventWindow.IsOpen || Plugin.ChoiceWindow.IsOpen) return;

            // Don't consume screen clicks when quest NPCs are active — let QuestInputCheck handle those
            bool useScreenClick = _screenButtonClicked && !_cutsceneNpcSpawned;

            // Check for confirm input (gamepad south or screen click)
            bool confirmPressed = (Plugin.Configuration.EnableControllerInteraction && Plugin.GamepadState.Raw(GamepadButtons.South) == 1)
                || useScreenClick;

            if (confirmPressed && !_npcChatConfirmHeld)
            {
                _npcChatConfirmHeld = true;
                if (useScreenClick) _screenButtonClicked = false; // Consume the click so it doesn't persist
                
                // Get the player's current target
                var target = Plugin.ObjectTable.LocalPlayer?.TargetObject;
                
                // Custom NPCs cannot be targeted. If the player has a target, let FFXIV handle it natively.
                if (target != null) return;

                string interactionName = null;
                Dalamud.Game.ClientState.Objects.Types.ICharacter interactionCharacter = null;

                // Check if we are facing a custom NPC in range
                var player = Plugin.ObjectTable.LocalPlayer;
                if (player != null)
                {
                    foreach (var kvp in _customNpcCharacters)
                    {
                            if (kvp.Value == null || kvp.Value.Address == 0) continue;
                            var pos = kvp.Value.Position;
                            var native = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)kvp.Value.Address;
                            if (native != null) pos = native->GameObject.Position;

                            var dist = System.Numerics.Vector3.Distance(player.Position, pos);
                            if (dist < 3.5f)
                            {
                                var toNpc = new System.Numerics.Vector2(pos.X - player.Position.X, pos.Z - player.Position.Z);
                                toNpc = System.Numerics.Vector2.Normalize(toNpc);
                                var playerForward = new System.Numerics.Vector2((float)Math.Sin(player.Rotation), (float)Math.Cos(player.Rotation));
                                float dot = System.Numerics.Vector2.Dot(toNpc, playerForward);
                                
                                if (dot > 0.5f)
                                {
                                    interactionName = kvp.Key;
                                    interactionCharacter = kvp.Value;
                                    break;
                                }
                            }
                        }
                    }

                if (interactionName == null) return;

                // Find NPC config data
                CustomNpcCharacter npcData = null;
                foreach (var npc in Plugin.Configuration.CustomNpcCharacters)
                {
                    if (npc.NpcName == interactionName)
                    {
                        npcData = npc;
                        break;
                    }
                }
                if (npcData == null) return;

                // Ensure conversation manager exists
                if (!_customNpcConversationManagers.ContainsKey(interactionName)) return;

                // Open the conversation window
                Plugin.NpcChatWindow.OpenConversation(interactionName,
                    _customNpcConversationManagers[interactionName],
                    interactionCharacter, npcData);
            }
            else if (!confirmPressed)
            {
                _npcChatConfirmHeld = false;
            }
        }
        #endregion
    }
}
