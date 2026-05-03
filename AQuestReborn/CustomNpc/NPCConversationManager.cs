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

        public NPCConversationManager(string name, string baseDirectory, Plugin plugin, ICharacter receivingCharacter)
        {
            _fullName = name;
            string aiName = name.Split(" ")[0];
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
            string aiGreeting, string message, string setting, string aiDescription, string modelChoice = "")
        {
            string senderName = sendingCharacter.Name.TextValue.Split(" ")[0];
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

            string aiMessage = await _gptWrapper.SendMessage(senderName, message, $@" smiles. ""{aiGreeting}""",
            GetPlayerDescription(sendingCharacter), aiDescription.Trim('.').Trim() + ". " + GetPlayerDescription(receivingCharacter, true, aiName), setting, 2, modelChoice);
            
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
                GetPlayerDescription(sendingCharacter), aiDescription.Trim('.').Trim() + ". " + GetPlayerDescription(receivingCharacter, true, aiName), setting, 2, newModelChoice);
            }
            string correctedMessage = PenumbraAndGlamourerHelperFunctions.GetGender(sendingCharacter) == 1 ? GenderFix(aiMessage) : aiMessage;
            Task.Run(() =>
            {
                EmoteReaction(correctedMessage);
            });
            return correctedMessage;
        }
        public string GetPlayerDescription(ICharacter player, bool skipSummary = false, string alias = "")
        {
            int gender = PenumbraAndGlamourerHelperFunctions.GetGender(player);
            int race = PenumbraAndGlamourerHelperFunctions.GetRace(player);
            string genderStr = gender == 1 ? "female" : "male";
            string pronouns = gender == 1 ? "she/her" : "he/him";
            string pronounSingular = gender == 1 ? "her" : "his";
            string pronounSingularAlternate = gender == 1 ? "She" : "He";
            string raceStr = GetRaceDescription(race, pronounSingularAlternate);
            var summaries = !skipSummary ? _gptWrapper.GetConversationalMemory(player.Name.TextValue) : new List<string>();
            string chatSummaries = "\n\nIn the past " + _gptWrapper.Personality
            + " and " + player.Name.TextValue + " had the following situations:";
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
            string name = !string.IsNullOrEmpty(alias) ? alias : player.Name.TextValue.Split(" ")[0];
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

            return $"{name} is a {genderStr}. {pronounSingularAlternate} is a race of {raceStr}. " +
                $"{GetPlayerExperience(player.Level, player.ClassJob.Value.NameEnglish.ToString(), pronounSingularAlternate)}." +
                combatMemory + recentCombatMemory + recentDialogueMemory + chatSummaries;
        }
        private string GetRaceDescription(int race, string pronoun)
        {
            switch (race)
            {
                case 0:
                    return $"Hyur. {pronoun} looks like an average person.";
                case 1:
                    return $"Highlander. {pronoun} looks muscular, tough";
                case 2:
                    return $"Elezen. {pronoun} looks like a tall elf with pointy ears";
                case 4:
                    return $"Miqo'te. {pronoun} has cat ears, a tail, and likes to meow.";
                case 3:
                    return $"Roegadyn. {pronoun} a tall and muscular sea faring race.";
                case 5:
                    return $"Lalafel. {pronoun} looks like a short stubby person.";
                case 6:
                    return $"Au'Ra. {pronoun} has dragonlike scales, horns, a scaley tail";
                case 7:
                    return $"Hrothgar. {pronoun} looks like a furry humanoid cat.";
                case 8:
                    return $"Viera. {pronoun} is tall, and has cute bunny ears";
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
