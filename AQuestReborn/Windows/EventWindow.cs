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
using NAudio.Wave.SampleProviders;
using Dalamud.Game.ClientState.Objects.Types;
using PenumbraAndGlamourerHelpers.IPC.ThirdParty.Glamourer;

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
    private CancellationTokenSource? _typewriterCts;

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
    /// <summary>
    /// Validate a jump target and emit a quest-creator-facing error if it's out of range.
    /// Returns the clamped (safe) value.
    /// </summary>
    private int ValidateJumpTarget(int target, int maxEvent, string context)
    {
        if (target < 0 || target >= maxEvent)
        {
            string questName = _questDisplayObject?.RoleplayingQuest?.QuestId ?? "Unknown Quest";
            string msg = $"[Quest Error] \"{questName}\": {context} references dialogue #{target}, but only {maxEvent} dialogue entries exist (valid range: 0–{maxEvent - 1}). Ending dialogue to prevent soft-lock.";
            Plugin.PluginLog?.Warning(msg);
            try
            {
                Plugin.ChatGui.PrintError(msg);
                Plugin.ToastGui.ShowError(msg);
            }
            catch { }
            return maxEvent; // end dialogue gracefully
        }
        return target;
    }

    private void ChoiceWindow_OnChoiceMade(object? sender, int e)
    {
        IsOpen = true;
        var questText = _questDisplayObject.QuestObjective.QuestText[_index];
        int maxEvent = _questDisplayObject.QuestObjective.QuestText.Count;
        if (questText.BranchingChoices.Count > 0)
        {
            if (e < questText.BranchingChoices.Count)
            {
                var branchingChoice = questText.BranchingChoices[e];
                string choiceLabel = $"Branching Choice \"{branchingChoice.ChoiceText}\" (dialogue #{_index}, choice #{e})";
                switch (branchingChoice.ChoiceType)
                {
                    case BranchingChoice.BranchingChoiceType.SkipToEventNumber:
                        SetEvent(ValidateJumpTarget(branchingChoice.EventToJumpTo, maxEvent, choiceLabel));
                        break;
                    case BranchingChoice.BranchingChoiceType.BranchingQuestline:
                        Plugin.RoleplayingQuestManager.ReplaceQuest(branchingChoice.RoleplayingQuest);
                        break;
                    case BranchingChoice.BranchingChoiceType.RollD20ThenSkipToEventNumber:
                        var roll = new Random().Next(0, 20);
                        if (roll >= branchingChoice.MinimumDiceRoll)
                        {
                            SetEvent(ValidateJumpTarget(branchingChoice.EventToJumpTo, maxEvent, choiceLabel + " (success)"));
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
                            SetEvent(ValidateJumpTarget(branchingChoice.EventToJumpToFailure, maxEvent, choiceLabel + " (failure)"));
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
                        if (branchingChoice.RandomizedEventToSkipTo.Count > 0)
                        {
                            roll = new Random().Next(0, branchingChoice.RandomizedEventToSkipTo.Count);
                            SetEvent(ValidateJumpTarget(branchingChoice.RandomizedEventToSkipTo[roll], maxEvent, choiceLabel + " (randomized)"));
                        }
                        else
                        {
                            string questName = _questDisplayObject?.RoleplayingQuest?.QuestId ?? "Unknown Quest";
                            string msg = $"[Quest Error] \"{questName}\": {choiceLabel} uses randomized skip but has no target entries configured. Ending dialogue.";
                            Plugin.PluginLog?.Warning(msg);
                            try { Plugin.ChatGui.PrintError(msg); Plugin.ToastGui.ShowError(msg); } catch { }
                            SetEvent(maxEvent);
                        }
                        break;
                }
            }
        }
    }

    public QuestDisplayObject QuestTexts { get => _questDisplayObject; set => _questDisplayObject = value; }
    internal DummyObject DummyObject { get => _dummyObject; set => _dummyObject = value; }
    public Stopwatch TimeSinceLastDialogueDisplayed { get => _timeSinceLastDialogueDisplayed; set => _timeSinceLastDialogueDisplayed = value; }

    public void Dispose()
    {
        _typewriterCts?.Cancel();
        _typewriterCts?.Dispose();
    }

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
                _alreadyLoadingFrame = true;
                Task.Run(async () =>
                {
                    try
                    {
                        for (int i = 0; i < _dialogueBoxStyles.Count; i++)
                        {
                            if (!_dialogueStylesToLoad.ContainsKey(i) || _dialogueStylesToLoad[i] == null)
                            {
                                _dialogueStylesToLoad[i] = await Plugin.TextureProvider.CreateFromImageAsync(_dialogueBoxStyles[i]);
                            }
                        }
                    }
                    finally
                    {
                        _alreadyLoadingFrame = false;
                    }
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
                    _alreadyLoadingTitleFrame = true;
                    Task.Run(async () =>
                    {
                        try
                        {
                            if (_lastLoadedFrame != _nameTitleStyle)
                            {
                                _dialogueTitleStyleToLoad = await Plugin.TextureProvider.CreateFromImageAsync(_nameTitleStyle);
                                _lastLoadedTitleFrame = _nameTitleStyle;
                            }
                        }
                        finally
                        {
                            _alreadyLoadingTitleFrame = false;
                        }
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
    public string FormatDialogue(string value, RoleplayingVoiceDalamud.Glamourer.CharacterCustomization customization)
    {
        if (!string.IsNullOrEmpty(value))
        {
            if (Plugin.ObjectTable.LocalPlayer != null)
            {
                string[] names = Plugin.ObjectTable.LocalPlayer.Name.TextValue.Split(" ");
                if (names.Length < 2)
                {
                    names = new string[2] { "", "" };
                }
                return value
                    .Replace(@"<fn>", names[0])
                    .Replace("<ln>", names[1])
                    .Replace("<n>", names[0] + " " + names[1])
                    .Replace("<r>", customization != null ? Race(customization.Customize.Race.Value) : "")
                    .Replace("<t>", customization != null ? Tribe(customization.Customize.Clan.Value) : "");
            }
        }
        return value;
    }

    public string Race(int value)
    {
        switch (value)
        {
            case 1: return "Hyur";
            case 2: return "Elezen";
            case 3: return "Lalafell";
            case 4: return "Miqo'te";
            case 5: return "Roegadyn";
            case 6: return "Au Ra";
            case 7: return "Hrothgar";
            case 8: return "Viera";
            default: return "";
        }
    }

    public string Tribe(int value)
    {
        switch (value)
        {
            case 1: return "Midlander";
            case 2: return "Highlander";
            case 3: return "Wildwood";
            case 4: return "Duskwight";
            case 5: return "Plainsfolk";
            case 6: return "Dunesfolk";
            case 7: return "Seeker of the Sun";
            case 8: return "Keeper of the Moon";
            case 9: return "Sea Wolf";
            case 10: return "Hellsguard";
            case 11: return "Raen";
            case 12: return "Xaela";
            case 13: return "Helions";
            case 14: return "The Lost";
            case 15: return "Rava";
            case 16: return "Veena";
            default: return "";
        }
    }

    public async void SetEvent(int index)
    {
        _index = index;
        _typewriterCts?.Cancel();
        _typewriterCts?.Dispose();
        _typewriterCts = new CancellationTokenSource();
        var typewriterToken = _typewriterCts.Token;
        bool allowedToContinue = true;
        Plugin.MediaManager.StopAudio(AQuestReborn.AQuestReborn.PlayerObject);
        Plugin.DialogueBackgroundWindow.ClearBackground();
        if (_index < _questDisplayObject.QuestObjective.QuestText.Count)
        {
            var item = _questDisplayObject.QuestObjective.QuestText[_index];
            _dialogueWindowIsHidden = item.DialogueWindowIsHidden;
            await Task.Delay(item.EventWaitTime);
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
                _targetText = FormatDialogue(item.Dialogue, customization);
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
                    try
                    {
                        _currentName = item.NpcName.ToLower() == "system" ? "system" : await Translator.LocalizeText(string.IsNullOrEmpty(item.NpcAlias) ? item.NpcName : item.NpcAlias, Plugin.Configuration.QuestLanguage, _questDisplayObject.RoleplayingQuest.QuestLanguage);
                        _targetText = await Translator.LocalizeText(_targetText, Plugin.Configuration.QuestLanguage, _questDisplayObject.RoleplayingQuest.QuestLanguage);

                        // Log dialogue to FFXIV chat
                        try
                        {
                            Plugin.ChatGui.Print(new Dalamud.Game.Text.XivChatEntry
                            {
                                Message = $"{_currentName}: {_targetText}",
                                Type = Dalamud.Game.Text.XivChatType.NPCDialogue,
                            });
                        }
                        catch { }

                        var targetTextValue = _targetText;
                        while (true)
                        {
                            typewriterToken.ThrowIfCancellationRequested();
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
                            try
                            {
                                await Task.Delay(5, typewriterToken);
                            }
                            catch (TaskCanceledException)
                            {
                                break;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }, typewriterToken);
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
                        Plugin.AQuestReborn.UpdateNPCAppearance((ushort)Plugin.ClientState.TerritoryType, _questDisplayObject.RoleplayingQuest.QuestId, item.NpcName, customNpcAppearancePath);
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
                        var data = Plugin.RoleplayingQuestManager.GetPlayerAppearanceForZone((int)Plugin.ClientState.TerritoryType, Plugin.AQuestReborn.Discriminator);
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
                if (true)
                {
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
                }
                if (Plugin.MediaManager != null)
                {
                    if (File.Exists(customDialoguePath))
                    {
                        ICharacter npcForLipSync = null;
                        if (Plugin.AQuestReborn.SpawnedNPCs.ContainsKey(_questDisplayObject.RoleplayingQuest.QuestId)
                            && Plugin.AQuestReborn.SpawnedNPCs[_questDisplayObject.RoleplayingQuest.QuestId].ContainsKey(item.NpcName))
                        {
                            npcForLipSync = Plugin.AQuestReborn.SpawnedNPCs[_questDisplayObject.RoleplayingQuest.QuestId][item.NpcName];
                        }
                        bool isTalking = false;
                        EventHandler<StreamVolumeEventArgs> volumeHandler = null;
                        EventHandler<string> stoppedHandler = null;
                        if (npcForLipSync != null)
                        {
                            var npcRef = npcForLipSync;
                            volumeHandler = (s, e) =>
                            {
                                try
                                {
                                    float maxVol = e.MaxSampleValues.Length > 0 ? e.MaxSampleValues[0] : 0;
                                    if (maxVol > 0.05f && !isTalking)
                                    {
                                        isTalking = true;
                                        Plugin.AnamcoreManager.TriggerLipSync(npcRef, 0);
                                    }
                                    else if (maxVol <= 0.05f && isTalking)
                                    {
                                        isTalking = false;
                                        Plugin.AnamcoreManager.StopLipSync(npcRef);
                                    }
                                }
                                catch { }
                            };
                            stoppedHandler = (s, e) =>
                            {
                                try { Plugin.AnamcoreManager.StopLipSync(npcRef); } catch { }
                            };
                        }
                        Plugin.MediaManager.PlayMedia(AQuestReborn.AQuestReborn.PlayerObject, customDialoguePath,
                            RoleplayingMediaCore.SoundType.NPC, true, 0, default, stoppedHandler, volumeHandler);
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
                var questNpcKey = AQuestReborn.AQuestReborn.QuestNpcKey(_questDisplayObject.RoleplayingQuest.QuestId, item.NpcName);
                // Build set of NPCs that should look at the player: the speaker + any explicitly listed extras
                var npcsAllowedToLook = new HashSet<string> { questNpcKey };
                foreach (var additionalName in item.AdditionalNpcsLookAtPlayer)
                {
                    npcsAllowedToLook.Add(AQuestReborn.AQuestReborn.QuestNpcKey(_questDisplayObject.RoleplayingQuest.QuestId, additionalName));
                }
                // Clear LooksAtPlayer on all other quest NPCs not in the allowed set
                string questPrefix = _questDisplayObject.RoleplayingQuest.QuestId + "::";
                foreach (var kvp in Plugin.AQuestReborn.InteractiveNpcDictionary)
                {
                    if (kvp.Key.StartsWith(questPrefix) && !npcsAllowedToLook.Contains(kvp.Key))
                    {
                        kvp.Value.LooksAtPlayer = false;
                    }
                }
                // Set look-at for the speaking NPC
                if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(questNpcKey))
                {
                    Plugin.AQuestReborn.InteractiveNpcDictionary[questNpcKey].LooksAtPlayer = item.LooksAtPlayerDuringEvent;
                    Plugin.AQuestReborn.InteractiveNpcDictionary[questNpcKey].ShouldBeMoving = item.EventSetsNewNpcCoordinates;
                    if (item.EventSetsNewNpcCoordinates)
                    {
                        Plugin.AQuestReborn.InteractiveNpcDictionary[questNpcKey].SetDefaults(item.NpcMovementPosition, item.NpcMovementRotation,
                        item.NpcEventMovementType == QuestEvent.EventMovementType.Lerp ? 5 : item.NpcMovementTime, item.NpcEventMovementType);
                        Plugin.AQuestReborn.InteractiveNpcDictionary[questNpcKey].EventMovementAnimationType = item.NpcEventMovementAnimation;
                    }
                    else
                    {
                        // This event doesn't set new coordinates for the speaking NPC,
                        // so snap its default to current position to prevent it running
                        // back to a stale position from a previous objective.
                        Plugin.AQuestReborn.InteractiveNpcDictionary[questNpcKey].SnapDefaultsToCurrent();
                    }
                }
                // Snap all other quest NPCs (not the speaker) to their current positions
                // so they don't run back to stale defaults when dialogue opens.
                foreach (var kvp in Plugin.AQuestReborn.InteractiveNpcDictionary)
                {
                    if (kvp.Key != questNpcKey && kvp.Key.StartsWith(questPrefix))
                    {
                        kvp.Value.SnapDefaultsToCurrent();
                    }
                }
                // Enable look-at for any additional NPCs the creator specified
                foreach (var additionalName in item.AdditionalNpcsLookAtPlayer)
                {
                    var addKey = AQuestReborn.AQuestReborn.QuestNpcKey(_questDisplayObject.RoleplayingQuest.QuestId, additionalName);
                    if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(addKey))
                    {
                        Plugin.AQuestReborn.InteractiveNpcDictionary[addKey].LooksAtPlayer = true;
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
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(questNpcKey))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[questNpcKey].FollowPlayer(2);
                                Plugin.RoleplayingQuestManager.AddPartyMember(new NpcPartyMember()
                                {
                                    NpcName = item.NpcName,
                                    QuestId = _questDisplayObject.RoleplayingQuest.QuestId,
                                    ZoneWhiteList = new List<int> { (int)Plugin.ClientState.TerritoryType }
                                });
                                _questFollowing = true;
                            }
                            break;
                        case QuestEvent.EventBehaviourType.EventEndsEarlyWhenHitAndNPCStopsFollowingPlayer:
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(questNpcKey))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[questNpcKey].StopFollowingPlayer();
                                Plugin.RoleplayingQuestManager.RemovePartyMember(
                                Plugin.RoleplayingQuestManager.GetNpcPartyMember(_questDisplayObject.RoleplayingQuest.QuestId, item.NpcName));
                                _questStopFollowing = true;
                            }
                            break;
                        case QuestEvent.EventBehaviourType.NPCFollowsPlayer:
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(questNpcKey))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[questNpcKey].FollowPlayer(2);
                                Plugin.RoleplayingQuestManager.AddPartyMember(new NpcPartyMember()
                                {
                                    NpcName = item.NpcName,
                                    QuestId = _questDisplayObject.RoleplayingQuest.QuestId,
                                    ZoneWhiteList = new List<int> { (int)Plugin.ClientState.TerritoryType }
                                });
                                _questFollowing = true;
                            }
                            break;
                        case QuestEvent.EventBehaviourType.NPCStopsFollowingPlayer:
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(questNpcKey))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[questNpcKey].StopFollowingPlayer();
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
                            _index = ValidateJumpTarget(item.EventNumberToSkipTo, _questDisplayObject.QuestObjective.QuestText.Count,
                                $"Event End Behaviour \"Skip To Dialogue\" (dialogue #{_index})");
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
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(questNpcKey))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[questNpcKey].FollowPlayer(2);
                                Plugin.RoleplayingQuestManager.AddPartyMember(new NpcPartyMember()
                                {
                                    NpcName = item.NpcName,
                                    QuestId = _questDisplayObject.RoleplayingQuest.QuestId,
                                    ZoneWhiteList = new List<int> { (int)Plugin.ClientState.TerritoryType }
                                });
                                _questFollowing = true;
                            }
                            break;
                        case QuestEvent.EventBehaviourType.EventEndsEarlyWhenHitAndNPCStopsFollowingPlayer:
                            _index = _questDisplayObject.QuestObjective.QuestText.Count;
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(questNpcKey))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[questNpcKey].StopFollowingPlayer();
                                Plugin.RoleplayingQuestManager.RemovePartyMember(
                                Plugin.RoleplayingQuestManager.GetNpcPartyMember(_questDisplayObject.RoleplayingQuest.QuestId, item.NpcName));
                                _questStopFollowing = true;
                            }
                            break;
                        case QuestEvent.EventBehaviourType.NPCFollowsPlayer:
                            _index++;
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(questNpcKey))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[questNpcKey].FollowPlayer(2);
                                Plugin.RoleplayingQuestManager.AddPartyMember(new NpcPartyMember()
                                {
                                    NpcName = item.NpcName,
                                    QuestId = _questDisplayObject.RoleplayingQuest.QuestId,
                                    ZoneWhiteList = new List<int> { (int)Plugin.ClientState.TerritoryType }
                                });
                                _questFollowing = true;
                            }
                            break;
                        case QuestEvent.EventBehaviourType.NPCStopsFollowingPlayer:
                            _index++;
                            if (Plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(questNpcKey))
                            {
                                Plugin.AQuestReborn.InteractiveNpcDictionary[questNpcKey].StopFollowingPlayer();
                                var partyMember = Plugin.RoleplayingQuestManager.GetNpcPartyMember(_questDisplayObject.RoleplayingQuest.QuestId, item.NpcName);
                                if (partyMember != null)
                                {
                                    Plugin.RoleplayingQuestManager.RemovePartyMember(partyMember);
                                }
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
                if (item.EventHasNoReading)
                {
                    _previousEventHasNoReading = true;
                    NextEvent();
                }
            }
        }
        else
        {
            _typewriterCts?.Cancel();
            _typewriterCts?.Dispose();
            _typewriterCts = null;

            _dontUnblockMovement = false;
            Plugin.DialogueBackgroundWindow.IsOpen = false;
            IsOpen = false;
            // Safety net: always unlock movement when dialogue ends, regardless of flag state
            Plugin.Movement.DisableMovementLock();
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
            Plugin.AQuestReborn.RefreshNpcs((ushort)Plugin.ClientState.TerritoryType, _questDisplayObject.RoleplayingQuest.QuestId, true);
            Plugin.AQuestReborn.RefreshMapMarkers();
            Plugin.AQuestReborn.RefreshMapMarkers();
            Plugin.SaveProgress();
        }
    }

    /// <summary>
    /// Forcibly closes the dialogue window and cleans up state without progressing the quest.
    /// Used when the player changes zones or teleports away during an active event.
    /// </summary>
    public void ForceCloseDialogue()
    {
        if (IsOpen || Plugin.ChoiceWindow.IsOpen)
        {
            _typewriterCts?.Cancel();
            _typewriterCts?.Dispose();
            _typewriterCts = null;

            _dontUnblockMovement = false;
            Plugin.DialogueBackgroundWindow.IsOpen = false;
            IsOpen = false;
            Plugin.ChoiceWindow.IsOpen = false;
            
            Plugin.Movement.DisableMovementLock();
            Plugin.MediaManager.StopAudio(_backgroundMusic);
            try
            {
                Plugin.GameConfig.Set(SystemConfigOption.IsSndBgm, false);
            }
            catch { }

            if (_questDisplayObject != null && _questDisplayObject.QuestObjective != null && _questDisplayObject.QuestObjective.ObjectiveTriggersCutscene)
            {
                UIManager.HideUI(false);
                AQuestReborn.CutsceneCamera.ResetCamera();
                Plugin.AQuestReborn.CutscenePlayer.SetDefaults((new Vector3(0, float.MaxValue, 0) / 10), Quaternion.Identity.QuaternionToEuler());
            }

            _currentCharacter = 0;
            textTimer.Reset();
            _blockProgression = false;
            _objectiveSkip = false;
            _questFollowing = false;
            _questStopFollowing = false;
            _choicesAreNext = false;
            
            // Do not invoke QuestEvents to prevent forward progression.
        }
    }
}
