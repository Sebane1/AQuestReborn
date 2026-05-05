using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Bindings.ImGui;
using PenumbraAndGlamourerHelpers;
using RoleplayingQuestCore;
using SamplePlugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.IO;
using System.Threading.Tasks;
using McdfDataImporter;

namespace AQuestReborn.CustomNpc
{
    public class CustomNpcWindow : Window
    {
        private IDalamudPluginInterface _pluginInterface;
        private string[] npcItemNames = new string[] { };
        private List<CustomNpcCharacter> _customNpcCharacters = new List<CustomNpcCharacter>();
        private int _currentSelection = 0;
        private Dictionary<Guid, string> _currentGlamourerDesigns = new Dictionary<Guid, string>();
        private string[] _designListContents = new string[0];
        private int _designListSelectedIndex = 0;
        Plugin _plugin;
        private FileDialogManager _fileDialogManager;
        private bool _isCreatingMcdf;

        // Idle emote list built from Excel sheet
        private string[] _idleEmoteNames = new string[] { "None" };
        private ushort[] _idleEmoteRowIds = new ushort[] { 0 };
        private string _emoteSearchText = "";
        private string _victoryEmoteSearchText = "";
        private string _penumbraSearchText = "";
        
        // PlaceName list
        private string[] _placeNames = new string[] { "Unknown" };
        private string _placeNameSearchText = "";
        
        // ClassJob list
        private string[] _classJobNames = new string[] { "None" };
        private uint[] _classJobRowIds = new uint[] { 0 };
        private string _classJobSearchText = "";

        // Weapon list
        private string[] _weaponNames = new string[] { "None" };
        private uint[] _weaponItemIds = new uint[] { 0 };
        private string _weaponSearchText = "";
        private uint _lastWeaponClassJobId = uint.MaxValue; // Force refresh on first open

        // Monster list
        private string[] _monsterNames = new string[] { "None" };
        private int[] _monsterModelIds = new int[] { 0 };
        private string _monsterSearchText = "";

        // Memory tab cached data
        private string _memoryTabNpcName = "";
        private Dictionary<string, string> _cachedKeywordMemories = new Dictionary<string, string>();
        private Dictionary<string, List<string>> _cachedConversationSummaries = new Dictionary<string, List<string>>();
        private bool _memoryNeedsRefresh = true;
        private bool _showClearMemoryConfirm = false;
        private string _clearMemoryConfirmText = "";
        private bool _showDeleteNpcConfirm = false;
        private string _deleteNpcConfirmText = "";

        public Plugin Plugin { get => _plugin; set => _plugin = value; }
        public List<CustomNpcCharacter> CustomNpcCharacters { get => _customNpcCharacters; set => _customNpcCharacters = value; }

        public CustomNpcWindow(IDalamudPluginInterface pluginInterface) :
            base("Custom NPC Configuration")
        {
            _pluginInterface = pluginInterface;
            _customNpcCharacters.Add(new CustomNpcCharacter());
            Size = new Vector2(550, 800);
            SizeCondition = ImGuiCond.FirstUseEver;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(550, 400),
                MaximumSize = new Vector2(800, 2000),
            };
            _fileDialogManager = new FileDialogManager();
        }
        public override void OnOpen()
        {
            base.OnOpen();
            RefreshDesignList();
            RefreshEmoteList();
            RefreshPlaceNameList();
            RefreshClassJobList();
            RefreshMonsterList();
        }
        public override void OnClose()
        {
            base.OnClose();
        }
        public void RefreshDesignList()
        {
            _currentGlamourerDesigns = PenumbraAndGlamourerHelperFunctions.GetGlamourerDesigns();
            var list = _currentGlamourerDesigns.Values.ToList();
            list.Sort();
            _designListContents = list.ToArray();
        }
        public void RefreshEmoteList()
        {
            if (_plugin == null) return;
            try
            {
                var emotes = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>();
                var names = new List<string> { "None" };
                var rowIds = new List<ushort> { 0 };
                foreach (var emote in emotes)
                {
                    string name = emote.Name.ToString();
                    if (!string.IsNullOrWhiteSpace(name) && emote.ActionTimeline[0].RowId > 0)
                    {
                        names.Add(name);
                        rowIds.Add((ushort)emote.RowId);
                    }
                }
                _idleEmoteNames = names.ToArray();
                _idleEmoteRowIds = rowIds.ToArray();
            }
            catch (Exception e)
            {
                _plugin?.PluginLog?.Warning(e, "Failed to load emote list");
            }
        }

        public void RefreshPlaceNameList()
        {
            if (_plugin == null) return;
            try
            {
                var places = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.PlaceName>();
                var names = new HashSet<string>();
                foreach (var place in places)
                {
                    string name = place.Name.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }
                var sortedNames = names.ToList();
                sortedNames.Sort();
                sortedNames.Insert(0, "Unknown");
                _placeNames = sortedNames.ToArray();
            }
            catch (Exception e)
            {
                _plugin?.PluginLog?.Warning(e, "Failed to load placename list");
            }
        }

