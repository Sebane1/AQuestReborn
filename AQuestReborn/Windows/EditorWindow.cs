using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Components;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using RoleplayingQuestCore;
using static FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentLookingForGroup;
using static RoleplayingQuestCore.QuestObjective;
using static RoleplayingQuestCore.RoleplayingQuest;
using static RoleplayingQuestCore.BranchingChoice;
using static RoleplayingQuestCore.QuestText;

namespace SamplePlugin.Windows;

public class EditorWindow : Window, IDisposable
{
    private Plugin Plugin;
    private RoleplayingQuestCreator _roleplayingQuestCreator = new RoleplayingQuestCreator();
    private FileDialogManager _fileDialogManager;
    private EditorWindow subEditorWindow;
    private string[] _nodeNames = new string[] { };
    private int _selectedObjectiveNode = 0;
    private string[] _dialogues = new string[] { };
    private int _selectedDialogue;
    private int _selectedBranchingChoice;
    private string[] _branchingChoices = new string[] { };

    public RoleplayingQuestCreator RoleplayingQuestCreator { get => _roleplayingQuestCreator; set => _roleplayingQuestCreator = value; }

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public EditorWindow(Plugin plugin)
        : base("Quest Creator##" + Guid.NewGuid().ToString(), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
        _fileDialogManager = new FileDialogManager();
    }
    public override void OnOpen()
    {
        if (_roleplayingQuestCreator.CurrentQuest != null)
        {
            WindowName = (!_roleplayingQuestCreator.CurrentQuest.IsSubQuest ? "Quest Creator##" : "Branching Quest Creator##") + Guid.NewGuid().ToString();
        }
    }
    public void Dispose()
    {
        if (subEditorWindow != null)
        {
            subEditorWindow.Dispose();
        }
    }

