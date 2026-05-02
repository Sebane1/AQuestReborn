using Dalamud.Game.ClientState.Objects.Types;
using SamplePlugin;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AQuestReborn.CustomNpc
{
    public class SpeechBubbleManager : IDisposable
    {
        private readonly Plugin _plugin;
        private readonly Random _random = new Random();
        private Stopwatch _ambientTimer = new Stopwatch();
        private int _nextAmbientIntervalMs;
        private bool _ambientEnabled = true;
        private ConcurrentDictionary<string, string> _lastAmbientMessages = new ConcurrentDictionary<string, string>();
        private bool _isProcessingAmbient = false;

        public SpeechBubbleManager(Plugin plugin)
        {
            _plugin = plugin;
            _nextAmbientIntervalMs = 30000; // 30 seconds (testing)
            _ambientTimer.Start();
        }

        /// <summary>
        /// Active speech bubbles to render via ImGui overlay.
        /// </summary>
        public class ActiveBubble
        {
            public ICharacter Character;
            public string Text;
            public Stopwatch Timer = new Stopwatch();
            public int DurationMs = 8000;
        }

        private ConcurrentDictionary<string, ActiveBubble> _activeBubbles = new ConcurrentDictionary<string, ActiveBubble>();
        public IReadOnlyDictionary<string, ActiveBubble> ActiveBubbles => _activeBubbles;

        /// <summary>
        /// Shows a speech bubble above a character's head via ImGui overlay.
        /// </summary>
        public void ShowBubble(ICharacter character, string npcName, string text)
        {
            var bubble = new ActiveBubble
            {
                Character = character,
                Text = text,
            };
            bubble.Timer.Start();
            _activeBubbles[npcName] = bubble;

            // Log to FFXIV chat
            try
            {
                _plugin.ChatGui.Print(new Dalamud.Game.Text.XivChatEntry
                {
                    Message = $"{npcName}: {text}",
                    Type = Dalamud.Game.Text.XivChatType.NPCDialogue,
                });
            }
            catch { }
        }

        /// <summary>
        /// Expire old bubbles. Call from framework update.
        /// </summary>
        public void CleanupBubbles()
        {
            foreach (var kvp in _activeBubbles)
            {
                if (kvp.Value.Timer.ElapsedMilliseconds > kvp.Value.DurationMs)
                {
                    _activeBubbles.TryRemove(kvp.Key, out _);
                }
            }
        }

        /// <summary>
        /// Called from Framework.Update to check if it's time for ambient NPC chatter.
        /// </summary>
        private Stopwatch _debugLogTimer = new Stopwatch();
        public void Update()
        {
            // Periodic diagnostic (every 5s) to see what's blocking
            if (!_debugLogTimer.IsRunning) _debugLogTimer.Start();
            if (_debugLogTimer.ElapsedMilliseconds > 5000)
            {
                _debugLogTimer.Restart();
                var aq = _plugin.AQuestReborn;
                int npcCount = aq?.CustomNpcCharacters?.Count ?? -1;
                int convCount = aq?.CustomNpcConversationManagers?.Count ?? -1;
                bool chatActive = _plugin.NpcChatWindow?.IsConversationActive ?? false;
                _plugin.PluginLog.Information($"[SpeechBubble] DEBUG: enabled={_ambientEnabled}, processing={_isProcessingAmbient}, npcs={npcCount}, convMgrs={convCount}, chatActive={chatActive}, timer={_ambientTimer.ElapsedMilliseconds}/{_nextAmbientIntervalMs}");
            }

            if (!_ambientEnabled || _isProcessingAmbient)
            {
                CleanupBubbles();
                return;
            }

            CleanupBubbles();

            if (_plugin.AQuestReborn == null) return;
            var customNpcs = _plugin.AQuestReborn.CustomNpcCharacters;
            var conversationManagers = _plugin.AQuestReborn.CustomNpcConversationManagers;

            if (customNpcs == null || customNpcs.Count == 0) return;

            // Don't trigger ambient chat while player is in a conversation
            if (_plugin.NpcChatWindow != null && _plugin.NpcChatWindow.IsConversationActive) return;

            if (_ambientTimer.ElapsedMilliseconds >= _nextAmbientIntervalMs)
            {
                _plugin.PluginLog.Information($"[SpeechBubble] Timer fired! NPCs={customNpcs.Count}, ConvMgrs={conversationManagers?.Count ?? 0}");
                _ambientTimer.Restart();
                _nextAmbientIntervalMs = 30000; // 30 seconds (testing)
                _isProcessingAmbient = true;

                Task.Run(async () =>
                {
                    try
                    {
                        var npcNames = customNpcs.Keys.ToList();
                        if (npcNames.Count == 0) return;

                        _plugin.PluginLog.Information($"[SpeechBubble] Picking from {npcNames.Count} NPCs: {string.Join(", ", npcNames)}");

                        // If multiple NPCs, 50% chance of NPC-to-NPC conversation
                        if (npcNames.Count >= 2 && _random.Next(2) == 0)
                        {
                            await TriggerNpcToNpcChat(npcNames, customNpcs, conversationManagers);
                        }
                        else
                        {
                            // Solo ambient thought
                            string npcName = npcNames[_random.Next(npcNames.Count)];
                            _plugin.PluginLog.Information($"[SpeechBubble] Solo ambient for: {npcName}");
                            await TriggerSoloAmbient(npcName, customNpcs, conversationManagers);
                        }
                    }
                    catch (Exception e)
                    {
                        _plugin.PluginLog.Warning(e, "Ambient chat error");
                    }
                    finally
                    {
                        _isProcessingAmbient = false;
                    }
                });
            }
        }

        private async Task TriggerSoloAmbient(string npcName,
            Dictionary<string, ICharacter> customNpcs,
            Dictionary<string, NPCConversationManager> conversationManagers)
        {
            if (!customNpcs.ContainsKey(npcName) || !conversationManagers.ContainsKey(npcName))
            {
                _plugin.PluginLog.Information($"[SpeechBubble] NPC '{npcName}' not in dictionaries. customNpcs={customNpcs.ContainsKey(npcName)}, convMgrs={conversationManagers.ContainsKey(npcName)}");
                return;
            }

            var npcChar = customNpcs[npcName];
            var convManager = conversationManagers[npcName];
            var sender = _plugin.ObjectTable.LocalPlayer;
            if (sender == null || npcChar == null)
            {
                _plugin.PluginLog.Information($"[SpeechBubble] sender or npcChar null");
                return;
            }

            // Find NPC data
            CustomNpcCharacter npcData = null;
            foreach (var npc in _plugin.Configuration.CustomNpcCharacters)
            {
                if (npc.NpcName == npcName)
                {
                    npcData = npc;
                    break;
                }
            }
            if (npcData == null)
            {
                _plugin.PluginLog.Information($"[SpeechBubble] npcData not found in config for '{npcName}'");
                return;
            }

            _plugin.PluginLog.Information($"[SpeechBubble] Sending ambient message for '{npcName}'...");

            string response = await convManager.SendMessage(
                sender, npcChar,
                npcData.NpcName,
                npcData.NPCGreeting,
                "*is standing nearby, looking around idly*",
                _plugin.GetEnvironmentContext(),
                npcData.NpcPersonality);

            _plugin.PluginLog.Information($"[SpeechBubble] Got response: '{response?.Substring(0, Math.Min(response?.Length ?? 0, 80))}'");

            if (!string.IsNullOrEmpty(response))
            {
                string clean = CleanBubbleText(response);
                if (clean.Length > 120) clean = clean.Substring(0, 117) + "...";

                _lastAmbientMessages[npcName] = clean;

                _plugin.Framework.RunOnFrameworkThread(() =>
                {
                    _plugin.PluginLog.Information($"[SpeechBubble] Showing bubble: '{clean}'");
                    ShowBubble(npcChar, npcName, clean);
                });
            }
        }

        private async Task TriggerNpcToNpcChat(List<string> npcNames,
            Dictionary<string, ICharacter> customNpcs,
            Dictionary<string, NPCConversationManager> conversationManagers)
        {
            // Pick two random NPCs
            var shuffled = npcNames.OrderBy(_ => _random.Next()).Take(2).ToList();
            string npcA = shuffled[0];
            string npcB = shuffled[1];

            if (!customNpcs.ContainsKey(npcA) || !customNpcs.ContainsKey(npcB)) return;
            if (!conversationManagers.ContainsKey(npcA) || !conversationManagers.ContainsKey(npcB)) return;

            var charA = customNpcs[npcA];
            var charB = customNpcs[npcB];
            var sender = _plugin.ObjectTable.LocalPlayer;
            if (sender == null || charA == null || charB == null) return;

            CustomNpcCharacter dataA = null, dataB = null;
            foreach (var npc in _plugin.Configuration.CustomNpcCharacters)
            {
                if (npc.NpcName == npcA) dataA = npc;
                if (npc.NpcName == npcB) dataB = npc;
            }
            if (dataA == null || dataB == null) return;

            // NPC A says something to NPC B
            string responseA = await conversationManagers[npcA].SendMessage(
                sender, charA,
                dataA.NpcName,
                dataA.NPCGreeting,
                $"*notices {npcB} nearby and makes casual conversation*",
                _plugin.GetEnvironmentContext(),
                dataA.NpcPersonality);

            if (!string.IsNullOrEmpty(responseA))
            {
                string cleanA = CleanBubbleText(responseA);
                if (cleanA.Length > 120) cleanA = cleanA.Substring(0, 117) + "...";
                _lastAmbientMessages[npcA] = cleanA;

                _plugin.Framework.RunOnFrameworkThread(() =>
                {
                    ShowBubble(charA, npcA, cleanA);
                });

                // Wait for bubble to be read, then NPC B responds
                await Task.Delay(4000);

                string responseB = await conversationManagers[npcB].SendMessage(
                    sender, charB,
                    dataB.NpcName,
                    dataB.NPCGreeting,
                    $"*{npcA} just said: \"{cleanA}\". Respond naturally.*",
                    _plugin.GetEnvironmentContext(),
                    dataB.NpcPersonality);

                if (!string.IsNullOrEmpty(responseB))
                {
                    string cleanB = CleanBubbleText(responseB);
                    if (cleanB.Length > 120) cleanB = cleanB.Substring(0, 117) + "...";
                    _lastAmbientMessages[npcB] = cleanB;

                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        ShowBubble(charB, npcB, cleanB);
                    });
                }
            }
        }

        private string CleanBubbleText(string text)
        {
            // Strip formatting artifacts from GPT pipeline
            foreach (var prefix in new[] { "says, ", "asks, ", "exclaims, " })
            {
                if (text.StartsWith(prefix))
                {
                    text = text.Substring(prefix.Length);
                    break;
                }
            }

            // Strip asterisk actions for the UI display
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*[^*]+\*", "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                text = "...";
            }

            if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length > 2)
                text = text.Substring(1, text.Length - 2);
            text = text.TrimEnd('"').Trim();
            return text;
        }

        /// <summary>
        /// Gets the last ambient message for a given NPC (for context carryover).
        /// Returns null if no recent ambient message exists.
        /// </summary>
        public string GetLastAmbientMessage(string npcName)
        {
            return _lastAmbientMessages.TryGetValue(npcName, out var msg) ? msg : null;
        }

        public void Dispose()
        {
            _ambientEnabled = false;
            _ambientTimer.Stop();
            _lastAmbientMessages.Clear();
        }
    }
}
