using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using RoleplayingQuestCore;
using SixLabors.ImageSharp.Drawing;
using Path = System.IO.Path;

namespace SamplePlugin.Windows;

public class MainWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;
    private FileDialogManager _fileDialogManager;
    private ExcelSheet<TerritoryType>? _territorySheets;
    private string[] _installedQuestList;
    private int _currentSelectedInstalledQuest;
    List<RoleplayingQuest> _roleplayingQuests = new List<RoleplayingQuest>();

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MainWindow(Plugin plugin)
        : base("A Quest Reborn##mainwindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        Plugin = plugin;
        _fileDialogManager = new FileDialogManager();
        _territorySheets = Plugin.DataManager.GameData.GetExcelSheet<TerritoryType>();
    }

    public void Dispose() { }

    public override void OnOpen()
    {
        Plugin.RoleplayingQuestManager.ScanDirectory();
        base.OnOpen();
    }
    public override void Draw()
    {
        if (string.IsNullOrEmpty(Plugin.Configuration.QuestInstallFolder))
        {
            DrawInitialSetup();
        }
        else if (ImGui.BeginTabBar("ConfigTabs"))
        {
            if (ImGui.BeginTabItem("Installed Quests"))
            {
                DrawInstalledQuests();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Quest Objectives"))
            {
                DrawQuestObjectives();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawInitialSetup()
    {
        _fileDialogManager.Draw();
        if (ImGui.Button("Pick Empty Folder For Custom Quest Installs"))
        {
            _fileDialogManager.Reset();
            ImGui.OpenPopup("OpenPathDialog##editorwindow");
        }
        if (ImGui.BeginPopup("OpenPathDialog##editorwindow"))
        {
            _fileDialogManager.SaveFolderDialog("Pick location", "QuestReborn", (isOk, folder) =>
            {
                if (isOk)
                {
                    Directory.CreateDirectory(folder);
                    if (Directory.GetFiles(folder).Length == 0)
                    {
                        Plugin.Configuration.QuestInstallFolder = folder;
                        Plugin.RoleplayingQuestManager.QuestInstallFolder = folder;
                        Plugin.Configuration.Save();
                    }
                }
            }, null, true);
            ImGui.EndPopup();
        }
    }

    private void DrawQuestObjectives()
    {
        int index = 0;
        foreach (var item in Plugin.RoleplayingQuestManager.GetCurrentObjectives())
        {
            ImGui.TextWrapped(item.Objective + $" ({_territorySheets.GetRow((uint)item.TerritoryId).PlaceName.Value.Name.ToString()})");
            index++;
        }
    }

    private void DrawInstalledQuests()
    {
        InstalledQuestsTab();
    }
    private void InstalledQuestsTab()
    {
        ImGui.BeginTable("##Installed Quests", 2);
        ImGui.TableSetupColumn("Installed Quest Selector", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn("Installed Quest Information", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        DrawInstalledQuestSelector();
        ImGui.TableSetColumnIndex(1);
        DrawInstalledQuestInformation();
        ImGui.EndTable();
    }

    private void DrawInstalledQuestInformation()
    {
        if (_currentSelectedInstalledQuest < Plugin.RoleplayingQuestManager.QuestChains.Count)
        {
            var roleplayingQuest = Plugin.RoleplayingQuestManager.QuestChains.ElementAt(_currentSelectedInstalledQuest).Value;
            string startingLocation = "";
            if (roleplayingQuest.QuestObjectives.Count > 0)
            {
                startingLocation = _territorySheets.GetRow((uint)roleplayingQuest.QuestObjectives[0].TerritoryId).PlaceName.Value.Name.ToString();
            }
            ImGui.Text($"Author: {roleplayingQuest.QuestAuthor}");
            ImGui.Text($"Quest Name: {roleplayingQuest.QuestName}");
            ImGui.TextWrapped($"Description: {roleplayingQuest.QuestDescription}");
            if (!string.IsNullOrEmpty(startingLocation))
            {
                ImGui.Text($"Starting Location: {startingLocation}");
            }
            if (ImGui.Button("Reset Quest Progress"))
            {
                try
                {
                    Plugin.RoleplayingQuestManager.AddQuest(Path.Combine(Plugin.Configuration.QuestInstallFolder, roleplayingQuest.QuestName + @"\main.quest"));
                }
                catch
                {
                    Plugin.RoleplayingQuestManager.QuestChains.Remove(roleplayingQuest.QuestId);
                }
                Plugin.AQuestReborn.RefreshNpcsForQuest(Plugin.ClientState.TerritoryType, roleplayingQuest.QuestId);
                Plugin.AQuestReborn.RefreshMapMarkers();
                Plugin.SaveProgress();
            }
            ImGui.SameLine();
            if (ImGui.Button("Edit Quest"))
            {
                Plugin.EditorWindow.RoleplayingQuestCreator.EditQuest(Path.Combine(Plugin.Configuration.QuestInstallFolder, roleplayingQuest.QuestName + @"\main.quest"));
                Plugin.EditorWindow.IsOpen = true;
            }
            if (ImGui.Button("Open Directory"))
            {
                string path = Path.Combine(Plugin.Configuration.QuestInstallFolder, roleplayingQuest.QuestName);
                ProcessStartInfo ProcessInfo;
                Process Process; ;
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch
                {
                }
                ProcessInfo = new ProcessStartInfo("explorer.exe", @"""" + path + @"""");
                ProcessInfo.UseShellExecute = true;
                Process = Process.Start(ProcessInfo);
            }
            ImGui.SameLine();
            if (ImGui.Button("Export Quest"))
            {
                string foundPath = roleplayingQuest.FoundPath;
                string zipPath = "";
                if (string.IsNullOrEmpty(foundPath))
                {
                    zipPath = Path.GetDirectoryName(foundPath);
                }
                else
                {
                    zipPath = Path.Combine(Plugin.Configuration.QuestInstallFolder, roleplayingQuest.QuestName);
                }
                Plugin.RoleplayingQuestManager.ExportQuestPack(zipPath);
                string path = Path.GetDirectoryName(zipPath + "qmp");
                ProcessStartInfo ProcessInfo;
                Process Process; ;
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch
                {
                }
                ProcessInfo = new ProcessStartInfo("explorer.exe", @"""" + path + @"""");
                ProcessInfo.UseShellExecute = true;
                Process = Process.Start(ProcessInfo);
            }
        }
    }

    private void DrawInstalledQuestSelector()
    {
        var installedQuests = Plugin.RoleplayingQuestManager.QuestChains;
        var questItems = new List<string>();
        foreach (var item in installedQuests)
        {
            questItems.Add(item.Value.QuestName);
        }
        if (_currentSelectedInstalledQuest > questItems.Count)
        {
            _currentSelectedInstalledQuest = 0;
        }
        ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
        ImGui.ListBox("##installedQuestInformation", ref _currentSelectedInstalledQuest, questItems.ToArray(), questItems.Count, 10);
        if (ImGui.Button("Quest Creator"))
        {
            Plugin.EditorWindow.IsOpen = true;
        }
        _fileDialogManager.Draw();
        if (ImGui.Button("Install Quest"))
        {
            _fileDialogManager.Reset();
            ImGui.OpenPopup("OpenPathDialog##editorwindow");
        }
        if (ImGui.BeginPopup("OpenPathDialog##editorwindow"))
        {
            _fileDialogManager.OpenFileDialog("Select quest file", ".qmp", (isOk, file) =>
            {
                if (isOk)
                {
                    Plugin.RoleplayingQuestManager.OpenQuestPack(file[0]);
                    Plugin.RoleplayingQuestManager.ScanDirectory();
                }
            }, 0, null, true);
            ImGui.EndPopup();
        }
        if (ImGui.Button("Re-scan Quests"))
        {
            Plugin.RoleplayingQuestManager.ScanDirectory();
        }
    }
}
