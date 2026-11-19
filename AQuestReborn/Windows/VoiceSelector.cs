using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using RoleplayingQuestCore;
using RoleplayingVoiceDalamudWrapper;
using SamplePlugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace AQuestReborn.Windows
{
    public class VoiceSelector
    {
        // Fish Audio voice search & model ID
        private string _fishVoiceSearchText = "";
        private List<string> _fishSearchResults = new List<string>();
        private bool _isSearchingFishVoices = false;
        private string _fishCustomModelId = "";
        private bool _isPlayingVoicePreview = false;
        private string _id = "";
        public VoiceSelector()
        {
            _id = Guid.NewGuid().ToString();
        }

        public bool Draw(ref string value)
        {
            bool triggerSave = false;

            ImGui.SetNextItemWidth(ImGui.GetColumnWidth() - 38);
            int currentVoice = Plugin.Instance.VoiceList.IndexOf<string>(value);
            if (ImGui.Combo("##npcVoiceCombo" + _id, ref currentVoice, Plugin.Instance.VoiceList, 10))
            {
                value = Plugin.Instance.VoiceList[currentVoice];
                triggerSave = true;
            }
            ImGui.SameLine();
            {
                bool isPlayingPreview = _isPlayingVoicePreview;
                if (isPlayingPreview)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.85f, 0.2f, 0.2f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.95f, 0.3f, 0.3f, 1f));
                }
                ImGui.PushFont(UiBuilder.IconFont);
                string icon = isPlayingPreview
                    ? FontAwesomeIcon.Stop.ToIconString()
                    : FontAwesomeIcon.VolumeUp.ToIconString();
                if (ImGui.Button(icon + "##previewCurrentVoice" + _id, new Vector2(30, 0)))
                {
                    if (!_isPlayingVoicePreview)
                    {
                        PreviewVoice(value);
                    }
                }
                ImGui.PopFont();
                if (isPlayingPreview)
                {
                    ImGui.PopStyleColor(2);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(isPlayingPreview ? "Previewing audio..." : "Listen to sample of current voice");
                }
            }

            // Additional voice tools for Fish Audio
            if (Plugin.Instance.Configuration.AccessibilityTextProvider == 0)
            {
                // Search Fish Audio Community Voices
                ImGui.SetNextItemWidth(ImGui.GetColumnWidth() - 75);
                bool enterPressed = ImGui.InputTextWithHint("##fishVoiceSearch" + _id, Translator.LocalizeUI("Search Fish Audio online..."), ref _fishVoiceSearchText, 100, ImGuiInputTextFlags.EnterReturnsTrue);
                ImGui.SameLine();
                if (ImGui.Button(Translator.LocalizeUI("Search") + "##fishSearchBtn" + _id, new Vector2(70, 0)) || enterPressed)
                {
                    SearchFishAudioVoices(_fishVoiceSearchText);
                }

                if (_isSearchingFishVoices)
                {
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Searching online voices...");
                }
                else if (_fishSearchResults.Count > 0)
                {
                    if (ImGui.BeginChild("##fishSearchResults" + _id, new Vector2(ImGui.GetColumnWidth(), 120), true))
                    {
                        for (int i = 0; i < _fishSearchResults.Count; i++)
                        {
                            string vName = _fishSearchResults[i];
                            bool isSelected = vName == value;

                            // Small preview button next to each search result
                            ImGui.PushFont(UiBuilder.IconFont);
                            if (ImGui.SmallButton(FontAwesomeIcon.VolumeUp.ToIconString() + "##prevRes_" + _id + i))
                            {
                                PreviewVoice(vName);
                            }
                            ImGui.PopFont();
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Preview this voice");

                            ImGui.SameLine();
                            if (ImGui.Selectable(vName + "##fishResult_" + _id + i, isSelected))
                            {
                                value = vName;
                                // Ensure the selected voice appears in Plugin.VoiceList so the combo box also reflects it
                                if (!Plugin.Instance.VoiceList.Contains(vName))
                                {
                                    var list = Plugin.Instance.VoiceList.ToList();
                                    list.Add(vName);
                                    Plugin.Instance.VoiceList = list.ToArray();
                                }
                                triggerSave = true;
                            }
                        }
                    }
                    ImGui.EndChild();
                }

                // Custom Fish Audio Model ID direct entry
                string customIdPlaceholder = "Custom Fish Model ID (from website)";
                ImGui.SetNextItemWidth(ImGui.GetColumnWidth() - 65);
                ImGui.InputTextWithHint("##fishCustomModelId" + _id, customIdPlaceholder, ref _fishCustomModelId, 64);
                ImGui.SameLine();
                if (ImGui.Button(Translator.LocalizeUI("Set") + "##setCustomModelId" + _id, new Vector2(60, 0)))
                {
                    if (!string.IsNullOrWhiteSpace(_fishCustomModelId))
                    {
                        string trimmedId = _fishCustomModelId.Trim();
                        value = trimmedId;
                        if (!Plugin.Instance.VoiceList.Contains(trimmedId))
                        {
                            var list = Plugin.Instance.VoiceList.ToList();
                            list.Add(trimmedId);
                            Plugin.Instance.VoiceList = list.ToArray();
                        }
                        triggerSave = true;
                        _fishCustomModelId = "";
                    }
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Paste a Model ID from fish.audio to use any voice on their site directly.");
                }
            }
            return triggerSave;
        }
        private void SearchFishAudioVoices(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            string apiKey = Plugin.Instance.Configuration?.FishAudioApiKey;
            if (string.IsNullOrWhiteSpace(apiKey)) return;

            _isSearchingFishVoices = true;
            _fishSearchResults.Clear();

            Task.Run(async () =>
            {
                try
                {
                    using (var client = new System.Net.Http.HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(15);
                        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                        string url = $"https://api.fish.audio/model?title={Uri.EscapeDataString(query.Trim())}&page_size=50";
                        var response = await client.GetAsync(url);
                        if (response.IsSuccessStatusCode)
                        {
                            string jsonStr = await response.Content.ReadAsStringAsync();
                            dynamic json = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);
                            if (json != null && json.items != null)
                            {
                                var results = new List<string>();
                                foreach (var item in json.items)
                                {
                                    string title = (string)item.title;
                                    string id = (string)item._id;
                                    if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(id))
                                    {
                                        results.Add(title);
                                        // Also register to the media manager voice map so it can generate audio
                                        if (Plugin.Instance.RoleplayingMediaManagerInstance?.FishAudioVoiceMap != null)
                                        {
                                            Plugin.Instance.RoleplayingMediaManagerInstance.FishAudioVoiceMap[title] = id;
                                        }
                                    }
                                }
                                _fishSearchResults = results;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Instance.PluginLog?.Warning(ex, "Failed searching Fish Audio models.");
                }
                finally
                {
                    _isSearchingFishVoices = false;
                }
            });
        }
        private void PreviewVoice(string voiceNameOrId)
        {
            if (string.IsNullOrWhiteSpace(voiceNameOrId) || voiceNameOrId == "None") return;
            if (Plugin.Instance.RoleplayingMediaManagerInstance == null || Plugin.Instance.MediaManager == null) return;

            _isPlayingVoicePreview = true;

            Task.Run(async () =>
            {
                try
                {
                    string sampleText = "Greetings, adventurer. It is a pleasure to make your acquaintance.";
                    string audioPath = "";

                    switch (Plugin.Instance.Configuration.AccessibilityTextProvider)
                    {
                        case 0:
                            audioPath = await Plugin.Instance.RoleplayingMediaManagerInstance.DoVoiceFishAudioWithSpecificVoice(
                                voiceNameOrId, sampleText, false, 1f, System.Numerics.Vector3.Zero, false);
                            break;
                        case 1:
                            audioPath = await Plugin.Instance.RoleplayingMediaManagerInstance.DoVoiceMicrosoftNarratorWithSpecificVoice(
                                voiceNameOrId, sampleText, false, 1f, System.Numerics.Vector3.Zero, false);
                            break;
                        case 2:
                            audioPath = await Plugin.Instance.RoleplayingMediaManagerInstance.DoCTTSWithSpecificVoice(
                                voiceNameOrId, sampleText, false, 1f, System.Numerics.Vector3.Zero, false);
                            break;
                    }

                    if (!string.IsNullOrEmpty(audioPath) && System.IO.File.Exists(audioPath))
                    {
                        Plugin.Instance.MediaManager.PlayMedia(new DummyObject(), audioPath, RoleplayingMediaCore.SoundType.NPC, false);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Instance?.PluginLog?.Warning(ex, "Failed to preview voice.");
                }
                finally
                {
                    _isPlayingVoicePreview = false;
                }
            });
        }
    }
}
