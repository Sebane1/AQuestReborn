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
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Common.Lua;
using Lumina.Excel.Sheets;
using McdfDataImporter;
using RoleplayingMediaCore;
using RoleplayingQuestCore;
using RoleplayingVoiceDalamudWrapper;
using SamplePlugin;
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
using Utf8String = FFXIVClientStructs.FFXIV.Client.System.String.Utf8String;

namespace AQuestReborn
{
    internal class AQuestReborn
    {
        public Plugin Plugin { get; }
        public Dictionary<string, Dictionary<string, ICharacter>> SpawnedNPCs { get => _spawnedNPCsDictionary; set => _spawnedNPCsDictionary = value; }
        public string Discriminator { get => _discriminator; set => _discriminator = value; }

        private Stopwatch _pollingTimer;
        private Stopwatch _inputCooldown;
        private Stopwatch _mcdfRefreshTimer = new Stopwatch();
        private Stopwatch _actorSpawnRefreshTimer = new Stopwatch();
        private Stopwatch _mapRefreshTimer = new Stopwatch();
        private bool _screenButtonClicked;
        private Dictionary<string, Dictionary<string, ICharacter>> _spawnedNPCsDictionary = new Dictionary<string, Dictionary<string, ICharacter>>();
        private bool _triggerRefresh;
        private bool _waitingForSelectionRelease;
        Queue<KeyValuePair<string, ICharacter>> _mcdfQueue = new Queue<KeyValuePair<string, ICharacter>>();
        Queue<Tuple<Transform, string, string, Dictionary<string, ICharacter>, bool, RoleplayingQuest>> _npcActorSpawnQueue = new Queue<Tuple<Transform, string, string, Dictionary<string, ICharacter>, bool, RoleplayingQuest>>();
        private ActorSpawnService _actorSpawnService;
        private MareService _mcdfService;
        private MediaGameObject _playerObject;
        private unsafe Camera* _camera;
        private MediaCameraObject _playerCamera;
        private List<Tuple<int, QuestObjective, RoleplayingQuest>> _activeQuestChainObjectives;
        private bool alreadyProcessingRespawns;
        private bool waitingForMcdfLoad;
        Stopwatch zoneChangeCooldown = new Stopwatch();
        private bool _isInitialized;
        private bool _initializationStarted;
        private bool _refreshingNPCQuests;
        private string _discriminator;


