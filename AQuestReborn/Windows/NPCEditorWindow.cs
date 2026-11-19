using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AQuestReborn;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using McdfDataImporter;
using RoleplayingQuestCore;
using static RoleplayingQuestCore.BranchingChoice;
using AQuestReborn.Windows;

namespace SamplePlugin.Windows;

public class NPCEditorWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;
    private FileDialogManager _fileDialogManager;
    private Dalamud.Interface.DragDrop.IDragDropManager _dragDropManager;
    private static readonly HashSet<string> ValidMcdfExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mcdf" };
    private RoleplayingQuest _roleplayingQuest;
    private int _selectedNpcCustomization;
    private string[] _npcCustomizations;
    private bool _isCreatingAppearance;
    private VoiceSelector voiceSelector = new VoiceSelector();
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
        _dragDropManager = Plugin.DragDropManager;
    }

    public void Dispose() { }

    public void SetEditingQuest(RoleplayingQuest quest)
    {
        _roleplayingQuest = quest;
        RefreshMenus();
    }

    public override void Draw()
    {
        _fileDialogManager.Draw();
        ImGui.BeginTable("##NPC Creation Table", 2);
        ImGui.TableSetupColumn(Translator.LocalizeUI("NPC Creation List"), ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn(Translator.LocalizeUI("NPC Creation Editor"), ImGuiTableColumnFlags.WidthStretch);
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
            if (_selectedNpcCustomization < npcCustomization.Count)
            {
                if (npcCustomization.ContainsKey(_selectedNpcCustomization))
                {
                    var item = npcCustomization[_selectedNpcCustomization];
                    var npcName = item.NpcName;
                    var npcVoice = item.Voice;
                    var appearanceData = item.AppearanceData;
                    var branchingChoiceTypes = Enum.GetNames(typeof(BranchingChoiceType));
                    if (ImGui.InputText(Translator.LocalizeUI("Npc Name##"), ref npcName, 40))
                    {
                        item.NpcName = npcName;
                    }

                    if (Plugin.Configuration.AccessibilityReaderActive)
                    {
                        if (ImGui.InputText(Translator.LocalizeUI("Npc Voice##"), ref npcVoice, 40))
                        {
                            item.Voice = npcVoice;
                        }

                        if (voiceSelector.Draw(ref npcVoice))
                        {
                            item.Voice = npcVoice;
                        }
                    }

                    ImGui.Text(Translator.LocalizeUI("Npc Appearance Data:"));
                    ImGui.SetNextItemWidth(250);
                    if (ImGui.InputText("##NpcAppearanceData", ref appearanceData, 255))
                    {
                        item.AppearanceData = appearanceData;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button(Translator.LocalizeUI("Browse##npcAppearance")))
                    {
                        _fileDialogManager.Reset();
                        ImGui.OpenPopup("McdfImportDialog##npcEditor");
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(Translator.LocalizeUI("Browse for an MCDF file (.mcdf)"));
                    if (ImGui.BeginPopup("McdfImportDialog##npcEditor"))
                    {
                        _fileDialogManager.OpenFileDialog(
                            Translator.LocalizeUI("Select MCDF File"),
                            ".mcdf",
                            (isOk, file) =>
                            {
                                if (isOk && !string.IsNullOrEmpty(file))
                                {
                                    string imported = ImportMcdfFileToQuestFolder(file);
                                    if (imported != null) item.AppearanceData = imported;
                                }
                            });
                        ImGui.EndPopup();
                    }
                    if (_dragDropManager != null && _dragDropManager.ServiceAvailable)
                    {
                        ImGui.SetNextItemWidth(320);
                        ImGui.Button(Translator.LocalizeUI("  Drop MCDF File Here  ##npcAppMcdfDrop"),
                            new Vector2(320, 24));
                        _dragDropManager.CreateImGuiSource("NpcEditorMcdfDragDrop", m => m.Extensions.Any(e => ValidMcdfExtensions.Contains(e.ToLowerInvariant())), m => true);
                        if (_dragDropManager.CreateImGuiTarget("NpcEditorMcdfDragDrop", out var mcdfFiles, out _)
                            && mcdfFiles != null && mcdfFiles.Count > 0)
                        {
                            string imported = ImportMcdfFileToQuestFolder(mcdfFiles[0]);
                            if (imported != null)
                            {
                                item.AppearanceData = imported;
                            }
                        }
                    }
                    if (_isCreatingAppearance)
                    {
                        ImGui.BeginDisabled();
                    }
                    if (ImGui.Button(Translator.LocalizeUI(_isCreatingAppearance ? "Creating Appearance Please Wait" : "Create NPC Appearance From Player Appearance##")))
                    {
                        Task.Run(() =>
                        {
                            _isCreatingAppearance = true;
                            string mcdfName = npcName + "-" + Guid.NewGuid().ToString() + ".mcdf";
                            string questPath = Path.Combine(Plugin.Configuration.QuestInstallFolder, _roleplayingQuest.QuestName);
                            string mcdfPath = Path.Combine(questPath, mcdfName);
                            Directory.CreateDirectory(questPath);
                            AppearanceAccessUtils.AppearanceManager.CreateMCDF(mcdfPath);
                            Plugin.EditorWindow.RoleplayingQuestCreator.SaveQuest(questPath);
                            item.AppearanceData = mcdfName;
                            _isCreatingAppearance = false;
                        });
                    }
                    var hideNameplate = item.HideNameplate;
                    if (ImGui.Checkbox(Translator.LocalizeUI("Hide Nameplate##"), ref hideNameplate))
                    {
                        item.HideNameplate = hideNameplate;
                    }

                    if (!hideNameplate)
                    {
                        var nameplateAlias = item.NameplateAlias ?? "";
                        if (ImGui.InputText(Translator.LocalizeUI("Nameplate Alias##"), ref nameplateAlias, 40))
                        {
                            item.NameplateAlias = nameplateAlias;
                        }
                    }

                    if (_isCreatingAppearance)
                    {
                        ImGui.EndDisabled();
                    }
                }
            }
        }
    }

    private void DrawNPCCustomizations()
    {
        if (_roleplayingQuest != null)
        {
            var npcCustomizations = _roleplayingQuest.NpcCustomizations;
            if (npcCustomizations != null)
            {
                ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                if (ImGui.ListBox("##npcCustomization", ref _selectedNpcCustomization, _npcCustomizations, 13))
                {
                    //RefreshMenus();
                }
                if (ImGui.Button(Translator.LocalizeUI("Add")))
                {
                    var npcCustomization = new NpcInformation();
                    npcCustomizations[npcCustomizations.Count] = npcCustomization;
                    Task.Run(async () =>
                    {
                        _npcCustomizations = Utility.FillNewList(npcCustomizations.Count, await Translator.LocalizeText("NPC Appearance", Translator.UiLanguage, LanguageConversionProxy.LanguageEnum.English));
                        _selectedNpcCustomization = _npcCustomizations.Length - 1;
                    });
                }
                ImGui.SameLine();
                if (ImGui.Button(Translator.LocalizeUI("Remove")))
                {
                    npcCustomizations.Remove(_selectedNpcCustomization);
                    Task.Run(async () =>
                    {
                        _npcCustomizations = Utility.FillNewList(npcCustomizations.Count, await Translator.LocalizeText("NPC Appearance", Translator.UiLanguage, LanguageConversionProxy.LanguageEnum.English));
                        _selectedNpcCustomization = _npcCustomizations.Length - 1;
                    });
                }
            }
            else
            {
                _roleplayingQuest.NpcCustomizations = new System.Collections.Generic.Dictionary<int, NpcInformation>();
            }
        }
    }

    private async void RefreshMenus()
    {
        _npcCustomizations = Utility.FillNewList(_roleplayingQuest.NpcCustomizations.Count, await Translator.LocalizeText("NPC Appearance", Translator.UiLanguage, LanguageConversionProxy.LanguageEnum.English));
        _selectedNpcCustomization = _npcCustomizations.Length - 1;
    }

    private string ImportMcdfFileToQuestFolder(string sourceFilePath)
    {
        try
        {
            if (!File.Exists(sourceFilePath)) return null;
            if (_roleplayingQuest == null) return null;

            string questPath = Path.Combine(
                Plugin.Configuration.QuestInstallFolder,
                _roleplayingQuest.QuestName);
            Directory.CreateDirectory(questPath);

            string destFileName = Path.GetFileName(sourceFilePath);
            string destPath = Path.Combine(questPath, destFileName);

            if (File.Exists(destPath) && destPath != sourceFilePath)
            {
                string nameNoExt = Path.GetFileNameWithoutExtension(destFileName);
                string ext = Path.GetExtension(destFileName);
                destFileName = $"{nameNoExt}_{Guid.NewGuid().ToString()[..8]}{ext}";
                destPath = Path.Combine(questPath, destFileName);
            }

            if (!string.Equals(Path.GetFullPath(sourceFilePath), Path.GetFullPath(destPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourceFilePath, destPath, overwrite: true);
            }

            return destFileName;
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Warning(ex, $"Failed to import MCDF file '{sourceFilePath}' to quest folder.");
            return null;
        }
    }
}
