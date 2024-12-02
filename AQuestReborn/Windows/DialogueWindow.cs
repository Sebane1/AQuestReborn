using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using RoleplayingQuestCore;
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
    }
    public override void OnClose()
    {
        base.OnClose();
        Plugin.DialogueBackgroundWindow.IsOpen = false;
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

    public void Dispose() { }

    public override void Draw()
    {
        var values = ImGui.GetWindowViewport().WorkSize;
        Position = new Vector2((values.X / 2) - (Size.Value.X / 2), values.Y - Size.Value.Y);
        if (textTimer.ElapsedMilliseconds > 50)
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
        if (!_choicesAreNext)
        {
            _index++;
        }
        textTimer.Restart();
        Plugin.Configuration.Save();
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
                    //_index++;
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
        if (_index < questDisplayObject.QuestObjective.QuestText.Count)
        {
            var item = questDisplayObject.QuestObjective.QuestText[_index];
            Plugin.DialogueBackgroundWindow.IsOpen = true;
            IsOpen = true;
            _currentCharacter = 0;
            _currentText = "";
            _targetText = item.Dialogue;
            _currentName = item.NpcName;
            if (_index < questDisplayObject.QuestObjective.QuestText.Count &&
            questDisplayObject.QuestObjective.QuestText[_index].BranchingChoices.Count > 0)
            {
                _choicesAreNext = true;
            }
            else if (item.DialogueEndsEarlyWhenHit)
            {
                _index = questDisplayObject.QuestObjective.QuestText.Count;
            }
            else if (item.DialogueSkipsToDialogueNumber)
            {
                _index = item.DialogueNumberToSkipTo;
            }
            else
            {
                _index++;
            }
            textTimer.Restart();
        }
        else
        {
            IsOpen = false;
            _currentCharacter = 0;
            textTimer.Reset();
            Plugin.ToastGui.ShowQuest(questDisplayObject.QuestObjective.Objective,
             new Dalamud.Game.Gui.Toast.QuestToastOptions() { DisplayCheckmark = questDisplayObject.QuestObjective.ObjectiveStatus == QuestObjective.ObjectiveStatusType.Complete });
        }
    }
}
