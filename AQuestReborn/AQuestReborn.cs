using ArtemisRoleplayingKit;
using Brio.Game.Actor;
using Brio.IPC;
using Controller_Wrapper;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.Toast;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DualSenseAPI;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using RoleplayingMediaCore;
using RoleplayingQuestCore;
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

namespace AQuestReborn
{
    internal class AQuestReborn
    {
        public Plugin Plugin { get; }
        public Dictionary<string, ICharacter> SpawnedNPCs { get => _spawnedNPCs; set => _spawnedNPCs = value; }
        private DualSense _dualSense;
        private Controller xboxController;
        private Stopwatch _pollingTimer;
        private Stopwatch _controllerCheckTimer;
        private Stopwatch _mcdfRefreshTimer = new Stopwatch();
        private bool _screenButtonClicked;
        private Dictionary<string, ICharacter> _spawnedNPCs = new Dictionary<string, ICharacter>();
        private bool _triggerRefresh;
        private bool _waitingForSelectionRelease;
        Queue<KeyValuePair<string, ICharacter>> _mcdfQueue = new Queue<KeyValuePair<string, ICharacter>>();
        private ActorSpawnService _actorSpawnService;
        private MareService _mcdfService;
        private MediaGameObject _playerObject;
        private unsafe Camera* _camera;
        private MediaCameraObject _playerCamera;

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
            Task.Run(() =>
            {
                while (Brio.Brio._services == null)
                {
                    Thread.Sleep(100);
                }
                Brio.Brio.TryGetService<ActorSpawnService>(out _actorSpawnService);
                Brio.Brio.TryGetService<MareService>(out _mcdfService);
            });
            _mcdfRefreshTimer.Start();
            Plugin.EmoteReaderHook.OnEmote += (instigator, emoteId) => OnEmote(instigator as ICharacter, emoteId);
            if (Plugin.ClientState.IsLoggedIn)
            {
                InitializeMediaManager();
                _triggerRefresh = true;
            }
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
                while (Plugin.ClientState.LocalPlayer == null)
                {
                    Thread.Sleep(1000);
                }
                _triggerRefresh = true;
            });
        }

        private void _clientState_Login()
        {
            if (Plugin.ClientState.IsLoggedIn)
            {
                InitializeMediaManager();
                RefreshNPCs(Plugin.ClientState.TerritoryType);
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
            CheckForNPCRefresh();
            ControllerLogic();
            QuestInputCheck();
        }

        private void CheckForNewMCDFLoad()
        {
            if (_mcdfQueue.Count > 0 && _mcdfRefreshTimer.ElapsedMilliseconds > 500)
            {
                var item = _mcdfQueue.Dequeue();
                _mcdfService.LoadMcdfAsync(item.Key, item.Value);
                _mcdfRefreshTimer.Restart();
            }
        }

        private void CheckForNPCRefresh()
        {
            if (_triggerRefresh)
            {
                RefreshNPCs(Plugin.ClientState.TerritoryType);
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
        public void DestroyAllNpcs()
        {
            foreach (var item in _spawnedNPCs)
            {
                if (item.Value != null)
                {
                    _actorSpawnService.DestroyObject(item.Value);
                }
            }
            _spawnedNPCs.Clear();
        }
        public void RefreshNPCs(ushort territoryId, bool softRefresh = false)
        {
            if (!softRefresh)
            {
                DestroyAllNpcs();
            }
            if (_actorSpawnService != null)
            {
                if (!softRefresh)
                {
                    _actorSpawnService.DestroyAllCreated();
                }
                if (!_spawnedNPCs.ContainsKey("First Spawn") || _spawnedNPCs.Count == 0)
                {
                    ICharacter firstSpawn = null;
                    _actorSpawnService.CreateCharacter(out firstSpawn, SpawnFlags.DefinePosition, true,
                    new System.Numerics.Vector3(float.MaxValue / 2, float.MaxValue / 2, float.MaxValue / 2), 0);
                    _spawnedNPCs["First Spawn"] = firstSpawn;
                }

                var questChains = Plugin.RoleplayingQuestManager.GetActiveQuestChainObjectives(territoryId);
                foreach (var item in questChains)
                {
                    string foundPath = item.Key.FoundPath;
                    foreach (var npcAppearance in item.Key.NpcCustomizations)
                    {
                        if (item.Value.Item2.NpcStartingPositions.ContainsKey(npcAppearance.Value.NpcName))
                        {
                            if (_spawnedNPCs.ContainsKey(npcAppearance.Value.NpcName))
                            {
                                var npc = _spawnedNPCs[npcAppearance.Value.NpcName];
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
                                _spawnedNPCs[npcAppearance.Value.NpcName] = character;
                                _mcdfQueue.Enqueue(new KeyValuePair<string, ICharacter>(Path.Combine(foundPath, npcAppearance.Value.AppearanceData), character));
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
