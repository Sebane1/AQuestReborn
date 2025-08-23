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
using Dalamud.Bindings.ImGui;
using LanguageConversionProxy;
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
        Size = new Vector2(800, 500);
    }

    public void Dispose() { }

    public override void OnOpen()
    {
        if (Plugin.RoleplayingQuestManager != null)
        {
            Plugin.RoleplayingQuestManager.ScanDirectory();
        }
        base.OnOpen();
    }
    public override void Draw()
    {
        _fileDialogManager.Draw();
        if (ImGui.BeginTabBar("ConfigTabs"))
        {
            if (!string.IsNullOrEmpty(Plugin.Configuration.QuestInstallFolder) && Directory.Exists(Plugin.Configuration.QuestInstallFolder))
            {
                if (ImGui.BeginTabItem(Translator.LocalizeUI("Installed Quests")))
                {
                    DrawInstalledQuests();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(Translator.LocalizeUI("Quest Objectives")))
                {
                    DrawQuestObjectives();
                    ImGui.EndTabItem();
                }
            }
            if (ImGui.BeginTabItem(Translator.LocalizeUI("Settings")))
            {
                DrawInitialSetup();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        if (ImGui.Button(Translator.LocalizeUI("Donate To Further Development"), new Vector2(Size.Value.X, 50)))
        {
            ProcessStartInfo ProcessInfo = new ProcessStartInfo();
            Process Process = new Process();
            ProcessInfo = new ProcessStartInfo("https://ko-fi.com/sebastina");
            ProcessInfo.UseShellExecute = true;
            Process = Process.Start(ProcessInfo);
        }
    }

    private void DrawInitialSetup()
    {
        if (ImGui.Button(Translator.LocalizeUI("Pick Empty Folder For Custom Quest Installs (Cannot Require Admin Rights)")))
        {
            _fileDialogManager.Reset();
            ImGui.OpenPopup(Translator.LocalizeUI("OpenPathDialog##editorwindow"));
        }
        if (ImGui.BeginPopup(Translator.LocalizeUI("OpenPathDialog##editorwindow")))
        {
            _fileDialogManager.SaveFolderDialog(Translator.LocalizeUI("Pick location"), "QuestReborn", (isOk, folder) =>
            {
                if (isOk)
                {
                    if (!folder.Contains("Program Files") && !folder.Contains("FINAL FANTASY XIV - A Realm Reborn"))
                    {
                        Directory.CreateDirectory(folder);
                        Plugin.Configuration.QuestInstallFolder = folder;
                        Plugin.RoleplayingQuestManager.QuestInstallFolder = folder;
                        Translator.LoadCache(Path.Combine(Plugin.Configuration.QuestInstallFolder, "languageCache.json"));
                        Plugin.Configuration.Save();
                    }
                }
            }, null, true);
            ImGui.EndPopup();
        }
        int currentSelection = (int)Plugin.Configuration.QuestLanguage;
        if (ImGui.Combo(Translator.LocalizeUI("Language"), ref currentSelection, Translator.LanguageStrings, Translator.LanguageStrings.Length))
        {
            Plugin.Configuration.QuestLanguage = (LanguageEnum)currentSelection;
            Translator.UiLanguage = Plugin.Configuration.QuestLanguage;
            Plugin.Configuration.Save();
        }
    }

    private void DrawQuestObjectives()
    {
        int index = 0;
        List<string> strings = new List<string>();
        foreach (var item in Plugin.RoleplayingQuestManager.GetCurrentObjectives())
        {
            if (item.Item2.SubObjectivesComplete() && !item.Item2.ObjectiveCompleted && !item.Item3.HasQuestAcceptancePopup)
            {
                if (!strings.Contains(item.Item3.QuestName))
                {
                    ImGui.TextWrapped(Translator.LocalizeUI(item.Item3.QuestName, item.Item3.QuestLanguage));
                    strings.Add(item.Item3.QuestName);
                }
                ImGui.SetCursorPosX(50);
                ImGui.TextWrapped(Translator.LocalizeUI(item.Item2.Objective, item.Item3.QuestLanguage) + (item.Item2.DontShowOnMap ? Translator.LocalizeUI(" (Hidden Location)") : $" ({_territorySheets.GetRow((uint)item.Item2.TerritoryId).PlaceName.Value.Name.ToString()})"));
                if (item.Item2.TypeOfObjectiveTrigger == QuestObjective.ObjectiveTriggerType.SayPhrase)
                {
                    ImGui.SetCursorPosX(50);
                    ImGui.TextWrapped("/questchat" + " " + Translator.LocalizeUI(item.Item2.TriggerText, item.Item3.QuestLanguage));
                }
            }
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
        ImGui.TableSetupColumn(Translator.LocalizeUI("Installed Quest Selector"), ImGuiTableColumnFlags.WidthFixed, 200);
        ImGui.TableSetupColumn(Translator.LocalizeUI("Installed Quest Information"), ImGuiTableColumnFlags.WidthStretch);
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
            ImGui.Text(Translator.LocalizeUI($"Author") + $": {roleplayingQuest.QuestAuthor}");
            ImGui.Text(Translator.LocalizeUI($"Quest Name") + ":" + Translator.LocalizeUI($"{roleplayingQuest.QuestName}", roleplayingQuest.QuestLanguage));
            ImGui.TextWrapped(Translator.LocalizeUI($"Description") + ":" + Translator.LocalizeUI($"{roleplayingQuest.QuestDescription}", roleplayingQuest.QuestLanguage));
            if (!string.IsNullOrEmpty(startingLocation))
            {
                ImGui.Text(Translator.LocalizeUI($"Starting Location") + ":" + Translator.LocalizeUI($"{startingLocation}"));
            }
            if (ImGui.Button(Translator.LocalizeUI("Reset Quest Progress")))
            {
                try
                {
                    Plugin.RoleplayingQuestManager.AddQuest(Path.Combine(Plugin.Configuration.QuestInstallFolder, roleplayingQuest.QuestName + @"\main.quest"));
                }
                catch
                {
                    Plugin.RoleplayingQuestManager.QuestChains.Remove(roleplayingQuest.QuestId);
                }
                Plugin.AQuestReborn.RefreshNpcs(Plugin.ClientState.TerritoryType, roleplayingQuest.QuestId);
                Plugin.AQuestReborn.RefreshMapMarkers();
                Plugin.SaveProgress();
            }
            ImGui.SameLine();
            if (ImGui.Button(Translator.LocalizeUI("Edit Quest")))
            {
                Plugin.EditorWindow.RoleplayingQuestCreator.EditQuest(Path.Combine(Plugin.Configuration.QuestInstallFolder, roleplayingQuest.QuestName + @"\main.quest"));
                Plugin.EditorWindow.IsOpen = true;
                Plugin.EditorWindow.Reset();
            }
            if (ImGui.Button(Translator.LocalizeUI("Open Directory")))
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
            if (ImGui.Button(Translator.LocalizeUI("Export Quest")))
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
                string path = Path.GetDirectoryName(zipPath);
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
            if (ImGui.Button(Translator.LocalizeUI("Recover Quest")))
            {
                string foundPath = roleplayingQuest.FoundPath;
                string recoveryPath = "";
                if (string.IsNullOrEmpty(foundPath))
                {
                    recoveryPath = Path.GetDirectoryName(foundPath);
                }
                else
                {
                    recoveryPath = Path.Combine(Plugin.Configuration.QuestInstallFolder, roleplayingQuest.QuestName);
                }
                Plugin.RoleplayingQuestManager.RecoverDeletedQuest(roleplayingQuest, recoveryPath);
            }
        }
    }

    private void DrawInstalledQuestSelector()
    {
        if (Plugin.RoleplayingQuestManager != null)
        {
            var installedQuests = Plugin.RoleplayingQuestManager.QuestChains;
            var questItems = new List<string>();
            foreach (var item in installedQuests)
            {
                if (item.Value != null && !string.IsNullOrEmpty(item.Value.QuestName))
                {
                    questItems.Add(Translator.LocalizeUI(item.Value.QuestName, item.Value.QuestLanguage));
                }
            }
            if (_currentSelectedInstalledQuest > questItems.Count)
            {
                _currentSelectedInstalledQuest = 0;
            }
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            ImGui.ListBox("##installedQuestInformation", ref _currentSelectedInstalledQuest,  questItems.ToArray(), 10);
            if (ImGui.Button(Translator.LocalizeUI("Quest Creator")))
            {
                Plugin.EditorWindow.IsOpen = true;
                Plugin.EditorWindow.Reset();
            }
            if (ImGui.Button(Translator.LocalizeUI("Install Quest")))
            {
                _fileDialogManager.Reset();
                ImGui.OpenPopup("OpenPathDialog##editorwindow");
            }
            if (ImGui.BeginPopup("OpenPathDialog##editorwindow"))
            {
                _fileDialogManager.OpenFileDialog(Translator.LocalizeUI("Select quest file"), ".qmp", (isOk, file) =>
                {
                    if (isOk)
                    {
                        Plugin.RoleplayingQuestManager.OpenQuestPack(file[0]);
                        Plugin.RoleplayingQuestManager.ScanDirectory();
                    }
                }, 0, null, true);
                ImGui.EndPopup();
            }
            if (ImGui.Button(Translator.LocalizeUI("Re-scan Quests")))
            {
                Plugin.RoleplayingQuestManager.ScanDirectory();
            }
        }
    }
}
