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
        public bool WaitingForMcdfLoad { get => _waitingForAppearanceLoad; set => _waitingForAppearanceLoad = value; }
        public static MediaGameObject PlayerObject { get => _playerObject; set => _playerObject = value; }
        public static nint PlayerAddress { get => _playerAddress; set => _playerAddress = value; }
        public static CharacterCustomization PlayerAppearanceData { get; internal set; }
        public static string PlayerClassJob { get; set; }
        public Stopwatch CheckCooldownTimer { get => _checkCooldownTimer; set => _checkCooldownTimer = value; }
        internal CutsceneCamera CutsceneCamera { get => _cutsceneCamera; set => _cutsceneCamera = value; }
        public InteractiveNpc CutscenePlayer { get => _cutscenePlayer; set => _cutscenePlayer = value; }

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
        private bool _hasCheckedForPlayerAppearance;
        private bool _disposed;
        private static nint _playerAddress;
        private CutsceneCamera _cutsceneCamera;

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
            Plugin.EmoteReaderHook.OnEmote += (instigator, emoteId) => OnEmote(instigator as ICharacter, emoteId);
            Translator.LoadCache(Path.Combine(Plugin.Configuration.QuestInstallFolder, "languageCache.json"));
            Translator.UiLanguage = Plugin.Configuration.QuestLanguage;
            Translator.OnError += Translator_OnError;
            Translator.OnTranslationEvent += Translator_OnTranslationEvent;
            _cutsceneCamera = new CutsceneCamera(plugin);
        }

        private void Translator_OnTranslationEvent(object? sender, string e)
        {
            Plugin.PluginLog.Verbose(e);
        }

        private void Translator_OnError(object? sender, string e)
        {
            Plugin.PluginLog.Warning(e);
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

        private unsafe void ChatGui_ChatMessage(Dalamud.Game.Text.XivChatType type, int timestamp, ref Dalamud.Game.Text.SeStringHandling.SeString sender, ref Dalamud.Game.Text.SeStringHandling.SeString message, ref bool isHandled)
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
            string soundPath = Path.Combine(e.FoundPath, e.QuestEndTitleCard);
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
                PenumbraAndGlamourerIpcWrapper.Instance.SetCollectionForObject.Invoke((int)201, Guid.Empty);
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
                        while (Plugin.ObjectTable.LocalPlayer == null || _actorSpawnService == null)
                        {
                            Thread.Sleep(3000);
                        }
                        _triggerRefresh = true;
                        _gotZoneDiscriminator = false;
                        _checkForPartyMembers = true;
                        _cutsceneNpcSpawned = false;
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
        public unsafe void RefreshMapMarkers()
        {
            try
            {
                if (Plugin.ClientState.IsLoggedIn && !Conditions.Instance()->BetweenAreas)
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
            Plugin.Framework.RunOnFrameworkThread(() =>
            {
                var collection = PenumbraAndGlamourerIpcWrapper.Instance.GetCollectionForObject.Invoke((int)Plugin.ObjectTable.LocalPlayer.ObjectIndex);
                PenumbraAndGlamourerIpcWrapper.Instance.SetCollectionForObject.Invoke((int)_cutscenePlayer.Character.ObjectIndex, collection.EffectiveCollection.Id);
                PenumbraAndGlamourerIpcWrapper.Instance.RedrawObject.Invoke((int)_cutscenePlayer.Character.ObjectIndex, Penumbra.Api.Enums.RedrawType.Redraw);
                var design = PenumbraAndGlamourerIpcWrapper.Instance.GetStateBase64.Invoke(Plugin.ObjectTable.LocalPlayer.ObjectIndex);
                AppearanceAccessUtils.AppearanceManager.LoadAppearance(design.Item2, _cutscenePlayer.Character, 0);
            });
        }
        private unsafe void _framework_Update(IFramework framework)
        {
            if (!_disposed)
            {
                try
                {
                    if (!Plugin.ClientState.IsGPosing && !Plugin.ClientState.IsPvPExcludingDen && !Conditions.Instance()->BetweenAreas && !Conditions.Instance()->WatchingCutscene
                        && !Conditions.Instance()->Occupied && !Conditions.Instance()->InCombat && Plugin.ClientState.IsLoggedIn)
                    {
                        // Hopefully waiting prevents crashing on zone changes?
                        if (zoneChangeCooldown.ElapsedMilliseconds > 500)
                        {
                            if (!_isInitialized)
                            {
                                CheckInitialization();
                            }
                            else
                            {
                                if (!_cutsceneNpcSpawned)
                                {
                                    // This NPC has to be sacrificed for object id 200. Object id 200 is hated by everything and wont do appearance changes.
                                    ICharacter character = null;
                                    _actorSpawnService.CreateCharacter(out character, SpawnFlags.DefinePosition, true,
                                    (new Vector3(0, float.MaxValue, 0) / 10), CoordinateUtility.ConvertDegreesToRadians(0));

                                    Task.Run(() =>
                                    {
                                        Thread.Sleep(1000);
                                        Plugin.Framework.RunOnFrameworkThread(() =>
                                        {
                                            _actorSpawnService.CreateCharacter(out character, SpawnFlags.DefinePosition, true,
                                            (new Vector3(0, float.MaxValue, 0) / 10), CoordinateUtility.ConvertDegreesToRadians(0));
                                            _cutscenePlayer = new InteractiveNpc(Plugin, character);
                                        });
                                    });
                                    _cutsceneNpcSpawned = true;
                                }
                                else
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
                _passiveObjectiveRefreshTimer.Restart();
            }
            if (!_passiveObjectiveRefreshTimer.IsRunning)
            {
                _passiveObjectiveRefreshTimer.Start();
            }
        }

        private void CheckForPlayerAppearance()
        {
            PlayerAppearanceData = AppearanceHelper.GetCustomization(Plugin.ObjectTable.LocalPlayer);
            PlayerClassJob = Plugin.ObjectTable.LocalPlayer.ClassJob.Value.Abbreviation.Data.ToString();
            Plugin.ObjectTable.LocalPlayer.ClassJob.Value.Abbreviation.Data.ToString();
            if (!_waitingForAppearanceLoad && !AppearanceAccessUtils.AppearanceManager.IsWorking() && !_hasCheckedForPlayerAppearance)
            {
                _hasCheckedForPlayerAppearance = true;
                var appearance = Plugin.RoleplayingQuestManager.GetPlayerAppearanceForZone(Plugin.ClientState.TerritoryType, _discriminator);
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
                        if (!_waitingForAppearanceLoad && !AppearanceAccessUtils.AppearanceManager.IsWorking())
                        {
                            var value = _npcActorSpawnQueue.Dequeue();
                            bool newNPC = !value.Item5;
                            if (!string.IsNullOrEmpty(value.Item3) && !value.Item3.Contains("none"))
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
                                    if (_interactiveNpcDictionary[value.Item2].LastAppearance != value.Item3
                                    || Plugin.RoleplayingQuestManager.QuestProgression[value.Item6.QuestId] == 0)
                                    {
                                        LoadAppearance(value.Item3, AppearanceSwapType.EntireAppearance, character);
                                        _interactiveNpcDictionary[value.Item2].LastAppearance = value.Item3;
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
                if (_waitingForAppearanceLoad && _mcdfRefreshTimer.ElapsedMilliseconds > 500 && !AppearanceAccessUtils.AppearanceManager.IsWorking())
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
                                        string[] appearanceItems = npcAppearance.Value.AppearanceData.StringToArray();
                                        for (int i = 0; i < appearanceItems.Length; i++)
                                        {
                                            if (appearanceItems[i].Contains(".chara") || appearanceItems[i].Contains(".mcdf"))
                                            {
                                                appearanceItems[i] = Path.Combine(foundPath, appearanceItems[i].Trim());
                                            }
                                        }
                                        string customNpcAppearancePath = appearanceItems.ArrayToString();
                                        var value = new Tuple<Transform, string, string, Dictionary<string, ICharacter>, bool, RoleplayingQuest, bool>
                                        (startingInfo, npcAppearance.Value.NpcName, Path.Combine(foundPath, customNpcAppearancePath), spawnedNpcsList, foundExistingNPC, item.Item3, false);
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
