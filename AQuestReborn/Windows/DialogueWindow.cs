using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public DialogueWindow(Plugin plugin)
        : base("Dialogue Window##dialoguewindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize)
    {
        Size = new Vector2(600, 200);
        Plugin = plugin;
        plugin.ChoiceWindow.OnChoiceMade += ChoiceWindow_OnChoiceMade;
        _dummyObject = new DummyObject();
    }
    public override void OnClose()
    {
        Plugin.DialogueBackgroundWindow.IsOpen = false;
        base.OnClose();
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
        var values = ImGui.GetWindowViewport().WorkSize;
        Position = new Vector2((values.X / 2) - (Size.Value.X / 2), values.Y - Size.Value.Y);
        if (textTimer.ElapsedMilliseconds > 10)
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
        ImGui.LabelText("##nameLabel", _currentName);
        ImGui.SetWindowFontScale(2);
        ImGui.TextWrapped(_currentText);
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
            string customAudioPath = Path.Combine(questDisplayObject.RoleplayingQuest.FoundPath, item.DialogueAudio);
            string customBackgroundPath = Path.Combine(questDisplayObject.RoleplayingQuest.FoundPath, item.DialogueBackground);
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
            Plugin.SaveProgress();
        }
    }
}
