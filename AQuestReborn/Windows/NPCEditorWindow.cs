using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AQuestReborn;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using McdfDataImporter;
using RoleplayingQuestCore;
using static RoleplayingQuestCore.BranchingChoice;

namespace SamplePlugin.Windows;

public class NPCEditorWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;
    private FileDialogManager _fileDialogManager;
    private RoleplayingQuest _roleplayingQuest;
    private int _selectedNpcCustomization;
    private string[] _npcCustomizations;
    private bool _isCreatingAppearance;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public NPCEditorWindow(Plugin plugin)
        : base("NPC Editor Window##" + Guid.NewGuid().ToString(), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
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

    public void SetEditingQuest(RoleplayingQuest quest)
    {
        _roleplayingQuest = quest;
        RefreshMenus();
    }

    public override void Draw()
    {
        ImGui.BeginTable("##NPC Creation Table", 2);
        ImGui.TableSetupColumn("NPC Creation List", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn("NPC Creation Editor", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        DrawNPCCustomizations();
        ImGui.TableSetColumnIndex(1);
        DrawNPCCustomizationsEditor();
        ImGui.EndTable();
    }

    private void DrawNPCCustomizationsEditor()
    {
        var npcCustomization = _roleplayingQuest.NpcCustomizations;
        if (npcCustomization.Count > 0)
        {
            var item = npcCustomization[_selectedNpcCustomization];
            var npcName = item.NpcName;
            var appearanceData = item.AppearanceData;
            var branchingChoiceTypes = Enum.GetNames(typeof(BranchingChoiceType));
            if (ImGui.InputText("Npc Name##", ref npcName, 40))
            {
                item.NpcName = npcName;
            }
            if (ImGui.InputText("Npc Appearance Data##", ref appearanceData, 255))
            {
                item.AppearanceData = appearanceData;
            }

            if (_isCreatingAppearance)
            {
                ImGui.BeginDisabled();
            }
            if (ImGui.Button(_isCreatingAppearance ? "Creating Appearance Please Wait" : "Create NPC Appearance From Player Appearance##"))
            {
                Task.Run(() =>
                {
                    _isCreatingAppearance = true;
                    string mcdfName = npcName + "-" + Guid.NewGuid().ToString() + ".mcdf";
                    string questPath = Path.Combine(Plugin.Configuration.QuestInstallFolder, _roleplayingQuest.QuestName);
                    string mcdfPath = Path.Combine(questPath, mcdfName);
                    Directory.CreateDirectory(questPath);
                    McdfAccessUtils.McdfManager.CreateMCDF(mcdfPath);
                    Plugin.EditorWindow.RoleplayingQuestCreator.SaveQuest(questPath);
                    item.AppearanceData = mcdfName;
                    _isCreatingAppearance = false;
                });
            }
            if (_isCreatingAppearance)
            {
                ImGui.EndDisabled();
            }
        }
    }

    private void DrawNPCCustomizations()
    {
        if (_roleplayingQuest != null)
        {
            var npcCustomizations = _roleplayingQuest.NpcCustomizations;
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            if (ImGui.ListBox("##npcCustomization", ref _selectedNpcCustomization, _npcCustomizations, _npcCustomizations.Length, 13))
            {
                //RefreshMenus();
            }
            if (ImGui.Button("Add"))
            {
                var npcCustomization = new NpcInformation();
                npcCustomizations[npcCustomizations.Count] = npcCustomization;
                _npcCustomizations = Utility.FillNewList(npcCustomizations.Count, "NPC Appearance");
                _selectedNpcCustomization = _npcCustomizations.Length - 1;
            }
            ImGui.SameLine();
            if (ImGui.Button("Remove"))
            {
                npcCustomizations.Remove(_selectedNpcCustomization);
                _npcCustomizations = Utility.FillNewList(npcCustomizations.Count, "NPC Appearance");
                _selectedNpcCustomization = _npcCustomizations.Length - 1;
            }
        }
    }

    private void RefreshMenus()
    {
        _npcCustomizations = Utility.FillNewList(_roleplayingQuest.NpcCustomizations.Count, "NPC Appearance");
        _selectedNpcCustomization = _npcCustomizations.Length - 1;
    }
}