        public AQuestReborn(Plugin plugin)
        {
            Plugin = plugin;
            Plugin.DialogueBackgroundWindow.ButtonClicked += DialogueBackgroundWindow_buttonClicked;
            Plugin.ObjectiveWindow.OnSelectionAttempt += DialogueBackgroundWindow_buttonClicked;
            plugin.RoleplayingQuestManager.LoadMainQuestGameObject(new QuestGameObject(plugin.ClientState));
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
            Plugin.ChatGui.ChatMessage += ChatGui_ChatMessage;
            Plugin.EmoteReaderHook.OnEmote += (instigator, emoteId) => OnEmote(instigator as ICharacter, emoteId);
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
                _mcdfQueue.Clear();
                _npcActorSpawnQueue.Clear();
                zoneChangeCooldown.Reset();
                Task.Run(() =>
                {
                    foreach (var file in Directory.EnumerateFiles(McdfAccessUtils.CacheLocation, "*.tmp"))
                    {
                        File.Delete(file);
                    }
                    while (Plugin.ClientState.LocalPlayer == null || _actorSpawnService == null)
                    {
                        Thread.Sleep(3000);
                    }
                    _triggerRefresh = true;
                    _discriminator = DiscriminatorGenerator.GetDiscriminator(Plugin.ClientState);
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
                RefreshNpcsForQuest(Plugin.ClientState.TerritoryType);
                _discriminator = DiscriminatorGenerator.GetDiscriminator(Plugin.ClientState);
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

        private void CheckInitialization()
        {
            if (!_initializationStarted)
            {
                _initializationStarted = true;
                Task.Run(() =>
                {
                    while (Brio.Brio._services == null)
                    {
                        Thread.Sleep(100);
                    }
                    Brio.Brio.TryGetService<ActorSpawnService>(out _actorSpawnService);
                    Brio.Brio.TryGetService<MareService>(out _mcdfService);
                    InitializeMediaManager();
                    while (!Plugin.ClientState.IsLoggedIn)
                    {
                        Thread.Sleep(500);
                    }
                    if (Plugin.ClientState.IsLoggedIn)
                    {
                        _clientState_TerritoryChanged(Plugin.ClientState.TerritoryType);
                        _isInitialized = true;
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
                        if (!waitingForMcdfLoad && !McdfAccessUtils.McdfManager.IsWorking())
                        {
                            var value = _npcActorSpawnQueue.Dequeue();
                            bool newNPC = !value.Item5;
                            ICharacter character = null;
                            if (newNPC)
                            {
                                _actorSpawnService.CreateCharacter(out character, SpawnFlags.DefinePosition, true,
                                value.Item1.Position, Utility.ConvertDegreesToRadians(value.Item1.EulerRotation.Y));
                                value.Item4[value.Item2] = character;
                            }
                            else
                            {
                                character = value.Item4[value.Item2];
                                BrioAccessUtils.EntityManager.SetSelectedEntity(character);
                                BrioAccessUtils.EntityManager.TryGetCapabilityFromSelectedEntity<PosingCapability>(out var posing);
                                if (posing != null)
                                {
                                    posing.ModelPosing.ResetTransform();
                                    posing.ModelPosing.Transform = new Brio.Core.Transform()
                                    {
                                        Position = value.Item1.Position,
                                        Rotation = CoordinateUtility.ToQuaternion(value.Item1.EulerRotation),
                                        Scale = value.Item1.TransformScale
                                    };
                                }
                                else
                                {
                                    _actorSpawnService.CreateCharacter(out character, SpawnFlags.DefinePosition, true,
                                    value.Item1.Position, Utility.ConvertDegreesToRadians(value.Item1.EulerRotation.Y));
                                    value.Item4[value.Item2] = character;
                                    newNPC = true;
                                }
                            }
                            if (character != null)
                            {
                                Plugin.AnamcoreManager.TriggerEmote(character.Address, (ushort)value.Item1.DefaultAnimationId);
                                if (newNPC || Plugin.RoleplayingQuestManager.SwapAppearanceData(value.Item6, value.Item2, Path.GetFileName(value.Item3)))
                                {
                                    LoadMCDF(value.Item3, character);
                                }
                            }
                        }
                    }
                }
            }
        }
        public void LoadMCDF(string mcdf, ICharacter character)
        {
            Task.Run(() =>
            {
                lock (_npcActorSpawnQueue)
                {
                    waitingForMcdfLoad = true;
                    Thread.Sleep(200);
                    while (_mcdfQueue.Count > 0)
                    {
                        Thread.Sleep(200);
                    }
                    _mcdfQueue.Enqueue(new KeyValuePair<string, ICharacter>(mcdf, character));
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
            if (_mcdfQueue.Count > 0)
            {
                if (waitingForMcdfLoad && _mcdfRefreshTimer.ElapsedMilliseconds > 500 && !McdfAccessUtils.McdfManager.IsWorking())
                {
                    var item = _mcdfQueue.Dequeue();
                    if (item.Value != null)
                    {
                        McdfAccessUtils.McdfManager?.LoadMcdf(item.Key, item.Value);
                        waitingForMcdfLoad = false;
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
            if (_triggerRefresh && !McdfAccessUtils.McdfManager.IsWorking())
            {
                RefreshMapMarkers();
                RefreshNpcsForQuest(Plugin.ClientState.TerritoryType);
                _triggerRefresh = false;
            }
        }


        private void QuestInputCheck()
        {
            if (_pollingTimer.ElapsedMilliseconds > 100)
            {
                Plugin.ObjectiveWindow.IsOpen = true;
                if (((Plugin.GamepadState.Raw(GamepadButtons.South) == 1) || _screenButtonClicked))
                {
                    _screenButtonClicked = false;
                    if (!_waitingForSelectionRelease)
                    {
                        if (Plugin.QuestAcceptanceWindow.TimeSinceLastQuestAccepted.ElapsedMilliseconds > 250
                            && Plugin.ChoiceWindow.TimeSinceLastChoiceMade.ElapsedMilliseconds > 250)
                        {
                            _inputCooldown.Restart();
                            if (!Plugin.DialogueWindow.IsOpen && !Plugin.ChoiceWindow.IsOpen)
                            {
                                Plugin.RoleplayingQuestManager.AttemptProgressingQuestObjective();
                            }
                            else
                            {
                                Plugin.DialogueWindow.NextText();
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
                if (_spawnedNPCsDictionary.ContainsKey(questId))
                {
                    int sleepTime = 100;
                    foreach (var item in _spawnedNPCsDictionary[questId])
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
                    _spawnedNPCsDictionary[questId].Clear();
                }
            }
            catch (Exception ex)
            {
                Plugin.PluginLog.Warning(ex, ex.Message);
            }
        }
        public void RefreshNpcsForQuest(ushort territoryId, string questId = "", bool softRefresh = false)
        {
            if (!_refreshingNPCQuests && _npcActorSpawnQueue.Count == 0)
            {
                try
                {
                    _refreshingNPCQuests = true;
                    if (_actorSpawnService != null)
                    {
                        if (!_spawnedNPCsDictionary.ContainsKey("DEBUG"))
                        {
                            _spawnedNPCsDictionary["DEBUG"] = new Dictionary<string, ICharacter>();
                        }
                        if (!_spawnedNPCsDictionary["DEBUG"].ContainsKey("First Spawn") || _spawnedNPCsDictionary["DEBUG"].Count == 0
                            || (_spawnedNPCsDictionary["DEBUG"].ContainsKey("First Spawn") && _spawnedNPCsDictionary["DEBUG"]["First Spawn"] == null))
                        {
                            ICharacter firstSpawn = null;
                            _actorSpawnService.CreateCharacter(out firstSpawn, SpawnFlags.DefinePosition, true,
                            new System.Numerics.Vector3(float.MaxValue / 2, float.MaxValue / 2, float.MaxValue / 2), 0);
                            _spawnedNPCsDictionary["DEBUG"]["First Spawn"] = firstSpawn;
                        }

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
                                        if (!_spawnedNPCsDictionary.ContainsKey(item.Item3.QuestId))
                                        {
                                            _spawnedNPCsDictionary[item.Item3.QuestId] = new Dictionary<string, ICharacter>();
                                        }
                                        var spawnedNpcsList = _spawnedNPCsDictionary[item.Item3.QuestId];
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
                                        var value = new Tuple<Transform, string, string, Dictionary<string, ICharacter>, bool, RoleplayingQuest>(startingInfo, npcAppearance.Value.NpcName, Path.Combine(foundPath, npcAppearance.Value.AppearanceData), spawnedNpcsList, foundExistingNPC, item.Item3);
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
        public void UpdateNPCAppearance(ushort territoryId, string questId, string npcName, string appearancePath)
        {
            try
            {
                LoadMCDF(appearancePath, _spawnedNPCsDictionary[questId][npcName]);
            }
            catch
            {
                try
                {
                    RefreshNpcsForQuest(territoryId, questId, true);
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
            string path = Path.Combine(e.FoundPath, e.QuestStartTitleCard);
            string soundPath = Path.Combine(e.FoundPath, e.QuestStartTitleSound);
            Plugin.TitleCardWindow.DisplayCard(path, soundPath);
            // Plugin.ToastGui.ShowQuest("Quest Started");
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
            _actorSpawnService.TargetService.GPoseTarget = null;
        }
    }
}
