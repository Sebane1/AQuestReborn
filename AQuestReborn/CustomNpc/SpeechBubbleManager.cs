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
        private bool _ambientEnabled => _plugin.Configuration.EnableAmbientChatter;
        private ConcurrentDictionary<string, string> _lastAmbientMessages = new ConcurrentDictionary<string, string>();
        private ConcurrentDictionary<string, DateTime> _lastNpcGreetings = new ConcurrentDictionary<string, DateTime>();
        private bool _isProcessingAmbient = false;

        // Track when NPCs were summoned to enable the "chatty first minute" phase
        private ConcurrentDictionary<string, Stopwatch> _npcSummonTimers = new ConcurrentDictionary<string, Stopwatch>();
        private const int EARLY_INTERVAL_MIN_MS = 15000;  // 15 seconds
        private const int EARLY_INTERVAL_MAX_MS = 30000;  // 30 seconds
        private const int EARLY_PHASE_DURATION_MS = 60000; // first 60 seconds
        private const int NORMAL_INTERVAL_MS = 300000;     // 5 minutes

        public SpeechBubbleManager(Plugin plugin)
        {
            _plugin = plugin;
            _nextAmbientIntervalMs = EARLY_INTERVAL_MIN_MS; // Start chatty
            _ambientTimer.Start();
        }

        /// <summary>
        /// Call when an NPC is summoned/spawned to start their "chatty" phase.
        /// </summary>
        public void NotifyNpcSummoned(string npcName)
        {
            var timer = new Stopwatch();
            timer.Start();
            _npcSummonTimers[npcName] = timer;

            // If we're currently on the long 5-min timer, shorten it so the new NPC talks soon
            if (_nextAmbientIntervalMs > EARLY_INTERVAL_MAX_MS)
            {
                _nextAmbientIntervalMs = _random.Next(EARLY_INTERVAL_MIN_MS, EARLY_INTERVAL_MAX_MS);
                _ambientTimer.Restart();
            }
        }

        /// <summary>
        /// Call when an NPC is dismissed/removed.
        /// </summary>
        public void NotifyNpcDismissed(string npcName)
        {
            _npcSummonTimers.TryRemove(npcName, out _);
            _lastAmbientMessages.TryRemove(npcName, out _);
        }

        /// <summary>
        /// Returns true if any NPC is still in its "chatty" early phase (first minute).
        /// </summary>
        private bool IsAnyNpcInEarlyPhase()
        {
            foreach (var kvp in _npcSummonTimers)
            {
                if (kvp.Value.ElapsedMilliseconds < EARLY_PHASE_DURATION_MS)
                    return true;
            }
            return false;
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
                    Name = npcName,
                    Message = text,
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

                // Use shorter intervals while any NPC is in its chatty first-minute phase
                if (IsAnyNpcInEarlyPhase())
                    _nextAmbientIntervalMs = _random.Next(EARLY_INTERVAL_MIN_MS, EARLY_INTERVAL_MAX_MS);
                else
                    _nextAmbientIntervalMs = NORMAL_INTERVAL_MS;
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
            if (!customNpcs.TryGetValue(npcName, out var npcChar) || !conversationManagers.TryGetValue(npcName, out var convManager))
            {
                _plugin.PluginLog.Information($"[SpeechBubble] NPC '{npcName}' not in dictionaries.");
                return;
            }

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
                "*is standing nearby, looking around idly* (Make a short, passing observation about your current environment. Keep it brief, 1-2 sentences max!)",
                _plugin.GetEnvironmentContext(npcChar),
                npcData.GetFullLore());

            _plugin.PluginLog.Information($"[SpeechBubble] Got response: '{response?.Substring(0, Math.Min(response?.Length ?? 0, 80))}'");

            if (!string.IsNullOrEmpty(response))
            {
                string clean = CleanBubbleText(response);
                if (clean.Length > 450) clean = clean.Substring(0, 447) + "...";

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

            if (!customNpcs.TryGetValue(npcA, out var charA) || !customNpcs.TryGetValue(npcB, out var charB)) return;
            if (!conversationManagers.TryGetValue(npcA, out var convManagerA) || !conversationManagers.TryGetValue(npcB, out var convManagerB)) return;

            var sender = _plugin.ObjectTable.LocalPlayer;
            if (sender == null || charA == null || charB == null) return;

            CustomNpcCharacter dataA = null, dataB = null;
            foreach (var npc in _plugin.Configuration.CustomNpcCharacters)
            {
                if (npc.NpcName == npcA) dataA = npc;
                if (npc.NpcName == npcB) dataB = npc;
            }
            if (dataA == null || dataB == null) return;

            string greetingKey = string.Compare(npcA, npcB) < 0 ? $"{npcA}_{npcB}" : $"{npcB}_{npcA}";
            bool shouldGreet = true;
            if (_lastNpcGreetings.TryGetValue(greetingKey, out DateTime lastGreetingTime))
            {
                if ((DateTime.Now - lastGreetingTime).TotalHours < 2)
                {
                    shouldGreet = false;
                }
            }

            string promptA = shouldGreet 
                ? $"You notice {npcB} nearby. Greet them and make casual conversation. (Keep your response brief, 1-2 sentences max!)"
                : $"You are traveling with {npcB}. Comment on your current environment or suggest something to do together here. DO NOT say hello or introduce yourself again. (Keep your response brief, 1-2 sentences max!)";
            
            if (shouldGreet)
            {
                _lastNpcGreetings[greetingKey] = DateTime.Now;
            }

            // NPC A says something to NPC B
            string responseA = await convManagerA.SendMessage(
                charB, charA,
                dataA.NpcName,
                dataA.NPCGreeting,
                promptA,
                _plugin.GetEnvironmentContext(charA),
                dataA.GetFullLore(),
                senderNameOverride: npcB);

            if (!string.IsNullOrEmpty(responseA))
            {
                string cleanA = CleanBubbleText(responseA);
                if (cleanA.Length > 450) cleanA = cleanA.Substring(0, 447) + "...";
                _lastAmbientMessages[npcA] = cleanA;

                _plugin.Framework.RunOnFrameworkThread(() =>
                {
                    ShowBubble(charA, npcA, cleanA);
                });

                // Wait for bubble to be read, then NPC B responds
                DateTime delayStart = DateTime.Now;
                await Task.Delay(4000);

                // If player talked via .npcchat during this 4-second pause, cancel NPC B's response so they don't get talked over.
                if (_plugin.AQuestReborn != null && _plugin.AQuestReborn.LastNpcChatTime > delayStart)
                {
                    _plugin.PluginLog.Information("[SpeechBubble] Player interrupted ambient chat with /npcchat. Canceling NPC B response.");
                    return;
                }

                string responseB = await convManagerB.SendMessage(
                    charA, charB,
                    dataB.NpcName,
                    dataB.NPCGreeting,
                    cleanA + " (Respond to them. Keep your response brief, 1-2 sentences max!)",
                    _plugin.GetEnvironmentContext(charB),
                    dataB.GetFullLore(),
                    senderNameOverride: npcA);

                if (!string.IsNullOrEmpty(responseB))
                {
                    string cleanB = CleanBubbleText(responseB);
                    if (cleanB.Length > 450) cleanB = cleanB.Substring(0, 447) + "...";
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

            int quoteCount = text.Split(new[] { '"', '“', '”' }).Length - 1;
            if (quoteCount % 2 != 0)
            {
                text += "\"";
            }

            var quoteMatches = System.Text.RegularExpressions.Regex.Matches(text, "[\"“]([^\"”]+)[\"”]");
            if (quoteMatches.Count > 0)
            {
                string dialogueOnly = "";
                foreach (System.Text.RegularExpressions.Match m in quoteMatches)
                {
                    dialogueOnly += m.Groups[1].Value.Trim() + " ";
                }
                text = dialogueOnly.Trim();
            }
            else
            {
                // Strip asterisk actions for the UI display
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\*[^*]+\*", "").Trim();
                // Strip bracketed meta-text
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\[[^\]]+\]", "").Trim();
                if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length > 2)
                    text = text.Substring(1, text.Length - 2);
                text = text.TrimEnd('"').Trim();
            }
            if (string.IsNullOrWhiteSpace(text)) text = "...";
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

        private bool _deathSpeechTriggered;

        /// <summary>
        /// Triggers an AI-generated grief speech bubble from a random active NPC when the player dies.
        /// </summary>
        public void NotifyPlayerDeath()
        {
            if (_deathSpeechTriggered || _isProcessingAmbient) return;
            _deathSpeechTriggered = true;

            var aq = _plugin.AQuestReborn;
            if (aq == null) return;
            var customNpcs = aq.CustomNpcCharacters;
            var conversationManagers = aq.CustomNpcConversationManagers;
            if (customNpcs == null || customNpcs.Count == 0) return;

            var npcNames = customNpcs.Keys.ToList();
            if (npcNames.Count == 0) return;

            _isProcessingAmbient = true;
            Task.Run(async () =>
            {
                try
                {
                    // Each NPC gets a chance to react (up to 3 for speed)
                    var reactors = npcNames.OrderBy(_ => _random.Next()).Take(Math.Min(npcNames.Count, 3)).ToList();

                    foreach (string npcName in reactors)
                    {
                        if (!customNpcs.TryGetValue(npcName, out var npcChar) || !conversationManagers.TryGetValue(npcName, out var convManager)) continue;

                        var sender = _plugin.ObjectTable.LocalPlayer;
                        if (sender == null || npcChar == null) continue;

                        CustomNpcCharacter npcData = null;
                        foreach (var npc in _plugin.Configuration.CustomNpcCharacters)
                        {
                            if (npc.NpcName == npcName) { npcData = npc; break; }
                        }
                        if (npcData == null) continue;

                        string deathPrompt = "*has just collapsed and is unconscious/dying! React with shock, grief, or urgency. This is a dramatic moment — cry out their name, rush to help, or beg them to hold on. Keep it brief and emotional (1-2 sentences).*";

                        string response = await convManager.SendMessage(
                            sender, npcChar,
                            npcData.NpcName,
                            npcData.NPCGreeting,
                            deathPrompt,
                            _plugin.GetEnvironmentContext(npcChar),
                            npcData.GetFullLore());

                        if (!string.IsNullOrEmpty(response))
                        {
                            string clean = CleanBubbleText(response);
                            if (clean.Length > 450) clean = clean.Substring(0, 447) + "...";

                            _plugin.Framework.RunOnFrameworkThread(() =>
                            {
                                ShowBubble(npcChar, npcName, clean);
                            });
                        }

                        // Stagger NPCs by 2s so they don't all talk at once
                        if (reactors.Count > 1) await Task.Delay(2000);
                    }
                }
                catch (Exception e)
                {
                    _plugin.PluginLog.Warning(e, "Death speech error");
                }
                finally
                {
                    _isProcessingAmbient = false;
                }
            });
        }

        /// <summary>
        /// Resets the death speech flag when the player revives.
        /// </summary>
        public void NotifyPlayerRevived()
        {
            _deathSpeechTriggered = false;
        }

        public void Dispose()
        {
            _ambientTimer.Stop();
            _lastAmbientMessages.Clear();
            _npcSummonTimers.Clear();
        }
    }
}
