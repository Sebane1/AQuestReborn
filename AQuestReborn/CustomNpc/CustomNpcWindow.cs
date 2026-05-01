using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Bindings.ImGui;
using PenumbraAndGlamourerHelpers;
using SamplePlugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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

        public Plugin Plugin { get => _plugin; set => _plugin = value; }
        public List<CustomNpcCharacter> CustomNpcCharacters { get => _customNpcCharacters; set => _customNpcCharacters = value; }

        public CustomNpcWindow(IDalamudPluginInterface pluginInterface) :
            base("Custom NPC Configuration")
        {
            _pluginInterface = pluginInterface;
            _customNpcCharacters.Add(new CustomNpcCharacter());
            Size = new Vector2(550, 500);
        }
        public override void OnOpen()
        {
            base.OnOpen();
            RefreshDesignList();
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

        public override void Draw()
        {
            try
            {
                if (_currentGlamourerDesigns.Count is 0)
                {
                    RefreshDesignList();
                }
                RefreshNPCItemNames();
                ImGui.BeginTable("##CustomNpcTable", 2);
                ImGui.TableSetupColumn("Custom NPC", ImGuiTableColumnFlags.WidthFixed, 200);
                ImGui.TableSetupColumn("Custom NPC Configuration", ImGuiTableColumnFlags.WidthStretch, 300);
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
                _customNpcCharacters.Add(new CustomNpcCharacter());
                SaveNPCCharacters();
            }

            ImGui.SameLine();
            if (ImGui.Button("-", new Vector2(35)))
            {
                if (_customNpcCharacters.Count > 0)
                {
                    // Dismiss the NPC if it's currently spawned
                    if (_plugin != null && _plugin.AQuestReborn != null)
                    {
                        _plugin.AQuestReborn.DismissCustomNpc(_customNpcCharacters[_currentSelection].NpcName);
                    }
                    _customNpcCharacters.RemoveAt(_currentSelection);
                    _currentSelection = 0;
                    SaveNPCCharacters();
                }
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

                    ImGui.LabelText("##personalityLabel", "NPC Name");
                    ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                    ImGui.InputText("##NPCName", ref _customNpcCharacters[_currentSelection].NpcName, 255);

                    ImGui.LabelText("##glamourerLabel", "Glamourer Design Appearance");
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
                                    break;
                                }
                            }
                        }
                    }

                    ImGui.LabelText("##greetingLabel", "NPC Greeting");
                    ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                    ImGui.InputText("##Greeting", ref _customNpcCharacters[_currentSelection].NPCGreeting, 500);

                    ImGui.LabelText("##personalityFieldLabel", "NPC Personality");
                    ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                    ImGui.InputTextMultiline("##NpcPersonality", ref _customNpcCharacters[_currentSelection].NpcPersonality, 2000, new Vector2(ImGui.GetColumnWidth(), 100));

                    ImGui.Dummy(new Vector2(0, 10));

                    bool isSpawned = _customNpcCharacters[_currentSelection].IsFollowingPlayer;
                    string buttonLabel = isSpawned ? "Dismiss NPC" : "Summon NPC";
                    if (ImGui.Button(buttonLabel, new Vector2(ImGui.GetColumnWidth(), 30)))
                    {
                        if (_plugin != null && _plugin.AQuestReborn != null)
                        {
                            if (!isSpawned)
                            {
                                _plugin.AQuestReborn.SummonCustomNpc(_customNpcCharacters[_currentSelection]);
                                _customNpcCharacters[_currentSelection].IsFollowingPlayer = true;
                            }
                            else
                            {
                                _plugin.AQuestReborn.DismissCustomNpc(_customNpcCharacters[_currentSelection].NpcName);
                                _customNpcCharacters[_currentSelection].IsFollowingPlayer = false;
                            }
                        }
                    }

                    ImGui.Dummy(new Vector2(0, 5));

                    if (ImGui.Button("Save", new Vector2(ImGui.GetColumnWidth(), 25)))
                    {
                        SaveNPCCharacters();
                    }
                }
            }
            else
            {
                ImGui.Text("Glamourer plugin was not detected! This is required to make Custom NPCs");
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
    }
}
