using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Numerics;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using RoleplayingQuestCore;
using RoleplayingVoiceDalamudWrapper;
using static FFXIVClientStructs.FFXIV.Component.GUI.AtkComponentButton.Delegates;
using FFXIVLooseTextureCompiler.ImageProcessing;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Style;

namespace SamplePlugin.Windows;

public class DialogueWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;
    QuestDisplayObject questDisplayObject;
    int _index = 0;
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
    private string _mcdfSwap;
    private bool _alreadyLoadingFrame;
    private IDalamudTextureWrap _dialogueStyleToLoad;
    private IDalamudTextureWrap _dialogueTitleStyleToLoad;
    private byte[] _lastLoadedTitleFrame;
    private byte[] _lastLoadedFrame;
    private byte[] _nameTitleStyle;
    private bool _alreadyLoadingTitleFrame;
    private Bitmap data1;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public DialogueWindow(Plugin plugin)
        : base("Dialogue Window##dialoguewindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBackground)
    {
        Size = new Vector2(1088, 288);
        Plugin = plugin;
        plugin.ChoiceWindow.OnChoiceMade += ChoiceWindow_OnChoiceMade;
        _dummyObject = new DummyObject();
        LoadBubbleBackgrounds();
    }
    public override void OnClose()
    {
        Plugin.DialogueBackgroundWindow.IsOpen = false;
        base.OnClose();
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
        var questText = questDisplayObject.QuestObjective.QuestText[_index];
        if (questText.BranchingChoices.Count > 0)
        {
            var branchingChoice = questText.BranchingChoices[e];
            switch (branchingChoice.ChoiceType)
            {
                case BranchingChoice.BranchingChoiceType.SkipToDialogueNumber:
                    SetText(branchingChoice.DialogueToJumpTo);
                    break;
                case BranchingChoice.BranchingChoiceType.BranchingQuestline:
                    Plugin.RoleplayingQuestManager.ReplaceQuest(branchingChoice.RoleplayingQuest);
                    break;
            }
        }
    }

    public QuestDisplayObject QuestTexts { get => questDisplayObject; set => questDisplayObject = value; }
    internal DummyObject DummyObject { get => _dummyObject; set => _dummyObject = value; }

    public void Dispose() { }

    public override void Draw()
    {
        var globalScale = ImGuiHelpers.GlobalScale;
        var values = ImGui.GetIO().DisplaySize;
        Size = new Vector2(1088 * globalScale, 288 * globalScale);
        Position = new Vector2((values.X / 2) - (Size.Value.X / 2), values.Y - Size.Value.Y);
        if (!_alreadyLoadingFrame)
        {
            Task.Run(async () =>
            {
                _alreadyLoadingFrame = true;
                if (_lastLoadedFrame != _dialogueBoxStyles[_currentDialogueBoxIndex])
                {
                    _dialogueStyleToLoad = await Plugin.TextureProvider.CreateFromImageAsync(_dialogueBoxStyles[_currentDialogueBoxIndex]);
                    _lastLoadedFrame = _dialogueBoxStyles[_currentDialogueBoxIndex];
                }
                _alreadyLoadingFrame = false;
            });
        }
        if (_dialogueStyleToLoad != null)
        {
            ImGui.Image(_dialogueStyleToLoad.ImGuiHandle, new Vector2(Size.Value.X, Size.Value.Y));
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
                ImGui.SetCursorPos(new Vector2(50 * globalScale, 0));
                ImGui.Image(_dialogueTitleStyleToLoad.ImGuiHandle, new Vector2(data1.Width * globalScale, data1.Height * globalScale));
            }
        }
        if (textTimer.ElapsedMilliseconds > 5)
        {
            if (_currentCharacter < _targetText.Length)
            {
                _currentText += _targetText[_currentCharacter++];
                textTimer.Restart();
            }
            else
            {
                textTimer.Reset();
            }
        }
        ImGui.SetCursorPos(new Vector2(0, 0));
        ImGui.BeginTable("##Dialogue Table", 3);
        ImGui.TableSetupColumn("Padding 1", ImGuiTableColumnFlags.WidthFixed, 100 * globalScale);
        ImGui.TableSetupColumn("Center", ImGuiTableColumnFlags.WidthFixed, 888 * globalScale);
        ImGui.TableSetupColumn("Padding 2", ImGuiTableColumnFlags.WidthFixed, 100 * globalScale);
        //ImGui.TableHeadersRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);

        ImGui.TableSetColumnIndex(1);
        DialogueDrawing();
        ImGui.TableSetColumnIndex(2);

        ImGui.EndTable();
    }

    private void DialogueDrawing()
    {
        var globalScale = ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosY(12 * globalScale);
        ImGui.LabelText("##nameLabel", _currentName.ToLower() == "system" ? "" : _currentName);
        ImGui.SetWindowFontScale(2);
        ImGui.SetCursorPosY(70 * globalScale);
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
        questDisplayObject = newQuestText;
        SetText(0);
        textTimer.Restart();
        Plugin.SaveProgress();
        _settingNewText = false;
    }

    public void NextText(bool bypassBranchingChoice = false)
    {
        if (!Plugin.ChoiceWindow.IsOpen && !_settingNewText)
        {
            if (questDisplayObject != null)
            {
                if (_choicesAreNext)
                {
                    var values = questDisplayObject.QuestObjective.QuestText[_index].BranchingChoices;
                    Plugin.ChoiceWindow.NewList(values);
                    _choicesAreNext = false;
                    IsOpen = false;
                    Plugin.DialogueBackgroundWindow.IsOpen = false;
                }
                else
                {
                    SetText(_index);
                }
            }
        }
    }
    public void SetText(int index)
    {
        _index = index;
        Plugin.MediaManager.StopAudio(_dummyObject);
        Plugin.DialogueBackgroundWindow.ClearBackground();
        if (_index < questDisplayObject.QuestObjective.QuestText.Count)
        {
            var item = questDisplayObject.QuestObjective.QuestText[_index];
            Plugin.DialogueBackgroundWindow.IsOpen = true;
            IsOpen = true;
            _currentCharacter = 0;
            _currentText = "";
            _targetText = item.Dialogue;
            _currentName = item.NpcName;
            _currentDialogueBoxIndex = item.DialogueBoxStyle;
            _mcdfSwap = item.AppearanceSwap;
            string customAudioPath = Path.Combine(questDisplayObject.RoleplayingQuest.FoundPath, item.DialogueAudio);
            string customBackgroundPath = Path.Combine(questDisplayObject.RoleplayingQuest.FoundPath, item.DialogueBackground);
            string customMcdfPath = Path.Combine(questDisplayObject.RoleplayingQuest.FoundPath, item.AppearanceSwap);
            if (!string.IsNullOrEmpty(_mcdfSwap) && File.Exists(customMcdfPath))
            {
                if (Plugin.RoleplayingQuestManager.SwapMCDF(questDisplayObject.RoleplayingQuest, _currentName, item.AppearanceSwap))
                {
                    Plugin.AQuestReborn.RefreshNPCs(Plugin.ClientState.TerritoryType);
                }
            }
            if (_currentName.ToLower() == "system")
            {
                _currentDialogueBoxIndex = _dialogueBoxStyles.Count - 1;
            }
            if (Plugin.AQuestReborn.SpawnedNPCs.ContainsKey(item.NpcName))
            {
                if ((ushort)item.BodyExpression > 0)
                {
                    Plugin.AnamcoreManager.TriggerEmoteTimed(Plugin.AQuestReborn.SpawnedNPCs[item.NpcName], (ushort)item.BodyExpression);
                }
                else
                {
                    Plugin.AnamcoreManager.TriggerEmoteTimed(Plugin.AQuestReborn.SpawnedNPCs[item.NpcName], (ushort)5810);
                }
            }
            if (Plugin.MediaManager != null)
            {
                if (File.Exists(customAudioPath))
                {
                    Plugin.MediaManager.PlayMedia(_dummyObject, customAudioPath, RoleplayingMediaCore.SoundType.NPC, true);
                }
                if (File.Exists(customBackgroundPath))
                {
                    Plugin.DialogueBackgroundWindow.SetBackground(customBackgroundPath, item.TypeOfDialogueBackground);
                }
                else
                {
                    Plugin.DialogueBackgroundWindow.ClearBackground();
                }
            }
            if (_index < questDisplayObject.QuestObjective.QuestText.Count &&
            questDisplayObject.QuestObjective.QuestText[_index].BranchingChoices.Count > 0)
            {
                _choicesAreNext = true;
            }
            else
            {
                switch (item.DialogueEndBehaviour)
                {
                    case QuestText.DialogueEndBehaviourType.DialogueSkipsToDialogueNumber:
                        _index = item.DialogueNumberToSkipTo;
                        break;
                    case QuestText.DialogueEndBehaviourType.DialogueEndsEarlyWhenHit:
                        _index = questDisplayObject.QuestObjective.QuestText.Count;
                        break;
                    case QuestText.DialogueEndBehaviourType.None:
                        _index++;
                        break;
                }
            }
            textTimer.Restart();
        }
        else
        {

            Plugin.DialogueBackgroundWindow.IsOpen = false;
            IsOpen = false;
            _currentCharacter = 0;
            textTimer.Reset();
            questDisplayObject.QuestEvents?.Invoke(this, EventArgs.Empty);
            Plugin.AQuestReborn.RefreshNPCs(Plugin.ClientState.TerritoryType, true);
            Plugin.AQuestReborn.RefreshMapMarkers();
            Plugin.SaveProgress();
        }
    }
}