    public override void Draw()
    {
        if (!_roleplayingQuestCreator.CurrentQuest.IsSubQuest)
        {
            _fileDialogManager.Draw();
            if (ImGui.Button("Load Quest"))
            {
                _fileDialogManager.Reset();
                ImGui.OpenPopup("OpenPathDialog##editorwindow");
            }
            if (ImGui.BeginPopup("OpenPathDialog##editorwindow"))
            {
                _fileDialogManager.OpenFileDialog("Select quest file", ".quest", (isOk, folder) =>
                {
                    if (isOk)
                    {
                        _roleplayingQuestCreator.EditQuest(folder[0]);
                        RefreshMenus();
                    }
                }, 0, null, true);
                ImGui.EndPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Save Quest"))
            {
                _fileDialogManager.Reset();
                ImGui.OpenPopup("SavePathDialog##editorwindow");
            }
            if (ImGui.BeginPopup("SavePathDialog##editorwindow"))
            {
                _fileDialogManager.SaveFolderDialog("Select save location", "RPVoiceCache", (isOk, folder) =>
                {
                    if (isOk)
                    {
                        _roleplayingQuestCreator.SaveQuest(folder);
                        RefreshMenus();
                    }
                }, null, true);
                ImGui.EndPopup();
            }
            if (_roleplayingQuestCreator != null && _roleplayingQuestCreator.CurrentQuest != null)
            {
                var questAuthor = _roleplayingQuestCreator.CurrentQuest.QuestAuthor;
                var questName = _roleplayingQuestCreator.CurrentQuest.QuestName;
                var questDescription = _roleplayingQuestCreator.CurrentQuest.QuestDescription;
                var contentRating = (int)_roleplayingQuestCreator.CurrentQuest.ContentRating;
                var questReward = _roleplayingQuestCreator.CurrentQuest.QuestReward;
                var questRewardType = (int)_roleplayingQuestCreator.CurrentQuest.TypeOfReward;
                var contentRatingTypes = Enum.GetNames(typeof(QuestContentRating));
                var questRewardTypes = Enum.GetNames(typeof(QuestRewardType));

                if (ImGui.InputText("Author##", ref questAuthor, 255))
                {
                    _roleplayingQuestCreator.CurrentQuest.QuestAuthor = questAuthor;
                }
                if (ImGui.InputText("Quest Name##", ref questName, 255))
                {
                    _roleplayingQuestCreator.CurrentQuest.QuestName = questName;
                }
                if (ImGui.InputText("Quest Description##", ref questDescription, 255))
                {
                    _roleplayingQuestCreator.CurrentQuest.QuestDescription = questDescription;
                }
                if (ImGui.Combo("Content Rating##", ref contentRating, contentRatingTypes, contentRatingTypes.Length))
                {
                    _roleplayingQuestCreator.CurrentQuest.ContentRating = (QuestContentRating)contentRating;
                }
                if (ImGui.Combo("Quest Reward Type##", ref questRewardType, questRewardTypes, questRewardTypes.Length))
                {
                    _roleplayingQuestCreator.CurrentQuest.TypeOfReward = (QuestRewardType)questRewardType;
                }
                switch (_roleplayingQuestCreator.CurrentQuest.TypeOfReward)
                {
                    case QuestRewardType.SecretMessage:
                        if (ImGui.InputText("Quest Reward (Secret Message)", ref questReward, 255))
                        {
                            _roleplayingQuestCreator.CurrentQuest.QuestReward = questReward;
                        }
                        break;
                    case QuestRewardType.DownloadLink:
                        if (ImGui.InputText("Quest Reward (Download Link)", ref questReward, 255))
                        {
                            _roleplayingQuestCreator.CurrentQuest.QuestReward = questReward;
                        }
                        break;
                    case QuestRewardType.MediaFile:
                        if (ImGui.InputText("Quest Reward (Media File Path)", ref questReward, 255))
                        {
                            _roleplayingQuestCreator.CurrentQuest.QuestReward = questReward;
                        }
                        break;
                }
            }
        }
        ImGui.BeginTable("##Editor Table", 2);
        ImGui.TableSetupColumn("Objective List", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("Objective Editor", ImGuiTableColumnFlags.WidthStretch, 300);
        ImGui.TableHeadersRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        DrawQuestNodes();
        ImGui.TableSetColumnIndex(1);
        DrawQuestNodeEditor();
        ImGui.EndTable();
    }

    private void DrawQuestNodeEditor()
    {
        if (_roleplayingQuestCreator.CurrentQuest.QuestObjectives.Count > 0)
        {
            if (_selectedObjectiveNode > _roleplayingQuestCreator.CurrentQuest.QuestObjectives.Count)
            {
                _selectedObjectiveNode = _roleplayingQuestCreator.CurrentQuest.QuestObjectives.Count - 1;
            }
            var questObjective = _roleplayingQuestCreator.CurrentQuest.QuestObjectives[_selectedObjectiveNode];
            var territoryId = questObjective.TerritoryId;
            var objective = questObjective.Objective;
            var coordinates = questObjective.Coordinates;
            var questText = questObjective.QuestText;
            var questPointType = (int)questObjective.TypeOfQuestPoint;
            var objectiveStatusType = (int)questObjective.ObjectiveStatus;
            var objectiveTriggerType = (int)questObjective.TypeOfObjectiveTrigger;
            var questPointTypes = Enum.GetNames(typeof(QuestPointType));
            var objectiveStatusTypes = Enum.GetNames(typeof(ObjectiveStatusType));
            var objectiveTriggerTypes = Enum.GetNames(typeof(ObjectiveTriggerType));
            var triggerText = questObjective.TriggerText;

            ImGui.LabelText("##coordinatesLabel", $"Coordinates: X:{questObjective.Coordinates.X},Y:{questObjective.Coordinates.Y},Z:{questObjective.Coordinates.Z}");
            ImGui.LabelText("##territoryLabel", $"Territory Id: {questObjective.TerritoryId}");
            if (ImGui.Button("Set Quest Objective Coordinates"))
            {
                questObjective.Coordinates = Plugin.ClientState.LocalPlayer.Position;
                questObjective.TerritoryId = Plugin.ClientState.TerritoryType;
            }
            if (ImGui.InputText("Objective Text##", ref objective, 500))
            {
                questObjective.Objective = objective;
            }
            if (ImGui.Combo("Quest Point Type##", ref questPointType, questPointTypes, questPointTypes.Length))
            {
                questObjective.TypeOfQuestPoint = (QuestPointType)questPointType;
            }
            if (ImGui.Combo("Objective Quest Status Type##", ref objectiveStatusType, objectiveStatusTypes, objectiveStatusTypes.Length))
            {
                questObjective.ObjectiveStatus = (ObjectiveStatusType)objectiveStatusType;
            }
            if (ImGui.Combo("Objective Trigger Type##", ref objectiveTriggerType, objectiveTriggerTypes, objectiveTriggerTypes.Length))
            {
                questObjective.TypeOfObjectiveTrigger = (ObjectiveTriggerType)objectiveTriggerType;
            }
            switch (questObjective.TypeOfObjectiveTrigger)
            {
                case ObjectiveTriggerType.DoEmote:
                    if (ImGui.InputText("Emote Id##", ref triggerText, 500))
                    {
                        questObjective.TriggerText = triggerText;
                    }
                    break;
                case ObjectiveTriggerType.SayPhrase:
                    if (ImGui.InputText("Say Phrase##", ref triggerText, 500))
                    {
                        questObjective.TriggerText = triggerText;
                    }
                    break;
            }
            ImGui.BeginTable("##Dialogue Table", 2);
            ImGui.TableSetupColumn("Dialogue List", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Dialogue Editor", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            DrawQuestDialogues();
            ImGui.TableSetColumnIndex(1);
            DrawQuestDialogueEditor();
            ImGui.EndTable();
        }
    }

    private void DrawQuestDialogueEditor()
    {
        var questText = _roleplayingQuestCreator.CurrentQuest.QuestObjectives[_selectedObjectiveNode].QuestText;
        if (questText.Count > 0)
        {
            var item = questText[_selectedDialogue];
            var faceExpression = item.FaceExpression;
            var bodyExpression = item.BodyExpression;
            var npcName = item.NpcName;
            var dialogue = item.Dialogue;
            var dialogueAudio = item.DialogueAudio;
            var dialogueEndBehaviour = (int)item.DialogueEndBehaviour;
            var dialogueNumberToSkipTo = item.DialogueNumberToSkipTo;

            var dialogueEndTypes = Enum.GetNames(typeof(QuestText.DialogueEndBehaviourType));

            if (ImGui.InputInt("Face Expression Id##", ref faceExpression))
            {
                item.FaceExpression = faceExpression;
            }
            if (ImGui.InputInt("Body Expression Id##", ref bodyExpression))
            {
                item.BodyExpression = bodyExpression;
            }
            if (ImGui.InputText("Npc Name##", ref npcName, 20))
            {
                item.NpcName = npcName;
            }
            if (ImGui.InputText("Dialogue##", ref dialogue, 500))
            {
                item.Dialogue = dialogue;
            }
            if (ImGui.InputText("Dialogue Audio Path##", ref dialogueAudio, 255))
            {
                item.DialogueAudio = dialogueAudio;
            }
            if (ImGui.Combo("Dialogue End Behaviour##", ref dialogueEndBehaviour, dialogueEndTypes, dialogueEndTypes.Length))
            {
                item.DialogueEndBehaviour = (DialogueEndBehaviourType)dialogueEndBehaviour;
            }

            switch (item.DialogueEndBehaviour)
            {
                case DialogueEndBehaviourType.DialogueSkipsToDialogueNumber:
                    if (ImGui.InputInt("Dialogue Number To Skip To##", ref dialogueNumberToSkipTo))
                    {
                        item.DialogueNumberToSkipTo = dialogueNumberToSkipTo;
                    }
                    break;
            }
            DrawBranchingChoicesMenu();
        }
    }

    private void DrawBranchingChoicesMenu()
    {
        ImGui.BeginTable("##Branching Choices Table", 2);
        ImGui.TableSetupColumn("Branching Choices List", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn("Branching Choices Editor", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        DrawBranchingChoices();
        ImGui.TableSetColumnIndex(1);
        DrawBranchingChoicesEditor();
        ImGui.EndTable();
    }

    private void DrawBranchingChoicesEditor()
    {
        var questText = _roleplayingQuestCreator.CurrentQuest.QuestObjectives[_selectedObjectiveNode].QuestText;
        if (questText.Count > 0)
        {
            if (_selectedDialogue > questText.Count)
            {
                _selectedDialogue = questText.Count - 1;
            }
            var branchingChoices = questText[_selectedDialogue].BranchingChoices;
            if (branchingChoices.Count > 0)
            {
                if (_selectedBranchingChoice > branchingChoices.Count)
                {
                    _selectedBranchingChoice = branchingChoices.Count - 1;
                }
                var item = branchingChoices[_selectedBranchingChoice];
                var choiceText = item.ChoiceText;
                var choiceType = (int)item.ChoiceType;
                var roleplayingQuest = item.RoleplayingQuest;
                var dialogueToJumpTo = item.DialogueToJumpTo;
                var branchingChoiceTypes = Enum.GetNames(typeof(BranchingChoiceType));
                if (ImGui.InputText("Choice Text##", ref choiceText, 255))
                {
                    item.ChoiceText = choiceText;
                }
                if (ImGui.Combo("Branching Choice Type##", ref choiceType, branchingChoiceTypes, branchingChoiceTypes.Length))
                {
                    item.ChoiceType = (BranchingChoiceType)choiceType;
                }
                switch (item.ChoiceType)
                {
                    case BranchingChoiceType.SkipToDialogueNumber:
                        if (ImGui.InputInt("Dialogue Number To Jump To##", ref dialogueToJumpTo))
                        {
                            item.DialogueToJumpTo = dialogueToJumpTo;
                        }
                        break;
                    case BranchingChoiceType.BranchingQuestline:
                        if (ImGui.Button("Configure Branching Questline"))
                        {
                            if (subEditorWindow == null)
                            {
                                subEditorWindow = new EditorWindow(Plugin);
                                Plugin.WindowSystem.AddWindow(subEditorWindow);
                            }
                            subEditorWindow.IsOpen = true;
                            subEditorWindow.RoleplayingQuestCreator.EditQuest(roleplayingQuest);
                            subEditorWindow.RefreshMenus();
                        }
                        break;
                }
            }
        }
    }

    private void DrawBranchingChoices()
    {
        var questText = _roleplayingQuestCreator.CurrentQuest.QuestObjectives[_selectedObjectiveNode].QuestText;
        if (questText.Count > 0)
        {
            var branchingChoices = _roleplayingQuestCreator.CurrentQuest.QuestObjectives[_selectedObjectiveNode].QuestText[_selectedDialogue].BranchingChoices;
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            if (ImGui.ListBox("##branchingChoice", ref _selectedBranchingChoice, _branchingChoices, _branchingChoices.Length, 13))
            {
                RefreshMenus();
            }
            if (ImGui.Button("Add"))
            {
                var branchingChoice = new BranchingChoice();
                branchingChoices.Add(branchingChoice);
                branchingChoice.RoleplayingQuest.CopyAuthorData(_roleplayingQuestCreator.CurrentQuest);
                branchingChoice.RoleplayingQuest.IsSubQuest = true;
                _branchingChoices = FillNewList(branchingChoices.Count, "Choice");
                _selectedBranchingChoice = branchingChoices.Count - 1;
                RefreshMenus();
            }
            ImGui.SameLine();
            if (ImGui.Button("Remove"))
            {
                branchingChoices.RemoveAt(_selectedBranchingChoice);
                _branchingChoices = FillNewList(branchingChoices.Count, "Choice");
                _selectedBranchingChoice = branchingChoices.Count - 1;
                RefreshMenus();
            }
        }
    }

    private void DrawQuestDialogues()
    {
        var questText = _roleplayingQuestCreator.CurrentQuest.QuestObjectives[_selectedObjectiveNode].QuestText;
        ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
        if (ImGui.ListBox("##questDialogue", ref _selectedDialogue, _dialogues, _dialogues.Length, 21))
        {
            _selectedBranchingChoice = 0;
            RefreshMenus();
        }
        if (ImGui.Button("Add"))
        {
            questText.Add(new QuestText());
            _dialogues = FillNewList(questText.Count, "Dialogue");
            _selectedDialogue = questText.Count - 1;
            RefreshMenus();
        }
        ImGui.SameLine();
        if (ImGui.Button("Remove"))
        {
            questText.RemoveAt(_selectedDialogue);
            _dialogues = FillNewList(questText.Count, "Dialogue");
            _selectedDialogue = questText.Count - 1;
            RefreshMenus();
        }
    }
    private void RefreshMenus()
    {
        var questText = _roleplayingQuestCreator.CurrentQuest.QuestObjectives[_selectedObjectiveNode].QuestText;
        _dialogues = FillNewList(questText.Count, "Dialogue");
        _nodeNames = FillNewList(_roleplayingQuestCreator.CurrentQuest.QuestObjectives.Count, "Objective");
        if (questText.Count > 0)
        {
            var choices = questText[_selectedDialogue].BranchingChoices;
            if (_selectedBranchingChoice > choices.Count)
            {
                _selectedBranchingChoice = choices.Count - 1;
            }
            _branchingChoices = FillNewList(choices.Count, "Choice");
        }
        else
        {
            _branchingChoices = new string[] { };
        }
        if (subEditorWindow != null)
        {
            subEditorWindow.RefreshMenus();
            subEditorWindow.IsOpen = false;
        }
    }
    private void DrawQuestNodes()
    {
        ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
        if (ImGui.ListBox("##questNodes", ref _selectedObjectiveNode, _nodeNames, _nodeNames.Length, 30))
        {
            _selectedDialogue = 0;
            RefreshMenus();
        }
        if (ImGui.Button("Add## Objective"))
        {
            _roleplayingQuestCreator.AddQuestObjective(new QuestObjective()
            {
                Coordinates = Plugin.ClientState.LocalPlayer.Position,
                TerritoryId = Plugin.ClientState.TerritoryType
            });
            _nodeNames = FillNewList(_roleplayingQuestCreator.CurrentQuest.QuestObjectives.Count, "Objective");
            _selectedObjectiveNode = _roleplayingQuestCreator.CurrentQuest.QuestObjectives.Count - 1;
            RefreshMenus();
        }
        ImGui.SameLine();
        if (ImGui.Button("Remove## Objective"))
        {
            _roleplayingQuestCreator.CurrentQuest.QuestObjectives.RemoveAt(_selectedObjectiveNode);
            _nodeNames = FillNewList(_roleplayingQuestCreator.CurrentQuest.QuestObjectives.Count, "Objective");
            _selectedObjectiveNode = _roleplayingQuestCreator.CurrentQuest.QuestObjectives.Count - 1;
            RefreshMenus();
        }
    }
    private void OpenBranchingQuest(RoleplayingQuest roleplayingQuest)
    {
        subEditorWindow.RoleplayingQuestCreator.EditQuest(roleplayingQuest);
        subEditorWindow.RefreshMenus();
    }
    private string[] FillNewList(int count, string phrase)
    {
        List<string> values = new List<string>();
        for (int i = 0; i < count; i++)
        {
            values.Add(phrase + " " + i);
        }
        return values.ToArray();
    }
}
