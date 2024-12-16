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
using AQuestReborn;
using System.IO;
using System.Speech.Recognition;

namespace SamplePlugin.Windows;

public class EditorWindow : Window, IDisposable
{
    private Plugin Plugin;
    private RoleplayingQuestCreator _roleplayingQuestCreator = new RoleplayingQuestCreator();
    private FileDialogManager _fileDialogManager;
    private EditorWindow _subEditorWindow;
    private NPCEditorWindow _npcEditorWindow;
    private NPCTransformEditorWindow _npcTransformEditorWindow;
    private string[] _nodeNames = new string[] { };
    private string[] _dialogues = new string[] { };
    private int _selectedDialogue;
    private int _selectedBranchingChoice;
    private string[] _branchingChoices = new string[] { };
    private string[] _boxStyles = new string[] {
        "Normal", "Style2", "Telepathic", "Omicron/Machine", "Shout",
        "Written Lore", "Monster/Creature", "Dragon/Linkpearl", "System/Ascian" };
    private QuestObjective _objectiveInFocus;

    public RoleplayingQuestCreator RoleplayingQuestCreator { get => _roleplayingQuestCreator; set => _roleplayingQuestCreator = value; }

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public EditorWindow(Plugin plugin)
        : base("Quest Creator##" + Guid.NewGuid().ToString(), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(800, 800),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
        _fileDialogManager = new FileDialogManager();
        if (_npcTransformEditorWindow == null)
        {
            _npcTransformEditorWindow = new NPCTransformEditorWindow(Plugin, _roleplayingQuestCreator);
            Plugin.WindowSystem.AddWindow(_npcTransformEditorWindow);
        }
    }
    public override void OnOpen()
    {
        if (_roleplayingQuestCreator.CurrentQuest != null)
        {
            WindowName = (!_roleplayingQuestCreator.CurrentQuest.IsSubQuest ? "Quest Creator##" : "Branching Quest Creator##") + Guid.NewGuid().ToString();
        }
        RefreshMenus();
    }
    public void Dispose()
    {
        if (_subEditorWindow != null)
        {
            _subEditorWindow.Dispose();
        }
        if (_npcEditorWindow != null)
        {
            _npcEditorWindow.Dispose();
        }
        if (_npcTransformEditorWindow != null)
        {
            _npcTransformEditorWindow.Dispose();
        }
    }
    public override void Draw()
    {
        _fileDialogManager.Draw();
        if (!_roleplayingQuestCreator.CurrentQuest.IsSubQuest)
        {
            if (ImGui.Button("Save Quest"))
            {
                _roleplayingQuestCreator.SaveQuest(Path.Combine(Plugin.Configuration.QuestInstallFolder, _roleplayingQuestCreator.CurrentQuest.QuestName));
                Plugin.RoleplayingQuestManager.ScanDirectory();
                Plugin.AQuestReborn.RefreshNpcsForQuest(Plugin.ClientState.TerritoryType, _roleplayingQuestCreator.CurrentQuest.QuestId, true);
                Plugin.AQuestReborn.RefreshMapMarkers();
            }
            ImGui.SameLine();
            if (ImGui.Button("New Quest"))
            {
                _roleplayingQuestCreator.EditQuest(new RoleplayingQuest());
                RefreshMenus();
            }
            if (_roleplayingQuestCreator != null && _roleplayingQuestCreator.CurrentQuest != null)
            {
                var questAuthor = _roleplayingQuestCreator.CurrentQuest.QuestAuthor;
                var questName = _roleplayingQuestCreator.CurrentQuest.QuestName;
                var questDescription = _roleplayingQuestCreator.CurrentQuest.QuestDescription;
                var contentRating = (int)_roleplayingQuestCreator.CurrentQuest.ContentRating;
                var questReward = _roleplayingQuestCreator.CurrentQuest.QuestReward;
                var questRewardType = (int)_roleplayingQuestCreator.CurrentQuest.TypeOfReward;
                var questThumbnail = _roleplayingQuestCreator.CurrentQuest.QuestThumbnailPath;
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
                if (ImGui.InputText("Quest Description##", ref questDescription, 56))
                {
                    _roleplayingQuestCreator.CurrentQuest.QuestDescription = questDescription;
                }
                if (ImGui.InputText("Quest Thumbnail##", ref questThumbnail, 255))
                {
                    _roleplayingQuestCreator.CurrentQuest.QuestThumbnailPath = questThumbnail;
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
                if (ImGui.Button("Edit NPC Appearance Data##"))
                {
                    if (_npcEditorWindow == null)
                    {
                        _npcEditorWindow = new NPCEditorWindow(Plugin);
                        Plugin.WindowSystem.AddWindow(_npcEditorWindow);
                    }
                    if (_npcEditorWindow != null)
                    {
                        _npcEditorWindow.SetEditingQuest(_roleplayingQuestCreator.CurrentQuest);
                        _npcEditorWindow.IsOpen = true;
                    }
                }
            }
        }
        if (ImGui.Button("Export for re-use"))
        {
            _fileDialogManager.Reset();
            ImGui.OpenPopup("OpenPathDialog##editorwindow");
        }
        if (ImGui.BeginPopup("OpenPathDialog##editorwindow"))
        {
            _fileDialogManager.SaveFileDialog("Export quest line data", ".quest", "", ".quest", (isOk, file) =>
            {
                if (isOk)
                {
                    _roleplayingQuestCreator.SaveQuestline(_roleplayingQuestCreator.CurrentQuest, file);
                }
            }, "", true);
            ImGui.EndPopup();
        }
        ImGui.BeginTable("##Editor Table", 2);
        ImGui.TableSetupColumn("Objective List", ImGuiTableColumnFlags.WidthFixed, 300);
        ImGui.TableSetupColumn("Objective Editor", ImGuiTableColumnFlags.WidthStretch, 600);
        ImGui.TableHeadersRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        DrawQuestObjectives();
        ImGui.TableSetColumnIndex(1);
        DrawQuestNodeEditor();
        ImGui.EndTable();
    }

    private void DrawQuestNodeEditor()
    {
        if (_objectiveInFocus != null)
        {
            var questObjective = _objectiveInFocus;
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
            var objectiveImmediatelySatisfiesParent = questObjective.ObjectiveImmediatelySatisfiesParent;

            var maximum3dIndicatorDistance = questObjective.Maximum3dIndicatorDistance;
            var dontShowOnMap = questObjective.DontShowOnMap;

            ImGui.SetNextItemWidth(200);
            ImGui.LabelText("##coordinatesLabel", $"Coordinates: X:{Math.Round(questObjective.Coordinates.X)}," +
                $"Y:{Math.Round(questObjective.Coordinates.Y)}," +
                $"Z:{Math.Round(questObjective.Coordinates.Z)}");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(125);
            ImGui.LabelText("##territoryLabel", $"Territory Id: {questObjective.TerritoryId}");
            ImGui.SameLine();
            if (ImGui.Button("Set Quest Objective Coordinates"))
            {
                questObjective.Coordinates = Plugin.ClientState.LocalPlayer.Position;
                questObjective.TerritoryId = Plugin.ClientState.TerritoryType;
            }
            if (ImGui.InputFloat("Maximum Indicator Distance##", ref maximum3dIndicatorDistance))
            {
                questObjective.Maximum3dIndicatorDistance = maximum3dIndicatorDistance;
            }
            if (ImGui.Checkbox("Dont Show On Map##", ref dontShowOnMap))
            {
                questObjective.DontShowOnMap = dontShowOnMap;
            }
            if (!questObjective.IsAPrimaryObjective)
            {
                if (ImGui.Checkbox("Immediately Satisfies Parent Objective##", ref objectiveImmediatelySatisfiesParent))
                {
                    questObjective.ObjectiveImmediatelySatisfiesParent = objectiveImmediatelySatisfiesParent;
                }
            }
            if (ImGui.InputText("Objective Text##", ref objective, 500))
            {
                questObjective.Objective = objective;
            }
            ImGui.SetNextItemWidth(110);
            if (ImGui.Combo("Quest Point Type##", ref questPointType, questPointTypes, questPointTypes.Length))
            {
                questObjective.TypeOfQuestPoint = (QuestPointType)questPointType;
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(110);
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
                case ObjectiveTriggerType.KillEnemy:
                    if (ImGui.InputText("Enemy Name##", ref triggerText, 500))
                    {
                        questObjective.TriggerText = triggerText;
                    }
                    break;
                case ObjectiveTriggerType.SearchArea:
                    break;
            }
            if (ImGui.Button("Edit NPC Transform Data##"))
            {
                if (_npcTransformEditorWindow != null)
                {
                    _npcTransformEditorWindow.SetEditingQuest(questObjective);
                    _npcTransformEditorWindow.IsOpen = true;
                }
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
        if (_objectiveInFocus != null)
        {
            var questText = _objectiveInFocus.QuestText;
            if (questText.Count > 0)
            {
                if (_selectedDialogue > questText.Count)
                {
                    _selectedDialogue = 0;
                }
                var item = questText[_selectedDialogue];
                var faceExpression = item.FaceExpression;
                var bodyExpression = item.BodyExpression;
                var npcAlias = item.NpcAlias;
                var npcName = item.NpcName;
                var dialogue = item.Dialogue;
                var boxStyle = item.DialogueBoxStyle;
                var dialogueAudio = item.DialogueAudio;
                var dialogueBackgroundType = (int)item.TypeOfDialogueBackground;
                var dialogueBackground = item.DialogueBackground;
                var dialogueEndBehaviour = (int)item.DialogueEndBehaviour;
                var dialogueNumberToSkipTo = item.DialogueNumberToSkipTo;
                var dialogueEndTypes = Enum.GetNames(typeof(QuestText.DialogueEndBehaviourType));
                var dialogueBackgroundTypes = Enum.GetNames(typeof(QuestText.DialogueBackgroundType));
                var appearanceSwap = item.AppearanceSwap;
                var loopAnimation = item.LoopAnimation;
                if (ImGui.InputText("Npc Alias##", ref npcAlias, 40))
                {
                    item.NpcAlias = npcAlias;
                }
                if (ImGui.InputText("Npc Name##", ref npcName, 40))
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
                if (ImGui.InputText("Appearance Swap##", ref appearanceSwap, 255))
                {
                    item.AppearanceSwap = appearanceSwap;
                }
                if (ImGui.Combo("Box Style##", ref boxStyle, _boxStyles, _boxStyles.Length))
                {
                    item.DialogueBoxStyle = boxStyle;
                }
                ImGui.SetNextItemWidth(100);
                if (ImGui.InputInt("Face Expression Id##", ref faceExpression))
                {
                    item.FaceExpression = faceExpression;
                }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                if (ImGui.InputInt("Body Expression Id##", ref bodyExpression))
                {
                    item.BodyExpression = bodyExpression;
                }
                if (ImGui.Checkbox("Loop Animation##", ref loopAnimation))
                {
                    item.LoopAnimation = loopAnimation;
                }
                if (ImGui.Combo("Dialogue Background Type##", ref dialogueBackgroundType, dialogueBackgroundTypes, dialogueBackgroundTypes.Length))
                {
                    item.TypeOfDialogueBackground = (DialogueBackgroundType)dialogueBackgroundType;
                }
                switch (item.TypeOfDialogueBackground)
                {
                    case DialogueBackgroundType.Image:
                        if (ImGui.InputText("Dialogue Background Image Path##", ref dialogueBackground, 255))
                        {
                            item.DialogueBackground = dialogueBackground;
                        }
                        break;
                    case DialogueBackgroundType.Video:
                        if (ImGui.InputText("Dialogue Background Video Path##", ref dialogueBackground, 255))
                        {
                            item.DialogueBackground = dialogueBackground;
                        }
                        break;
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
        if (_objectiveInFocus != null)
        {
            var questText = _objectiveInFocus.QuestText;
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
                                if (_subEditorWindow == null)
                                {
                                    _subEditorWindow = new EditorWindow(Plugin);
                                    Plugin.WindowSystem.AddWindow(_subEditorWindow);
                                }
                                _subEditorWindow.IsOpen = true;
                                _subEditorWindow.RoleplayingQuestCreator.EditQuest(roleplayingQuest);
                                _subEditorWindow.RefreshMenus();
                            }
                            ImGui.SameLine();
                            if (ImGui.Button("Import Branching Questline"))
                            {
                                _fileDialogManager.Reset();
                                ImGui.OpenPopup("OpenPathDialog##editorwindow");
                            }
                            if (ImGui.BeginPopup("OpenPathDialog##editorwindow"))
                            {
                                _fileDialogManager.OpenFileDialog("Select quest line data", ".quest", (isOk, file) =>
                                {
                                    if (isOk)
                                    {
                                        item.RoleplayingQuest = _roleplayingQuestCreator.ImportQuestline(file[0]);
                                        item.RoleplayingQuest.ConfigureSubQuest(_roleplayingQuestCreator.CurrentQuest);
                                    }
                                }, 0, "", true);
                                ImGui.EndPopup();
                            }
                            break;
                    }
                }
            }
        }
    }

    private void DrawBranchingChoices()
    {
        if (_objectiveInFocus != null)
        {
            var questText = _objectiveInFocus.QuestText;
            if (questText.Count > 0)
            {
                var branchingChoices = questText[_selectedDialogue].BranchingChoices;
                ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                if (ImGui.ListBox("##branchingChoice", ref _selectedBranchingChoice, _branchingChoices, _branchingChoices.Length, 5))
                {
                    RefreshMenus();
                }
                if (ImGui.Button("Add"))
                {
                    var branchingChoice = new BranchingChoice();
                    branchingChoices.Add(branchingChoice);
                    branchingChoice.RoleplayingQuest.ConfigureSubQuest(_roleplayingQuestCreator.CurrentQuest);
                    branchingChoice.RoleplayingQuest.IsSubQuest = true;
                    _branchingChoices = Utility.FillNewList(branchingChoices.Count, "Choice");
                    _selectedBranchingChoice = branchingChoices.Count - 1;
                    RefreshMenus();
                }
                ImGui.SameLine();
                if (ImGui.Button("Remove"))
                {
                    branchingChoices.RemoveAt(_selectedBranchingChoice);
                    _branchingChoices = Utility.FillNewList(branchingChoices.Count, "Choice");
                    _selectedBranchingChoice = branchingChoices.Count - 1;
                    RefreshMenus();
                }
            }
        }
    }

    private void DrawQuestDialogues()
    {
        if (_objectiveInFocus != null)
        {
            var questText = _objectiveInFocus.QuestText;
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            if (ImGui.ListBox("##questDialogue", ref _selectedDialogue, _dialogues, _dialogues.Length, 15))
            {
                _selectedBranchingChoice = 0;
                RefreshMenus();
            }
            if (ImGui.Button("Add"))
            {
                questText.Add(new QuestText());
                _dialogues = Utility.FillNewList(questText.Count, "Dialogue");
                _selectedDialogue = questText.Count - 1;
                RefreshMenus();
            }
            ImGui.SameLine();
            if (ImGui.Button("Remove"))
            {
                questText.RemoveAt(_selectedDialogue);
                _dialogues = Utility.FillNewList(questText.Count, "Dialogue");
                _selectedDialogue = questText.Count - 1;
                RefreshMenus();
            }
        }
    }

    private void RefreshMenus()
    {
        if (_objectiveInFocus != null)
        {
            var questText = _objectiveInFocus.QuestText;
            _dialogues = Utility.FillNewList(questText.Count, "Dialogue");
            _nodeNames = Utility.FillNewList(_roleplayingQuestCreator.CurrentQuest.QuestObjectives.Count, "Objective");
            if (questText.Count > 0)
            {
                var choices = questText[_selectedDialogue].BranchingChoices;
                if (_selectedBranchingChoice > choices.Count)
                {
                    _selectedBranchingChoice = choices.Count - 1;
                }
                _branchingChoices = Utility.FillNewList(choices.Count, "Choice");
            }
            else
            {
                _branchingChoices = new string[] { };
            }
            if (_subEditorWindow != null)
            {
                _subEditorWindow.RefreshMenus();
                _subEditorWindow.IsOpen = false;
            }
        }
        else
        {
            _branchingChoices = new string[] { };
            _nodeNames = new string[] { };
            _dialogues = new string[] { };
            _dialogues = Utility.FillNewList(0, "Dialogue");
            _nodeNames = Utility.FillNewList(0, "Objective");
            _branchingChoices = Utility.FillNewList(0, "Choice");
            _selectedBranchingChoice = 0;
            _selectedDialogue = 0;
        }
    }

    public void DrawQuestObjectivesRecursive(List<QuestObjective> questObjectives, int level)
    {
        int i = 0;
        List<QuestObjective> invalidatedObjectives = new List<QuestObjective>();
        foreach (var objective in questObjectives)
        {
            if (objective != null && !objective.Invalidate)
            {
                if (level > 0)
                {
                    objective.IsAPrimaryObjective = false;
                }
                if (ImGui.TreeNode(objective.Objective + "##" + i))
                {
                    if (ImGui.Button("Edit##" + i))
                    {
                        _objectiveInFocus = objective;
                        RefreshMenus();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Add Sub Objective##" + i))
                    {
                        objective.SubObjectives.Add(new QuestObjective()
                        {
                            Coordinates = Plugin.ClientState.LocalPlayer.Position,
                            Rotation = new Vector3(0, Utility.ConvertRadiansToDegrees(Plugin.ClientState.LocalPlayer.Rotation), 0),
                            TerritoryId = Plugin.ClientState.TerritoryType
                        });
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Delete##" + i))
                    {
                        objective.Invalidate = true;
                    }
                    DrawQuestObjectivesRecursive(objective.SubObjectives, level + 1);
                    ImGui.TreePop();
                }
            }
            else
            {
                invalidatedObjectives.Add(objective);
            }
            i++;
        }
        foreach (var objective in invalidatedObjectives)
        {
            questObjectives.Remove(objective);
        }
    }
    private void DrawQuestObjectives()
    {
        DrawQuestObjectivesRecursive(_roleplayingQuestCreator.CurrentQuest.QuestObjectives, 0);
        if (ImGui.Button("Add Dominant Objective"))
        {
            _npcTransformEditorWindow.RefreshMenus();
            _roleplayingQuestCreator.AddQuestObjective(new QuestObjective()
            {
                Coordinates = Plugin.ClientState.LocalPlayer.Position,
                Rotation = new Vector3(0, Utility.ConvertRadiansToDegrees(Plugin.ClientState.LocalPlayer.Rotation), 0),
                TerritoryId = Plugin.ClientState.TerritoryType
            });
            _nodeNames = Utility.FillNewList(_roleplayingQuestCreator.CurrentQuest.QuestObjectives.Count, "Objective");
            RefreshMenus();
        }
        //ImGui.SameLine();
        //if (ImGui.Button("Remove## Objective"))
        //{
        //    _roleplayingQuestCreator.CurrentQuest.QuestObjectives.RemoveAt(_selectedObjectiveNode);
        //    _nodeNames = Utility.FillNewList(_roleplayingQuestCreator.CurrentQuest.QuestObjectives.Count, "Objective");
        //    RefreshMenus();
        //}
    }

    private void OpenBranchingQuest(RoleplayingQuest roleplayingQuest)
    {
        _subEditorWindow.RoleplayingQuestCreator.EditQuest(roleplayingQuest);
        if (roleplayingQuest.QuestObjectives.Count > 0)
        {
            _objectiveInFocus = roleplayingQuest.QuestObjectives[0];
        }
        else
        {
            _objectiveInFocus = null;
        }
        _subEditorWindow.RefreshMenus();
    }
}
