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
            _nextAmbientIntervalMs = _random.Next(300000, 600000); // 5-10 minutes
            _ambientTimer.Start();
        }

        /// <summary>
        /// Shows a native FFXIV speech bubble above a character's head.
        /// </summary>
        public unsafe void ShowNativeBubble(ICharacter character, string text)
        {
            try
            {
                var charStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)character.Address;
                if (charStruct != null)
                {
                    charStruct->Balloon.PlayTimer = 1;
                    charStruct->Balloon.Text = new FFXIVClientStructs.FFXIV.Client.System.String.Utf8String(text);
                    // Write Type and State directly via pointer cast
                    *((byte*)&charStruct->Balloon.Type) = 0; // Timer
                    *((byte*)&charStruct->Balloon.State) = 1; // Active
                }
            }
            catch (Exception e)
            {
                _plugin.PluginLog.Warning(e, "Failed to show native bubble");
            }
        }

        /// <summary>
        /// Called from Framework.Update to check if it's time for ambient NPC chatter.
        /// </summary>
        public void Update()
        {
            if (!_ambientEnabled || _isProcessingAmbient) return;

            if (_plugin.AQuestReborn == null) return;
            var customNpcs = _plugin.AQuestReborn.CustomNpcCharacters;
            var conversationManagers = _plugin.AQuestReborn.CustomNpcConversationManagers;

            if (customNpcs == null || customNpcs.Count == 0) return;

            // Don't trigger ambient chat while player is in a conversation
            if (_plugin.NpcChatWindow != null && _plugin.NpcChatWindow.IsConversationActive) return;

            if (_ambientTimer.ElapsedMilliseconds >= _nextAmbientIntervalMs)
            {
                _ambientTimer.Restart();
                _nextAmbientIntervalMs = _random.Next(300000, 600000); // Reset to 5-10 min
                _isProcessingAmbient = true;

                Task.Run(async () =>
                {
                    try
                    {
                        var npcNames = customNpcs.Keys.ToList();
                        if (npcNames.Count == 0) return;

                        // If multiple NPCs, 50% chance of NPC-to-NPC conversation
                        if (npcNames.Count >= 2 && _random.Next(2) == 0)
                        {
                            await TriggerNpcToNpcChat(npcNames, customNpcs, conversationManagers);
                        }
                        else
                        {
                            // Solo ambient thought
                            string npcName = npcNames[_random.Next(npcNames.Count)];
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
            if (!customNpcs.ContainsKey(npcName) || !conversationManagers.ContainsKey(npcName)) return;

            var npcChar = customNpcs[npcName];
            var convManager = conversationManagers[npcName];
            var sender = _plugin.ObjectTable.LocalPlayer;
            if (sender == null || npcChar == null) return;

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
            if (npcData == null) return;

            string response = await convManager.SendMessage(
                sender, npcChar,
                npcData.NpcName,
                npcData.NPCGreeting,
                "*is standing nearby, looking around idly*",
                "The world of Final Fantasy XIV, Eorzea.",
                npcData.NpcPersonality);

            if (!string.IsNullOrEmpty(response))
            {
                string clean = CleanBubbleText(response);
                // Truncate for bubble display (bubbles can't be too long)
                if (clean.Length > 120) clean = clean.Substring(0, 117) + "...";

                _lastAmbientMessages[npcName] = clean;

                _plugin.Framework.RunOnFrameworkThread(() =>
                {
                    ShowNativeBubble(npcChar, clean);
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
                "The world of Final Fantasy XIV, Eorzea.",
                dataA.NpcPersonality);

            if (!string.IsNullOrEmpty(responseA))
            {
                string cleanA = CleanBubbleText(responseA);
                if (cleanA.Length > 120) cleanA = cleanA.Substring(0, 117) + "...";
                _lastAmbientMessages[npcA] = cleanA;

                _plugin.Framework.RunOnFrameworkThread(() =>
                {
                    ShowNativeBubble(charA, cleanA);
                });

                // Wait for bubble to be read, then NPC B responds
                await Task.Delay(4000);

                string responseB = await conversationManagers[npcB].SendMessage(
                    sender, charB,
                    dataB.NpcName,
                    dataB.NPCGreeting,
                    $"*{npcA} just said: \"{cleanA}\". Respond naturally.*",
                    "The world of Final Fantasy XIV, Eorzea.",
                    dataB.NpcPersonality);

                if (!string.IsNullOrEmpty(responseB))
                {
                    string cleanB = CleanBubbleText(responseB);
                    if (cleanB.Length > 120) cleanB = cleanB.Substring(0, 117) + "...";
                    _lastAmbientMessages[npcB] = cleanB;

                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        ShowNativeBubble(charB, cleanB);
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
            if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length > 2)
                text = text.Substring(1, text.Length - 2);
            text = text.TrimEnd('"');
            return text.Trim();
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