        public void RefreshClassJobList()
        {
            if (_plugin == null) return;
            try
            {
                var jobs = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>();
                var names = new List<string> { "None" };
                var rowIds = new List<uint> { 0 };
                foreach (var job in jobs)
                {
                    string name = job.Name.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                        rowIds.Add(job.RowId);
                    }
                }
                _classJobNames = names.ToArray();
                _classJobRowIds = rowIds.ToArray();
            }
            catch (Exception e)
            {
                _plugin?.PluginLog?.Warning(e, "Failed to load classjob list");
            }
        }

        public void RefreshMonsterList()
        {
            if (_plugin == null) return;
            try
            {
                var bNpcBases = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.BNpcBase>();
                var bNpcNames = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.BNpcName>();

                var names = new List<string> { "None" };
                var modelIds = new List<int> { 0 };
                var seenIds = new HashSet<int> { 0 };

                if (bNpcBases != null && bNpcNames != null)
                {
                    System.Collections.Generic.IReadOnlyDictionary<string, string> brioNameMap = null;
                    try
                    {
                        if (Brio.Resources.ResourceProvider.Instance != null)
                        {
                            brioNameMap = Brio.Resources.ResourceProvider.Instance.GetResourceDocument<System.Collections.Generic.IReadOnlyDictionary<string, string>>("Data.NpcNames.json");
                        }
                    }
                    catch { }

                    foreach (var bnpc in bNpcBases)
                    {
                        if (bnpc.ModelChara.RowId > 0 && bnpc.ModelChara.RowId < 10000)
                        {
                            int modelId = (int)bnpc.ModelChara.RowId;
                            if (seenIds.Contains(modelId)) continue;

                            string name = "";
                            string rawName = $"B:{bnpc.RowId:D7}";
                            
                            if (brioNameMap != null && brioNameMap.TryGetValue(rawName, out var mappedName))
                            {
                                rawName = mappedName;
                            }

                            if (rawName.StartsWith("N:"))
                            {
                                if (uint.TryParse(rawName.Substring(2), out var nameId))
                                {
                                    var nameRow = bNpcNames.GetRow(nameId);
                                    name = nameRow.Singular.ToString();
                                }
                            }
                            else if (!rawName.StartsWith("B:")) // if it was mapped to a direct string instead of an N: id
                            {
                                name = rawName;
                            }

                            if (string.IsNullOrWhiteSpace(name))
                            {
                                name = $"Monster Base {bnpc.RowId}";
                            }

                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                names.Add($"{name} (ID: {modelId})");
                                modelIds.Add(modelId);
                                seenIds.Add(modelId);
                            }
                        }
                    }
                }

                // Sort everything after "None"
                var combined = names.Skip(1).Zip(modelIds.Skip(1), (n, i) => new { Name = n, Id = i })
                    .OrderBy(x => x.Name).ToList();

                names = new List<string> { "None" };
                modelIds = new List<int> { 0 };

                names.AddRange(combined.Select(x => x.Name));
                modelIds.AddRange(combined.Select(x => x.Id));

                _monsterNames = names.ToArray();
                _monsterModelIds = modelIds.ToArray();
            }
            catch (Exception e)
            {
                _plugin?.PluginLog?.Warning(e, "Failed to load monster list");
            }
        }

        public void RefreshWeaponList(uint classJobId)
        {
            if (_plugin?.DataManager == null) return;
            if (_lastWeaponClassJobId == classJobId) return;

            try
            {
                List<string> names = new List<string> { "Default (From Class)" };
                List<uint> itemIds = new List<uint> { 0 };

                if (classJobId > 0)
                {
                    var cj = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>().GetRow(classJobId);
                    if (cj.RowId > 0)
                    {
                        string abrv = cj.Abbreviation.ToString();
                        var prop = typeof(Lumina.Excel.Sheets.ClassJobCategory).GetProperty(abrv);
                        if (prop != null)
                        {
                            var items = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                            foreach (var item in items)
                            {
                                if (item.EquipSlotCategory.RowId == 1 || item.EquipSlotCategory.RowId == 13) 
                                {
                                    var cjc = item.ClassJobCategory.Value;
                                    if (cjc.RowId != 0 && (bool)prop.GetValue(cjc))
                                    {
                                        string itemName = item.Name.ToString();
                                        if (!string.IsNullOrWhiteSpace(itemName) && item.ModelMain != 0)
                                        {
                                            names.Add(itemName);
                                            itemIds.Add(item.RowId);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Sort everything after "Default"
                var combined = names.Skip(1).Zip(itemIds.Skip(1), (n, i) => new { Name = n, Id = i })
                    .OrderBy(x => x.Name).ToList();

                names = new List<string> { "Default (From Class)" };
                itemIds = new List<uint> { 0 };

                names.AddRange(combined.Select(x => x.Name));
                itemIds.AddRange(combined.Select(x => x.Id));

                _weaponNames = names.ToArray();
                _weaponItemIds = itemIds.ToArray();
                _lastWeaponClassJobId = classJobId;
            }
            catch (Exception e)
            {
                _plugin?.PluginLog?.Warning(e, "Failed to load weapon list");
            }
        }

        public override void Draw()
        {
            try
            {
                _fileDialogManager.Draw();
                if (_currentGlamourerDesigns.Count is 0)
                {
                    RefreshDesignList();
                }
                if (_idleEmoteNames.Length <= 1)
                {
                    RefreshEmoteList();
                }
                if (_placeNames.Length <= 1)
                {
                    RefreshPlaceNameList();
                }
                if (_classJobNames.Length <= 1)
                {
                    RefreshClassJobList();
                }
                if (_monsterNames.Length <= 1)
                {
                    RefreshMonsterList();
                }
                RefreshNPCItemNames();

                // === Conversational Provider Settings (global, shown above NPC list) ===
                if (ImGui.CollapsingHeader("Conversational Provider Settings"))
                {
                    DrawAiProviderSettings();
                }
                ImGui.Spacing();

                ImGui.BeginTable("##CustomNpcTable", 2);
                ImGui.TableSetupColumn(Translator.LocalizeUI("Custom NPC"), ImGuiTableColumnFlags.WidthFixed, 200);
                ImGui.TableSetupColumn(Translator.LocalizeUI("Custom NPC Configuration"), ImGuiTableColumnFlags.WidthStretch, 300);
                ImGui.TableHeadersRow();
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                DrawListBox();
                ImGui.TableSetColumnIndex(1);
                DrawNPCConfigurator();
                ImGui.EndTable();
            }
            catch (Exception e)
            {
                Plugin?.PluginLog?.Warning(e, e.Message);
            }
        }
        public void LoadNPCCharacters(List<CustomNpcCharacter> customNpcCharacters)
        {
            if (customNpcCharacters.Count > 0)
            {
                _customNpcCharacters = customNpcCharacters;
            }
        }
        public void SaveNPCCharacters()
        {
            Plugin.Configuration.CustomNpcCharacters = _customNpcCharacters;
            Plugin.Configuration.Save();
        }

        private void DrawListBox()
        {
            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
            ImGui.ListBox("##NPCEditing", ref _currentSelection, npcItemNames);

            if (ImGui.Button("+", new Vector2(35)))
            {
                var newNpc = new CustomNpcCharacter();
                if (_currentGlamourerDesigns.Count > 0)
                {
                    newNpc.NpcGlamourerAppearanceString = _currentGlamourerDesigns.Keys.First().ToString();
                }
                _customNpcCharacters.Add(newNpc);
                SaveNPCCharacters();
            }

            ImGui.SameLine();

            // Shift must be held to activate delete
            bool deleteShiftHeld = ImGui.GetIO().KeyShift;
            string deleteNpcName = (_customNpcCharacters.Count > 0 && _currentSelection < _customNpcCharacters.Count)
                ? _customNpcCharacters[_currentSelection].NpcName : "";

            if (!deleteShiftHeld || _customNpcCharacters.Count == 0)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.4f);
                ImGui.Button("-", new Vector2(35));
                ImGui.PopStyleVar();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Hold Shift to delete this NPC.");
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.1f, 0.1f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.15f, 0.15f, 1f));
                if (ImGui.Button("-", new Vector2(35)))
                {
                    _showDeleteNpcConfirm = true;
                    _deleteNpcConfirmText = "";
                    ImGui.OpenPopup("##DeleteNpcConfirm");
                }
                ImGui.PopStyleColor(2);
            }

            // --- Delete NPC Confirmation Popup ---
            var deletePopupCenter = ImGui.GetMainViewport().Size / 2;
            ImGui.SetNextWindowPos(deletePopupCenter, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vector2(400, 0), ImGuiCond.Appearing);
            if (ImGui.BeginPopupModal("##DeleteNpcConfirm", ref _showDeleteNpcConfirm,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
            {
                ImGui.Dummy(new Vector2(0, 5));
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1f));
                ImGui.TextWrapped("\u26a0  WARNING: This will permanently delete \"" + deleteNpcName + "\" and ALL their data.");
                ImGui.PopStyleColor();
                ImGui.Dummy(new Vector2(0, 3));
                ImGui.TextWrapped("This includes:\n\u2022 All configuration and appearance settings\n\u2022 Relationships, memories, and conversation history\n\u2022 Travel history, sentiment, and mood data");
                ImGui.Dummy(new Vector2(0, 3));
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.4f, 1f), "This action cannot be undone.");
                ImGui.Dummy(new Vector2(0, 8));

                ImGui.TextWrapped("Type \"" + deleteNpcName + "\" to confirm:");
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                ImGui.InputTextWithHint("##deleteConfirmInput", deleteNpcName, ref _deleteNpcConfirmText, 100);
                ImGui.Dummy(new Vector2(0, 5));

                bool deleteNameMatches = string.Equals(_deleteNpcConfirmText.Trim(), deleteNpcName, StringComparison.OrdinalIgnoreCase);
                float deletePopupWidth = ImGui.GetContentRegionAvail().X;
                float deleteBtnW = (deletePopupWidth - 8) / 2;

                if (!deleteNameMatches)
                    ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.35f);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.7f, 0.1f, 0.1f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.2f, 0.2f, 1f));
                if (ImGui.Button("Yes, Delete NPC", new Vector2(deleteBtnW, 30)) && deleteNameMatches)
                {
                    if (_plugin != null && _plugin.AQuestReborn != null)
                    {
                        _plugin.AQuestReborn.DismissCustomNpc(deleteNpcName);
                    }
                    _customNpcCharacters.RemoveAt(_currentSelection);
                    _currentSelection = 0;
                    SaveNPCCharacters();
                    _showDeleteNpcConfirm = false;
                    _deleteNpcConfirmText = "";
                    ImGui.CloseCurrentPopup();
                }
                ImGui.PopStyleColor(2);
                if (!deleteNameMatches)
                    ImGui.PopStyleVar();

                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(deleteBtnW, 30)))
                {
                    _showDeleteNpcConfirm = false;
                    _deleteNpcConfirmText = "";
                    ImGui.CloseCurrentPopup();
                }

                ImGui.Dummy(new Vector2(0, 3));
                ImGui.EndPopup();
            }
        }

        private void DrawNPCConfigurator()
        {
            if (_currentGlamourerDesigns.Count > 0)
            {
                if (_customNpcCharacters.Count > 0 && _currentSelection < _customNpcCharacters.Count)
                {
                    // Sync the design list selection with the current NPC's stored design
                    Guid guid = Guid.Empty;
                    Guid.TryParse(_customNpcCharacters[_currentSelection].NpcGlamourerAppearanceString, out guid);
                    if (_currentGlamourerDesigns.ContainsKey(guid))
                    {
                        var sortedList = _currentGlamourerDesigns.Values.ToList();
                        sortedList.Sort();
                        int idx = sortedList.IndexOf(_currentGlamourerDesigns[guid]);
                        if (idx >= 0) _designListSelectedIndex = idx;
                    }

                    if (ImGui.BeginTabBar("NpcConfigTabs"))
                    {
                        if (ImGui.BeginTabItem(Translator.LocalizeUI("General & Appearance")))
                        {
                            ImGui.LabelText("##personalityLabel", Translator.LocalizeUI("NPC Name"));
                            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                            if (ImGui.InputText("##NPCName", ref _customNpcCharacters[_currentSelection].NpcName, 255))
                            {
                                SaveNPCCharacters();
                            }

                            ImGui.Dummy(new Vector2(0, 5));

                            // Appearance mode combo
                            string[] appearanceMethods = { Translator.LocalizeUI("MCDF File"), Translator.LocalizeUI("Monster Model"), Translator.LocalizeUI("Glamourer / Penumbra") };
                            int appearanceMethod = 2;
                            if (_customNpcCharacters[_currentSelection].UseMcdfAppearance) appearanceMethod = 0;
                            else if (_customNpcCharacters[_currentSelection].UseMonsterModel) appearanceMethod = 1;

                            ImGui.LabelText("##appearanceModeLabel", Translator.LocalizeUI("Appearance Method"));
                            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                            if (ImGui.Combo("##appearanceMode", ref appearanceMethod, appearanceMethods, appearanceMethods.Length))
                            {
                                bool wasMonster = _customNpcCharacters[_currentSelection].UseMonsterModel;

                                _customNpcCharacters[_currentSelection].UseMcdfAppearance = (appearanceMethod == 0);
                                _customNpcCharacters[_currentSelection].UseMonsterModel = (appearanceMethod == 1);
                                _customNpcCharacters[_currentSelection].UsePenumbraCollection = (appearanceMethod == 2);
                                SaveNPCCharacters();

                                // Re-apply immediately if spawned
                                if (_customNpcCharacters[_currentSelection].IsFollowingPlayer || _customNpcCharacters[_currentSelection].IsStaying)
                                {
                                    if (_plugin != null && _plugin.AQuestReborn != null)
                                    {
                                        if (appearanceMethod == 1) // Switched TO Monster
                                        {
                                            _plugin.AQuestReborn.ReapplyCustomNpcMonsterAppearance(
                                                _customNpcCharacters[_currentSelection].NpcName, _customNpcCharacters[_currentSelection].MonsterModelId);
                                        }
                                        else if (wasMonster && appearanceMethod != 1) // Switched AWAY FROM Monster
                                        {
                                            // Reset back to Humanoid (0)
                                            _plugin.AQuestReborn.ReapplyCustomNpcMonsterAppearance(
                                                _customNpcCharacters[_currentSelection].NpcName, 0);

                                            // Re-apply the newly selected appearance
                                            if (appearanceMethod == 0 && !string.IsNullOrEmpty(_customNpcCharacters[_currentSelection].McdfFilePath))
                                            {
                                                _plugin.AQuestReborn.ReapplyCustomNpcMcdfAppearance(
                                                    _customNpcCharacters[_currentSelection].NpcName, _customNpcCharacters[_currentSelection].McdfFilePath);
                                            }
                                            else if (appearanceMethod == 2 && Guid.TryParse(_customNpcCharacters[_currentSelection].NpcGlamourerAppearanceString, out var designGuid))
                                            {
                                                _plugin.AQuestReborn.ReapplyCustomNpcAppearance(
                                                    _customNpcCharacters[_currentSelection].NpcName, designGuid);
                                            }
                                        }
                                    }
                                }
                            }

                            if (_customNpcCharacters[_currentSelection].UseMcdfAppearance)
                            {
                                // Create MCDF from player appearance
                                if (_isCreatingMcdf)
                                {
                                    ImGui.BeginDisabled();
                                }
                                if (ImGui.Button(Translator.LocalizeUI(_isCreatingMcdf ? "Creating Appearance..." : "Create MCDF From Player Appearance##customnpc")))
                                {
                                    Task.Run(() =>
                                    {
                                        _isCreatingMcdf = true;
                                        try
                                        {
                                            string npcName = _customNpcCharacters[_currentSelection].NpcName;
                                            string mcdfDir = Path.Combine(_plugin.Configuration.QuestInstallFolder, "CustomNpcs");
                                            Directory.CreateDirectory(mcdfDir);
                                            string mcdfName = npcName + "-" + Guid.NewGuid().ToString() + ".mcdf";
                                            string mcdfPath = Path.Combine(mcdfDir, mcdfName);
                                            AppearanceAccessUtils.AppearanceManager.CreateMCDF(mcdfPath);
                                            _customNpcCharacters[_currentSelection].McdfFilePath = mcdfPath;
                                            _customNpcCharacters[_currentSelection].UseMcdfAppearance = true;
                                            SaveNPCCharacters();

                                            // Apply immediately if NPC is spawned
                                            if (_plugin?.AQuestReborn != null)
                                            {
                                                _plugin.AQuestReborn.ReapplyCustomNpcMcdfAppearance(npcName, mcdfPath);
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            _plugin?.PluginLog?.Warning(e, "Failed to create MCDF");
                                        }
                                        finally
                                        {
                                            _isCreatingMcdf = false;
                                        }
                                    });
                                }
                                if (_isCreatingMcdf)
                                {
                                    ImGui.EndDisabled();
                                }

                                // MCDF file path input
                                ImGui.LabelText("##mcdfLabel", Translator.LocalizeUI("MCDF File Path"));
                                ImGui.SetNextItemWidth(ImGui.GetColumnWidth() - 80);
                                if (ImGui.InputText("##McdfPath", ref _customNpcCharacters[_currentSelection].McdfFilePath, 1024))
                                {
                                    SaveNPCCharacters();
                                }
                                ImGui.SameLine();
                                if (ImGui.Button(Translator.LocalizeUI("Browse"), new Vector2(70, 0)))
                                {
                                    _fileDialogManager.Reset();
                                    ImGui.OpenPopup("OpenMcdfDialog##customnpc");
                                }
                                if (ImGui.BeginPopup("OpenMcdfDialog##customnpc"))
                                {
                                    _fileDialogManager.OpenFileDialog(Translator.LocalizeUI("Select MCDF File"), ".mcdf", (isOk, file) =>
                                    {
                                        if (isOk && file.Count > 0)
                                        {
                                            _customNpcCharacters[_currentSelection].McdfFilePath = file[0];
                                            SaveNPCCharacters();

                                            // Apply immediately if NPC is spawned
                                            if ((_customNpcCharacters[_currentSelection].IsFollowingPlayer || _customNpcCharacters[_currentSelection].IsStaying)
                                                && _plugin != null && _plugin.AQuestReborn != null)
                                            {
                                                _plugin.AQuestReborn.ReapplyCustomNpcMcdfAppearance(
                                                    _customNpcCharacters[_currentSelection].NpcName, file[0]);
                                            }
                                        }
                                    }, 0, null, true);
                                    ImGui.EndPopup();
                                }
                            }
                            else if (_customNpcCharacters[_currentSelection].UseMonsterModel)
                            {
                                ImGui.LabelText("##monsterLabel", Translator.LocalizeUI("Monster Model"));
                                ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                                
                                int currentModelId = (int)_customNpcCharacters[_currentSelection].MonsterModelId;
                                string previewValue = currentModelId.ToString();
                                for (int i = 0; i < _monsterModelIds.Length; i++)
                                {
                                    if (_monsterModelIds[i] == currentModelId)
                                    {
                                        previewValue = _monsterNames[i];
                                        break;
                                    }
                                }

                                if (_monsterNames.Length > 1)
                                {
                                    ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), Translator.LocalizeUI("Current") + ": " + previewValue);
                                    ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                                    ImGui.InputTextWithHint("##monsterSearch", Translator.LocalizeUI("Search monsters..."), ref _monsterSearchText, 100);
                                    
                                    if (ImGui.BeginChild("##MonsterModelList", new Vector2(ImGui.GetColumnWidth(), 120), true))
                                    {
                                        for (int i = 0; i < _monsterNames.Length; i++)
                                        {
                                            string name = _monsterNames[i];
                                            int id = _monsterModelIds[i];

                                            if (!string.IsNullOrEmpty(_monsterSearchText) && !name.Contains(_monsterSearchText, StringComparison.OrdinalIgnoreCase))
                                                continue;

                                            bool isSelected = (currentModelId == id);
                                            if (ImGui.Selectable(name + "##monster_" + i, isSelected))
                                            {
                                                _customNpcCharacters[_currentSelection].MonsterModelId = (uint)id;
                                                SaveNPCCharacters();
                                                
                                                // Re-apply immediately if spawned
                                                if (_customNpcCharacters[_currentSelection].IsFollowingPlayer || _customNpcCharacters[_currentSelection].IsStaying)
                                                {
                                                    if (_plugin != null && _plugin.AQuestReborn != null)
                                                    {
                                                        _plugin.AQuestReborn.ReapplyCustomNpcMonsterAppearance(
                                                            _customNpcCharacters[_currentSelection].NpcName, (uint)id);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    ImGui.EndChild();
                                }

                                // Keep manual entry as an option for raw IDs just in case
                                ImGui.SetNextItemWidth(ImGui.GetColumnWidth() - 80);
                                if (ImGui.InputInt("##MonsterModelIdRaw", ref currentModelId))
                                {
                                    _customNpcCharacters[_currentSelection].MonsterModelId = (uint)Math.Max(0, currentModelId);
                                    SaveNPCCharacters();
                                }
                            }
                            else
                            {
                                // Glamourer / Penumbra
                                ImGui.LabelText("##glamourerLabel", Translator.LocalizeUI("Glamourer Design Appearance"));
                                ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                                if (ImGui.Combo("##savedDesigns", ref _designListSelectedIndex, _designListContents, _designListContents.Length))
                                {
                                    // Find the GUID that corresponds to the selected design name
                                    if (_designListSelectedIndex >= 0 && _designListSelectedIndex < _designListContents.Length)
                                    {
                                        string selectedName = _designListContents[_designListSelectedIndex];
                                        foreach (var kvp in _currentGlamourerDesigns)
                                        {
                                            if (kvp.Value == selectedName)
                                            {
                                                _customNpcCharacters[_currentSelection].NpcGlamourerAppearanceString = kvp.Key.ToString();
                                                SaveNPCCharacters();

                                                // Re-apply appearance if NPC is currently spawned
                                                if ((_customNpcCharacters[_currentSelection].IsFollowingPlayer || _customNpcCharacters[_currentSelection].IsStaying)
                                                    && _plugin != null && _plugin.AQuestReborn != null)
                                                {
                                                    _plugin.AQuestReborn.ReapplyCustomNpcAppearance(
                                                        _customNpcCharacters[_currentSelection].NpcName, kvp.Key);
                                                }
                                                break;
                                            }
                                        }
                                    }
                                }

                                ImGui.LabelText("##penumbraLabel", Translator.LocalizeUI("Penumbra Collection Name"));
                                ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                                if (Brio.Brio.TryGetService<Brio.IPC.PenumbraService>(out var penumbraService))
                                {
                                    var collections = penumbraService.GetCollections().Values.OrderBy(name => name).ToArray();
                                    
                                    if (ImGui.BeginCombo("##PenumbraCollectionCombo", _customNpcCharacters[_currentSelection].PenumbraCollection ?? ""))
                                    {
                                        ImGui.InputTextWithHint("##penumbraSearch", Translator.LocalizeUI("Search collections..."), ref _penumbraSearchText, 100);
                                        
                                        foreach (var collectionName in collections)
                                        {
                                            if (!string.IsNullOrEmpty(_penumbraSearchText) && !collectionName.Contains(_penumbraSearchText, StringComparison.OrdinalIgnoreCase))
                                                continue;

                                            bool isSelected = (_customNpcCharacters[_currentSelection].PenumbraCollection == collectionName);
                                            if (ImGui.Selectable(collectionName, isSelected))
                                            {
                                                _customNpcCharacters[_currentSelection].PenumbraCollection = collectionName;
                                                SaveNPCCharacters();
                                                
                                                // Auto apply if spawned
                                                if (_customNpcCharacters[_currentSelection].IsFollowingPlayer || _customNpcCharacters[_currentSelection].IsStaying)
                                                {
                                                    if (_plugin != null && _plugin.AQuestReborn != null)
                                                    {
                                                        _plugin.AQuestReborn.ReapplyCustomNpcPenumbraAppearance(
                                                            _customNpcCharacters[_currentSelection].NpcName, _customNpcCharacters[_currentSelection].PenumbraCollection);
                                                    }
                                                }
                                            }
                                            if (isSelected)
                                                ImGui.SetItemDefaultFocus();
                                        }
                                        ImGui.EndCombo();
                                    }
                                }
                                else
                                {
                                    ImGui.TextColored(new Vector4(1, 0, 0, 1), Translator.LocalizeUI("Penumbra is not installed or available."));
                                }
                            }
                            
                            ImGui.EndTabItem();
                        }

                        if (ImGui.BeginTabItem(Translator.LocalizeUI("Lore & Personality")))
                        {
                            if (ImGui.BeginTabBar("LorePersonalityTabBar"))
                            {
                                if (ImGui.BeginTabItem(Translator.LocalizeUI("Personality")))
                                {
                                    ImGui.LabelText("##greetingLabel", Translator.LocalizeUI("NPC Greeting"));
                                    ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                                    if (ImGui.InputText("##Greeting", ref _customNpcCharacters[_currentSelection].NPCGreeting, 500))
                                    {
                                        SaveNPCCharacters();
                                    }

                                    ImGui.LabelText("##personalityFieldLabel", Translator.LocalizeUI("NPC Personality"));
                                    ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                                    if (ImGui.InputTextMultiline("##NpcPersonality", ref _customNpcCharacters[_currentSelection].NpcPersonality, 2000, new Vector2(ImGui.GetColumnWidth(), 100)))
                                    {
                                        SaveNPCCharacters();
                                    }

                                    ImGui.LabelText("##npcHobbiesLabel", Translator.LocalizeUI("Hobbies"));
                                    ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                                    if (ImGui.InputText("##NpcHobbies", ref _customNpcCharacters[_currentSelection].NpcHobbies, 500))
                                    {
                                        SaveNPCCharacters();
                                    }
                                    
                                    ImGui.Dummy(new Vector2(0, 15));

                                    // Only show re-roll button when using the default built-in AI
                                    string currentProvider = _plugin?.Configuration?.AiProvider ?? "default";
                                    if (currentProvider == "default" || string.IsNullOrEmpty(currentProvider))
                                    {
                                        if (ImGui.Button(Translator.LocalizeUI("Re-Roll Brain"), new Vector2(ImGui.GetColumnWidth(), 30)))
                                        {
                                            _customNpcCharacters[_currentSelection].ModelChoice = "";
                                            SaveNPCCharacters();
                                        }
                                        if (ImGui.IsItemHovered())
                                            ImGui.SetTooltip("Randomly assigns a new default AI brain for this NPC.\nDoes not affect memories or relationships.");
                                    }
                                    
                                    ImGui.EndTabItem();
                                }

                                if (ImGui.BeginTabItem(Translator.LocalizeUI("Lore")))
                                {
                                    ImGui.LabelText("##npcBirthDateLabel", Translator.LocalizeUI("Birth Date (Lore)"));
                                    ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                                    if (ImGui.InputText("##NpcBirthDate", ref _customNpcCharacters[_currentSelection].NpcBirthDate, 100))
                                    {
                                        SaveNPCCharacters();
                                    }

                                    ImGui.LabelText("##npcBirthLocationLabel", Translator.LocalizeUI("Birth Location (Lore)"));
                                    ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), Translator.LocalizeUI("Current") + ": " + 
                                        (string.IsNullOrEmpty(_customNpcCharacters[_currentSelection].NpcBirthLocation) ? Translator.LocalizeUI("Unknown") : _customNpcCharacters[_currentSelection].NpcBirthLocation));
                                    ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                                    ImGui.InputTextWithHint("##placeNameSearch", Translator.LocalizeUI("Search locations..."), ref _placeNameSearchText, 100);
                                    if (ImGui.BeginChild("##placeNameList", new Vector2(ImGui.GetColumnWidth(), 120), true))
                                    {
                                        for (int i = 0; i < _placeNames.Length; i++)
                                        {
                                            if (!string.IsNullOrEmpty(_placeNameSearchText)
                                                && !_placeNames[i].Contains(_placeNameSearchText, StringComparison.OrdinalIgnoreCase))
                                                continue;
                                            bool isSelected = _placeNames[i] == _customNpcCharacters[_currentSelection].NpcBirthLocation;
                                            if (ImGui.Selectable(_placeNames[i] + "##" + i, isSelected))
                                            {
                                                _customNpcCharacters[_currentSelection].NpcBirthLocation = _placeNames[i] == "Unknown" ? "" : _placeNames[i];
                                                // Home location counts as a place they've been
                                                if (!string.IsNullOrEmpty(_customNpcCharacters[_currentSelection].NpcBirthLocation))
                                                    _customNpcCharacters[_currentSelection].RecordVisit(_customNpcCharacters[_currentSelection].NpcBirthLocation);
                                                SaveNPCCharacters();
                                            }
                                        }
                                    }
                                    ImGui.EndChild();
                                    
                                    ImGui.EndTabItem();
                                }
                                
                                ImGui.EndTabBar();
                            }
                            
                            ImGui.EndTabItem();
                        }

                        if (ImGui.BeginTabItem(Translator.LocalizeUI("Behavior & Animation")))
                        {
                            ImGui.Dummy(new Vector2(0, 10));

                            // Idle pose selector with search
                            ImGui.LabelText("##idlePoseLabel", Translator.LocalizeUI("Idle Pose"));
                            _customNpcCharacters[_currentSelection].RandomIdleEmotes ??= new System.Collections.Generic.List<ushort>();
                            string currentEmoteName = Translator.LocalizeUI("None");
                            if (_customNpcCharacters[_currentSelection].RandomIdleEmotes.Count > 0)
                            {
                                currentEmoteName = Translator.LocalizeUI("Multiple (Random)");
                            }
                            else
                            {
                                int currentEmoteIdx = Array.IndexOf(_idleEmoteRowIds, _customNpcCharacters[_currentSelection].IdleEmoteId);
                                currentEmoteName = currentEmoteIdx >= 0 && currentEmoteIdx < _idleEmoteNames.Length
                                    ? _idleEmoteNames[currentEmoteIdx] : Translator.LocalizeUI("None");
                            }
                            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), Translator.LocalizeUI("Current") + ": " + currentEmoteName);
                            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                            ImGui.InputTextWithHint("##emoteSearch", Translator.LocalizeUI("Search emotes..."), ref _emoteSearchText, 100);
                            if (ImGui.BeginChild("##emoteList", new Vector2(ImGui.GetColumnWidth(), 120), true))
                            {
                                for (int i = 0; i < _idleEmoteNames.Length; i++)
                                {
                                    if (!string.IsNullOrEmpty(_emoteSearchText)
                                        && !_idleEmoteNames[i].Contains(_emoteSearchText, StringComparison.OrdinalIgnoreCase))
                                        continue;
                                    
                                    ushort currentId = _idleEmoteRowIds[i];
                                    bool isSelected = _customNpcCharacters[_currentSelection].RandomIdleEmotes.Contains(currentId) 
                                        || (_customNpcCharacters[_currentSelection].RandomIdleEmotes.Count == 0 && currentId == _customNpcCharacters[_currentSelection].IdleEmoteId);
                                        
                                    if (ImGui.Selectable(_idleEmoteNames[i] + "##" + i, isSelected))
                                    {
                                        if (currentId == 0) // "None" clicked
                                        {
                                            _customNpcCharacters[_currentSelection].RandomIdleEmotes.Clear();
                                            _customNpcCharacters[_currentSelection].IdleEmoteId = 0;
                                        }
                                        else
                                        {
                                            if (_customNpcCharacters[_currentSelection].RandomIdleEmotes.Contains(currentId))
                                            {
                                                _customNpcCharacters[_currentSelection].RandomIdleEmotes.Remove(currentId);
                                                if (_customNpcCharacters[_currentSelection].RandomIdleEmotes.Count == 1)
                                                {
                                                    _customNpcCharacters[_currentSelection].IdleEmoteId = _customNpcCharacters[_currentSelection].RandomIdleEmotes[0];
                                                    _customNpcCharacters[_currentSelection].RandomIdleEmotes.Clear();
                                                }
                                                else if (_customNpcCharacters[_currentSelection].RandomIdleEmotes.Count == 0)
                                                {
                                                    _customNpcCharacters[_currentSelection].IdleEmoteId = 0;
                                                }
                                            }
                                            else
                                            {
                                                if (_customNpcCharacters[_currentSelection].RandomIdleEmotes.Count == 0 && _customNpcCharacters[_currentSelection].IdleEmoteId > 0 && _customNpcCharacters[_currentSelection].IdleEmoteId != currentId)
                                                {
                                                    _customNpcCharacters[_currentSelection].RandomIdleEmotes.Add(_customNpcCharacters[_currentSelection].IdleEmoteId);
                                                }
                                                _customNpcCharacters[_currentSelection].RandomIdleEmotes.Add(currentId);
                                                _customNpcCharacters[_currentSelection].IdleEmoteId = 0; // Handled by RandomIdleEmotes now
                                            }
                                        }

                                        SaveNPCCharacters();
                                        // Push to live NPC immediately
                                        if (_plugin?.AQuestReborn?.InteractiveNpcDictionary != null
                                            && _plugin.AQuestReborn.InteractiveNpcDictionary.TryGetValue(
                                                _customNpcCharacters[_currentSelection].NpcName, out var liveNpc))
                                        {
                                            liveNpc.RandomIdleEmotes = _customNpcCharacters[_currentSelection].RandomIdleEmotes.ToList();
                                            liveNpc.IdleEmoteId = _customNpcCharacters[_currentSelection].IdleEmoteId;
                                        }
                                    }
                                }
                            }
                            ImGui.EndChild();

                            ImGui.Dummy(new Vector2(0, 5));

                            // Victory pose selector with search
                            ImGui.LabelText("##victoryPoseLabel", Translator.LocalizeUI("Victory Pose"));
                            int currentVicEmoteIdx = Array.IndexOf(_idleEmoteRowIds, _customNpcCharacters[_currentSelection].VictoryPoseEmoteId);
                            string currentVicEmoteName = currentVicEmoteIdx >= 0 && currentVicEmoteIdx < _idleEmoteNames.Length
                                ? _idleEmoteNames[currentVicEmoteIdx] : Translator.LocalizeUI("None");
                            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), Translator.LocalizeUI("Current") + ": " + currentVicEmoteName);
                            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                            ImGui.InputTextWithHint("##victoryEmoteSearch", Translator.LocalizeUI("Search emotes..."), ref _victoryEmoteSearchText, 100);
                            if (ImGui.BeginChild("##victoryEmoteList", new Vector2(ImGui.GetColumnWidth(), 120), true))
                            {
                                // Add "None" option
                                bool isVicNoneSelected = _customNpcCharacters[_currentSelection].VictoryPoseEmoteId == 0;
                                if (ImGui.Selectable("None##vic_none", isVicNoneSelected))
                                {
                                    _customNpcCharacters[_currentSelection].VictoryPoseEmoteId = 0;
                                    SaveNPCCharacters();
                                    if (_plugin?.AQuestReborn?.InteractiveNpcDictionary != null
                                        && _plugin.AQuestReborn.InteractiveNpcDictionary.TryGetValue(
                                            _customNpcCharacters[_currentSelection].NpcName, out var liveNpc))
                                    {
                                        liveNpc.VictoryPoseEmoteId = 0;
                                    }
                                }

                                for (int i = 0; i < _idleEmoteNames.Length; i++)
                                {
                                    if (!string.IsNullOrEmpty(_victoryEmoteSearchText)
                                        && !_idleEmoteNames[i].Contains(_victoryEmoteSearchText, StringComparison.OrdinalIgnoreCase))
                                        continue;
                                    bool isSelected = _idleEmoteRowIds[i] == _customNpcCharacters[_currentSelection].VictoryPoseEmoteId;
                                    if (ImGui.Selectable(_idleEmoteNames[i] + "##vic_" + i, isSelected))
                                    {
                                        _customNpcCharacters[_currentSelection].VictoryPoseEmoteId = _idleEmoteRowIds[i];
                                        SaveNPCCharacters();
                                        // Push to live NPC immediately
                                        if (_plugin?.AQuestReborn?.InteractiveNpcDictionary != null
                                            && _plugin.AQuestReborn.InteractiveNpcDictionary.TryGetValue(
                                                _customNpcCharacters[_currentSelection].NpcName, out var liveNpc))
                                        {
                                            liveNpc.VictoryPoseEmoteId = _idleEmoteRowIds[i];
                                        }
                                    }
                                }
                            }
                            ImGui.EndChild();
                            
                            ImGui.EndTabItem();
                        }

                        if (ImGui.BeginTabItem(Translator.LocalizeUI("Job & Weapon")))
                        {
                            ImGui.Dummy(new Vector2(0, 10));

                            ImGui.LabelText("##npcJobLabel2", Translator.LocalizeUI("Profession/Job"));
                            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), Translator.LocalizeUI("Current") + ": " + 
                                (string.IsNullOrEmpty(_customNpcCharacters[_currentSelection].NpcJob) ? Translator.LocalizeUI("None") : _customNpcCharacters[_currentSelection].NpcJob));
                            ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                            ImGui.InputTextWithHint("##classJobSearch2", Translator.LocalizeUI("Search jobs..."), ref _classJobSearchText, 100);
                            if (ImGui.BeginChild("##classJobList2", new Vector2(ImGui.GetColumnWidth(), 150), true))
                            {
                                for (int i = 0; i < _classJobNames.Length; i++)
                                {
                                    if (!string.IsNullOrEmpty(_classJobSearchText)
                                        && !_classJobNames[i].Contains(_classJobSearchText, StringComparison.OrdinalIgnoreCase))
                                        continue;
                                    bool isSelected = _classJobRowIds[i] == _customNpcCharacters[_currentSelection].NpcClassJobId;
                                    if (ImGui.Selectable(_classJobNames[i] + "##job2_" + i, isSelected))
                                    {
                                        _customNpcCharacters[_currentSelection].NpcJob = _classJobNames[i] == "None" ? "" : _classJobNames[i];
                                        _customNpcCharacters[_currentSelection].NpcClassJobId = _classJobRowIds[i];
                                        _customNpcCharacters[_currentSelection].NpcEquippedWeaponItemId = 0;
                                        SaveNPCCharacters();
                                        if (_plugin?.AQuestReborn?.InteractiveNpcDictionary != null
                                            && _plugin.AQuestReborn.InteractiveNpcDictionary.TryGetValue(
                                                _customNpcCharacters[_currentSelection].NpcName, out var liveNpc))
                                        {
                                            liveNpc.TargetClassJobId = _classJobRowIds[i];
                                            liveNpc.TargetWeaponItemId = 0;
                                            _plugin.AnamcoreManager.SetWeapon(liveNpc.Character, 0, 0);
                                            liveNpc.ClassWeaponApplied = false;
                                        }
                                    }
                                }
                            }
                            ImGui.EndChild();

                            if (_customNpcCharacters[_currentSelection].NpcClassJobId > 0)
                            {
                                ImGui.Dummy(new Vector2(0, 10));
                                RefreshWeaponList(_customNpcCharacters[_currentSelection].NpcClassJobId);
                                
                                ImGui.LabelText("##npcWeaponLabel2", Translator.LocalizeUI("Equipped Weapon"));
                                
                                int currentWeaponIdx = Array.IndexOf(_weaponItemIds, _customNpcCharacters[_currentSelection].NpcEquippedWeaponItemId);
                                string currentWeaponName = currentWeaponIdx >= 0 && currentWeaponIdx < _weaponNames.Length
                                    ? _weaponNames[currentWeaponIdx] : Translator.LocalizeUI("Default (From Class)");
                                    
                                ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), Translator.LocalizeUI("Current") + ": " + currentWeaponName);
                                ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                                ImGui.InputTextWithHint("##weaponSearch2", Translator.LocalizeUI("Search weapons..."), ref _weaponSearchText, 100);
                                if (ImGui.BeginChild("##weaponList2", new Vector2(ImGui.GetColumnWidth(), 150), true))
                                {
                                    for (int i = 0; i < _weaponNames.Length; i++)
                                    {
                                        if (!string.IsNullOrEmpty(_weaponSearchText)
                                            && !_weaponNames[i].Contains(_weaponSearchText, StringComparison.OrdinalIgnoreCase))
                                            continue;
                                            
                                        bool isSelected = _weaponItemIds[i] == _customNpcCharacters[_currentSelection].NpcEquippedWeaponItemId;
                                        if (ImGui.Selectable(_weaponNames[i] + "##wep2_" + i, isSelected))
                                        {
                                            _customNpcCharacters[_currentSelection].NpcEquippedWeaponItemId = _weaponItemIds[i];
                                            SaveNPCCharacters();
                                            
                                            if (_plugin?.AQuestReborn?.InteractiveNpcDictionary != null
                                                && _plugin.AQuestReborn.InteractiveNpcDictionary.TryGetValue(
                                                    _customNpcCharacters[_currentSelection].NpcName, out var liveNpc))
                                            {
                                                liveNpc.TargetWeaponItemId = _weaponItemIds[i];
                                                _plugin.AnamcoreManager.SetWeapon(liveNpc.Character, 0, 0);
                                                liveNpc.ClassWeaponApplied = false;
                                            }
                                        }
                                    }
                                }
                                ImGui.EndChild();
                            }

                            ImGui.EndTabItem();
                        }

                        if (ImGui.BeginTabItem(Translator.LocalizeUI("Memory")))
                        {
                            var npc = _customNpcCharacters[_currentSelection];

                            // Refresh cached memory files when NPC selection changes or on demand
                            if (_memoryTabNpcName != npc.NpcName || _memoryNeedsRefresh)
                            {
                                _memoryTabNpcName = npc.NpcName;
                                _memoryNeedsRefresh = false;
                                LoadMemoryFilesForNpc(npc.NpcName);
                            }

                            // --- Conversation Summaries ---
                            ImGui.TextColored(new Vector4(0.6f, 1f, 0.8f, 1f), "Conversation Summaries");
                            ImGui.Separator();

                            if (_cachedConversationSummaries.Count == 0)
                            {
                                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No conversation summaries yet.");
                            }
                            else
                            {
                                if (ImGui.BeginTabBar("##ConvSummaryTabs"))
                                {
                                    foreach (var kvp in _cachedConversationSummaries)
                                    {
                                        if (ImGui.BeginTabItem(kvp.Key + "##convTab"))
                                        {
                                            if (ImGui.BeginChild($"##convScroll_{kvp.Key}", new Vector2(ImGui.GetColumnWidth(), 140), true))
                                            {
                                                for (int i = 0; i < kvp.Value.Count; i++)
                                                {
                                                    ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
                                                    ImGui.TextWrapped($"{i + 1}. {kvp.Value[i]}");
                                                    ImGui.PopTextWrapPos();
                                                }
                                            }
                                            ImGui.EndChild();
                                            ImGui.EndTabItem();
                                        }
                                    }
                                    ImGui.EndTabBar();
                                }
                            }

                            ImGui.Dummy(new Vector2(0, 5));

                            // --- Keyword Memories ---
                            ImGui.TextColored(new Vector4(1f, 0.7f, 0.5f, 1f), "Keyword Memories");
                            ImGui.Separator();

                            if (_cachedKeywordMemories.Count == 0)
                            {
                                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No keyword memories yet.");
                            }
                            else
                            {
                                if (ImGui.BeginTable("##KeywordTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
                                {
                                    ImGui.TableSetupColumn("Keyword", ImGuiTableColumnFlags.WidthFixed, 120);
                                    ImGui.TableSetupColumn("Memory", ImGuiTableColumnFlags.WidthStretch);
                                    ImGui.TableHeadersRow();

                                    foreach (var kvp in _cachedKeywordMemories)
                                    {
                                        ImGui.TableNextRow();
                                        ImGui.TableSetColumnIndex(0);
                                        ImGui.Text(kvp.Key);
                                        ImGui.TableSetColumnIndex(1);
                                        ImGui.TextWrapped(kvp.Value);
                                    }
                                    ImGui.EndTable();
                                }
                            }

                            ImGui.Dummy(new Vector2(0, 5));

                            // --- Relationships ---
                            ImGui.TextColored(new Vector4(0.9f, 0.6f, 1f, 1f), "Relationships");
                            ImGui.Separator();

                            if (npc.EncounterCounts.Count == 0)
                            {
                                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No relationships formed yet.");
                            }
                            else
                            {
                                if (ImGui.BeginTable("##RelationshipTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
                                {
                                    ImGui.TableSetupColumn("Person", ImGuiTableColumnFlags.WidthStretch);
                                    ImGui.TableSetupColumn("Bond", ImGuiTableColumnFlags.WidthFixed, 90);
                                    ImGui.TableSetupColumn("Meetings", ImGuiTableColumnFlags.WidthFixed, 55);
                                    ImGui.TableSetupColumn("Last Seen", ImGuiTableColumnFlags.WidthFixed, 65);
                                    ImGui.TableHeadersRow();

                                    foreach (var kvp in npc.EncounterCounts)
                                    {
                                        var relationship = npc.GetRelationshipWith(kvp.Key);

                                        ImGui.TableNextRow();
                                        ImGui.TableSetColumnIndex(0);
                                        ImGui.Text(kvp.Key);

                                        ImGui.TableSetColumnIndex(1);
                                        // Color the bond label based on score
                                        Vector4 bondColor;
                                        if (relationship.Score >= 80)
                                            bondColor = new Vector4(1f, 0.84f, 0f, 1f);    // Gold
                                        else if (relationship.Score >= 60)
                                            bondColor = new Vector4(0.4f, 1f, 0.4f, 1f);   // Green
                                        else if (relationship.Score >= 40)
                                            bondColor = new Vector4(0.4f, 0.8f, 1f, 1f);   // Blue
                                        else if (relationship.Score >= 20)
                                            bondColor = new Vector4(0.7f, 0.7f, 0.7f, 1f); // Gray
                                        else
                                            bondColor = new Vector4(0.5f, 0.5f, 0.5f, 1f); // Dim

                                        ImGui.TextColored(bondColor, $"{relationship.Label} ({relationship.Score})");

                                        ImGui.TableSetColumnIndex(2);
                                        ImGui.Text(kvp.Value.ToString());

                                        ImGui.TableSetColumnIndex(3);
                                        if (npc.LastSeenTimestamps.TryGetValue(kvp.Key, out long ticks))
                                        {
                                            var lastSeen = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
                                            var elapsed = DateTime.Now - lastSeen;

                                            string timeAgo;
                                            if (elapsed.TotalMinutes < 2)
                                                timeAgo = "Just now";
                                            else if (elapsed.TotalMinutes < 60)
                                                timeAgo = $"{(int)elapsed.TotalMinutes}m ago";
                                            else if (elapsed.TotalHours < 24)
                                                timeAgo = $"{(int)elapsed.TotalHours}h ago";
                                            else
                                                timeAgo = $"{(int)elapsed.TotalDays}d ago";

                                            ImGui.Text(timeAgo);
                                        }
                                        else
                                        {
                                            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "—");
                                        }
                                    }
                                    ImGui.EndTable();
                                }

                                // Show the player's relationship description below
                                string playerName = _plugin?.ObjectTable?.LocalPlayer?.Name?.TextValue ?? "";
                                if (!string.IsNullOrEmpty(playerName) && npc.EncounterCounts.ContainsKey(playerName))
                                {
                                    var playerRel = npc.GetRelationshipWith(playerName);
                                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), playerRel.Description);
                                }
                            }

                            ImGui.Dummy(new Vector2(0, 5));

                            // --- Travel History ---
                            ImGui.TextColored(new Vector4(0.4f, 0.9f, 1f, 1f), "Places Visited");
                            ImGui.Separator();

                            if (npc.VisitedLocations.Count == 0)
                            {
                                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No travel history yet.");
                            }
                            else
                            {
                                ImGui.TextWrapped(string.Join(", ", npc.VisitedLocations));
                            }

                            ImGui.Dummy(new Vector2(0, 10));

                            // --- Actions ---
                            float buttonWidth = (ImGui.GetColumnWidth() - 8) / 2;
                            if (ImGui.Button(Translator.LocalizeUI("Refresh"), new Vector2(buttonWidth, 28)))
                            {
                                _memoryNeedsRefresh = true;
                            }
                            ImGui.SameLine();

                            // Shift must be held to activate the button
                            bool shiftHeld = ImGui.GetIO().KeyShift;
                            if (!shiftHeld)
                            {
                                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.4f);
                                ImGui.Button(Translator.LocalizeUI("Clear All Memory"), new Vector2(buttonWidth, 28));
                                ImGui.PopStyleVar();
                                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                                    ImGui.SetTooltip("Hold Shift to enable this button.");
                            }
                            else
                            {
                                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.1f, 0.1f, 1f));
                                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.7f, 0.15f, 0.15f, 1f));
                                if (ImGui.Button(Translator.LocalizeUI("Clear All Memory"), new Vector2(buttonWidth, 28)))
                                {
                                    _showClearMemoryConfirm = true;
                                    _clearMemoryConfirmText = "";
                                    ImGui.OpenPopup("##ClearMemoryConfirm");
                                }
                                ImGui.PopStyleColor(2);
                            }

                            // --- Confirmation Popup ---
                            var popupCenter = ImGui.GetMainViewport().Size / 2;
                            ImGui.SetNextWindowPos(popupCenter, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
                            ImGui.SetNextWindowSize(new Vector2(400, 0), ImGuiCond.Appearing);
                            if (ImGui.BeginPopupModal("##ClearMemoryConfirm", ref _showClearMemoryConfirm,
                                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
                            {
                                ImGui.Dummy(new Vector2(0, 5));
                                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1f));
                                ImGui.TextWrapped("⚠  WARNING: This will permanently erase ALL memory data for " + npc.NpcName + ".");
                                ImGui.PopStyleColor();
                                ImGui.Dummy(new Vector2(0, 3));
                                ImGui.TextWrapped("This includes:\n• All relationships and encounter history\n• Conversation summaries\n• Visited locations\n• Sentiment and mood data");
                                ImGui.Dummy(new Vector2(0, 3));
                                ImGui.TextColored(new Vector4(1f, 0.85f, 0.4f, 1f), "This action cannot be undone.");
                                ImGui.Dummy(new Vector2(0, 8));

                                ImGui.TextWrapped("Type \"" + npc.NpcName + "\" to confirm:");
                                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                                ImGui.InputTextWithHint("##clearConfirmInput", npc.NpcName, ref _clearMemoryConfirmText, 100);
                                ImGui.Dummy(new Vector2(0, 5));

                                bool nameMatches = string.Equals(_clearMemoryConfirmText.Trim(), npc.NpcName, StringComparison.OrdinalIgnoreCase);
                                float popupWidth = ImGui.GetContentRegionAvail().X;
                                float btnW = (popupWidth - 8) / 2;

                                if (!nameMatches)
                                    ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.35f);
                                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.7f, 0.1f, 0.1f, 1f));
                                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.2f, 0.2f, 1f));
                                if (ImGui.Button("Yes, Erase Everything", new Vector2(btnW, 30)) && nameMatches)
                                {
                                    npc.EncounterCounts.Clear();
                                    npc.LastSeenTimestamps.Clear();
                                    npc.VisitedLocations.Clear();
                                    npc.SentimentModifiers.Clear();
                                    npc.WasLeftBehind = false;
                                    SaveNPCCharacters();

                                    // Also delete the memory files
                                    try
                                    {
                                        string baseDir = _plugin.Configuration.QuestInstallFolder ?? Path.GetTempPath();
                                        string npcMemoryDir = Path.Combine(baseDir, "CustomNpcMemories");
                                        string memPath = Path.Combine(npcMemoryDir, npc.NpcName + "-memories.json");
                                        string convPath = Path.Combine(npcMemoryDir, npc.NpcName + "-memories-conversation.json");
                                        if (File.Exists(memPath)) File.Delete(memPath);
                                        if (File.Exists(convPath)) File.Delete(convPath);
                                    }
                                    catch { }

                                    _memoryNeedsRefresh = true;
                                    _showClearMemoryConfirm = false;
                                    _clearMemoryConfirmText = "";
                                    ImGui.CloseCurrentPopup();
                                }
                                ImGui.PopStyleColor(2);
                                if (!nameMatches)
                                    ImGui.PopStyleVar();

                                ImGui.SameLine();
                                if (ImGui.Button("Cancel", new Vector2(btnW, 30)))
                                {
                                    _showClearMemoryConfirm = false;
                                    _clearMemoryConfirmText = "";
                                    ImGui.CloseCurrentPopup();
                                }

                                ImGui.Dummy(new Vector2(0, 3));
                                ImGui.EndPopup();
                            }

                            ImGui.EndTabItem();
                        }

                        if (ImGui.BeginTabItem(Translator.LocalizeUI("Debug Context")))
                        {
                            var debugNpc = _customNpcCharacters[_currentSelection];
                            string playerName = _plugin?.ObjectTable?.LocalPlayer?.Name?.TextValue ?? "Adventurer";

                            ImGui.TextColored(new Vector4(1f, 0.85f, 0.4f, 1f), "Full AI Context Preview");
                            ImGui.TextWrapped("This is what the AI sees when responding to you.");
                            ImGui.Separator();
                            ImGui.Dummy(new Vector2(0, 5));

                            // Build the full context preview
                            var sb = new System.Text.StringBuilder();

                            // --- EXACT PROMPT PREVIEW ---
                            if (_plugin?.ObjectTable?.LocalPlayer != null
                                && _plugin.AQuestReborn?.CustomNpcConversationManagers != null
                                && _plugin.AQuestReborn.CustomNpcConversationManagers.TryGetValue(debugNpc.NpcName, out var manager))
                            {
                                if (_plugin.AQuestReborn.InteractiveNpcDictionary.TryGetValue(debugNpc.NpcName, out var liveNpc))
                                {
                                    string preview = manager.GetPromptPreview(
                                        _plugin.ObjectTable.LocalPlayer,
                                        liveNpc.Character,
                                        debugNpc.NpcName,
                                        "Hello!",
                                        "(Your message here)",
                                        _plugin.GetEnvironmentContext(liveNpc.Character),
                                        debugNpc.GetFullLore() + debugNpc.GetMoodContext(playerName)
                                    );
                                    sb.AppendLine(preview);
                                }
                                else
                                {
                                    sb.AppendLine("(NPC is not currently spawned. Spawn them to generate an accurate prompt preview.)");
                                }
                            }
                            else
                            {
                                sb.AppendLine("(Conversation Manager not active. Spawn the NPC to initialize memory and generate an accurate prompt preview.)");
                            }

                            string debugText = sb.ToString();
                            if (ImGui.BeginChild("##debugContextScroll", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - 35), true))
                            {
                                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.9f, 0.95f, 1f));
                                ImGui.TextWrapped(debugText);
                                ImGui.PopStyleColor();
                            }
                            ImGui.EndChild();

                            if (ImGui.Button(Translator.LocalizeUI("Copy to Clipboard"), new Vector2(ImGui.GetContentRegionAvail().X, 28)))
                            {
                                ImGui.SetClipboardText(debugText);
                            }

                            ImGui.EndTabItem();
                        }
                        ImGui.EndTabBar();
                    }

                    ImGui.Dummy(new Vector2(0, 15));

                    // Show stay location if NPC is staying in another zone
                    var currentNpc = _customNpcCharacters[_currentSelection];
                    if (currentNpc.IsStaying && currentNpc.StayTerritoryId > 0)
                    {
                        string territoryName = "Unknown";
                        try
                        {
                            var territory = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRow(currentNpc.StayTerritoryId);
                            territoryName = territory.PlaceName.Value.Name.ToString();
                        }
                        catch { }
                        bool isHere = _plugin != null && _plugin.ClientState.TerritoryType == currentNpc.StayTerritoryId;
                        if (isHere)
                        {
                            ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), Translator.LocalizeUI("Standing nearby in") + " " + territoryName);
                        }
                        else
                        {
                            ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), Translator.LocalizeUI("Left at") + " " + territoryName);
                        }
                        ImGui.Dummy(new Vector2(0, 5));
                    }

                    bool isSpawned = _customNpcCharacters[_currentSelection].IsFollowingPlayer
                        || _customNpcCharacters[_currentSelection].IsStaying;
                    string buttonLabel = isSpawned ? Translator.LocalizeUI("Dismiss NPC") : Translator.LocalizeUI("Summon NPC");
                    if (ImGui.Button(buttonLabel, new Vector2(ImGui.GetColumnWidth(), 30)))
                    {
                        if (_plugin != null && _plugin.AQuestReborn != null)
                        {
                            if (!isSpawned)
                            {
                                _plugin.AQuestReborn.SummonCustomNpc(_customNpcCharacters[_currentSelection]);
                                _customNpcCharacters[_currentSelection].IsFollowingPlayer = false;
                                _customNpcCharacters[_currentSelection].IsStaying = true;
                                _customNpcCharacters[_currentSelection].StayTerritoryId = _plugin.ClientState.TerritoryType;
                                var playerPos = _plugin.ObjectTable.LocalPlayer?.Position ?? System.Numerics.Vector3.Zero;
                                var spawnPos = playerPos + new System.Numerics.Vector3(2, 0, 2);
                                _customNpcCharacters[_currentSelection].StayPositionX = spawnPos.X;
                                _customNpcCharacters[_currentSelection].StayPositionY = _plugin.AQuestReborn.GroundMap.GetGroundY(spawnPos.X, spawnPos.Z, playerPos.Y);
                                _customNpcCharacters[_currentSelection].StayPositionZ = spawnPos.Z;
                            }
                            else
                            {
                                _plugin.AQuestReborn.DismissCustomNpc(_customNpcCharacters[_currentSelection].NpcName);
                                _customNpcCharacters[_currentSelection].IsFollowingPlayer = false;
                                _customNpcCharacters[_currentSelection].IsStaying = false;
                                _customNpcCharacters[_currentSelection].StayTerritoryId = 0;
                            }
                            SaveNPCCharacters();
                        }
                    }

                    if (isSpawned)
                    {
                        bool isStaying = _customNpcCharacters[_currentSelection].IsStaying;
                        string followLabel = isStaying ? Translator.LocalizeUI("Follow") : Translator.LocalizeUI("Stay");
                        if (ImGui.Button(followLabel, new Vector2(ImGui.GetColumnWidth(), 30)))
                        {
                            if (_plugin != null && _plugin.AQuestReborn != null)
                            {
                                bool shouldFollow = isStaying; // If it's currently staying, we want it to follow.
                                _customNpcCharacters[_currentSelection].IsStaying = !shouldFollow;
                                _customNpcCharacters[_currentSelection].IsFollowingPlayer = shouldFollow;
                                _plugin.AQuestReborn.ToggleCustomNpcFollow(
                                    _customNpcCharacters[_currentSelection].NpcName,
                                    shouldFollow);
                                SaveNPCCharacters();
                            }
                        }
                    }

                    ImGui.Dummy(new Vector2(0, 15));
                    if (ImGui.CollapsingHeader(Translator.LocalizeUI("Debug: Environmental Context")))
                    {
                        if (_plugin != null && _plugin.AQuestReborn != null)
                        {
                            if (_plugin.AQuestReborn.InteractiveNpcDictionary.TryGetValue(_customNpcCharacters[_currentSelection].NpcName, out var liveNpc))
                            {
                                string context = _plugin.GetEnvironmentContext(liveNpc.Character);
                                ImGui.TextWrapped(context);
                            }
                            else
                            {
                                ImGui.TextColored(new Vector4(1, 0, 0, 1), Translator.LocalizeUI("NPC is not spawned."));
                            }
                        }
                    }
                }
            }
            else
            {
                ImGui.Text(Translator.LocalizeUI("Glamourer plugin was not detected! This is required to make Custom NPCs"));
            }
        }

        public void RefreshNPCItemNames()
        {
            List<string> names = new List<string>();
            foreach (var item in _customNpcCharacters)
            {
                names.Add(item.NpcName);
            }
            if (_currentSelection >= names.Count)
            {
                _currentSelection = 0;
            }
            if (names.Count > 0)
            {
                npcItemNames = names.ToArray();
            }
            else
            {
                npcItemNames = new string[0];
            }
        }

        // === Conversational Provider Settings UI ===
        private static readonly string[] _aiProviderNames = { "Default (Built-in)", "Google Gemini", "OpenAI Compatible (CosmoRP, LM Studio, etc.)", "NovelAI" };
        private static readonly string[] _aiProviderKeys = { "default", "gemini", "openai_compatible", "novelai" };
        private string _testConnectionResult = "";
        private bool _testingConnection = false;

        private void DrawAiProviderSettings()
        {
            if (_plugin?.Configuration == null) return;
            var config = _plugin.Configuration;

            ImGui.Indent(10f);
            ImGui.TextWrapped("Choose which conversational service powers your NPC conversations. The default server is free but uses smaller models. External providers may produce higher quality responses.");
            ImGui.Spacing();

            // Provider dropdown
            int currentIndex = Array.IndexOf(_aiProviderKeys, config.AiProvider);
            if (currentIndex < 0) currentIndex = 0;

            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 10f);
            if (ImGui.Combo("##AiProviderCombo", ref currentIndex, _aiProviderNames, _aiProviderNames.Length))
            {
                config.AiProvider = _aiProviderKeys[currentIndex];
                config.Save();
                _testConnectionResult = "";
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            switch (config.AiProvider)
            {
                case "gemini":
                    DrawGeminiSettings(config);
                    break;
                case "openai_compatible":
                    DrawOpenAiCompatibleSettings(config);
                    break;
                case "novelai":
                    DrawNovelAiSettings(config);
                    break;
                default:
                    ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1f), "Using the built-in conversational server. No configuration needed.");
                    ImGui.TextWrapped("This is a free service using 6B parameter models. For higher quality conversations, try one of the other providers.");
                    break;
            }

            // Test Connection button
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (config.AiProvider != "default")
            {
                if (_testingConnection)
                {
                    ImGui.TextColored(new Vector4(1f, 1f, 0f, 1f), "Testing connection...");
                }
                else
                {
                    if (ImGui.Button("Test Connection", new Vector2(150, 28)))
                    {
                        TestAiConnection();
                    }
                }

                if (!string.IsNullOrEmpty(_testConnectionResult))
                {
                    bool isSuccess = _testConnectionResult.StartsWith("Success");
                    var color = isSuccess ? new Vector4(0.3f, 1f, 0.3f, 1f) : new Vector4(1f, 0.3f, 0.3f, 1f);
                    ImGui.TextColored(color, _testConnectionResult);
                }
            }

            ImGui.Unindent(10f);
        }

        private void DrawOpenAiCompatibleSettings(SamplePlugin.Configuration config)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.85f, 1f, 1f), "OpenAI-Compatible Endpoint");
            ImGui.TextWrapped("Works with CosmoRP, OpenRouter, LM Studio, Oobabooga, Ollama, or any service that implements the OpenAI chat completion API.");
            ImGui.Spacing();

            // Base URL
            ImGui.Text("API Base URL:");
            string url = config.OpenAiCompatibleUrl ?? "";
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 10f);
            if (ImGui.InputText("##OpenAiUrl", ref url, 512))
            {
                config.OpenAiCompatibleUrl = url;
                config.Save();
            }
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "e.g. https://api.pawan.krd/cosmosrp/v1 or http://localhost:1234/v1");

            ImGui.Spacing();

            // API Key
            ImGui.Text("API Key (optional for local servers):");
            string apiKey = config.OpenAiCompatibleApiKey ?? "";
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 10f);
            if (ImGui.InputText("##OpenAiKey", ref apiKey, 256, ImGuiInputTextFlags.Password))
            {
                config.OpenAiCompatibleApiKey = apiKey;
                config.Save();
            }

            ImGui.Spacing();

            // Model Name
            ImGui.Text("Model Name (optional):");
            string modelName = config.OpenAiCompatibleModelName ?? "";
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 10f);
            if (ImGui.InputText("##OpenAiModel", ref modelName, 128))
            {
                config.OpenAiCompatibleModelName = modelName;
                config.Save();
            }
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "e.g. cosmosrp, gpt-4o-mini, local-model");
        }

        private void DrawNovelAiSettings(SamplePlugin.Configuration config)
        {
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.5f, 1f), "NovelAI");
            ImGui.TextWrapped("Uses NovelAI's high-quality text generation models (Kayra, Erato). Requires an active NovelAI subscription.");
            ImGui.Spacing();

            // API Token
            ImGui.Text("Persistent API Token:");
            string token = config.NovelAiApiToken ?? "";
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 10f);
            if (ImGui.InputText("##NovelAiToken", ref token, 512, ImGuiInputTextFlags.Password))
            {
                config.NovelAiApiToken = token;
                config.Save();
            }
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Found in NovelAI Account Settings > Get Persistent API Token");

            ImGui.Spacing();

            // Model selection
            ImGui.Text("Model:");
            string[] novelAiModels = { "kayra-v2", "erato-v1" };
            int modelIdx = Array.IndexOf(novelAiModels, config.NovelAiModel);
            if (modelIdx < 0) modelIdx = 0;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 10f);
            if (ImGui.Combo("##NovelAiModelCombo", ref modelIdx, novelAiModels, novelAiModels.Length))
            {
                config.NovelAiModel = novelAiModels[modelIdx];
                config.Save();
            }
        }

        private void DrawGeminiSettings(SamplePlugin.Configuration config)
        {
            ImGui.TextColored(new Vector4(0.3f, 0.7f, 1f, 1f), "Google Gemini");
            ImGui.TextWrapped("Uses Google's Gemini models for high-quality conversations. Requires a Google AI Studio API key.");
            ImGui.Spacing();

            // API Key
            ImGui.Text("API Key:");
            string apiKey = config.GeminiApiKey ?? "";
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 10f);
            if (ImGui.InputText("##GeminiApiKey", ref apiKey, 256, ImGuiInputTextFlags.Password))
            {
                config.GeminiApiKey = apiKey;
                config.Save();
            }
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Get your key at aistudio.google.com/apikey");

            ImGui.Spacing();

            // Model selection
            ImGui.Text("Model:");
            string[] geminiModels = { "gemini-2.0-flash", "gemini-2.5-flash-preview-05-20", "gemini-2.5-pro-preview-05-06" };
            string[] geminiModelLabels = { "Gemini 2.0 Flash (Fast, Free Tier)", "Gemini 2.5 Flash (Balanced)", "Gemini 2.5 Pro (Highest Quality)" };
            int modelIdx = Array.IndexOf(geminiModels, config.GeminiModel);
            if (modelIdx < 0) modelIdx = 0;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 10f);
            if (ImGui.Combo("##GeminiModelCombo", ref modelIdx, geminiModelLabels, geminiModelLabels.Length))
            {
                config.GeminiModel = geminiModels[modelIdx];
                config.Save();
            }
        }

        private async void TestAiConnection()
        {
            _testingConnection = true;
            _testConnectionResult = "";

            try
            {
                var provider = GPTApi.AiProviderFactory.CreateProvider();
                // Avoid using "\n" as a stop sequence for tests because many local LLMs start responses with a newline
                var stopSequences = new List<string>(); 
                string testPrompt = "You are a friendly NPC. Say hello in one sentence.\nNPC: ";

                string result;
                if (provider.UsesChatFormat)
                {
                    var messages = new List<GPTApi.AiChatMessage>
                    {
                        new GPTApi.AiChatMessage("system", "You are a friendly NPC in Final Fantasy XIV. Respond with a single short greeting. Do NOT use newlines."),
                        new GPTApi.AiChatMessage("user", "Hello!")
                    };
                    result = await provider.GenerateResponseAsync(testPrompt, "TestNPC", "Player", stopSequences, null, messages);
                }
                else
                {
                    result = await provider.GenerateResponseAsync(testPrompt, "TestNPC", "Player", stopSequences);
                }

                if (!string.IsNullOrEmpty(result) && !string.IsNullOrWhiteSpace(result))
                {
                    string preview = result.Length > 80 ? result.Substring(0, 80).Replace("\n", " ").Trim() + "..." : result.Replace("\n", " ").Trim();
                    _testConnectionResult = "Success! Response: " + preview;
                }
                else
                {
                    _testConnectionResult = "Failed: Empty response received. The model may have generated nothing or timed out.";
                }
            }
            catch (Exception e)
            {
                _testConnectionResult = "Failed: " + e.Message;
            }
            finally
            {
                _testingConnection = false;
            }
        }
        private void LoadMemoryFilesForNpc(string npcName)
        {
            _cachedKeywordMemories.Clear();
            _cachedConversationSummaries.Clear();

            if (_plugin == null || string.IsNullOrEmpty(npcName)) return;

            try
            {
                string baseDir = _plugin.Configuration.QuestInstallFolder ?? Path.GetTempPath();
                string npcMemoryDir = Path.Combine(baseDir, "CustomNpcMemories");

                // Keyword memories
                string memPath = Path.Combine(npcMemoryDir, npcName + "-memories.json");
                if (File.Exists(memPath))
                {
                    string json = File.ReadAllText(memPath);
                    var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (parsed != null)
                        _cachedKeywordMemories = parsed
                            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                }

                // Conversation summaries
                string convPath = Path.Combine(npcMemoryDir, npcName + "-memories-conversation.json");
                if (File.Exists(convPath))
                {
                    string json = File.ReadAllText(convPath);
                    var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);
                    if (parsed != null)
                        _cachedConversationSummaries = parsed
                            .ToDictionary(
                                kvp => kvp.Key,
                                kvp => kvp.Value.Where(s => !string.IsNullOrWhiteSpace(s)).ToList())
                            .Where(kvp => kvp.Value.Count > 0)
                            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                }
            }
            catch (Exception e)
            {
                _plugin?.PluginLog?.Warning(e, "Failed to load NPC memory files for display.");
            }
        }
    }
}

