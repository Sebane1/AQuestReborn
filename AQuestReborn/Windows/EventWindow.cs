using AQuestReborn;
using AQuestReborn.UiHide;
using Dalamud.Game.Config;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using FFXIVLooseTextureCompiler.ImageProcessing;
using Dalamud.Bindings.ImGui;
using McdfDataImporter;
using RoleplayingQuestCore;
using RoleplayingVoiceDalamudWrapper;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace SamplePlugin.Windows;

public class EventWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;
    QuestDisplayObject _questDisplayObject;
    int _index = 0;
    private int _objectiveSkipValue;
    private bool _blockProgression;
    private bool _settingNewText;
    int _currentCharacter = 0;
    string _targetText = "";
    string _currentText = "";
    string _currentName = "";
    Stopwatch textTimer = new Stopwatch();
    private bool _choicesAreNext;
    private DummyObject _dummyObject;
    List<byte[]> _dialogueBoxStyles = new List<byte[]>();
    int _currentDialogueBoxIndex = 0;
    private string _npcAppearanceSwap;
    private string _playerAppearanceSwap;
    private QuestEvent.AppearanceSwapType _playerAppearanceSwapType;
    private bool _playerAppearanceSwapAffectsRacial;
    private string _lastNpcName;
    private bool _alreadyLoadingFrame;
    private ConcurrentDictionary<int, IDalamudTextureWrap> _dialogueStylesToLoad = new ConcurrentDictionary<int, IDalamudTextureWrap>();
    private IDalamudTextureWrap _dialogueTitleStyleToLoad;
    private byte[] _lastLoadedTitleFrame;
    private byte[] _lastLoadedFrame;
    private byte[] _nameTitleStyle;
    private bool _alreadyLoadingTitleFrame;
    private Bitmap data1;
    private float _globalScale;
    private bool _objectiveSkip;
    private bool _dontUnblockMovement;
    private bool _questFollowing;
    private bool _questStopFollowing;
    Stopwatch _timeSinceLastDialogueDisplayed = new Stopwatch();
    private bool _previousEventHasNoReading;
    private bool _dialogueWindowIsHidden;
    private DummyObject _backgroundMusic;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public EventWindow(Plugin plugin)
        : base("Dialogue Window##dialoguewindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBackground, true)
    {
        Size = new Vector2(1088, 288);
        Plugin = plugin;
        plugin.ChoiceWindow.OnChoiceMade += ChoiceWindow_OnChoiceMade;
        _dummyObject = new DummyObject();
        LoadBubbleBackgrounds();
        _timeSinceLastDialogueDisplayed.Start();
        _backgroundMusic = new DummyObject() { Name = "BackgroundMusic" };
    }
    public override void OnClose()
    {
        if (!_dontUnblockMovement)
        {
            _dontUnblockMovement = false;
            Plugin.Movement.DisableMovementLock();
            Plugin.DialogueBackgroundWindow.IsOpen = false;
        }
        _timeSinceLastDialogueDisplayed.Restart();
        base.OnClose();
    }
    public override void OnOpen()
    {
        if (_questDisplayObject.QuestObjective.PlayerPositionIsLockedDuringEvents)
        {
            Plugin.Movement.EnableMovementLock();
        }
        Plugin.AQuestReborn.RefreshPlaceHolderCutscenePlayer();
        base.OnOpen();
    }

    public byte[] ImageToBytes(Bitmap image)
    {
        MemoryStream memoryStream = new MemoryStream();
        image.Save(memoryStream, ImageFormat.Png);
        memoryStream.Position = 0;
        return memoryStream.ToArray();
    }
    public void LoadBubbleBackgrounds()
    {
        // Dialogue name background
        data1 = ImageManipulation.Crop(TexIO.TexToBitmap(new MemoryStream(Plugin.DataManager.GetFile("ui/uld/talk_hr1.tex").Data)), new Vector2(575, 72));
        // First 3 talk bubbles.
        var data2 = TexIO.TexToBitmap(new MemoryStream(Plugin.DataManager.GetFile("ui/uld/talk_basic_hr1.tex").Data));
        // Next 6 talk bubbles
        var data3 = TexIO.TexToBitmap(new MemoryStream(Plugin.DataManager.GetFile("ui/uld/talk_other_hr1.tex").Data));
        _nameTitleStyle = ImageToBytes(data1);
        foreach (var item in ImageManipulation.DivideImageVertically(data2, 3))
        {
            _dialogueBoxStyles.Add(ImageToBytes(item));
        }
        foreach (var item in ImageManipulation.DivideImageVertically(data3, 6))
        {
            _dialogueBoxStyles.Add(ImageToBytes(item));
        }
    }
    private void ChoiceWindow_OnChoiceMade(object? sender, int e)
    {
        IsOpen = true;
        var questText = _questDisplayObject.QuestObjective.QuestText[_index];
        if (questText.BranchingChoices.Count > 0)
        {
            if (e < questText.BranchingChoices.Count)
            {
                var branchingChoice = questText.BranchingChoices[e];
                switch (branchingChoice.ChoiceType)
                {
                    case BranchingChoice.BranchingChoiceType.SkipToEventNumber:
                        SetEvent(branchingChoice.EventToJumpTo);
                        break;
                    case BranchingChoice.BranchingChoiceType.BranchingQuestline:
                        Plugin.RoleplayingQuestManager.ReplaceQuest(branchingChoice.RoleplayingQuest);
                        break;
                    case BranchingChoice.BranchingChoiceType.RollD20ThenSkipToEventNumber:
                        var roll = new Random().Next(0, 20);
                        if (roll >= branchingChoice.MinimumDiceRoll)
                        {
                            SetEvent(branchingChoice.EventToJumpTo);
                            Task.Run(async () =>
                            {
                                var toast = await Translator.LocalizeText("You roll a " + roll + "/" + branchingChoice.MinimumDiceRoll + " and succeed.", Plugin.Configuration.QuestLanguage, _questDisplayObject.RoleplayingQuest.QuestLanguage);

                                Plugin.Framework.RunOnFrameworkThread(() =>
                                {
                                    Plugin.ToastGui.ShowNormal(toast);
                                    _lastNpcName = "";
                                    _questFollowing = false;
                                });
                            });
                        }
                        else
                        {
                            SetEvent(branchingChoice.EventToJumpToFailure);
                            Task.Run(async () =>
                            {
                                var toast = await Translator.LocalizeText("You roll a " + roll + "/" + branchingChoice.MinimumDiceRoll + " and fail.", Plugin.Configuration.QuestLanguage, _questDisplayObject.RoleplayingQuest.QuestLanguage);

                                Plugin.Framework.RunOnFrameworkThread(() =>
                                {
                                    Plugin.ToastGui.ShowNormal(toast);
                                    _lastNpcName = "";
                                    _questFollowing = false;
                                });
                            });
                        }
                        break;
                    case BranchingChoice.BranchingChoiceType.SkipToEventNumberRandomized:
                        roll = new Random().Next(0, branchingChoice.RandomizedEventToSkipTo.Count);
                        SetEvent(branchingChoice.RandomizedEventToSkipTo[roll]);
                        break;
                }
            }
        }
    }

    public QuestDisplayObject QuestTexts { get => _questDisplayObject; set => _questDisplayObject = value; }
    internal DummyObject DummyObject { get => _dummyObject; set => _dummyObject = value; }
    public Stopwatch TimeSinceLastDialogueDisplayed { get => _timeSinceLastDialogueDisplayed; set => _timeSinceLastDialogueDisplayed = value; }

    public void Dispose() { }

    public override void Draw()
    {
        if (!_dialogueWindowIsHidden)
        {
            _globalScale = ImGuiHelpers.GlobalScale * 0.95f;
            var values = ImGui.GetIO().DisplaySize;
            Size = new Vector2(1088 * _globalScale, 288 * _globalScale);
            Position = new Vector2((values.X / 2) - (Size.Value.X / 2), values.Y - Size.Value.Y);
            if (!_alreadyLoadingFrame)
            {
                Task.Run(async () =>
                {
                    for (int i = 0; i < _dialogueBoxStyles.Count; i++)
                    {
                        if (!_dialogueStylesToLoad.ContainsKey(i) || _dialogueStylesToLoad[i] == null)
                        {
                            _dialogueStylesToLoad[i] = await Plugin.TextureProvider.CreateFromImageAsync(_dialogueBoxStyles[i]);
                        }
                    }
                    _alreadyLoadingFrame = false;
                });
            }
            if (_dialogueStylesToLoad.ContainsKey(_currentDialogueBoxIndex) && _dialogueStylesToLoad[_currentDialogueBoxIndex] != null)
            {
                ImGui.Image(_dialogueStylesToLoad[_currentDialogueBoxIndex].Handle, new Vector2(Size.Value.X, Size.Value.Y));
            }

            if (_currentName.ToLower() != "system")
            {
                if (!_alreadyLoadingTitleFrame)
                {
                    Task.Run(async () =>
                    {
                        _alreadyLoadingTitleFrame = true;
                        if (_lastLoadedFrame != _nameTitleStyle)
                        {
                            _dialogueTitleStyleToLoad = await Plugin.TextureProvider.CreateFromImageAsync(_nameTitleStyle);
                            _lastLoadedTitleFrame = _nameTitleStyle;
                        }
                        _alreadyLoadingTitleFrame = false;
                    });
                }
                if (_dialogueTitleStyleToLoad != null)
                {
                    ImGui.SetCursorPos(new Vector2(50 * _globalScale, 8 * _globalScale));
                    ImGui.Image(_dialogueTitleStyleToLoad.Handle, new Vector2(data1.Width * _globalScale, data1.Height * _globalScale));
                }
            }
            ImGui.SetCursorPos(new Vector2(0, 0));
            ImGui.BeginTable("##Dialogue Table", 3);
            ImGui.TableSetupColumn("Padding 1", ImGuiTableColumnFlags.WidthFixed, 100 * _globalScale);
            ImGui.TableSetupColumn("Center", ImGuiTableColumnFlags.WidthFixed, 888 * _globalScale);
            ImGui.TableSetupColumn("Padding 2", ImGuiTableColumnFlags.WidthFixed, 100 * _globalScale);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);

            ImGui.TableSetColumnIndex(1);
            DialogueDrawing();
            ImGui.TableSetColumnIndex(2);

            ImGui.EndTable();
        }
    }

    private void DialogueDrawing()
    {
        ImGui.SetCursorPosY(22 * _globalScale);
        ImGui.SetWindowFontScale(2.2f);
        ImGui.LabelText("##nameLabel", _currentName.ToLower() == "system" ? "" : _currentName);
        ImGui.SetWindowFontScale(2);
        ImGui.SetCursorPosY(75 * _globalScale);
        if (_currentDialogueBoxIndex != 8 && _currentDialogueBoxIndex != 2 && _currentDialogueBoxIndex != 3)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 0, 0, 255));
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(255, 255, 255, 255));
        }
        ImGui.TextWrapped(_currentText);
        ImGui.PopStyleColor();
    }

    public void NewText(QuestDisplayObject newQuestText)
    {
        _settingNewText = true;
        _currentCharacter = 0;
        _questDisplayObject = newQuestText;
        SetEvent(0);
        textTimer.Restart();
        Plugin.SaveProgress();
        _settingNewText = false;
        Plugin.DialogueBackgroundWindow.PreCacheImages(newQuestText);
    }

    public void NextEvent(bool bypassBranchingChoice = false)
    {
        if ((!Plugin.ChoiceWindow.IsOpen && !_settingNewText) || _previousEventHasNoReading)
        {
            if (_questDisplayObject != null)
            {
                if (_choicesAreNext)
                {
                    _dontUnblockMovement = true;
                    IsOpen = false;
                    Plugin.DialogueBackgroundWindow.IsOpen = false;
                    var values = _questDisplayObject.QuestObjective.QuestText[_index].BranchingChoices;
                    if (values.Count > 0)
                    {
                        Plugin.ChoiceWindow.NewList(values, _questDisplayObject.RoleplayingQuest.QuestLanguage);
                        _choicesAreNext = false;
                    }
                }
                else
                {
                    SetEvent(_index);
                }
            }
            _previousEventHasNoReading = false;
        }
    }
    public void SetEvent(int index)
    {
        _index = index;
        bool allowedToContinue = true;
        Plugin.MediaManager.StopAudio(AQuestReborn.AQuestReborn.PlayerObject);
        Plugin.DialogueBackgroundWindow.ClearBackground();
        if (_index < _questDisplayObject.QuestObjective.QuestText.Count)
        {
            var item = _questDisplayObject.QuestObjective.QuestText[_index];

            var customization = AQuestReborn.AQuestReborn.PlayerAppearanceData;
            switch (item.ConditionForDialogueToOccur)
            {
                case QuestEvent.EventConditionType.CompletedSpecificObjectiveId:
                    if (!Plugin.RoleplayingQuestManager.CompletedObjectiveExists(item.ObjectiveIdToComplete))
                    {
                        SetEvent(index + 1);
                        allowedToContinue = false;
                    }
                    break;
                case QuestEvent.EventConditionType.PlayerClanId:
                    if (customization.Customize.Clan.ToString() != item.ObjectiveIdToComplete)
                    {
                        SetEvent(index + 1);
                        allowedToContinue = false;
                    }
                    break;
                case QuestEvent.EventConditionType.PlayerPhysicalPresentationId:
                    if (customization.Customize.Gender.ToString() != item.ObjectiveIdToComplete)
                    {
                        SetEvent(index + 1);
                        allowedToContinue = false;
                    }
                    break;
                case QuestEvent.EventConditionType.PlayerClassId:
                    if (AQuestReborn.AQuestReborn.PlayerClassJob != item.ObjectiveIdToComplete)
                    {
                        SetEvent(index + 1);
                        allowedToContinue = false;
                    }
                    break;
                case QuestEvent.EventConditionType.PlayerOutfitTopId:
                    if (customization.Equipment.Body.ItemId.ToString() != item.ObjectiveIdToComplete)
                    {
                        SetEvent(index + 1);
                        allowedToContinue = false;
                    }
                    break;
                case QuestEvent.EventConditionType.PlayerOutfitBottomId:
                    if (customization.Equipment.Legs.ItemId.ToString() != item.ObjectiveIdToComplete)
                    {
                        SetEvent(index + 1);
                        allowedToContinue = false;
                    }
                    break;
                case QuestEvent.EventConditionType.TimeLimitFailure:
                    bool failedEventCondition = true;
                    try
                    {
                        failedEventCondition = Plugin.AQuestReborn.FailedTimeLimit(_questDisplayObject.RoleplayingQuest.QuestId);
                    }
                    catch
                    {

                    }
                    if (failedEventCondition)
                    {
                        SetEvent(index + 1);
                        allowedToContinue = false;
                    }
                    Plugin.AQuestReborn.RemoveTimer(_questDisplayObject.RoleplayingQuest.QuestId);
                    break;
            }
            if (allowedToContinue)
            {
                Plugin.DialogueBackgroundWindow.IsOpen = true;
                IsOpen = true;
                _currentCharacter = 0;
                _currentText = "";
                _targetText = item.Dialogue;
                _currentDialogueBoxIndex = item.DialogueBoxStyle;
                _npcAppearanceSwap = item.AppearanceSwap;
                _playerAppearanceSwap = item.PlayerAppearanceSwap;
                _playerAppearanceSwapType = item.PlayerAppearanceSwapType;
                if (_questDisplayObject.QuestObjective.ObjectiveTriggersCutscene)
                {
                    if (!AQuestReborn.CutsceneCamera.IsDoingCutScene)
                    {
                        AQuestReborn.CutsceneCamera.IsDoingCutScene = true;
                    }
                    UIManager.HideUI(true);
                    if (!item.CameraIsNotAffectedDuringEvent)
                    {
                        if (!item.CameraLooksAtTalkingNpc)
                        {
                            if (item.CameraUsesDolly)
                            {
                                AQuestReborn.CutsceneCamera.SetCameraPosition(item.CameraStartPosition, item.CameraEndPosition, item.CameraDollySpeed);
                                AQuestReborn.CutsceneCamera.SetCameraRotation(item.CameraStartRotation, item.CameraEndRotation);
                                AQuestReborn.CutsceneCamera.SetFov(item.CameraStartingFov);
                                AQuestReborn.CutsceneCamera.SetZoom(item.CameraStartingFov);

                            }
                            else
                            {
                                AQuestReborn.CutsceneCamera.SetCameraPosition(item.CameraStartPosition);
                                AQuestReborn.CutsceneCamera.SetCameraRotation(item.CameraStartRotation);
                                AQuestReborn.CutsceneCamera.SetFov(item.CameraStartingFov, item.CameraEndingFov);
                                AQuestReborn.CutsceneCamera.SetZoom(item.CameraStartingZoom, item.CameraEndingZoom);
                            }
                        }
                    }
                }
                Task.Run(async () =>
                {
                    _currentName = item.NpcName.ToLower() == "system" ? "system" : await Translator.LocalizeText(string.IsNullOrEmpty(item.NpcAlias) ? item.NpcName : item.NpcAlias, Plugin.Configuration.QuestLanguage, _questDisplayObject.RoleplayingQuest.QuestLanguage);
                    _targetText = await Translator.LocalizeText(_targetText, Plugin.Configuration.QuestLanguage, _questDisplayObject.RoleplayingQuest.QuestLanguage);
                    var targetTextValue = _targetText;
                    while (true)
                    {
                        if (targetTextValue == _targetText)
                        {
                            if (_currentCharacter < _targetText.Length)
                            {
                                _currentText += _targetText[_currentCharacter++];
                                textTimer.Restart();
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                        Thread.Sleep(5);
                    }
                });
                string customDialoguePath = Path.Combine(_questDisplayObject.RoleplayingQuest.FoundPath, item.DialogueAudio);
                string customBGMPath = Path.Combine(_questDisplayObject.RoleplayingQuest.FoundPath, item.DialogueBackgroundMusic);
                string customBackgroundPath = Path.Combine(_questDisplayObject.RoleplayingQuest.FoundPath, item.EventBackground);
                string[] appearanceItems = item.AppearanceSwap.StringToArray();
                for (int i = 0; i < appearanceItems.Length; i++)
                {
                    if (appearanceItems[i].Contains(".chara") || appearanceItems[i].Contains(".mcdf"))
                    {
                        appearanceItems[i] = Path.Combine(_questDisplayObject.RoleplayingQuest.FoundPath, appearanceItems[i].Trim());
                    }
                }
                string customNpcAppearancePath = appearanceItems.ArrayToString();

                appearanceItems = item.PlayerAppearanceSwap.StringToArray();
                for (int i = 0; i < appearanceItems.Length; i++)
                {
                    if (appearanceItems[i].Contains(".chara") || appearanceItems[i].Contains(".mcdf"))
                    {
                        appearanceItems[i] = Path.Combine(_questDisplayObject.RoleplayingQuest.FoundPath, appearanceItems[i].Trim());
                    }
                }

                string customPlayerAppearancePath = appearanceItems.ArrayToString();

                bool isGlamourerString = !_npcAppearanceSwap.Contains(".mcdf") && !_npcAppearanceSwap.Contains(".chara");
                if (!string.IsNullOrEmpty(_npcAppearanceSwap) && File.Exists(customNpcAppearancePath) || isGlamourerString)
                {
                    if (isGlamourerString)
                    {
                        customNpcAppearancePath = _npcAppearanceSwap;
                    }
                    if (Plugin.RoleplayingQuestManager.SwapAppearanceData(_questDisplayObject.RoleplayingQuest, item.NpcName, item.AppearanceSwap))
                    {
                        Plugin.AQuestReborn.UpdateNPCAppearance(Plugin.ClientState.TerritoryType, _questDisplayObject.RoleplayingQuest.QuestId, item.NpcName, customNpcAppearancePath);
                    }
                }
                if (_playerAppearanceSwapType != QuestEvent.AppearanceSwapType.RevertAppearance)
                {
                    if (!string.IsNullOrEmpty(_playerAppearanceSwap) && File.Exists(customPlayerAppearancePath) || item.PlayerAppearanceSwap.Length > 255)
                    {
                        if (!item.PlayerAppearanceSwap.Contains(".mcdf") && !item.PlayerAppearanceSwap.Contains(".chara"))
                        {
                            customPlayerAppearancePath = item.PlayerAppearanceSwap;
                        }
                        var data = Plugin.RoleplayingQuestManager.GetPlayerAppearanceForZone(Plugin.ClientState.TerritoryType, Plugin.AQuestReborn.Discriminator);
                        if (data == null || customPlayerAppearancePath != data.AppearanceData)
                        {
                            Task.Run(() =>
                            {
                                Thread.Sleep(1000);
                                Plugin.Framework.RunOnFrameworkThread(() =>
                                {
                                    Plugin.SetAutomationGlobalState(false);
                                    Plugin.AQuestReborn.LoadAppearance(customPlayerAppearancePath, _playerAppearanceSwapType, Plugin.ObjectTable.LocalPlayer);
                                    Plugin.RoleplayingQuestManager.AddPlayerAppearance(_questDisplayObject.RoleplayingQuest.QuestId, customPlayerAppearancePath, _playerAppearanceSwapType);
                                });
                            });
                        }
                    }
                }
                else
                {
                    Plugin.SetAutomationGlobalState(true);
                    Plugin.RoleplayingQuestManager.RemovePlayerAppearance(_questDisplayObject.RoleplayingQuest.QuestId);
                    AppearanceAccessUtils.AppearanceManager.RemoveTemporaryCollection(Plugin.ObjectTable.LocalPlayer.Name.TextValue);
                }
                if (item.NpcName.ToLower() == "system")
                {
                    _currentDialogueBoxIndex = _dialogueBoxStyles.Count - 1;
                }
                if (Plugin.AQuestReborn.SpawnedNPCs.ContainsKey(_questDisplayObject.RoleplayingQuest.QuestId))
                {
                    if (Plugin.AQuestReborn.SpawnedNPCs[_questDisplayObject.RoleplayingQuest.QuestId].ContainsKey(item.NpcName))
                    {
                        if ((ushort)item.BodyExpression > 0)
                        {
                            if (!item.LoopAnimation)
                            {
                                Plugin.AnamcoreManager.TriggerEmoteTimed(Plugin.AQuestReborn.SpawnedNPCs[_questDisplayObject.RoleplayingQuest.QuestId][item.NpcName], (ushort)item.BodyExpression);
                            }
                            else
                            {
                                Plugin.AnamcoreManager.TriggerEmote(Plugin.AQuestReborn.SpawnedNPCs[_questDisplayObject.RoleplayingQuest.QuestId][item.NpcName].Address, (ushort)item.BodyExpression);
                            }
                        }
                        else
                        {
                            Plugin.AnamcoreManager.TriggerEmoteTimed(Plugin.AQuestReborn.SpawnedNPCs[_questDisplayObject.RoleplayingQuest.QuestId][item.NpcName], (ushort)5810);
                        }
                    }
                }
                if ((ushort)item.BodyExpressionPlayer > 0)
                {
                    if (!item.LoopAnimationPlayer)
                    {
                        Plugin.AnamcoreManager.TriggerEmoteTimed(Plugin.ObjectTable.LocalPlayer, (ushort)item.BodyExpressionPlayer);
                    }
                    else
                    {
                        Plugin.AnamcoreManager.TriggerEmote(Plugin.ObjectTable.LocalPlayer.Address, (ushort)item.BodyExpressionPlayer);
                    }
                }
                else
                {
                    Plugin.AnamcoreManager.TriggerEmoteTimed(Plugin.ObjectTable.LocalPlayer, (ushort)0);
                }
                if (Plugin.MediaManager != null)
                {
                    if (File.Exists(customDialoguePath))
                    {
                        Plugin.MediaManager.PlayMedia(AQuestReborn.AQuestReborn.PlayerObject, customDialoguePath, RoleplayingMediaCore.SoundType.NPC, true);
                    }
                    if (File.Exists(customBGMPath))
                    {
                        Plugin.MediaManager.PlayMedia(_backgroundMusic, customBGMPath, RoleplayingMediaCore.SoundType.Loop, true);
                        try
                        {
                            Plugin.GameConfig.Set(SystemConfigOption.IsSndBgm, true);
                        }
                        catch (Exception e)
                        {
                            Plugin.PluginLog?.Warning(e, e.Message);
                        }
                    }
                    foreach (var soundEffect in item.SoundEffects)
                    {
                        if (File.Exists(soundEffect))
                        {
                            var combinedPath = Path.Combine(_questDisplayObject.RoleplayingQuest.FoundPath, soundEffect);
                            Plugin.MediaManager.PlayMedia(new DummyObject(), combinedPath, RoleplayingMediaCore.SoundType.ChatSound, true);
                        }
                        index++;
                    }
                    if (File.Exists(customBackgroundPath))
                    {
                        Plugin.DialogueBackgroundWindow.SetBackground(customBackgroundPath, item.TypeOfEventBackground);
                    }
                    else
                    {
                        Plugin.DialogueBackgroundWindow.ClearBackground();
                    }
                }
                if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(item.NpcName))
                {
                    Plugin.AQuestReborn.InteractiveNpcDictionary[item.NpcName].LooksAtPlayer = item.LooksAtPlayerDuringEvent;
                    Plugin.AQuestReborn.InteractiveNpcDictionary[item.NpcName].ShouldBeMoving = item.EventSetsNewNpcCoordinates;
                    if (item.EventSetsNewNpcCoordinates)
                    {
                        Plugin.AQuestReborn.InteractiveNpcDictionary[item.NpcName].SetDefaults(item.NpcMovementPosition, item.NpcMovementRotation,
                        item.NpcEventMovementType == QuestEvent.EventMovementType.Lerp ? 5 : item.NpcMovementTime, item.NpcEventMovementType);
                        Plugin.AQuestReborn.InteractiveNpcDictionary[item.NpcName].EventMovementAnimationType = item.NpcEventMovementAnimation;
                    }
                }
                if (item.EventSetsNewCutscenePlayerCoordinates)
                {
                    Plugin.AQuestReborn.CutscenePlayer.ShowNPC();
                    Plugin.AQuestReborn.CutscenePlayer.ShouldBeMoving = item.EventSetsNewCutscenePlayerCoordinates;
                    Plugin.AQuestReborn.CutscenePlayer.SetDefaults(item.CutscenePlayerMovementPosition, item.CutscenePlayerMovementRotation,
                    item.CutscenePlayerMovementType == QuestEvent.EventMovementType.Lerp ? 5 : item.CutscenePlayerMovementTime, item.CutscenePlayerMovementType);
                    Plugin.AQuestReborn.CutscenePlayer.EventMovementAnimationType = item.CutscenePlayerEventMovementAnimation;
                }
                if (_index < _questDisplayObject.QuestObjective.QuestText.Count &&
                _questDisplayObject.QuestObjective.QuestText[_index].BranchingChoices.Count > 0)
                {
                    _choicesAreNext = true;
                    switch (item.EventEndBehaviour)
                    {

                        case QuestEvent.EventBehaviourType.EventEndsEarlyWhenHitAndNPCFollowsPlayer:
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(item.NpcName))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[item.NpcName].FollowPlayer(2);
                                Plugin.RoleplayingQuestManager.AddPartyMember(new NpcPartyMember()
                                {
                                    NpcName = item.NpcName,
                                    QuestId = _questDisplayObject.RoleplayingQuest.QuestId,
                                    ZoneWhiteList = new List<int> { Plugin.ClientState.TerritoryType }
                                });
                                _questFollowing = true;
                            }
                            break;
                        case QuestEvent.EventBehaviourType.EventEndsEarlyWhenHitAndNPCStopsFollowingPlayer:
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(item.NpcName))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[item.NpcName].StopFollowingPlayer();
                                Plugin.RoleplayingQuestManager.RemovePartyMember(
                                Plugin.RoleplayingQuestManager.GetNpcPartyMember(_questDisplayObject.RoleplayingQuest.QuestId, item.NpcName));
                                _questStopFollowing = true;
                            }
                            break;
                        case QuestEvent.EventBehaviourType.NPCFollowsPlayer:
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(item.NpcName))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[item.NpcName].FollowPlayer(2);
                                Plugin.RoleplayingQuestManager.AddPartyMember(new NpcPartyMember()
                                {
                                    NpcName = item.NpcName,
                                    QuestId = _questDisplayObject.RoleplayingQuest.QuestId,
                                    ZoneWhiteList = new List<int> { Plugin.ClientState.TerritoryType }
                                });
                                _questFollowing = true;
                            }
                            break;
                        case QuestEvent.EventBehaviourType.NPCStopsFollowingPlayer:
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(item.NpcName))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[item.NpcName].StopFollowingPlayer();
                                Plugin.RoleplayingQuestManager.RemovePartyMember(
                                Plugin.RoleplayingQuestManager.GetNpcPartyMember(_questDisplayObject.RoleplayingQuest.QuestId, item.NpcName));
                                _questStopFollowing = true;
                            }
                            break;
                    }
                    if (_questFollowing || _questStopFollowing)
                    {
                        Task.Run(async () =>
                        {
                            _lastNpcName = await Translator.LocalizeText(item.NpcName, Plugin.Configuration.QuestLanguage, _questDisplayObject.RoleplayingQuest.QuestLanguage);
                        });
                    }
                }
                else
                {
                    switch (item.EventEndBehaviour)
                    {
                        case QuestEvent.EventBehaviourType.EventSkipsToDialogueNumber:
                            _index = item.EventNumberToSkipTo;
                            break;
                        case QuestEvent.EventBehaviourType.EventEndsEarlyWhenHit:
                            _index = _questDisplayObject.QuestObjective.QuestText.Count;
                            break;
                        case QuestEvent.EventBehaviourType.EventEndsEarlyWhenHitNoProgression:
                            _index = _questDisplayObject.QuestObjective.QuestText.Count;
                            _blockProgression = true;
                            break;
                        case QuestEvent.EventBehaviourType.EventEndsEarlyWhenHitAndSkipsToObjective:
                            _index = _questDisplayObject.QuestObjective.QuestText.Count;
                            _objectiveSkipValue = item.ObjectiveNumberToSkipTo;
                            if (_objectiveSkipValue < _questDisplayObject.RoleplayingQuest.QuestObjectives.Count)
                            {
                                _questDisplayObject.RoleplayingQuest.QuestObjectives[_objectiveSkipValue].ClearProgression();
                            }
                            _objectiveSkip = true;
                            break;
                        case QuestEvent.EventBehaviourType.EventEndsEarlyWhenHitAndNPCFollowsPlayer:
                            _index = _questDisplayObject.QuestObjective.QuestText.Count;
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(item.NpcName))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[item.NpcName].FollowPlayer(2);
                                Plugin.RoleplayingQuestManager.AddPartyMember(new NpcPartyMember()
                                {
                                    NpcName = item.NpcName,
                                    QuestId = _questDisplayObject.RoleplayingQuest.QuestId,
                                    ZoneWhiteList = new List<int> { Plugin.ClientState.TerritoryType }
                                });
                                _questFollowing = true;
                            }
                            break;
                        case QuestEvent.EventBehaviourType.EventEndsEarlyWhenHitAndNPCStopsFollowingPlayer:
                            _index = _questDisplayObject.QuestObjective.QuestText.Count;
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(item.NpcName))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[item.NpcName].StopFollowingPlayer();
                                Plugin.RoleplayingQuestManager.RemovePartyMember(
                                Plugin.RoleplayingQuestManager.GetNpcPartyMember(_questDisplayObject.RoleplayingQuest.QuestId, item.NpcName));
                                _questStopFollowing = true;
                            }
                            break;
                        case QuestEvent.EventBehaviourType.NPCFollowsPlayer:
                            _index++;
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(item.NpcName))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[item.NpcName].FollowPlayer(2);
                                Plugin.RoleplayingQuestManager.AddPartyMember(new NpcPartyMember()
                                {
                                    NpcName = item.NpcName,
                                    QuestId = _questDisplayObject.RoleplayingQuest.QuestId,
                                    ZoneWhiteList = new List<int> { Plugin.ClientState.TerritoryType }
                                });
                                _questFollowing = true;
                            }
                            break;
                        case QuestEvent.EventBehaviourType.NPCStopsFollowingPlayer:
                            _index++;
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(item.NpcName))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[item.NpcName].StopFollowingPlayer();
                                Plugin.RoleplayingQuestManager.RemovePartyMember(
                                Plugin.RoleplayingQuestManager.GetNpcPartyMember(_questDisplayObject.RoleplayingQuest.QuestId, item.NpcName));
                                _questStopFollowing = true;
                            }

                            break;
                        case QuestEvent.EventBehaviourType.EventEndsEarlyWhenHitAndStartsTimer:
                            _index = _questDisplayObject.QuestObjective.QuestText.Count;
                            Plugin.AQuestReborn.StartObjectiveTimer(item.TimeLimit, _questDisplayObject.RoleplayingQuest.QuestId);
                            break;
                        case QuestEvent.EventBehaviourType.StartsTimer:
                            _index++;
                            Plugin.AQuestReborn.StartObjectiveTimer(item.TimeLimit, _questDisplayObject.RoleplayingQuest.QuestId);
                            break;
                        case QuestEvent.EventBehaviourType.None:
                            _index++;
                            break;
                    }
                    if (_questFollowing || _questStopFollowing)
                    {
                        Task.Run(async () =>
                        {
                            _lastNpcName = await Translator.LocalizeText(item.NpcName, Plugin.Configuration.QuestLanguage, _questDisplayObject.RoleplayingQuest.QuestLanguage);
                        });
                    }
                }
                textTimer.Restart();
                _dialogueWindowIsHidden = item.DialogueWindowIsHidden;
                if (item.EventHasNoReading)
                {
                    _previousEventHasNoReading = true;
                    NextEvent();
                }
            }
        }
        else
        {

            _dontUnblockMovement = false;
            Plugin.DialogueBackgroundWindow.IsOpen = false;
            IsOpen = false;
            Plugin.MediaManager.StopAudio(_backgroundMusic);
            try
            {
                Plugin.GameConfig.Set(SystemConfigOption.IsSndBgm, false);
            }
            catch (Exception e)
            {
                Plugin.PluginLog?.Warning(e, e.Message);
            }
            if (_questDisplayObject.QuestObjective.ObjectiveTriggersCutscene)
            {
                UIManager.HideUI(false);
                AQuestReborn.CutsceneCamera.ResetCamera();
                Plugin.AQuestReborn.CutscenePlayer.SetDefaults((new Vector3(0, float.MaxValue, 0) / 10), Quaternion.Identity.QuaternionToEuler());
            }
            _currentCharacter = 0;
            textTimer.Reset();
            if (_questFollowing)
            {
                Task.Run(async () =>
                {
                    var toast = await Translator.LocalizeText(_lastNpcName + " is now following you in zones related to this quest.", Plugin.Configuration.QuestLanguage, _questDisplayObject.RoleplayingQuest.QuestLanguage);

                    Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        Plugin.ToastGui.ShowNormal(toast);
                        _lastNpcName = "";
                        _questFollowing = false;
                    });
                });
            }
            if (_questStopFollowing)
            {
                Task.Run(async () =>
                {
                    var toast = await Translator.LocalizeText(_lastNpcName + " has stopped following you.", Plugin.Configuration.QuestLanguage, _questDisplayObject.RoleplayingQuest.QuestLanguage);

                    Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        Plugin.ToastGui.ShowNormal(toast);
                        _lastNpcName = "";
                        _questStopFollowing = false;
                    });
                });

            }
            if (!_blockProgression && !_objectiveSkip)
            {
                _questDisplayObject.QuestEvents?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                if (_objectiveSkip)
                {
                    Plugin.RoleplayingQuestManager.SkipToObjective(_questDisplayObject.RoleplayingQuest, _objectiveSkipValue);
                }
                _blockProgression = false;
                _objectiveSkip = false;
            }
            Plugin.AQuestReborn.RefreshNpcs(Plugin.ClientState.TerritoryType, _questDisplayObject.RoleplayingQuest.QuestId, true);
            Plugin.AQuestReborn.RefreshMapMarkers();
            Plugin.SaveProgress();
        }
    }
}
