using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using RoleplayingQuestCore;
using static RoleplayingQuestCore.QuestObjective;
using static RoleplayingQuestCore.RoleplayingQuest;

namespace SamplePlugin.Windows;

public class DialogueWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;
    private RoleplayingQuestCreator _roleplayingQuestCreator;
    private string[] nodeNames;
    private int selectedNode;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public DialogueWindow(Plugin plugin, string goatImagePath)
        : base("A Quest Reborn##mainwindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        GoatImagePath = goatImagePath;
        Plugin = plugin;
        _roleplayingQuestCreator = new RoleplayingQuestCreator();
    }

    public void Dispose() { }

    public override void Draw()
    {
        var questAuthor = _roleplayingQuestCreator.CurrentQuest.QuestAuthor;
        var questName = _roleplayingQuestCreator.CurrentQuest.QuestName;
        var questDescription = _roleplayingQuestCreator.CurrentQuest.QuestDescription;
        var contentRating = (int)_roleplayingQuestCreator.CurrentQuest.ContentRating;
        var contentRatingTypes = Enum.GetNames(typeof(QuestContentRating));

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

        ImGui.BeginTable("##Editor Table", 2);
        ImGui.TableSetupColumn("Objective List", ImGuiTableColumnFlags.WidthFixed, 200);
        ImGui.TableSetupColumn("Objective Editor List", ImGuiTableColumnFlags.WidthStretch, 300);
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
            var questObjective = _roleplayingQuestCreator.CurrentQuest.QuestObjectives[selectedNode];
            var territoryId = questObjective.TerritoryId;
            var objective = questObjective.Objective;
            var coordinates = questObjective.Coordinates;
            var objectiveStatus = questObjective.ObjectiveStatus;
            var questText = questObjective.QuestText;
            var questPointType = (int)questObjective.TypeOfQuestPoint;
            var objectiveStatusType = (int)questObjective.ObjectiveStatus;
            var questPointTypes = Enum.GetNames(typeof(QuestPointType));
            var objectiveStatusTypes = Enum.GetNames(typeof(ObjectiveStatusType));

            ImGui.LabelText("##coordinatesLabel", $"Objective Coordinates: X:{questObjective.Coordinates.X},Y:{questObjective.Coordinates.Y},Z:{questObjective.Coordinates.Z}");
            ImGui.LabelText("##territoryLabel", $"Territory Id: {questObjective.TerritoryId}");
            if (ImGui.Button("Set Quest Objective Coordinates"))
            {
                _roleplayingQuestCreator.CurrentQuest.QuestObjectives[selectedNode].Coordinates = Plugin.ClientState.LocalPlayer.Position;
                _roleplayingQuestCreator.CurrentQuest.QuestObjectives[selectedNode].TerritoryId = Plugin.ClientState.TerritoryType;
            }
            if (ImGui.InputText("Objective Input##", ref objective, 500))
            {
                _roleplayingQuestCreator.CurrentQuest.QuestObjectives[selectedNode].Objective = objective;
            }
            if (ImGui.Combo("Quest Point Type Input##", ref questPointType, questPointTypes, questPointTypes.Length))
            {
                _roleplayingQuestCreator.CurrentQuest.QuestObjectives[selectedNode].TypeOfQuestPoint = (QuestPointType)questPointType;
            }
            if (ImGui.Combo("Objective Status Type Input##", ref questPointType, questPointTypes, questPointTypes.Length))
            {
                _roleplayingQuestCreator.CurrentQuest.QuestObjectives[selectedNode].ObjectiveStatus = (ObjectiveStatusType)objectiveStatus;
            }
        }
    }

    private void DrawQuestNodes()
    {
        ImGui.ListBox("#questNodes", ref selectedNode, nodeNames, nodeNames.Length);
        if (ImGui.Button("Add Objective"))
        {
            _roleplayingQuestCreator.AddQuestObjective(new QuestObjective() { 
                Coordinates = Plugin.ClientState.LocalPlayer.Position, 
                TerritoryId = Plugin.ClientState.TerritoryType });
            nodeNames = FillNewList(_roleplayingQuestCreator.CurrentQuest.QuestObjectives.Count, "Objective");
        }
        ImGui.SameLine();
        if (ImGui.Button("Remove Objective"))
        {
            _roleplayingQuestCreator.CurrentQuest.QuestObjectives.RemoveAt(selectedNode);
        }
    }
    private string[] FillNewList(int count, string phrase)
    {
        List<string> values = new List<string>();
        for (int i = 0; i < count; i++)
        {
            values.Add(phrase + " " + values);
        }
        return values.ToArray();
    }
}
