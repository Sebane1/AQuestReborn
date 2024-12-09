using System;
using System.Linq;
using System.Numerics;
using AQuestReborn;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using RoleplayingQuestCore;
using static RoleplayingQuestCore.BranchingChoice;

namespace SamplePlugin.Windows;

public class NPCTransformEditorWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;
    private FileDialogManager _fileDialogManager;
    private QuestObjective _questObjective;
    private RoleplayingQuest _roleplayingQuest;
    private int _selectedNpcTransform;
    private string[] _npcTransformsSelection;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public NPCTransformEditorWindow(Plugin plugin)
        : base("NPC Transform Editor Window##" + Guid.NewGuid().ToString(), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
        _fileDialogManager = new FileDialogManager();
    }

    public void Dispose() { }

    public override void OnOpen()
    {
        RefreshMenus();
    }
    public void SetEditingQuest(QuestObjective quest)
    {
        _questObjective = quest;
        RefreshMenus();
    }

    public override void Draw()
    {
        ImGui.BeginTable("##NPC Transform Table", 2);
        ImGui.TableSetupColumn("NPC Transform List", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn("NPC Transform Editor", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        DrawTransformChoices();
        ImGui.TableSetColumnIndex(1);
        DrawTransformEditor();
        ImGui.EndTable();
    }

    private void DrawTransformEditor()
    {
        var npcCustomization = _questObjective.NpcStartingPositions;
        if (npcCustomization.Count > 0)
        {
            var item = npcCustomization.ElementAt(_selectedNpcTransform);
            var name = item.Value.Name;

            var position = item.Value.Position;

            var eulerRotation = item.Value.EulerRotation;

            var scale = item.Value.Scale;

            ImGui.SetNextItemWidth(100);
            if (ImGui.InputText("##name", ref name, 20))
            {
                item.Value.Name = name;
            }
            ImGui.SetNextItemWidth(100);
            ImGui.LabelText("##positon", "Position: ");
            ImGui.SameLine();
            if (ImGui.InputFloat3("##Position Entry", ref position))
            {
                item.Value.Position = position;
            }

            ImGui.SetNextItemWidth(100);
            ImGui.LabelText("##rotation", "Rotation: ");
            ImGui.SameLine();
            if (ImGui.InputFloat3("##Rotation Entry", ref eulerRotation))
            {
                item.Value.EulerRotation = eulerRotation;
            }

            ImGui.SetNextItemWidth(100);
            ImGui.LabelText("##scale", "Scale: ");
            ImGui.SameLine();
            if (ImGui.InputFloat3("##Scale Entry", ref scale))
            {
                item.Value.Scale = scale;
            }

            if (ImGui.Button("Set Transform Coordinates From Standing Position"))
            {
                SetStartingTransformDataToPlayer(item.Value);
            }
        }
    }

    private void DrawTransformChoices()
    {
        if (_questObjective != null)
        {
            var npcStartingPositions = _questObjective.NpcStartingPositions;
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            if (ImGui.ListBox("##npcCustomization", ref _selectedNpcTransform, _npcTransformsSelection, _npcTransformsSelection.Length, 13))
            {
                //RefreshMenus();
            }
        }
    }

    public void RefreshMenus()
    {
        if (_questObjective != null)
        {
            _npcTransformsSelection = Utility.FillNewList(_questObjective.NpcStartingPositions.Count, "NPC Transform");
            _selectedNpcTransform = _npcTransformsSelection.Length - 1;
            var npcsInDialogue = _questObjective.EnumerateCharactersAtObjective();
            foreach (var npc in npcsInDialogue)
            {
                if (!_questObjective.NpcStartingPositions.ContainsKey(npc))
                {
                    var newTransform = new Transform() { Name = npc };
                    SetStartingTransformData(newTransform);
                    _questObjective.NpcStartingPositions[npc] = newTransform;
                }
            }
            for (int i = 0; i > 0; i--)
            {
                var transform = _questObjective.NpcStartingPositions.ElementAt(i);
                if (!npcsInDialogue.Contains(transform.Key))
                {
                    _questObjective.NpcStartingPositions.Remove(transform.Key);
                }
            }
        }
    }

    public void SetStartingTransformData(Transform item)
    {
        item.Position = _questObjective.Coordinates;
        item.EulerRotation = new Vector3(0, Utility.ConvertRadiansToDegrees(Plugin.ClientState.LocalPlayer.Rotation) + 180, 0);
    }

    public void SetStartingTransformDataToPlayer(Transform item)
    {
        item.Position = Plugin.ClientState.LocalPlayer.Position;
        item.EulerRotation = new Vector3(0, Utility.ConvertRadiansToDegrees(Plugin.ClientState.LocalPlayer.Rotation) + 180, 0);
    }
}