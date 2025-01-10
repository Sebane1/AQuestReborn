using Anamnesis.GameData;
using Brio;
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
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Common.Lua;
using Lumina.Excel.Sheets;
using McdfDataImporter;
using RoleplayingMediaCore;
using RoleplayingQuestCore;
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
        public bool WaitingForMcdfLoad { get => _waitingForMcdfLoad; set => _waitingForMcdfLoad = value; }

        private Stopwatch _pollingTimer;
        private Stopwatch _inputCooldown;
        private Stopwatch _mcdfRefreshTimer = new Stopwatch();
        private Stopwatch _actorSpawnRefreshTimer = new Stopwatch();
        private Stopwatch _mapRefreshTimer = new Stopwatch();
        private bool _screenButtonClicked;
        private Dictionary<string, Dictionary<string, ICharacter>> _spawnedNpcsDictionary = new Dictionary<string, Dictionary<string, ICharacter>>();
        private Dictionary<string, InteractiveNpc> _interactiveNpcDictionary = new Dictionary<string, InteractiveNpc>();
        private Dictionary<string, Tuple<int, Stopwatch>> _objectiveTimers = new Dictionary<string, Tuple<int, Stopwatch>>();
        private bool _triggerRefresh;
        private bool _waitingForSelectionRelease;
        Queue<Tuple<string, AppearanceSwapType, ICharacter>> _appearanceApplicationQueue = new Queue<Tuple<string, AppearanceSwapType, ICharacter>>();
        Queue<Tuple<Transform, string, string, Dictionary<string, ICharacter>, bool, RoleplayingQuest, bool>> _npcActorSpawnQueue = new Queue<Tuple<Transform, string, string, Dictionary<string, ICharacter>, bool, RoleplayingQuest, bool>>();
        private ActorSpawnService _actorSpawnService;
        private MediaGameObject _playerObject;
        private unsafe Camera* _camera;
        private MediaCameraObject _playerCamera;
        private List<Tuple<int, QuestObjective, RoleplayingQuest>> _activeQuestChainObjectives;
        private bool alreadyProcessingRespawns;
        private bool _waitingForMcdfLoad;
        Stopwatch zoneChangeCooldown = new Stopwatch();
        private bool _isInitialized;
        private bool _initializationStarted;
        private bool _refreshingNPCQuests;
        private string _discriminator;
        private bool _gotZoneDiscriminator;
        private bool _checkForPartyMembers;
        private bool _hasCheckedForPlayerAppearance;
        private bool _disposed;

        public AQuestReborn(Plugin plugin)
        {
            Plugin = plugin;
            plugin.RoleplayingQuestManager.LoadMainQuestGameObject(new QuestGameObject(plugin.ClientState));
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
            Plugin.EmoteReaderHook.OnEmote += (instigator, emoteId) => OnEmote(instigator as ICharacter, emoteId);
        }

        private void ClientState_Logout(int type, int code)
        {
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

        private void ChatGui_ChatMessage(Dalamud.Game.Text.XivChatType type, int timestamp, ref Dalamud.Game.Text.SeStringHandling.SeString sender, ref Dalamud.Game.Text.SeStringHandling.SeString message, ref bool isHandled)
        {
            try
            {
                Plugin.PluginLog.Debug((int)type + " " + message);
                var messageAsString = message.ToString();
                switch ((int)type)
                {
                    case 2874:
                        Task.Run(() =>
                        {
                            while (Conditions.IsInCombat)
                            {
                                Thread.Sleep(1000);
                            }
                            Plugin.RoleplayingQuestManager.AttemptProgressingQuestObjective(QuestObjective.ObjectiveTriggerType.KillEnemy, messageAsString, true);
                        });
                        break;
                }
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Warning(e, e.Message);
            }
        }

        private void RewardWindow_OnRewardClosed(object? sender, RoleplayingQuest e)
        {
            QuestToastOptions questToastOptions = new QuestToastOptions();
            string path = Path.Combine(e.FoundPath, e.QuestEndTitleCard);
            string soundPath = Path.Combine(e.FoundPath, e.QuestEndTitleCard);
            Plugin.TitleCardWindow.DisplayCard(path, soundPath, true);
            //Plugin.ToastGui.ShowQuest("Quest Completed");
            Plugin.Configuration.Save();
        }

        private void OnEmote(ICharacter character, ushort emoteId)
        {
            try
            {
                if (!Plugin.DialogueWindow.IsOpen && !Plugin.ChoiceWindow.IsOpen)
                {
                    Plugin.RoleplayingQuestManager.AttemptProgressingQuestObjective(QuestObjective.ObjectiveTriggerType.DoEmote, emoteId.ToString());
                }
                if (Plugin.EditorWindow.IsOpen)
                {
                    Emote emote = Plugin.DataManager.GetExcelSheet<Emote>().GetRow(emoteId);
                    Plugin.ChatGui.Print("Emote Id: " + emoteId);
                    Plugin.ChatGui.Print("Body Animation Id: " + emote.ActionTimeline[0].Value.RowId);
                }
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Warning(e, e.Message);
            }
        }

        private void _clientState_TerritoryChanged(ushort territory)
        {
            try
            {
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
                _hasCheckedForPlayerAppearance = false;
                Task.Run(() =>
                {
                    try
                    {
                        while (Plugin.ClientState.LocalPlayer == null || _actorSpawnService == null)
                        {
                            Thread.Sleep(3000);
                        }
                        _triggerRefresh = true;
                        _gotZoneDiscriminator = false;
                        _checkForPartyMembers = true;
                        ICharacter character = null;
                        _actorSpawnService.CreateCharacter(out character, SpawnFlags.DefinePosition, true,
                        (new Vector3(0, float.MaxValue, 0) / 10), CoordinateUtility.ConvertDegreesToRadians(0));
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
        public void RefreshMapMarkers()
        {
            try
            {
                if (Plugin.ClientState.IsLoggedIn && !Conditions.IsInBetweenAreas)
                {
                    _activeQuestChainObjectives = Plugin.RoleplayingQuestManager.GetActiveQuestChainObjectivesInZone(Plugin.ClientState.TerritoryType, _discriminator);
                    unsafe
                    {
                        AgentMap.Instance()->ResetMapMarkers();
                        AgentMap.Instance()->ResetMiniMapMarkers();
                        foreach (var item in _activeQuestChainObjectives)
                        {
                            if (!item.Item2.DontShowOnMap && !item.Item2.ObjectiveCompleted)
                            {
                                {
                                    var map = Plugin.DataManager.GetExcelSheet<TerritoryType>().GetRow(Plugin.ClientState.TerritoryType).Map.Value;
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
                RefreshNpcs(Plugin.ClientState.TerritoryType);
                _gotZoneDiscriminator = false;
            }
        }

        public unsafe void InitializeMediaManager()
        {
            try
            {
                if (_playerObject == null)
                {
                    _playerObject = new MediaGameObject(Plugin.ClientState.LocalPlayer);
                }

                if (_playerCamera == null)
                {
                    _camera = CameraManager.Instance()->GetActiveCamera();
                    _playerCamera = new MediaCameraObject(_camera);
                }
                Plugin.MediaManager = new MediaManager(_playerObject, _playerCamera,
                Path.GetDirectoryName(Plugin.DalamudPluginInterface.AssemblyLocation.FullName));
                Plugin.DialogueBackgroundWindow.MediaManager = Plugin.MediaManager;
            }
            catch (Exception e)
            {
                Plugin.PluginLog.Warning(e, e.Message);
            }
        }

        private void _framework_Update(IFramework framework)
        {
            if (!_disposed)
            {
                try
                {
                    if (!Plugin.ClientState.IsGPosing && !Plugin.ClientState.IsPvPExcludingDen && !Conditions.IsInBetweenAreas && !Conditions.IsWatchingCutscene
                        && !Conditions.IsOccupied && !Conditions.IsInCombat && Plugin.ClientState.IsLoggedIn)
                    {
                        // Hopefully waiting prevents crashing on zone changes?
                        if (zoneChangeCooldown.ElapsedMilliseconds > 3000)
                        {
                            if (!_isInitialized)
                            {
                                CheckInitialization();
                            }
                            else
                            {
                                CheckForNewMCDFLoad();
                                QuestInputCheck();
                                CheckForNewPlayerCreationLoad();
                                CheckForNPCRefresh();
                                CheckForMapRefresh();
                                CheckZoneDiscriminator();
                                CheckForPlayerAppearance();
                            }
                        }
                        if (!zoneChangeCooldown.IsRunning)
                        {
                            zoneChangeCooldown.Start();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.PluginLog.Warning(ex, ex.Message);
                }
            }
        }

        private void CheckForPlayerAppearance()
        {
            if (!_waitingForMcdfLoad && !AppearanceAccessUtils.AppearanceManager.IsWorking() && !_hasCheckedForPlayerAppearance)
            {
                _hasCheckedForPlayerAppearance = true;
                var appearance = Plugin.RoleplayingQuestManager.GetPlayerAppearanceForZone(Plugin.ClientState.TerritoryType, _discriminator);
                if (appearance != null)
                {
                    LoadAppearance(appearance.AppearanceData, appearance.AppearanceSwapType, Plugin.ClientState.LocalPlayer);
                    Plugin.ToastGui.ShowNormal("A quest in this zone is affecting your characters appearance.");
                }
                else
                {
                    AppearanceAccessUtils.AppearanceManager.RemoveTemporaryCollection(Plugin.ClientState.LocalPlayer.Name.TextValue);
                }
            }
        }

        private void CheckZoneDiscriminator()
        {
            if (!_gotZoneDiscriminator)
            {
                try
                {
                    _discriminator = DiscriminatorGenerator.GetDiscriminator(Plugin.ClientState);
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
                            Brio.Brio.TryGetService<ActorSpawnService>(out _actorSpawnService);
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
                        if (!_waitingForMcdfLoad && !AppearanceAccessUtils.AppearanceManager.IsWorking())
                        {
                            var value = _npcActorSpawnQueue.Dequeue();
                            bool newNPC = !value.Item5;
                            if (File.Exists(value.Item3))
                            {
                                ICharacter character = null;
                                if (newNPC)
                                {
                                    if (!_interactiveNpcDictionary.ContainsKey(value.Item2))
                                    {
                                        _actorSpawnService.CreateCharacter(out character, SpawnFlags.DefinePosition, true,
                                    value.Item1.Position + new Vector3(0, -20, 0), CoordinateUtility.ConvertDegreesToRadians(value.Item1.EulerRotation.Y));
                                        value.Item4[value.Item2] = character;
                                        var npc = new InteractiveNpc(Plugin, character);
                                        _interactiveNpcDictionary.Add(value.Item2, npc);
                                    }
                                }
                                else
                                {
                                    character = value.Item4[value.Item2];
                                }
                                _interactiveNpcDictionary[value.Item2].SetDefaults(value.Item1.Position, value.Item1.EulerRotation);
                                _interactiveNpcDictionary[value.Item2].SetScale(value.Item1.TransformScale, 2);
                                if (character != null)
                                {
                                    if (_interactiveNpcDictionary[value.Item2].LastMcdf != value.Item3
                                    || Plugin.RoleplayingQuestManager.QuestProgression[value.Item6.QuestId] == 0)
                                    {
                                        LoadAppearance(value.Item3, AppearanceSwapType.EntireAppearance, character);
                                        _interactiveNpcDictionary[value.Item2].LastMcdf = value.Item3;
                                    }
                                    Plugin.AnamcoreManager.SetVoice(character, 0);
                                    Plugin.AnamcoreManager.TriggerEmote(character.Address, (ushort)value.Item1.DefaultAnimationId);
                                }
                                if (value.Item7)
                                {
                                    _interactiveNpcDictionary[value.Item2].FollowPlayer(2, true);
                                }
                            }
                        }
                    }
                }
            }
        }
        public void LoadAppearance(string appearanceData, AppearanceSwapType appearanceSwapType, ICharacter character)
        {
            _waitingForMcdfLoad = true;
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
            if (_mapRefreshTimer.ElapsedMilliseconds > 10000)
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
                            Plugin.MediaManager.NpcVolume = ((float)voiceVolume / 100f) * ((float)masterVolume / 100f);
                        Plugin.MediaManager.CameraAndPlayerPositionSlider = (float)soundMicPos / 100f;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.PluginLog?.Warning(e, e.Message);
            }
        }
        private async void CheckForNewMCDFLoad()
        {
            if (_appearanceApplicationQueue.Count > 0)
            {
                if (_waitingForMcdfLoad && _mcdfRefreshTimer.ElapsedMilliseconds > 500 && !AppearanceAccessUtils.AppearanceManager.IsWorking())
                {
                    var item = _appearanceApplicationQueue.Dequeue();
                    if (item.Item3 != null)
                    {
                        AppearanceAccessUtils.AppearanceManager?.LoadAppearance(item.Item1, item.Item3, (int)item.Item2);
                        _waitingForMcdfLoad = false;
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
            if (_triggerRefresh && !AppearanceAccessUtils.AppearanceManager.IsWorking())
            {
                RefreshMapMarkers();
                RefreshNpcs(Plugin.ClientState.TerritoryType);
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
                            if (!Plugin.DialogueWindow.IsOpen && !Plugin.ChoiceWindow.IsOpen)
                            {
                                Plugin.RoleplayingQuestManager.AttemptProgressingQuestObjective();
                            }
                            else
                            {
                                Plugin.DialogueWindow.NextEvent();
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
                        foreach (var item in questChains)
                        {
                            if (item.Item3.QuestId == questId || string.IsNullOrEmpty(questId))
                            {
                                string foundPath = item.Item3.FoundPath;
                                foreach (var npcAppearance in item.Item3.NpcCustomizations)
                                {
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
                                        var value = new Tuple<Transform, string, string, Dictionary<string, ICharacter>, bool, RoleplayingQuest, bool>
                                        (startingInfo, npcAppearance.Value.NpcName, Path.Combine(foundPath, npcAppearance.Value.AppearanceData), spawnedNpcsList, foundExistingNPC, item.Item3, false);
                                        _npcActorSpawnQueue.Enqueue(value);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.PluginLog.Warning(e, e.Message);
                }
                _refreshingNPCQuests = false;
            }
        }
        private void RefreshPartyMembers(ushort territoryType, string discriminator)
        {
            var members = Plugin.RoleplayingQuestManager.GetPartyMembersForZone(territoryType, discriminator);
            foreach (var member in members)
            {
                if (Plugin.RoleplayingQuestManager.QuestChains.ContainsKey(member.QuestId))
                {
                    var transform = new Transform() { Name = member.NpcName, Position = Plugin.ClientState.LocalPlayer.Position, TransformScale = new Vector3(1, 1, 1) };
                    if (!_spawnedNpcsDictionary.ContainsKey(member.QuestId))
                    {
                        _spawnedNpcsDictionary[member.QuestId] = new Dictionary<string, ICharacter>();
                    }
                    var spawnedNpcList = _spawnedNpcsDictionary[member.QuestId];
                    var foundExistingNpc = _spawnedNpcsDictionary.ContainsKey(member.NpcName);
                    var customization = Plugin.RoleplayingQuestManager.GetNpcInformation(member.QuestId, member.NpcName);
                    var quest = Plugin.RoleplayingQuestManager.QuestChains[member.QuestId];
                    var value = new Tuple<Transform, string, string, Dictionary<string, ICharacter>, bool, RoleplayingQuest, bool>(
                    transform, member.NpcName, Path.Combine(quest.FoundPath, customization.AppearanceData), spawnedNpcList, foundExistingNpc, quest, true);
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

        private void _roleplayingQuestManager_OnObjectiveCompleted(object? sender, QuestObjective e)
        {
            Plugin.ToastGui.ShowQuest(e.Objective,
            new Dalamud.Game.Gui.Toast.QuestToastOptions()
            {
                DisplayCheckmark = e.ObjectiveStatus == QuestObjective.ObjectiveStatusType.Complete,
                PlaySound = e.ObjectiveStatus == QuestObjective.ObjectiveStatusType.Complete
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
                Plugin.DialogueWindow.IsOpen = true;
                Plugin.DialogueWindow.NewText(e);
            }
            else
            {
                e.QuestEvents.Invoke(this, EventArgs.Empty);
            }
        }
        public void Dispose()
        {
            _disposed = true;
            AppearanceAccessUtils.AppearanceManager.RemoveAllTemporaryCollections();
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
    }
}
