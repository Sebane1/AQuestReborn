using Anamnesis.Memory;
using ArtemisRoleplayingKit;
using Brio.Game.Actor;
using Brio.IPC;
using Controller_Wrapper;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Config;
using Dalamud.Game.Gui.Toast;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DualSenseAPI;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using NAudio.Lame;
using RoleplayingMediaCore;
using RoleplayingQuestCore;
using RoleplayingVoiceCore;
using RoleplayingVoiceDalamudWrapper;
using SamplePlugin;
using SamplePlugin.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static FFXIVClientStructs.FFXIV.Client.LayoutEngine.LayoutManager;
using Utf8String = FFXIVClientStructs.FFXIV.Client.System.String.Utf8String;

namespace AQuestReborn
{
    internal class AQuestReborn
    {
        public Plugin Plugin { get; }
        public Dictionary<string, Dictionary<string, ICharacter>> SpawnedNPCs { get => _spawnedNPCs; set => _spawnedNPCs = value; }
        private DualSense _dualSense;
        private Controller xboxController;
        private Stopwatch _pollingTimer;
        private Stopwatch _controllerCheckTimer;
        private Stopwatch _mcdfRefreshTimer = new Stopwatch();
        private bool _screenButtonClicked;
        private Dictionary<string, Dictionary<string, ICharacter>> _spawnedNPCs = new Dictionary<string, Dictionary<string, ICharacter>>();
        private bool _triggerRefresh;
        private bool _waitingForSelectionRelease;
        Queue<KeyValuePair<string, ICharacter>> _mcdfQueue = new Queue<KeyValuePair<string, ICharacter>>();
        private ActorSpawnService _actorSpawnService;
        private MareService _mcdfService;
        private MediaGameObject _playerObject;
        private unsafe Camera* _camera;
        private MediaCameraObject _playerCamera;
        private Dictionary<RoleplayingQuest, Tuple<int, QuestObjective>> _activeQuestChainObjectives;

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
            Plugin.Framework.Update += _framework_Update;
            Plugin.ClientState.Login += _clientState_Login;
            Plugin.ClientState.TerritoryChanged += _clientState_TerritoryChanged;
            _pollingTimer = new Stopwatch();
            _pollingTimer.Start();
            _controllerCheckTimer = new Stopwatch();
            _controllerCheckTimer.Start();
            _mcdfRefreshTimer.Start();
            Plugin.EmoteReaderHook.OnEmote += (instigator, emoteId) => OnEmote(instigator as ICharacter, emoteId);
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
                }
            });
        }
        private void OnEmote(ICharacter character, ushort emoteId)
        {
            if (!Plugin.DialogueWindow.IsOpen && !Plugin.ChoiceWindow.IsOpen)
            {
                Plugin.RoleplayingQuestManager.ProgressTriggerQuestObjective(QuestObjective.ObjectiveTriggerType.DoEmote, emoteId.ToString());
            }
        }

        private void _clientState_TerritoryChanged(ushort territory)
        {
            Task.Run(() =>
            {
                while (Plugin.ClientState.LocalPlayer == null || _actorSpawnService == null)
                {
                    Thread.Sleep(3000);
                }
                _triggerRefresh = true;
            });
        }
        public void RefreshMapMarkers()
        {
            _activeQuestChainObjectives = Plugin.RoleplayingQuestManager.GetActiveQuestChainObjectives(Plugin.ClientState.TerritoryType);
            unsafe
            {
                AgentMap.Instance()->ResetMapMarkers();
                AgentMap.Instance()->ResetMiniMapMarkers();
                foreach (var item in _activeQuestChainObjectives)
                {
                    Utf8String* stringBuffer = Utf8String.CreateEmpty();
                    stringBuffer->SetString(item.Key.QuestName);
                    if (item.Value.Item1 == 0)
                    {
                        AgentMap.Instance()->AddMapMarker(item.Value.Item2.Coordinates, 230604, 0, stringBuffer->StringPtr);
                        AgentMap.Instance()->AddMiniMapMarker(item.Value.Item2.Coordinates, 230604);
                    }
                    else
                    {
                        AgentMap.Instance()->AddMapMarker(item.Value.Item2.Coordinates, 230605, 0, stringBuffer->StringPtr);
                        AgentMap.Instance()->AddMiniMapMarker(item.Value.Item2.Coordinates, 230605);
                    }
                }
            }
        }
        private void _clientState_Login()
        {
            if (Plugin.ClientState.IsLoggedIn)
            {
                InitializeMediaManager();
                RefreshNpcsForQuest(Plugin.ClientState.TerritoryType);
            }
        }

        public unsafe void InitializeMediaManager()
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

        private void _framework_Update(IFramework framework)
        {
            CheckForNewMCDFLoad();
            ControllerLogic();
            QuestInputCheck();
            CheckForNPCRefresh();
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
        private void CheckForNewMCDFLoad()
        {
            if (_mcdfQueue.Count > 0 && _mcdfRefreshTimer.ElapsedMilliseconds > 100)
            {
                _mcdfRefreshTimer.Reset();
                var item = _mcdfQueue.Dequeue();

                if (!_mcdfService.LoadMcdfAsync(item.Key, item.Value))
                {
                    _mcdfQueue.Enqueue(item);
                }
                _mcdfRefreshTimer.Restart();
            }
        }

        private void CheckForNPCRefresh()
        {
            if (_triggerRefresh)
            {
                RefreshMapMarkers();
                RefreshNpcsForQuest(Plugin.ClientState.TerritoryType);
                _triggerRefresh = false;
            }
        }

        private void ControllerLogic()
        {
            if (_controllerCheckTimer.ElapsedMilliseconds > 5000)
            {
                ControllerConnectionCheck();
                _controllerCheckTimer.Restart();
            }
        }

        private void QuestInputCheck()
        {
            if (_pollingTimer.ElapsedMilliseconds > 100)
            {
                Plugin.ObjectiveWindow.IsOpen = true;
                if ((CheckXboxInput() || CheckDualsenseInput() || _screenButtonClicked))
                {
                    _screenButtonClicked = false;
                    if (!_waitingForSelectionRelease)
                    {
                        if (!Plugin.DialogueWindow.IsOpen && !Plugin.ChoiceWindow.IsOpen)
                        {
                            Plugin.RoleplayingQuestManager.ProgressTriggerQuestObjective();
                        }
                        else
                        {
                            Plugin.DialogueWindow.NextText();
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

        private bool CheckXboxInput()
        {
            if (xboxController != null)
            {
                return xboxController.A;
            }
            return false;
        }

        private void ControllerConnectionCheck()
        {
            if (xboxController == null)
            {
                Task.Run(() =>
                {
                    var controllers = Controller_Wrapper.Controller.GetConnectedControllers();
                    if (controllers.Length > 0)
                    {
                        xboxController = controllers[0];
                    }
                });
            }
            if (_dualSense == null)
            {
                Task.Run(() =>
                {
                    var controllers = DualSense.EnumerateControllers();
                    if (controllers.Count() > 0)
                    {
                        _dualSense = controllers.First();
                        _dualSense.Acquire();
                        _dualSense.BeginPolling(20);
                    }
                });
            }
        }
        private bool CheckDualsenseInput()
        {
            if (_dualSense != null)
            {
                return _dualSense.InputState.CrossButton;
            }
            return false;
        }
        public void DestroyAllNpcsInQuestId(string questId)
        {
            if (_spawnedNPCs.ContainsKey(questId))
            {
                int sleepTime = 100;
                foreach (var item in _spawnedNPCs[questId])
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
                _spawnedNPCs[questId].Clear();
            }
        }
        public void RefreshNpcsForQuest(ushort territoryId, string questId = "", bool softRefresh = false)
        {
            if (_actorSpawnService != null)
            {
                if (!_spawnedNPCs.ContainsKey("DEBUG"))
                {
                    _spawnedNPCs["DEBUG"] = new Dictionary<string, ICharacter>();
                }
                if (!softRefresh)
                {
                    if (!string.IsNullOrEmpty(questId))
                    {
                        DestroyAllNpcsInQuestId("DEBUG");
                        DestroyAllNpcsInQuestId(questId);
                    }
                    else
                    {
                        DestroyAllNpcsInQuestId("DEBUG");
                        foreach (var item in Plugin.RoleplayingQuestManager.QuestChains)
                        {
                            DestroyAllNpcsInQuestId(item.Value.QuestId);
                        }
                        try
                        {
                            _actorSpawnService.DestroyAllCreated();
                        }
                        catch { }
                    }
                }
                if (!_spawnedNPCs["DEBUG"].ContainsKey("First Spawn") || _spawnedNPCs["DEBUG"].Count == 0)
                {
                    ICharacter firstSpawn = null;
                    _actorSpawnService.CreateCharacter(out firstSpawn, SpawnFlags.DefinePosition, true,
                    new System.Numerics.Vector3(float.MaxValue / 2, float.MaxValue / 2, float.MaxValue / 2), 0);
                    _spawnedNPCs["DEBUG"]["First Spawn"] = firstSpawn;
                }

                var questChains = Plugin.RoleplayingQuestManager.GetActiveQuestChainObjectives(territoryId);
                foreach (var item in questChains)
                {
                    if (item.Key.QuestId == questId || string.IsNullOrEmpty(questId))
                    {
                        string foundPath = item.Key.FoundPath;
                        foreach (var npcAppearance in item.Key.NpcCustomizations)
                        {
                            if (item.Value.Item2.NpcStartingPositions.ContainsKey(npcAppearance.Value.NpcName))
                            {
                                if (!_spawnedNPCs.ContainsKey(item.Key.QuestId))
                                {
                                    _spawnedNPCs[item.Key.QuestId] = new Dictionary<string, ICharacter>();
                                }
                                var spawnedNpcsList = _spawnedNPCs[item.Key.QuestId];
                                if (spawnedNpcsList.ContainsKey(npcAppearance.Value.NpcName))
                                {
                                    var npc = spawnedNpcsList[npcAppearance.Value.NpcName];
                                    if (npc != null)
                                    {
                                        try
                                        {
                                            _actorSpawnService.DestroyObject(npc);
                                        }
                                        catch (Exception e)
                                        {
                                            Plugin.PluginLog.Warning(e, e.Message);
                                        }
                                    }
                                }
                                ICharacter character = null;
                                var startingInfo = item.Value.Item2.NpcStartingPositions[npcAppearance.Value.NpcName];
                                _actorSpawnService.CreateCharacter(out character, SpawnFlags.DefinePosition, true, startingInfo.Position, Utility.ConvertDegreesToRadians(startingInfo.EulerRotation.Y));
                                if (character != null)
                                {
                                    spawnedNpcsList[npcAppearance.Value.NpcName] = character;
                                    _mcdfQueue.Enqueue(new KeyValuePair<string, ICharacter>(Path.Combine(foundPath, npcAppearance.Value.AppearanceData), character));
                                }
                            }
                        }
                    }
                }
            }
        }
        private void _roleplayingQuestManager_OnQuestAcceptancePopup(object? sender, RoleplayingQuest e)
        {
            Plugin.QuestAcceptanceWindow.PromptQuest(e);
        }

        private void QuestAcceptanceWindow_OnQuestAccepted(object? sender, EventArgs e)
        {
            Plugin.RoleplayingQuestManager.ProgressTriggerQuestObjective();
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

        private void _roleplayingQuestManager_OnQuestCompleted(object? sender, EventArgs e)
        {
            QuestToastOptions questToastOptions = new QuestToastOptions();
            Plugin.ToastGui.ShowQuest("Quest Completed");
            Plugin.Configuration.Save();
        }

        private void _roleplayingQuestManager_OnQuestStarted(object? sender, EventArgs e)
        {
            Plugin.ToastGui.ShowQuest("Quest Started");
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
            if (_dualSense != null)
            {
                _dualSense.EndPolling();
                _dualSense.Release();
            }
        }
    }
}
