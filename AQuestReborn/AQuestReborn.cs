using Brio.Game.Actor;
using Brio.IPC;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Config;
using Dalamud.Game.Gui.Toast;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using RoleplayingMediaCore;
using RoleplayingQuestCore;
using RoleplayingVoiceDalamudWrapper;
using SamplePlugin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private Stopwatch _pollingTimer;
        private Stopwatch _inputCooldown;
        private Stopwatch _mcdfRefreshTimer = new Stopwatch();
        private Stopwatch _mapRefreshTimer = new Stopwatch();
        private bool _screenButtonClicked;
        private Dictionary<string, Dictionary<string, ICharacter>> _spawnedNPCsDictionary = new Dictionary<string, Dictionary<string, ICharacter>>();
        private bool _triggerRefresh;
        private bool _waitingForSelectionRelease;
        Queue<KeyValuePair<string, ICharacter>> _mcdfQueue = new Queue<KeyValuePair<string, ICharacter>>();
        private ActorSpawnService _actorSpawnService;
        private MareService _mcdfService;
        private MediaGameObject _playerObject;
        private unsafe Camera* _camera;
        private MediaCameraObject _playerCamera;
        private List<Tuple<int, QuestObjective, RoleplayingQuest>> _activeQuestChainObjectives;

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
            _pollingTimer = new Stopwatch();
            _pollingTimer.Start();
            _inputCooldown = new Stopwatch();
            _inputCooldown.Start();
            _mcdfRefreshTimer.Start();
            _mapRefreshTimer.Start();
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

        private void ChatGui_ChatMessage(Dalamud.Game.Text.XivChatType type, int timestamp, ref Dalamud.Game.Text.SeStringHandling.SeString sender, ref Dalamud.Game.Text.SeStringHandling.SeString message, ref bool isHandled)
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

        private void RewardWindow_OnRewardClosed(object? sender, EventArgs e)
        {
            QuestToastOptions questToastOptions = new QuestToastOptions();
            Plugin.ToastGui.ShowQuest("Quest Completed");
            Plugin.Configuration.Save();
        }

        private void OnEmote(ICharacter character, ushort emoteId)
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
            _activeQuestChainObjectives = Plugin.RoleplayingQuestManager.GetActiveQuestChainObjectivesInZone(Plugin.ClientState.TerritoryType);
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
            if (!Plugin.ClientState.IsGPosing && !Plugin.ClientState.IsPvPExcludingDen && !Conditions.IsInBetweenAreas && !Conditions.IsWatchingCutscene
                && !Conditions.IsOccupied && !Conditions.IsInCombat && Plugin.ClientState.IsLoggedIn)
            {
                CheckForNewMCDFLoad();
                QuestInputCheck();
                CheckForNPCRefresh();
                CheckForMapRefresh();
            }
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
        private void CheckForNewMCDFLoad()
        {
            if (_mcdfQueue.Count > 0)
            {
                if (_mcdfRefreshTimer.ElapsedMilliseconds > 100)
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
        public void RefreshNpcsForQuest(ushort territoryId, string questId = "", bool softRefresh = false)
        {
            if (_actorSpawnService != null)
            {
                if (!_spawnedNPCsDictionary.ContainsKey("DEBUG"))
                {
                    _spawnedNPCsDictionary["DEBUG"] = new Dictionary<string, ICharacter>();
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
                if (!_spawnedNPCsDictionary["DEBUG"].ContainsKey("First Spawn") || _spawnedNPCsDictionary["DEBUG"].Count == 0)
                {
                    ICharacter firstSpawn = null;
                    _actorSpawnService.CreateCharacter(out firstSpawn, SpawnFlags.DefinePosition, true,
                    new System.Numerics.Vector3(float.MaxValue / 2, float.MaxValue / 2, float.MaxValue / 2), 0);
                    _spawnedNPCsDictionary["DEBUG"]["First Spawn"] = firstSpawn;
                }

                var questChains = Plugin.RoleplayingQuestManager.GetActiveQuestChainObjectivesInZone(territoryId);
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
                                var startingInfo = item.Item2.NpcStartingPositions[npcAppearance.Value.NpcName];
                                _actorSpawnService.CreateCharacter(out character, SpawnFlags.DefinePosition, true, startingInfo.Position, Utility.ConvertDegreesToRadians(startingInfo.EulerRotation.Y));
                                if (character != null)
                                {
                                    spawnedNpcsList[npcAppearance.Value.NpcName] = character;
                                    _mcdfQueue.Enqueue(new KeyValuePair<string, ICharacter>(Path.Combine(foundPath, npcAppearance.Value.AppearanceData), character));
                                    Plugin.AnamcoreManager.TriggerEmote(character.Address, (ushort)startingInfo.DefaultAnimationId);
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
            _actorSpawnService.TargetService.GPoseTarget = null;
        }
    }
}
