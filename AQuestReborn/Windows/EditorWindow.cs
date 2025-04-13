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
using static RoleplayingQuestCore.QuestEvent;
using AQuestReborn;
using System.IO;
using System.Speech.Recognition;
using Lumina.Excel.Sheets;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using McdfDataImporter;
using LanguageConversionProxy;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

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
    private int _selectedEvent;
    private int _selectedBranchingChoice;
    private string[] _branchingChoices = new string[] { };
    private string[] _boxStyles = new string[] {
        "Normal", "Style2", "Telepathic", "Omicron/Machine", "Shout",
        "Written Lore", "Monster/Creature", "Dragon/Linkpearl", "System/Ascian" };
    private QuestObjective _objectiveInFocus;
    private float _globalScale;
    private bool _shiftModifierHeld;
    private bool _isCreatingAppearance;

    public RoleplayingQuestCreator RoleplayingQuestCreator { get => _roleplayingQuestCreator; set => _roleplayingQuestCreator = value; }

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public EditorWindow(Plugin plugin)
        : base("Quest Creator##" + Guid.NewGuid().ToString(), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize)
    {
        Size = new Vector2(1500, 1200);
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
        _objectiveInFocus = null;
        RefreshMenus();
    }

    public void Reset()
    {
        _objectiveInFocus = null;
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
        _globalScale = ImGuiHelpers.GlobalScale;
        _shiftModifierHeld = ImGui.GetIO().KeyShift;
        _fileDialogManager.Draw();
        if (!_roleplayingQuestCreator.CurrentQuest.IsSubQuest)
        {
            if (ImGui.Button(Translator.LocalizeUI("Save Quest")))
            {
                PersistQuest();
            }
            ImGui.SameLine();
            if (ImGui.Button(Translator.LocalizeUI("New Quest")))
            {
                _roleplayingQuestCreator.EditQuest(new RoleplayingQuest());
                RefreshMenus();
            }
            ImGui.SameLine();
            if (ImGui.Button(Translator.LocalizeUI("Tutorial")))
            {
                ProcessStartInfo ProcessInfo = new ProcessStartInfo();
                Process Process = new Process();
                ProcessInfo = new ProcessStartInfo(Translator.LocalizeUI("https://www.youtube.com/watch?v=JJM9aHRHkDw"));
                ProcessInfo.UseShellExecute = true;
                Process = Process.Start(ProcessInfo);
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
                var contentRatingTypes = Translator.LocalizeTextArray(Enum.GetNames(typeof(QuestContentRating)));
                var questRewardTypes = Translator.LocalizeTextArray(Enum.GetNames(typeof(QuestRewardType)));
                var questLanguage = (int)_roleplayingQuestCreator.CurrentQuest.QuestLanguage;

                var questStartTitleCard = _roleplayingQuestCreator.CurrentQuest.QuestStartTitleCard;
                var questEndTitleCard = _roleplayingQuestCreator.CurrentQuest.QuestEndTitleCard;
                var questStartTitleSound = _roleplayingQuestCreator.CurrentQuest.QuestStartTitleSound;
                var questEndTitleSound = _roleplayingQuestCreator.CurrentQuest.QuestEndTitleSound;
                var hasQuestAcceptancePopup = _roleplayingQuestCreator.CurrentQuest.HasQuestAcceptancePopup;


                ImGui.BeginTable("##Info Table", 2);
                ImGui.TableSetupColumn(Translator.LocalizeUI("Info 1"), ImGuiTableColumnFlags.WidthFixed, 400);
                ImGui.TableSetupColumn(Translator.LocalizeUI("Info 2"), ImGuiTableColumnFlags.WidthStretch, 600);
                ImGui.TableHeadersRow();
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                if (ImGui.Combo(Translator.LocalizeUI("Language"), ref questLanguage, Translator.LanguageStrings, Translator.LanguageStrings.Length))
                {
                    _roleplayingQuestCreator.CurrentQuest.QuestLanguage = (LanguageEnum)questLanguage;
                }
                if (ImGui.InputText(Translator.LocalizeUI("Author##"), ref questAuthor, 255))
                {
                    _roleplayingQuestCreator.CurrentQuest.QuestAuthor = questAuthor;
                }
                if (ImGui.InputText(Translator.LocalizeUI("Quest Name##"), ref questName, 255))
                {
                    _roleplayingQuestCreator.CurrentQuest.QuestName = questName;
                }
                if (ImGui.InputText(Translator.LocalizeUI("Quest Description##"), ref questDescription, 56))
                {
                    _roleplayingQuestCreator.CurrentQuest.QuestDescription = questDescription;
                }
                if (ImGui.InputText(Translator.LocalizeUI("Quest Thumbnail##"), ref questThumbnail, 255))
                {
                    _roleplayingQuestCreator.CurrentQuest.QuestThumbnailPath = questThumbnail;
                }
                if (ImGui.Combo(Translator.LocalizeUI("Content Rating##"), ref contentRating, contentRatingTypes, contentRatingTypes.Length))
                {
                    _roleplayingQuestCreator.CurrentQuest.ContentRating = (QuestContentRating)contentRating;
                }
                if (ImGui.Checkbox(Translator.LocalizeUI("Has Quest Acceptance Popup"), ref hasQuestAcceptancePopup))
                {
                    _roleplayingQuestCreator.CurrentQuest.HasQuestAcceptancePopup = hasQuestAcceptancePopup;
                }
                ImGui.TableSetColumnIndex(1);
                if (ImGui.InputText(Translator.LocalizeUI("Quest Start Title Card##"), ref questStartTitleCard, 255))
                {
                    _roleplayingQuestCreator.CurrentQuest.QuestStartTitleCard = questStartTitleCard;
                }
                if (ImGui.InputText(Translator.LocalizeUI("Quest End Title Card##"), ref questEndTitleCard, 255))
                {
                    _roleplayingQuestCreator.CurrentQuest.QuestEndTitleCard = questEndTitleCard;
                }
                if (ImGui.InputText(Translator.LocalizeUI("Quest Start Title Sound##"), ref questStartTitleSound, 255))
                {
                    _roleplayingQuestCreator.CurrentQuest.QuestStartTitleSound = questStartTitleSound;
                }
                if (ImGui.InputText(Translator.LocalizeUI("Quest End Title Sound##"), ref questEndTitleSound, 255))
                {
                    _roleplayingQuestCreator.CurrentQuest.QuestEndTitleSound = questEndTitleSound;
                }
                if (ImGui.Combo(Translator.LocalizeUI("Quest Reward Type##"), ref questRewardType, questRewardTypes, questRewardTypes.Length))
                {
                    _roleplayingQuestCreator.CurrentQuest.TypeOfReward = (QuestRewardType)questRewardType;
                }
                switch (_roleplayingQuestCreator.CurrentQuest.TypeOfReward)
                {
                    case QuestRewardType.SecretMessage:
                        if (ImGui.InputText(Translator.LocalizeUI("Quest Reward (Secret Message)"), ref questReward, 255))
                        {
                            _roleplayingQuestCreator.CurrentQuest.QuestReward = questReward;
                        }
                        break;
                    case QuestRewardType.OnlineLink:
                        if (ImGui.InputText(Translator.LocalizeUI("Quest Reward (Download Link)"), ref questReward, 255))
                        {
                            _roleplayingQuestCreator.CurrentQuest.QuestReward = questReward;
                        }
                        break;
                    case QuestRewardType.MediaFile:
                        if (ImGui.InputText(Translator.LocalizeUI("Quest Reward (Media File Path)"), ref questReward, 255))
                        {
                            _roleplayingQuestCreator.CurrentQuest.QuestReward = questReward;
                        }
                        break;
                }
                ImGui.EndTable();
                if (ImGui.Button(Translator.LocalizeUI("Edit NPC Appearance Data##")))
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
        ImGui.SameLine();
        if (ImGui.Button(Translator.LocalizeUI("Export for re-use")))
        {
            _fileDialogManager.Reset();
            ImGui.OpenPopup(Translator.LocalizeUI("OpenPathDialog##editorwindow"));
        }
        if (ImGui.BeginPopup(Translator.LocalizeUI("OpenPathDialog##editorwindow")))
        {
            _fileDialogManager.SaveFileDialog(Translator.LocalizeUI("Export quest line data"), ".quest", "", ".quest", (isOk, file) =>
            {
                if (isOk)
                {
                    _roleplayingQuestCreator.SaveQuestline(_roleplayingQuestCreator.CurrentQuest, file);
                }
            }, "", true);
            ImGui.EndPopup();
        }
        ImGui.BeginTable("##Editor Table", 2);
        ImGui.TableSetupColumn(Translator.LocalizeUI("Objective List"), ImGuiTableColumnFlags.WidthFixed, 300);
        ImGui.TableSetupColumn(Translator.LocalizeUI("Objective Editor"), ImGuiTableColumnFlags.WidthStretch, 600);
        ImGui.TableHeadersRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        DrawQuestObjectives();
        ImGui.TableSetColumnIndex(1);
        DrawQuestNodeEditor();
        ImGui.EndTable();
    }

    private void PersistQuest()
    {
        string questPath = Path.Combine(Plugin.Configuration.QuestInstallFolder, _roleplayingQuestCreator.CurrentQuest.QuestName);
        _roleplayingQuestCreator.SaveQuest(questPath);
        Plugin.RoleplayingQuestManager.AddQuest(Path.Combine(questPath, "main.quest"), false, true);
        Plugin.AQuestReborn.RefreshNpcs(Plugin.ClientState.TerritoryType, _roleplayingQuestCreator.CurrentQuest.QuestId, true);
        Plugin.AQuestReborn.RefreshMapMarkers();
    }

    private void DrawQuestNodeEditor()
    {
        if (_objectiveInFocus != null)
        {
            var questObjective = _objectiveInFocus;
            var territoryId = questObjective.TerritoryId;
            var territoryDiscriminator = questObjective.TerritoryDiscriminator;
            var usesTerritoryDiscriminator = questObjective.UsesTerritoryDiscriminator;
            var objective = questObjective.Objective;
            var coordinates = questObjective.Coordinates;
            var questText = questObjective.QuestText;
            var questPointType = (int)questObjective.TypeOfQuestPoint;
            var objectiveStatusType = (int)questObjective.ObjectiveStatus;
            var objectiveTriggerType = (int)questObjective.TypeOfObjectiveTrigger;
            var questPointTypes = Translator.LocalizeTextArray(Enum.GetNames(typeof(QuestPointType)));
            var objectiveStatusTypes = Translator.LocalizeTextArray(Enum.GetNames(typeof(ObjectiveStatusType)));
            var objectiveTriggerTypes = Translator.LocalizeTextArray(Enum.GetNames(typeof(ObjectiveTriggerType)));
            var triggerText = questObjective.TriggerText;
            var objectiveImmediatelySatisfiesParent = questObjective.ObjectiveImmediatelySatisfiesParent;
            var maximum3dIndicatorDistance = questObjective.Maximum3dIndicatorDistance;
            var dontShowOnMap = questObjective.DontShowOnMap;
            var playerPositionIsLockedDuringEvents = questObjective.PlayerPositionIsLockedDuringEvents;
            var triggerMonsterId = questObjective.TriggerMonsterIndex;
            var objectiveTriggersCutscene = questObjective.ObjectiveTriggersCutscene;

            ImGui.SetNextItemWidth(400);
            ImGui.LabelText("##objectiveIdLabel", Translator.LocalizeUI($"Objective Id: ") + questObjective.Id);
            ImGui.SameLine();
            if (ImGui.Button(Translator.LocalizeUI("Copy Id To Clipboard")))
            {
                ImGui.SetClipboardText(questObjective.Id.Trim());
            }
            ImGui.SameLine();
            if (ImGui.Button(Translator.LocalizeUI("Set Quest Objective Coordinates")))
            {
                questObjective.Coordinates = Plugin.ObjectTable.LocalPlayer.Position;
                questObjective.TerritoryId = Plugin.ClientState.TerritoryType;
                questObjective.TerritoryDiscriminator = Plugin.AQuestReborn.Discriminator;
            }
            ImGui.SetNextItemWidth(200);
            ImGui.LabelText("##coordinatesLabel", Translator.LocalizeUI($"Coordinates:") + $" X:{Math.Round(questObjective.Coordinates.X)}," +
                $" Y:{Math.Round(questObjective.Coordinates.Y)}," +
                $" Z:{Math.Round(questObjective.Coordinates.Z)}");
            ImGui.SetNextItemWidth(125);
            ImGui.SameLine();
            ImGui.LabelText("##territoryLabel", Translator.LocalizeUI($"Territory Id:") + $" {questObjective.TerritoryId}");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(300);
            ImGui.LabelText("##discriminatorLabel", Translator.LocalizeUI($"Discriminator: ") + questObjective.TerritoryDiscriminator);
            ImGui.SetNextItemWidth(110);
            if (ImGui.InputFloat(Translator.LocalizeUI("Maximum Indicator Distance##"), ref maximum3dIndicatorDistance))
            {
                questObjective.Maximum3dIndicatorDistance = maximum3dIndicatorDistance;
            }
            ImGui.SameLine();
            if (ImGui.Checkbox(Translator.LocalizeUI("Dont Show On Map##"), ref dontShowOnMap))
            {
                questObjective.DontShowOnMap = dontShowOnMap;
            }
            ImGui.SameLine();
            if (ImGui.Checkbox(Translator.LocalizeUI("Lock to server/ward/plot/room##"), ref usesTerritoryDiscriminator))
            {
                questObjective.UsesTerritoryDiscriminator = usesTerritoryDiscriminator;
            }
            if (!questObjective.IsAPrimaryObjective)
            {
                if (ImGui.Checkbox(Translator.LocalizeUI("Immediately Satisfies Parent Objective##"), ref objectiveImmediatelySatisfiesParent))
                {
                    questObjective.ObjectiveImmediatelySatisfiesParent = objectiveImmediatelySatisfiesParent;
                }
            }
            if (ImGui.InputText(Translator.LocalizeUI("Objective Text##"), ref objective, 500))
            {
                questObjective.Objective = objective;
            }
            ImGui.SetNextItemWidth(110);
            if (ImGui.Combo(Translator.LocalizeUI("Quest Point Type##"), ref questPointType, questPointTypes, questPointTypes.Length))
            {
                questObjective.TypeOfQuestPoint = (QuestPointType)questPointType;
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(110);
            if (ImGui.Combo(Translator.LocalizeUI("Objective Quest Status Type##"), ref objectiveStatusType, objectiveStatusTypes, objectiveStatusTypes.Length))
            {
                questObjective.ObjectiveStatus = (ObjectiveStatusType)objectiveStatusType;
            }
            if (ImGui.Combo(Translator.LocalizeUI("Objective Trigger Type##"), ref objectiveTriggerType, objectiveTriggerTypes, objectiveTriggerTypes.Length))
            {
                questObjective.TypeOfObjectiveTrigger = (ObjectiveTriggerType)objectiveTriggerType;
            }
            switch (questObjective.TypeOfObjectiveTrigger)
            {
                case ObjectiveTriggerType.DoEmote:
                    if (ImGui.InputText(Translator.LocalizeUI("Emote Id##"), ref triggerText, 500))
                    {
                        questObjective.TriggerText = triggerText;
                    }
                    break;
                case ObjectiveTriggerType.SayPhrase:
                    if (ImGui.InputText(Translator.LocalizeUI("Say Phrase##"), ref triggerText, 500))
                    {
                        questObjective.TriggerText = triggerText;
                    }
                    break;
                case ObjectiveTriggerType.KillEnemy:
                    if (ImGui.InputText(Translator.LocalizeUI("Enemy Name##"), ref triggerText, 500))
                    {
                        questObjective.TriggerText = triggerText;
                        questObjective.TriggerMonsterIndex = Plugin.AQuestReborn.GetMonsterIndex(triggerText);
                    }
                    ImGui.Text(Translator.LocalizeUI("Enemy Id:") + " " + questObjective.TriggerMonsterIndex);
                    break;
                case ObjectiveTriggerType.BoundingTrigger:
                    var minimumX = questObjective.Collider.MinimumX;
                    var maximumX = questObjective.Collider.MaximumX;
                    var minimumY = questObjective.Collider.MinimumY;
                    var maximumY = questObjective.Collider.MaximumY;
                    var minimumZ = questObjective.Collider.MinimumZ;
                    var maximumZ = questObjective.Collider.MaximumZ;
                    ImGui.TextWrapped(Translator.LocalizeUI($"Min ") + " X: " +
                        $"{minimumX}, " +
                        Translator.LocalizeUI($"Max") + " X: " +
                        $"{maximumX}, " +
                        Translator.LocalizeUI($"Min") + " Y: " +
                        $"{minimumY}, " +
                        Translator.LocalizeUI($"Max") + " Y: " +
                        $"{maximumY}, " +
                        Translator.LocalizeUI($"Min") + " Z: " +
                        $"{minimumZ}, " +
                        Translator.LocalizeUI($"Max") + " Z: " +
                        $"{maximumZ}");
                    if (ImGui.Button(Translator.LocalizeUI("Set Min") + " XZ##"))
                    {
                        questObjective.Collider.MinimumX = Plugin.ObjectTable.LocalPlayer.Position.X;
                        questObjective.Collider.MinimumZ = Plugin.ObjectTable.LocalPlayer.Position.Z;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button(Translator.LocalizeUI("Set Max") + " XZ##"))
                    {
                        if (Plugin.ObjectTable.LocalPlayer.Position.X < minimumX)
                        {
                            questObjective.Collider.MaximumX = questObjective.Collider.MinimumX;
                            questObjective.Collider.MinimumX = Plugin.ObjectTable.LocalPlayer.Position.X;
                        }
                        else
                        {
                            questObjective.Collider.MaximumX = Plugin.ObjectTable.LocalPlayer.Position.X;
                        }
                        if (Plugin.ObjectTable.LocalPlayer.Position.Z < minimumZ)
                        {
                            questObjective.Collider.MaximumZ = questObjective.Collider.MinimumZ;
                            questObjective.Collider.MinimumZ = Plugin.ObjectTable.LocalPlayer.Position.Z;
                        }
                        else
                        {
                            questObjective.Collider.MaximumZ = Plugin.ObjectTable.LocalPlayer.Position.Z;
                        }
                    }
                    ImGui.SameLine();
                    if (ImGui.Button(Translator.LocalizeUI("Set Min") + " Y##"))
                    {
                        questObjective.Collider.MinimumY = Plugin.ObjectTable.LocalPlayer.Position.Y - 5;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button(Translator.LocalizeUI("Set Max") + " Y##"))
                    {
                        questObjective.Collider.MaximumY = Plugin.ObjectTable.LocalPlayer.Position.Y;
                    }
                    break;
            }
            if (ImGui.Button(Translator.LocalizeUI("Edit NPC Transform Data##")))
            {
                if (_npcTransformEditorWindow != null)
                {
                    _npcTransformEditorWindow.SetEditingQuest(questObjective);
                    _npcTransformEditorWindow.IsOpen = true;
                }
            }
            if (questObjective.IsAPrimaryObjective)
            {
                ImGui.SameLine();
                if (ImGui.Button(Translator.LocalizeUI("Preview Quest Objective##")))
                {
                    Plugin.RoleplayingQuestManager.SkipToObjective(_roleplayingQuestCreator.CurrentQuest, questObjective.Index);
                    PersistQuest();
                }
            }
            ImGui.SameLine();
            if (ImGui.Checkbox(Translator.LocalizeUI("Player Position Is Locked During Events"), ref playerPositionIsLockedDuringEvents))
            {
                questObjective.PlayerPositionIsLockedDuringEvents = playerPositionIsLockedDuringEvents;
            }
            ImGui.SameLine();
            if (ImGui.Checkbox(Translator.LocalizeUI("Objective Triggers Cutscene"), ref objectiveTriggersCutscene))
            {
                questObjective.ObjectiveTriggersCutscene = objectiveTriggersCutscene;
            }
            ImGui.BeginTable("##Event Table", 2);
            ImGui.TableSetupColumn(Translator.LocalizeUI("Event List"), ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn(Translator.LocalizeUI("Event Editor"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            DrawQuestEvents();
            ImGui.TableSetColumnIndex(1);
            DrawQuestEventEditor();
            ImGui.EndTable();
        }
    }

    private void DrawQuestEventEditor()
    {
        if (_objectiveInFocus != null)
        {
            var questEvent = _objectiveInFocus.QuestText;
            if (questEvent.Count > 0)
            {
                if (_selectedEvent > questEvent.Count || _selectedEvent < 0)
                {
                    _selectedEvent = 0;
                }
                var item = questEvent[_selectedEvent];
                var dialogueCondition = (int)item.ConditionForDialogueToOccur;
                var objectiveIdToComplete = item.ObjectiveIdToComplete;
                var faceExpression = item.FaceExpression;
                var bodyExpression = item.BodyExpression;
                var faceExpressionPlayer = item.FaceExpressionPlayer;
                var bodyExpressionPlayer = item.BodyExpressionPlayer;
                var npcAlias = item.NpcAlias;
                var npcName = item.NpcName;
                var dialogue = item.Dialogue;
                var boxStyle = item.DialogueBoxStyle;
                var dialogueAudio = item.DialogueAudio;
                var eventBackgroundType = (int)item.TypeOfEventBackground;
                var eventBackground = item.EventBackground;
                var eventEndBehaviour = (int)item.EventEndBehaviour;
                var eventNumberToSkipTo = item.EventNumberToSkipTo;
                var objectiveNumberToSkipTo = item.ObjectiveNumberToSkipTo;
                var eventEndTypes = Translator.LocalizeTextArray(Enum.GetNames(typeof(QuestEvent.EventBehaviourType)));
                var eventBackgroundTypes = Translator.LocalizeTextArray(Enum.GetNames(typeof(QuestEvent.EventBackgroundType)));
                var eventConditionTypes = Translator.LocalizeTextArray(Enum.GetNames(typeof(QuestEvent.EventConditionType)));
                var eventPlayerAppearanceApplicationTypes = Translator.LocalizeTextArray(Enum.GetNames(typeof(QuestEvent.AppearanceSwapType)));
                var eventPlayerMovementTypes = Translator.LocalizeTextArray(Enum.GetNames(typeof(QuestEvent.EventMovementType)));
                var eventPlayerMovementAnimationTypes = Translator.LocalizeTextArray(Enum.GetNames(typeof(QuestEvent.EventMovementAnimation)));
                var appearanceSwap = item.AppearanceSwap;
                var playerAppearanceSwap = item.PlayerAppearanceSwap;
                var playerAppearanceSwapType = (int)item.PlayerAppearanceSwapType;
                var loopAnimation = item.LoopAnimation;
                var loopAnimationPlayer = item.LoopAnimationPlayer;
                var timeLimit = item.TimeLimit;
                var eventHasNoReading = item.EventHasNoReading;
                var looksAtPlayerDuringEvent = item.LooksAtPlayerDuringEvent;
                var eventSetsNewNpcPosition = item.EventSetsNewNpcCoordinates;
                var eventSetsNewCutscenePlayerPosition = item.EventSetsNewCutscenePlayerCoordinates;
                var npcMovementPosition = item.NpcMovementPosition;
                var npcMovementRotation = item.NpcMovementRotation;
                var npcMovementType = (int)item.NpcEventMovementType;
                var npcMovementTime = item.NpcMovementTime;
                var npcMovementAnimation = (int)item.NpcEventMovementAnimation;

                var cutscenePlayerMovementPosition = item.CutscenePlayerMovementPosition;
                var cutscenePlayerMovementRotation = item.CutscenePlayerMovementRotation;
                var cutscenePlayerMovementType = (int)item.CutscenePlayerMovementType;
                var cutscenePlayerMovementTime = item.CutscenePlayerMovementTime;
                var cutscenePlayerMovementAnimation = (int)item.CutscenePlayerEventMovementAnimation;

                var cameraLooksAtTalkingNpc = item.CameraLooksAtTalkingNpc;
                var cameraUsesDolly = item.CameraUsesDolly;
                var cameraStartPosition = item.CameraStartPosition;
                var cameraStartRotation = item.CameraStartRotation;
                var cameraEndPosition = item.CameraEndPosition;
                var cameraEndRotation = item.CameraEndRotation;
                var cameraIsNotAffectedDuringEvent = item.CameraIsNotAffectedDuringEvent;
                var cameraDollySpeed = (int)item.CameraDollySpeed;
                var cameraStartingZoom = item.CameraStartingZoom;
                var cameraEndZoom = item.CameraEndingZoom;
                var cameraStartFov = item.CameraStartingFov;
                var cameraEndFov = item.CameraEndingFov;

                var dialogueWindowIsHidden = item.DialogueWindowIsHidden;


                if (ImGui.BeginTabBar("Event Editor Tabs"))
                {
                    if (ImGui.BeginTabItem(Translator.LocalizeUI("Narrative")))
                    {
                        if (ImGui.Combo(Translator.LocalizeUI("Condition For Event To Occur##"), ref dialogueCondition, eventConditionTypes, eventConditionTypes.Length))
                        {
                            item.ConditionForDialogueToOccur = (EventConditionType)dialogueCondition;
                        }
                        switch (item.ConditionForDialogueToOccur)
                        {
                            case EventConditionType.None:
                                break;
                            case EventConditionType.CompletedSpecificObjectiveId:
                                if (ImGui.InputText(Translator.LocalizeUI("Objective Id To Complete##"), ref objectiveIdToComplete, 40))
                                {
                                    item.ObjectiveIdToComplete = objectiveIdToComplete;
                                }
                                break;
                            case EventConditionType.PlayerClanId:
                                if (ImGui.InputText(Translator.LocalizeUI("Clan Id Required##"), ref objectiveIdToComplete, 40))
                                {
                                    item.ObjectiveIdToComplete = objectiveIdToComplete;
                                }
                                break;
                            case EventConditionType.PlayerPhysicalPresentationId:
                                if (ImGui.InputText(Translator.LocalizeUI("(Masculine: 0, Feminine: 1)##"), ref objectiveIdToComplete, 40))
                                {
                                    item.ObjectiveIdToComplete = objectiveIdToComplete;
                                }
                                break;
                            case EventConditionType.PlayerClassId:
                                if (ImGui.InputText(Translator.LocalizeUI("Player Class Id (SMN, RPR, WHM, etc)##"), ref objectiveIdToComplete, 40))
                                {
                                    item.ObjectiveIdToComplete = objectiveIdToComplete;
                                }
                                break;
                            case EventConditionType.PlayerOutfitTopId:
                                if (ImGui.InputText(Translator.LocalizeUI("Player Outfit Top Id##"), ref objectiveIdToComplete, 40))
                                {
                                    item.ObjectiveIdToComplete = objectiveIdToComplete;
                                }
                                break;
                            case EventConditionType.PlayerOutfitBottomId:
                                if (ImGui.InputText(Translator.LocalizeUI("Player Outfit Bottom Id##"), ref objectiveIdToComplete, 40))
                                {
                                    item.ObjectiveIdToComplete = objectiveIdToComplete;
                                }
                                break;
                            case EventConditionType.TimeLimitFailure:
                                break;
                        }
                        ImGui.SetNextItemWidth(150);
                        if (ImGui.InputText(Translator.LocalizeUI("Npc Alias##"), ref npcAlias, 40))
                        {
                            item.NpcAlias = npcAlias;
                        }
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(150);
                        if (ImGui.InputText(Translator.LocalizeUI("Npc Name##"), ref npcName, 40))
                        {
                            item.NpcName = npcName;
                        }
                        if (ImGui.InputText(Translator.LocalizeUI("Dialogue##"), ref dialogue, 500))
                        {
                            item.Dialogue = dialogue;
                        }
                        if (ImGui.InputText(Translator.LocalizeUI("Dialogue Audio Path##"), ref dialogueAudio, 255))
                        {
                            item.DialogueAudio = dialogueAudio;
                        }
                        var boxStyles = Translator.LocalizeTextArray(_boxStyles);
                        if (ImGui.Combo(Translator.LocalizeUI("Box Style##"), ref boxStyle, boxStyles, _boxStyles.Length))
                        {
                            item.DialogueBoxStyle = boxStyle;
                        }
                        ImGui.SetNextItemWidth(100);
                        if (ImGui.InputInt(Translator.LocalizeUI("NPC Face Expression Id##"), ref faceExpression))
                        {
                            item.FaceExpression = faceExpression;
                        }
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(100);
                        if (ImGui.InputInt(Translator.LocalizeUI("NPC Body Expression Id##"), ref bodyExpression))
                        {
                            item.BodyExpression = bodyExpression;
                        }
                        ImGui.SameLine();
                        if (ImGui.Checkbox(Translator.LocalizeUI("Loop Animation##"), ref loopAnimation))
                        {
                            item.LoopAnimation = loopAnimation;
                        }

                        ImGui.SetNextItemWidth(100);
                        if (ImGui.InputInt(Translator.LocalizeUI("Player Face Expression Id##"), ref faceExpressionPlayer))
                        {
                            item.FaceExpressionPlayer = faceExpressionPlayer;
                        }
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(100);
                        if (ImGui.InputInt(Translator.LocalizeUI("Player Body Expression Id##"), ref bodyExpressionPlayer))
                        {
                            item.BodyExpressionPlayer = bodyExpressionPlayer;
                        }
                        ImGui.SameLine();
                        if (ImGui.Checkbox(Translator.LocalizeUI("Loop Player Animation##"), ref loopAnimationPlayer))
                        {
                            item.LoopAnimationPlayer = loopAnimationPlayer;
                        }

                        if (ImGui.Combo(Translator.LocalizeUI("Event Background Type##"), ref eventBackgroundType, eventBackgroundTypes, eventBackgroundTypes.Length))
                        {
                            item.TypeOfEventBackground = (EventBackgroundType)eventBackgroundType;
                        }
                        switch (item.TypeOfEventBackground)
                        {
                            case EventBackgroundType.Image:
                            case EventBackgroundType.ImageTransparent:
                                if (ImGui.InputText(Translator.LocalizeUI("Event Background Image Path##"), ref eventBackground, 255))
                                {
                                    item.EventBackground = eventBackground;
                                }
                                break;
                            case EventBackgroundType.Video:
                                if (ImGui.InputText(Translator.LocalizeUI("Event Background Video Path##"), ref eventBackground, 255))
                                {
                                    item.EventBackground = eventBackground;
                                }
                                break;
                        }
                        if (ImGui.Combo(Translator.LocalizeUI("Event End Behaviour##"), ref eventEndBehaviour, eventEndTypes, eventEndTypes.Length))
                        {
                            item.EventEndBehaviour = (EventBehaviourType)eventEndBehaviour;
                        }

                        switch (item.EventEndBehaviour)
                        {
                            case EventBehaviourType.EventSkipsToDialogueNumber:
                                if (ImGui.InputInt(Translator.LocalizeUI("Event Number To Skip To##"), ref eventNumberToSkipTo))
                                {
                                    item.EventNumberToSkipTo = eventNumberToSkipTo;
                                }
                                break;
                            case EventBehaviourType.EventEndsEarlyWhenHitAndSkipsToObjective:
                                if (ImGui.InputInt(Translator.LocalizeUI("Objective Number To Skip To##"), ref objectiveNumberToSkipTo))
                                {
                                    item.ObjectiveNumberToSkipTo = objectiveNumberToSkipTo;
                                }
                                break;
                            case EventBehaviourType.EventEndsEarlyWhenHitAndStartsTimer:
                            case EventBehaviourType.StartsTimer:
                                if (ImGui.InputInt(Translator.LocalizeUI("Time Limit (Milliseconds)##"), ref timeLimit))
                                {
                                    item.TimeLimit = timeLimit;
                                }
                                break;
                        }
                        if (ImGui.Checkbox(Translator.LocalizeUI("Event Has No Reading##"), ref eventHasNoReading))
                        {
                            item.EventHasNoReading = eventHasNoReading;
                        }
                        if (ImGui.Checkbox(Translator.LocalizeUI("Dialogue Window Is Hidden##"), ref dialogueWindowIsHidden))
                        {
                            item.DialogueWindowIsHidden = dialogueWindowIsHidden;
                        }
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem(Translator.LocalizeUI("Branching Choices## we're unique")))
                    {
                        DrawBranchingChoicesMenu();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem(Translator.LocalizeUI("Appearance Swap##we're unique and such")))
                    {
                        if (ImGui.InputText(Translator.LocalizeUI("Npc Appearance Swap##"), ref appearanceSwap, 4000))
                        {
                            item.AppearanceSwap = appearanceSwap;
                        }
                        if (_isCreatingAppearance)
                        {
                            ImGui.BeginDisabled();
                        }
                        if (ImGui.Button(Translator.LocalizeUI(_isCreatingAppearance ? "Creating Appearance Please Wait" : "Create NPC Appearance From Current Player Appearance##")))
                        {
                            Task.Run(() =>
                            {
                                _isCreatingAppearance = true;
                                string mcdfName = npcName + "-" + Guid.NewGuid().ToString() + ".mcdf";
                                string questPath = Path.Combine(Plugin.Configuration.QuestInstallFolder, _roleplayingQuestCreator.CurrentQuest.QuestName);
                                string mcdfPath = Path.Combine(questPath, mcdfName);
                                Directory.CreateDirectory(questPath);
                                AppearanceAccessUtils.AppearanceManager.CreateMCDF(mcdfPath);
                                Plugin.EditorWindow.RoleplayingQuestCreator.SaveQuest(questPath);
                                item.AppearanceSwap = mcdfName;
                                _isCreatingAppearance = false;
                            });
                        }
                        if (_isCreatingAppearance)
                        {
                            ImGui.EndDisabled();
                        }
                        if (ImGui.InputText(Translator.LocalizeUI("Player Appearance Swap##"), ref playerAppearanceSwap, 4000))
                        {
                            item.PlayerAppearanceSwap = playerAppearanceSwap;
                        }
                        if (ImGui.Combo(Translator.LocalizeUI("Player Appearance Swap Type"), ref playerAppearanceSwapType, eventPlayerAppearanceApplicationTypes, eventPlayerAppearanceApplicationTypes.Length))
                        {
                            item.PlayerAppearanceSwapType = (AppearanceSwapType)playerAppearanceSwapType;
                        }
                        if (_isCreatingAppearance)
                        {
                            ImGui.BeginDisabled();
                        }
                        if (ImGui.Button(Translator.LocalizeUI(_isCreatingAppearance ? "Creating Appearance Please Wait" : "Create Player Appearance From Current Player Appearance##")))
                        {
                            Task.Run(() =>
                            {
                                _isCreatingAppearance = true;
                                string mcdfName = npcName + "-" + Guid.NewGuid().ToString() + ".mcdf";
                                string questPath = Path.Combine(Plugin.Configuration.QuestInstallFolder, _roleplayingQuestCreator.CurrentQuest.QuestName);
                                string mcdfPath = Path.Combine(questPath, mcdfName);
                                Directory.CreateDirectory(questPath);
                                AppearanceAccessUtils.AppearanceManager.CreateMCDF(mcdfPath);
                                Plugin.EditorWindow.RoleplayingQuestCreator.SaveQuest(questPath);
                                item.PlayerAppearanceSwap = mcdfName;
                                _isCreatingAppearance = false;
                            });
                        }
                        if (_isCreatingAppearance)
                        {
                            ImGui.EndDisabled();
                        }
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem(Translator.LocalizeUI("Positioning## we're unique")))
                    {
                        if (ImGui.Checkbox(Translator.LocalizeUI("Looks At Player During Event"), ref looksAtPlayerDuringEvent))
                        {
                            item.LooksAtPlayerDuringEvent = looksAtPlayerDuringEvent;
                        }
                        if (ImGui.Checkbox(Translator.LocalizeUI("Event Sets New NPC Position"), ref eventSetsNewNpcPosition))
                        {
                            item.EventSetsNewNpcCoordinates = eventSetsNewNpcPosition;
                        }
                        if (eventSetsNewNpcPosition)
                        {
                            if (ImGui.DragFloat3(Translator.LocalizeUI("Npc Movement Position"), ref npcMovementPosition))
                            {
                                item.NpcMovementPosition = npcMovementPosition;
                            }
                            if (ImGui.DragFloat3(Translator.LocalizeUI("Npc Movement Rotation"), ref npcMovementRotation))
                            {
                                item.NpcMovementRotation = npcMovementRotation;
                            }
                            if (ImGui.Combo(Translator.LocalizeUI("Npc Movement Type##npc"), ref npcMovementType, eventPlayerMovementTypes, eventPlayerMovementTypes.Length))
                            {
                                item.NpcEventMovementType = (QuestEvent.EventMovementType)npcMovementType;
                            }
                            switch (item.NpcEventMovementType)
                            {
                                case EventMovementType.Lerp:

                                    break;
                                case EventMovementType.FixedTime:
                                    if (ImGui.InputInt(Translator.LocalizeUI("Time To Complete Travel (In Milliseconds)##npc"), ref npcMovementTime))
                                    {
                                        item.NpcMovementTime = npcMovementTime;
                                    }
                                    if (ImGui.Combo(Translator.LocalizeUI("Npc Movement Animation##npc"), ref npcMovementAnimation, eventPlayerMovementAnimationTypes, eventPlayerMovementAnimationTypes.Length))
                                    {
                                        item.NpcEventMovementAnimation = (QuestEvent.EventMovementAnimation)npcMovementAnimation;
                                    }
                                    break;
                            }
                            if (ImGui.Button(Translator.LocalizeUI("Set Coordinates Based On Player Position##npc")))
                            {
                                item.NpcMovementPosition = Plugin.ObjectTable.LocalPlayer.Position;
                                item.NpcMovementRotation = new Vector3(0, CoordinateUtility.ConvertRadiansToDegrees(Plugin.ObjectTable.LocalPlayer.Rotation) + 180, 0);
                            }
                        }
                        //if (_objectiveInFocus.ObjectiveTriggersCutscene)
                        //{
                        if (ImGui.Checkbox(Translator.LocalizeUI("Event Sets New Cutscene Player Position"), ref eventSetsNewCutscenePlayerPosition))
                        {
                            item.EventSetsNewCutscenePlayerCoordinates = eventSetsNewCutscenePlayerPosition;
                        }
                        if (eventSetsNewCutscenePlayerPosition)
                        {
                            if (ImGui.DragFloat3(Translator.LocalizeUI("Cutscene Player Movement Position"), ref cutscenePlayerMovementPosition))
                            {
                                item.CutscenePlayerMovementPosition = cutscenePlayerMovementPosition;
                            }
                            if (ImGui.DragFloat3(Translator.LocalizeUI("Cutscene Player Movement Rotation"), ref cutscenePlayerMovementRotation))
                            {
                                item.CutscenePlayerMovementRotation = cutscenePlayerMovementRotation;
                            }
                            if (ImGui.Combo(Translator.LocalizeUI("Cutscene Player Movement Type##cutsceneplayer"), ref cutscenePlayerMovementType, eventPlayerMovementTypes, eventPlayerMovementTypes.Length))
                            {
                                item.CutscenePlayerMovementType = (QuestEvent.EventMovementType)cutscenePlayerMovementType;
                            }
                            switch (item.CutscenePlayerMovementType)
                            {
                                case EventMovementType.Lerp:

                                    break;
                                case EventMovementType.FixedTime:
                                    if (ImGui.InputInt(Translator.LocalizeUI("Time To Complete Travel (In Milliseconds)##cutsceneplayer"), ref cutscenePlayerMovementTime))
                                    {
                                        item.CutscenePlayerMovementTime = cutscenePlayerMovementTime;
                                    }
                                    if (ImGui.Combo(Translator.LocalizeUI("Cutscene Player Movement Animation##npc"), ref cutscenePlayerMovementAnimation, eventPlayerMovementAnimationTypes, eventPlayerMovementAnimationTypes.Length))
                                    {
                                        item.CutscenePlayerEventMovementAnimation = (QuestEvent.EventMovementAnimation)cutscenePlayerMovementAnimation;
                                    }
                                    break;
                            }
                            if (ImGui.Button(Translator.LocalizeUI("Set Coordinates Based On Player Position##cutsceneplayer")))
                            {
                                item.CutscenePlayerMovementPosition = Plugin.ObjectTable.LocalPlayer.Position;
                                item.CutscenePlayerMovementRotation = new Vector3(0, CoordinateUtility.ConvertRadiansToDegrees(Plugin.ObjectTable.LocalPlayer.Rotation) + 180, 0);
                            }
                        }
                        // }
                        ImGui.EndTabItem();
                    }
                    if (_objectiveInFocus.ObjectiveTriggersCutscene)
                    {
                        if (ImGui.BeginTabItem(Translator.LocalizeUI("Camera## we're unique")))
                        {
                            if (ImGui.Checkbox(Translator.LocalizeUI("Camera Is Not Affected During Event"), ref cameraIsNotAffectedDuringEvent))
                            {
                                item.CameraIsNotAffectedDuringEvent = cameraIsNotAffectedDuringEvent;
                            }
                            if (!cameraIsNotAffectedDuringEvent)
                            {
                                if (ImGui.Checkbox(Translator.LocalizeUI("Camera Uses Dolly"), ref cameraUsesDolly))
                                {
                                    item.CameraUsesDolly = cameraUsesDolly;
                                }
                                if (ImGui.Button(Translator.LocalizeUI("Set Camera Position From Current Camera##1")))
                                {
                                    item.CameraStartPosition = CutsceneCamera.Position;
                                    item.CameraStartRotation = CutsceneCamera.Rotation;
                                    item.CameraStartingFov = CutsceneCamera.CameraFov;
                                    item.CameraStartingZoom = CutsceneCamera.CameraZoom;
                                }
                                if (ImGui.InputFloat3(Translator.LocalizeUI("Camera Start Position"), ref cameraStartPosition))
                                {
                                    item.CameraStartPosition = cameraStartPosition;
                                }
                                if (ImGui.InputFloat3(Translator.LocalizeUI("Camera Start Rotation"), ref cameraStartRotation))
                                {
                                    item.CameraStartRotation = cameraStartRotation;
                                }
                                if (ImGui.InputFloat(Translator.LocalizeUI("Camera Starting Field Of View"), ref cameraStartFov))
                                {
                                    item.CameraStartingFov = cameraStartFov;
                                }
                                if (ImGui.InputFloat(Translator.LocalizeUI("Camera Starting Zoom"), ref cameraStartingZoom))
                                {
                                    item.CameraStartingZoom = cameraStartingZoom;
                                }
                                if (cameraUsesDolly)
                                {
                                    if (ImGui.Button(Translator.LocalizeUI("Set Camera Position From Current Camera##2")))
                                    {
                                        item.CameraEndPosition = CutsceneCamera.Position;
                                        item.CameraEndRotation = CutsceneCamera.Rotation;
                                        item.CameraEndingFov = CutsceneCamera.CameraFov;
                                        item.CameraEndingZoom = CutsceneCamera.CameraZoom;
                                    }
                                    if (ImGui.InputFloat3(Translator.LocalizeUI("Camera End Position"), ref cameraEndPosition))
                                    {
                                        item.CameraEndPosition = cameraEndPosition;
                                    }
                                    if (ImGui.InputFloat3(Translator.LocalizeUI("Camera End Rotation"), ref cameraEndRotation))
                                    {
                                        item.CameraEndRotation = cameraEndRotation;
                                    }
                                    if (ImGui.InputFloat(Translator.LocalizeUI("Camera Starting Field Of View"), ref cameraEndFov))
                                    {
                                        item.CameraEndingFov = cameraEndFov;
                                    }
                                    if (ImGui.InputFloat(Translator.LocalizeUI("Camera Starting Zoom"), ref cameraEndZoom))
                                    {
                                        item.CameraEndingZoom = cameraEndZoom;
                                    }
                                    if (ImGui.InputInt(Translator.LocalizeUI("Camera Movement Speed (In Milliseconds)"), ref cameraDollySpeed))
                                    {
                                        item.CameraDollySpeed = cameraDollySpeed;
                                    }

                                }
                                ImGui.EndTabItem();
                            }
                        }
                    }
                }
            }
        }
    }
    private void DrawBranchingChoicesMenu()
    {
        ImGui.BeginTable("##Branching Choices Table", 2);
        ImGui.TableSetupColumn(Translator.LocalizeUI("Branching Choices List"), ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn(Translator.LocalizeUI("Branching Choices Editor"), ImGuiTableColumnFlags.WidthStretch);
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
                if (_selectedEvent > questText.Count)
                {
                    _selectedEvent = questText.Count - 1;
                }
                var branchingChoices = questText[_selectedEvent].BranchingChoices;
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
                    var eventToJumpTo = item.EventToJumpTo;
                    var eventToJumpToFailure = item.EventToJumpToFailure;
                    var minimumDiceRoll = item.MinimumDiceRoll;
                    var branchingChoiceTypes = Translator.LocalizeTextArray(Enum.GetNames(typeof(BranchingChoiceType)));
                    if (ImGui.InputText(Translator.LocalizeUI("Choice Text##"), ref choiceText, 255))
                    {
                        item.ChoiceText = choiceText;
                    }
                    if (ImGui.Combo(Translator.LocalizeUI("Branching Choice Type##"), ref choiceType, branchingChoiceTypes, branchingChoiceTypes.Length))
                    {
                        item.ChoiceType = (BranchingChoiceType)choiceType;
                    }
                    switch (item.ChoiceType)
                    {
                        case BranchingChoiceType.SkipToEventNumber:
                            if (ImGui.InputInt(Translator.LocalizeUI("Event Number To Jump To##"), ref eventToJumpTo))
                            {
                                item.EventToJumpTo = eventToJumpTo;
                            }
                            break;
                        case BranchingChoiceType.RollD20ThenSkipToEventNumber:
                            if (ImGui.InputInt(Translator.LocalizeUI("Event Number To Jump To Success##"), ref eventToJumpTo))
                            {
                                item.EventToJumpTo = eventToJumpTo;
                            }
                            if (ImGui.InputInt(Translator.LocalizeUI("Event Number To Jump To Failure##"), ref eventToJumpToFailure))
                            {
                                item.EventToJumpToFailure = eventToJumpToFailure;
                            }
                            if (ImGui.InputInt(Translator.LocalizeUI("Minimum Roll For Success"), ref minimumDiceRoll))
                            {
                                if (minimumDiceRoll > 20)
                                {
                                    minimumDiceRoll = 20;
                                }
                                else if (minimumDiceRoll < 0)
                                {
                                    minimumDiceRoll = 0;
                                }
                                item.MinimumDiceRoll = minimumDiceRoll;
                            }
                            break;
                        case BranchingChoiceType.SkipToEventNumberRandomized:
                            var width = ImGui.GetColumnWidth();
                            ImGui.PushID(Translator.LocalizeUI("Vertical Scroll Branching"));
                            ImGui.BeginGroup();
                            const ImGuiWindowFlags child_flags = ImGuiWindowFlags.MenuBar;
                            var child_id = ImGui.GetID(Translator.LocalizeUI("Branching Events"));
                            bool child_is_visible = ImGui.BeginChild(child_id, new Vector2(width, 200), true, child_flags);
                            for (int i = 0; i < item.RandomizedEventToSkipTo.Count; i++)
                            {
                                try
                                {
                                    // Apparently the for loop evaluation is not enough
                                    if (i < item.RandomizedEventToSkipTo.Count)
                                    {
                                        var randomizedEventToJumpTo = item.RandomizedEventToSkipTo[i];
                                        ImGui.SetNextItemWidth(200);
                                        if (ImGui.InputInt(Translator.LocalizeUI($"Randomized Event Number To Jump To##{i}"), ref randomizedEventToJumpTo))
                                        {
                                            item.RandomizedEventToSkipTo[i] = randomizedEventToJumpTo;
                                        }
                                        ImGui.SameLine();
                                        if (item.RandomizedEventToSkipTo.Count < 2)
                                        {
                                            ImGui.BeginDisabled();
                                        }
                                        ImGui.SameLine();
                                        if (ImGui.Button($"Delete##{i}"))
                                        {
                                            item.RandomizedEventToSkipTo.RemoveAt(i);
                                            break;
                                        }
                                        if (item.RandomizedEventToSkipTo.Count < 2)
                                        {
                                            ImGui.EndDisabled();
                                        }
                                    }
                                }
                                catch (Exception e)
                                {
                                    Plugin.PluginLog.Warning(e, e.Message);
                                }
                            }
                            ImGui.EndChild();
                            ImGui.EndGroup();
                            ImGui.PopID();
                            if (ImGui.Button($"Add Randomized Skip##"))
                            {
                                item.RandomizedEventToSkipTo.Add(0);
                                break;
                            }
                            break;
                        case BranchingChoiceType.BranchingQuestline:
                            if (ImGui.Button(Translator.LocalizeUI("Configure Branching Questline")))
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
                            if (ImGui.Button(Translator.LocalizeUI("Import Branching Questline")))
                            {
                                _fileDialogManager.Reset();
                                ImGui.OpenPopup(Translator.LocalizeUI("OpenPathDialog##editorwindow"));
                            }
                            if (ImGui.BeginPopup(Translator.LocalizeUI("OpenPathDialog##editorwindow")))
                            {
                                _fileDialogManager.OpenFileDialog(Translator.LocalizeUI("Select quest line data"), ".quest", (isOk, file) =>
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
                var branchingChoices = questText[_selectedEvent].BranchingChoices;
                ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                if (ImGui.ListBox("##branchingChoice", ref _selectedBranchingChoice, _branchingChoices, _branchingChoices.Length, 12))
                {
                    RefreshMenus();
                }
                if (ImGui.Button(Translator.LocalizeUI("Add")))
                {
                    var branchingChoice = new BranchingChoice();
                    branchingChoices.Add(branchingChoice);
                    branchingChoice.RoleplayingQuest.ConfigureSubQuest(_roleplayingQuestCreator.CurrentQuest);
                    branchingChoice.RoleplayingQuest.IsSubQuest = true;
                    Task.Run(async () =>
                    {
                        _branchingChoices = Utility.FillNewList(branchingChoices.Count, await Translator.LocalizeText("Choice", Translator.UiLanguage, LanguageEnum.English));
                        _selectedBranchingChoice = branchingChoices.Count - 1;
                        RefreshMenus();
                    });
                }
                ImGui.SameLine();
                if (ImGui.Button(Translator.LocalizeUI("Remove")))
                {
                    branchingChoices.RemoveAt(_selectedBranchingChoice);
                    Task.Run(async () =>
                    {
                        _branchingChoices = Utility.FillNewList(branchingChoices.Count, await Translator.LocalizeText("Choice", Translator.UiLanguage, LanguageEnum.English));
                        _selectedBranchingChoice = branchingChoices.Count - 1;
                        RefreshMenus();
                    });
                }
            }
        }
    }

    private void DrawQuestEvents()
    {
        if (_objectiveInFocus != null)
        {
            var questText = _objectiveInFocus.QuestText;
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            if (ImGui.ListBox("##questEvent", ref _selectedEvent, _dialogues, _dialogues.Length, 15))
            {
                _selectedBranchingChoice = 0;
                RefreshMenus();
            }
            if (ImGui.Button(Translator.LocalizeUI("Add")))
            {
                questText.Add(new QuestEvent());
                Task.Run(async () =>
                {
                    _dialogues = Utility.FillNewList(questText.Count, await Translator.LocalizeText("Event", Translator.UiLanguage, LanguageEnum.English));
                    _selectedEvent = questText.Count - 1;
                    RefreshMenus();
                });
            }
            ImGui.SameLine();
            if (ImGui.Button(Translator.LocalizeUI("Remove")))
            {
                questText.RemoveAt(_selectedEvent);
                Task.Run(async () =>
                {
                    _dialogues = Utility.FillNewList(questText.Count, await Translator.LocalizeText("Event", Translator.UiLanguage, LanguageEnum.English));
                    _selectedEvent = questText.Count - 1;
                    RefreshMenus();
                });
            }
            if (ImGui.Button(Translator.LocalizeUI("Add Clipboard")))
            {
                _roleplayingQuestCreator.StoryScriptToObjectiveEvents(ImGui.GetClipboardText().Replace(Translator.LocalizeUI("…"), "..."), _objectiveInFocus);
                RefreshMenus();
            }
            if (ImGui.Button(Translator.LocalizeUI("To Clipboard")))
            {
                ImGui.SetClipboardText(_roleplayingQuestCreator.ObjectiveToStoryScriptFormat(_objectiveInFocus));
            }
        }
    }

    private async Task RefreshMenus()
    {
        if (_objectiveInFocus != null)
        {
            var questText = _objectiveInFocus.QuestText;
            _dialogues = Utility.FillNewList(questText.Count, await Translator.LocalizeText("Event", Translator.UiLanguage, LanguageEnum.English));
            _nodeNames = Utility.FillNewList(_roleplayingQuestCreator.CurrentQuest.QuestObjectives.Count, await Translator.LocalizeText("Objective", Translator.UiLanguage, LanguageEnum.English));
            if (questText.Count > 0)
            {
                if (_selectedEvent < questText.Count)
                {
                    var choices = questText[_selectedEvent].BranchingChoices;
                    if (_selectedBranchingChoice > choices.Count)
                    {
                        _selectedBranchingChoice = choices.Count - 1;
                    }
                    _branchingChoices = Utility.FillNewList(choices.Count, await Translator.LocalizeText("Choice", Translator.UiLanguage, LanguageEnum.English));
                }
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
            _dialogues = Utility.FillNewList(0, await Translator.LocalizeText("Event", Translator.UiLanguage, LanguageEnum.English));
            _nodeNames = Utility.FillNewList(0, await Translator.LocalizeText("Objective", Translator.UiLanguage, LanguageEnum.English));
            _branchingChoices = Utility.FillNewList(0, await Translator.LocalizeText("Choice", Translator.UiLanguage, LanguageEnum.English));
            _selectedBranchingChoice = 0;
            _selectedEvent = 0;
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
                else
                {
                    objective.Index = i;
                }
                if (ImGui.TreeNode((level == 0 ? "(" + i + ") " : "") + "" + objective.Objective + "##" + i))
                {
                    if (ImGui.Button(Translator.LocalizeUI("Edit##" + i)))
                    {
                        _objectiveInFocus = objective;
                        RefreshMenus();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button(Translator.LocalizeUI("Add Sub Objective##" + i)))
                    {
                        Task.Run(async () =>
                        {
                            objective.SubObjectives.Add(new QuestObjective()
                            {
                                Objective = await Translator.LocalizeText("Objective Name Here", Translator.UiLanguage, LanguageEnum.English),
                                Coordinates = Plugin.ObjectTable.LocalPlayer.Position,
                                Rotation = new Vector3(0, CoordinateUtility.ConvertRadiansToDegrees(Plugin.ObjectTable.LocalPlayer.Rotation), 0),
                                TerritoryId = Plugin.ClientState.TerritoryType
                            });
                        });
                    }
                    if (!_shiftModifierHeld)
                    {
                        ImGui.BeginDisabled();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button(Translator.LocalizeUI("Delete##" + i)))
                    {
                        objective.Invalidate = true;
                    }
                    if (!_shiftModifierHeld)
                    {
                        ImGui.EndDisabled();
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
        var width = ImGui.GetColumnWidth();
        ImGui.PushID("Vertical Scroll");
        ImGui.BeginGroup();
        const ImGuiWindowFlags child_flags = ImGuiWindowFlags.MenuBar;
        var child_id = ImGui.GetID(Translator.LocalizeUI("Objective"));
        bool child_is_visible = ImGui.BeginChild(child_id, new Vector2(width, 600), true, child_flags);
        DrawQuestObjectivesRecursive(_roleplayingQuestCreator.CurrentQuest.QuestObjectives, 0);
        ImGui.EndChild();
        ImGui.EndGroup();
        ImGui.PopID();
        ImGui.TextUnformatted(Translator.LocalizeUI("Hold Shift To Delete Objectives"));
        if (ImGui.Button(Translator.LocalizeUI("Add Dominant Objective")))
        {
            Task.Run(async () =>
            {
                _npcTransformEditorWindow.RefreshMenus();
                _roleplayingQuestCreator.AddQuestObjective(new QuestObjective()
                {
                    Objective = await Translator.LocalizeText("Objective Name Here", Translator.UiLanguage, LanguageEnum.English),
                    Coordinates = Plugin.ObjectTable.LocalPlayer.Position,
                    Rotation = new Vector3(0, CoordinateUtility.ConvertRadiansToDegrees(Plugin.ObjectTable.LocalPlayer.Rotation), 0),
                    TerritoryId = Plugin.ClientState.TerritoryType,
                    TerritoryDiscriminator = Plugin.AQuestReborn.Discriminator
                });
                RefreshMenus();
            });
        }
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
