using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using PenumbraAndGlamourerHelpers;
using SamplePlugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AQuestReborn.CustomNpc
{
    public class NPCConversationManager
    {
        public static List<string> RecentGameDialogue = new List<string>();
        public static List<string> RecentCombatEvents = new List<string>();
        private GPTWrapper _gptWrapper;
        private Plugin _plugin;
        private ICharacter _aiCharacter;
        private string _fullName;
        public GPTWrapper GptWrapper { get => _gptWrapper; }

        public NPCConversationManager(string name, string baseDirectory, Plugin plugin, ICharacter receivingCharacter)
        {
            _fullName = name;
            string cleanName = name.Contains("::") ? name.Split(new[] { "::" }, StringSplitOptions.None).Last() : name;
            string aiName = cleanName.Split(" ")[0];
            _gptWrapper = new GPTWrapper(aiName, Path.Combine(baseDirectory, name + "-memories.json"));
            _plugin = plugin;
            _aiCharacter = receivingCharacter;
        }

        private string GetLeastUsedModel(System.Collections.Generic.IEnumerable<string> availableModels)
        {
            var usedModels = _plugin.Configuration.CustomNpcCharacters
                .Where(c => !string.IsNullOrEmpty(c.ModelChoice))
                .GroupBy(c => c.ModelChoice)
                .ToDictionary(g => g.Key, g => g.Count());

            var leastUsedModels = availableModels.OrderBy(m => usedModels.ContainsKey(m) ? usedModels[m] : 0).ToList();
            int minCount = usedModels.ContainsKey(leastUsedModels[0]) ? usedModels[leastUsedModels[0]] : 0;
            var candidates = leastUsedModels.Where(m => (usedModels.ContainsKey(m) ? usedModels[m] : 0) == minCount).ToList();
            return candidates[System.Random.Shared.Next(candidates.Count)];
        }
        public async Task<string> SendMessage(ICharacter sendingCharacter, ICharacter receivingCharacter, string aiName,
            string aiGreeting, string message, string setting, string aiDescription, string modelChoice = "", string senderNameOverride = "")
        {
            // Use the override name if provided (for NPC-to-NPC chat where Brio actors are named "Reborn")
            string senderFullName = !string.IsNullOrEmpty(senderNameOverride) ? senderNameOverride : sendingCharacter.Name.TextValue;
            if (senderFullName.Contains("::")) senderFullName = senderFullName.Split(new[] { "::" }, StringSplitOptions.None).Last();
            senderFullName = System.Text.RegularExpressions.Regex.Replace(senderFullName, @"_+[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            senderFullName = System.Text.RegularExpressions.Regex.Replace(senderFullName, @"\b[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            string senderName = senderFullName.Split(" ")[0];
            if (string.IsNullOrEmpty(modelChoice))
            {
                foreach (var npc in _plugin.Configuration.CustomNpcCharacters)
                {
                    if (npc.NpcName == aiName || npc.NpcName.StartsWith(aiName + " "))
                    {
                        if (npc.ModelChoice.Contains("fairsqeq") || npc.ModelChoice.Contains("fairseq"))
                        {
                            npc.ModelChoice = "";
                        }
                        
                        if (string.IsNullOrEmpty(npc.ModelChoice))
                        {
                            var models = new[] { "cassandra-lit-6-9b", "convo-6b", "gpt-j-6b" };
                            npc.ModelChoice = GetLeastUsedModel(models);
                            _plugin.Configuration.Save();
                        }
                        modelChoice = npc.ModelChoice;
                        break;
                    }
                }
            }
            // Enrich the AI description with encounter history
            string encounterContext = "";
            string playerFullName = senderFullName;
            
            // Find the NPC data to pull encounter tracking from
            CustomNpcCharacter npcDataRef = null;
            foreach (var npc in _plugin.Configuration.CustomNpcCharacters)
            {
                if (npc.NpcName == _fullName || npc.NpcName.StartsWith(aiName + " "))
                {
                    npcDataRef = npc;
                    break;
                }
            }

            if (npcDataRef != null)
            {
                // Add player encounter context
                encounterContext += npcDataRef.GetEncounterContext(playerFullName);

                // Add encounter context for other summoned NPCs
                if (_plugin.AQuestReborn?.InteractiveNpcDictionary != null)
                {
                    foreach (var otherNpcKvp in _plugin.AQuestReborn.InteractiveNpcDictionary)
                    {
                        if (otherNpcKvp.Key != _fullName)
                        {
                            encounterContext += npcDataRef.GetEncounterContext(otherNpcKvp.Key);
                        }
                    }
                }
            }

            string enrichedDescription = aiDescription.Trim('.').Trim() + "." + encounterContext;
            string finalSetting = setting + GetEnvironmentMemory();

            string aiMessage = await _gptWrapper.SendMessage(senderName, message, $@" smiles. ""{aiGreeting}""",
            GetPlayerDescription(sendingCharacter, false, "", senderFullName), enrichedDescription + " " + GetPlayerDescription(receivingCharacter, true, aiName), finalSetting, 5, modelChoice);
            
            if (string.IsNullOrEmpty(aiMessage))
            {
                var models = new System.Collections.Generic.List<string> { "cassandra-lit-6-9b", "convo-6b", "gpt-j-6b" };
                models.Remove(modelChoice);
                string newModelChoice = GetLeastUsedModel(models);
                
                foreach (var npc in _plugin.Configuration.CustomNpcCharacters)
                {
                    if (npc.NpcName == aiName || npc.NpcName.StartsWith(aiName + " "))
                    {
                        npc.ModelChoice = newModelChoice;
                        _plugin.Configuration.Save();
                        break;
                    }
                }
                
                // Retry once with new model
                aiMessage = await _gptWrapper.SendMessage(senderName, message, $@" smiles. ""{aiGreeting}""",
                GetPlayerDescription(sendingCharacter, false, "", senderFullName), enrichedDescription + " " + GetPlayerDescription(receivingCharacter, true, aiName), finalSetting, 5, newModelChoice);
            }
            string correctedMessage = PenumbraAndGlamourerHelperFunctions.GetGender(sendingCharacter) == 1 ? GenderFix(aiMessage) : aiMessage;
            Task.Run(() =>
            {
                EmoteReaction(correctedMessage);
            });
            return correctedMessage;
        }

        public string GetPromptPreview(ICharacter sendingCharacter, ICharacter receivingCharacter, string aiName,
            string aiGreeting, string message, string setting, string aiDescription)
        {
            string senderFullName = sendingCharacter.Name.TextValue;
            if (senderFullName.Contains("::")) senderFullName = senderFullName.Split(new[] { "::" }, StringSplitOptions.None).Last();
            senderFullName = System.Text.RegularExpressions.Regex.Replace(senderFullName, @"_+[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            senderFullName = System.Text.RegularExpressions.Regex.Replace(senderFullName, @"\b[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            string senderName = senderFullName.Split(" ")[0];
            string encounterContext = "";
            string playerFullName = senderFullName;
            
            CustomNpcCharacter npcDataRef = null;
            foreach (var npc in _plugin.Configuration.CustomNpcCharacters)
            {
                if (npc.NpcName == _fullName || npc.NpcName.StartsWith(aiName + " "))
                {
                    npcDataRef = npc;
                    break;
                }
            }

            if (npcDataRef != null)
            {
                encounterContext += npcDataRef.GetEncounterContext(playerFullName);
                if (_plugin.AQuestReborn?.InteractiveNpcDictionary != null)
                {
                    foreach (var otherNpcKvp in _plugin.AQuestReborn.InteractiveNpcDictionary)
                    {
                        if (otherNpcKvp.Key != _fullName)
                        {
                            encounterContext += npcDataRef.GetEncounterContext(otherNpcKvp.Key);
                        }
                    }
                }
            }

            string enrichedDescription = aiDescription.Trim('.').Trim() + "." + encounterContext;
            string finalSetting = setting + GetEnvironmentMemory();

            return _gptWrapper.GetPreviewPrompt(senderName, message, $@" smiles. ""{aiGreeting}""",
            GetPlayerDescription(sendingCharacter, false, "", senderFullName), enrichedDescription + " " + GetPlayerDescription(receivingCharacter, true, aiName), finalSetting);
        }

        /// <summary>
        /// Injects a narrator/environmental context line into this NPC's conversation history.
        /// </summary>
        public void InjectNarratorContext(string contextLine)
        {
            _gptWrapper?.InjectNarratorContext(_fullName, contextLine);
        }

        /// <summary>
        /// Summarizes and persists all active conversations this NPC has had.
        /// Call before disposing on zone change to preserve conversation memory.
        /// </summary>
        public async Task FlushSummaries()
        {
            if (_gptWrapper != null)
                await _gptWrapper.FlushAllSummaries();
        }

        public string GetPlayerDescription(ICharacter player, bool skipSummary = false, string alias = "", string nameOverride = "")
        {
            int gender = PenumbraAndGlamourerHelperFunctions.GetGender(player);
            int race = PenumbraAndGlamourerHelperFunctions.GetRace(player);
            int tribe = PenumbraAndGlamourerHelperFunctions.GetTribe(player);
            string genderStr = gender == 1 ? "female" : "male";
            string pronouns = gender == 1 ? "she/her" : "he/him";
            string pronounSingular = gender == 1 ? "her" : "his";
            string pronounSingularAlternate = gender == 1 ? "She" : "He";
            string raceStr = GetRaceDescription(race, tribe, pronounSingularAlternate);
            string playerNameFull = !string.IsNullOrEmpty(nameOverride) ? nameOverride : player.Name.TextValue;
            playerNameFull = System.Text.RegularExpressions.Regex.Replace(playerNameFull, @"_+[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            playerNameFull = System.Text.RegularExpressions.Regex.Replace(playerNameFull, @"\b[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            string firstName = playerNameFull.Split(" ")[0];
            var summaries = !skipSummary ? _gptWrapper.GetConversationalMemory(firstName) : new List<string>();
            string chatSummaries = "\n\nIn the past " + _gptWrapper.Personality
            + " and " + playerNameFull + " had the following situations:";
            if (summaries.Count == 0)
            {
                chatSummaries = "";
            }
            else
            {
                for (int i = summaries.Count - 1; i >= Math.Clamp(summaries.Count - 5, 0, summaries.Count); i--)
                {
                    if (i > -1)
                    {
                        var summary = summaries[i];
                        chatSummaries += "\nEncounter " + i + summary;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            string name = !string.IsNullOrEmpty(alias) ? alias : playerNameFull.Split(" ")[0];
            if (name.Contains("::")) name = name.Split(new[] { "::" }, StringSplitOptions.None).Last();

            return $"{name} is a {genderStr}. {pronounSingularAlternate} is a race of {raceStr}. " +
                $"{GetPlayerExperience(player.Level, player.ClassJob.Value.NameEnglish.ToString(), pronounSingularAlternate)}." +
                GetAppearanceData(player) + chatSummaries;
        }
        
        private string GetEnvironmentMemory()
        {
            string combatMemory = "";
            if (!string.IsNullOrEmpty(InteractiveNpc.LastCombatTarget))
            {
                combatMemory = $" The player recently fought a {InteractiveNpc.LastCombatTarget}. ";
            }

            string recentDialogueMemory = "";
            lock (RecentGameDialogue)
            {
                if (RecentGameDialogue.Count > 0)
                {
                    recentDialogueMemory = " Recently overheard dialogue in the area: " + string.Join(" ", RecentGameDialogue) + " ";
                }
            }

            string recentCombatMemory = "";
            lock (RecentCombatEvents)
            {
                if (RecentCombatEvents.Count > 0)
                {
                    recentCombatMemory = " Recent combat events around you: " + string.Join(" ", RecentCombatEvents) + " ";
                }
            }

            return combatMemory + recentCombatMemory + recentDialogueMemory;
        }
        
        private string GetAppearanceData(ICharacter player)
        {
            string appearanceData = "";
            try
            {
                var cust = PenumbraAndGlamourerHelperFunctions.GetCustomization(player);
                if (cust?.Equipment != null)
                {
                    List<string> items = new List<string>();
                    var sheet = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                    
                    void AddItem(long itemId, string slotName)
                    {
                        if (itemId > 0 && itemId < 1000000)
                        {
                            var item = sheet.GetRow((uint)itemId);
                            if (!string.IsNullOrEmpty(item.Name.ToString()))
                            {
                                string name = item.Name.ToString();
                                if (name.Contains("Emperor's New", StringComparison.OrdinalIgnoreCase))
                                {
                                    items.Add("nothing on their " + slotName);
                                }
                                else
                                {
                                    items.Add(name);
                                }
                            }
                        }
                    }

                    if (cust.Equipment.Head != null && cust.Equipment.Hat != null && cust.Equipment.Hat.Show) AddItem(cust.Equipment.Head.ItemId, "head");
                    if (cust.Equipment.Body != null) AddItem(cust.Equipment.Body.ItemId, "torso");
                    if (cust.Equipment.Hands != null) AddItem(cust.Equipment.Hands.ItemId, "hands");
                    if (cust.Equipment.Legs != null) AddItem(cust.Equipment.Legs.ItemId, "legs");
                    if (cust.Equipment.Feet != null) AddItem(cust.Equipment.Feet.ItemId, "feet");

                    if (items.Count > 0)
                    {
                        appearanceData = " They are currently wearing: " + string.Join(", ", items) + ". ";
                    }
                }
            }
            catch (Exception ex)
            {
                _plugin.PluginLog.Warning("Failed to read appearance: " + ex.Message);
            }
            return appearanceData;
        }
        private string GetRaceDescription(int race, int tribe, string pronoun)
        {
            string tribeStr = _plugin?.EventWindow != null ? _plugin.EventWindow.Tribe(tribe) : "";
            string tribePrefix = string.IsNullOrEmpty(tribeStr) ? "" : tribeStr + " ";

            switch (race)
            {
                case 1: // Hyur
                    return $"{tribePrefix}Hyur. {pronoun} looks like an average person.";
                case 2: // Elezen
                    return $"{tribePrefix}Elezen. {pronoun} looks like a tall elf with pointy ears";
                case 3: // Lalafell
                    return $"{tribePrefix}Lalafell. {pronoun} looks like a short stubby person.";
                case 4: // Miqo'te
                    return $"{tribePrefix}Miqo'te. {pronoun} has cat ears, a tail, and likes to meow.";
                case 5: // Roegadyn
                    return $"{tribePrefix}Roegadyn. {pronoun} is a tall and muscular sea faring race.";
                case 6: // Au Ra
                    return $"{tribePrefix}Au Ra. {pronoun} has dragonlike scales, horns, a scaley tail";
                case 7: // Hrothgar
                    return $"{tribePrefix}Hrothgar. {pronoun} looks like a furry humanoid cat.";
                case 8: // Viera
                    return $"{tribePrefix}Viera. {pronoun} is tall, and has cute bunny ears";
            }
            return "Unidentified";
        }
        private string GetPlayerExperience(int level, string className, string pronoun)
        {
            if (level < 10)
            {
                return pronoun + " is a very inexperienced " + className;
            }
            else if (level < 20)
            {
                return pronoun + " is a learning " + className;
            }
            else if (level < 30)
            {
                return pronoun + " is an unimpressive " + className;
            }
            else if (level < 40)
            {
                return pronoun + " is an average " + className;
            }
            else if (level < 50)
            {
                return pronoun + " is an above average " + className;
            }
            else if (level < 60)
            {
                return pronoun + " is a decently skilled " + className;
            }
            else if (level < 70)
            {
                return pronoun + " is a an experienced " + className;
            }
            else if (level < 80)
            {
                return pronoun + " is a highly experienced " + className;
            }
            else if (level < 90)
            {
                return pronoun + " is a very outstanding " + className;
            }
            else if (level < 100)
            {
                return pronoun + " is the best of the best " + className;
            }
            return pronoun + " has no skills";
        }
        string GenderFix(string value)
        {
            return value.Replace(" himself", " herself").Replace("He ", "She ")
                                 .Replace(" he ", " she ").Replace(" he?", " she?")
                                 .Replace(" hes ", " she's ").Replace(" he's ", " she's ").Replace("He's ", "She's ")
                                 .Replace(" him ", " her ").Replace(" him,", " her,").Replace(" him.", " her.").Replace(" his ", " her ").Replace(" his.", " her.")
                                 .Replace("His ", "Her ").Replace(" men ", " women ").Replace(" men.", " women.").Replace(" sir ", " ma'am ")
                                 .Replace(" man ", " woman ").Replace(" boy", " girl").Replace(" man.", " woman.");
        }
        private async void EmoteReaction(string messageValue)
        {
            try
            {
                var emotes = _plugin.DataManager.GetExcelSheet<Emote>();
                string[] messageEmotes = messageValue.Replace("*", " ").Split("\"");
                string emoteString = " ";
                for (int i = 1; i < messageEmotes.Length + 1; i++)
                {
                    if ((i + 1) % 2 == 0)
                    {
                        emoteString += messageEmotes[i - 1] + " ";
                    }
                }
                foreach (var item in emotes)
                {
                    if (!string.IsNullOrWhiteSpace(item.Name.ToString()))
                    {
                        if ((emoteString.ToLower().Contains(" " + item.Name.ToString().ToLower() + " ") ||
                            emoteString.ToLower().Contains(" " + item.Name.ToString().ToLower() + "s ") ||
                            emoteString.ToLower().Contains(" " + item.Name.ToString().ToLower() + "ed ") ||
                            emoteString.ToLower().Contains(" " + item.Name.ToString().ToLower() + "ing ") ||
                            emoteString.ToLower().EndsWith(" " + item.Name.ToString().ToLower()) ||
                            emoteString.ToLower().Contains(" " + item.Name.ToString().ToLower() + "s") ||
                            emoteString.ToLower().Contains(" " + item.Name.ToString().ToLower() + "ed") ||
                            emoteString.ToLower().Contains(" " + item.Name.ToString().ToLower() + "ing"))
                            || (emoteString.ToLower().Contains(" " + item.Name.ToString().ToLower()) && item.Name.ToString().Length > 3))
                        {
                            if (_aiCharacter != null)
                            {
                                bool canEmote = true;
                                if (_plugin.AQuestReborn.InteractiveNpcDictionary.ContainsKey(_fullName))
                                {
                                    canEmote = _plugin.AQuestReborn.InteractiveNpcDictionary[_fullName].IsStationary;
                                }
                                // Suppress emotes while swimming/diving — land emotes look broken in water
                                unsafe
                                {
                                    if (FFXIVClientStructs.FFXIV.Client.Game.Conditions.Instance()->Swimming || FFXIVClientStructs.FFXIV.Client.Game.Conditions.Instance()->Diving)
                                        canEmote = false;
                                }

                                if (canEmote)
                                {
                                    _plugin.AnamcoreManager.TriggerEmoteTimed(_aiCharacter, (ushort)item.ActionTimeline[0].Value.RowId, 500);
                                }
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _plugin.PluginLog.Warning(e, e.Message);
            }
        }
    }
}
