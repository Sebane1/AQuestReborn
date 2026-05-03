using System;
using System.Collections.Generic;
using System.Numerics;

namespace AQuestReborn.CustomNpc
{
    public class CustomNpcCharacter
    {
        // Public fields because I cant use properties with Imgui code.
        public string NpcName = "New NPC";
        public string NPCGreeting = "Why hello there! How can I help you today?";
        public string NpcPersonality = "New NPC is a polite individual who likes to take long walks on the beach and see the world.";
        
        // Expanded Lore Fields
        public string NpcBirthDate = "";
        public string NpcBirthLocation = "";
        public string NpcJob = "";
        public uint NpcClassJobId = 0;
        public string NpcHobbies = "";
        public uint NpcEquippedWeaponItemId = 0;
        
        public string NpcGlamourerAppearanceString = "";
        public bool IsFollowingPlayer = false;
        public bool IsStaying = false;

        // Stay location persistence
        public uint StayTerritoryId = 0;
        public float StayPositionX = 0;
        public float StayPositionY = 0;
        public float StayPositionZ = 0;
        public float StayRotationX = 0;
        public float StayRotationY = 0;
        public float StayRotationZ = 0;

        // Idle pose
        public ushort IdleEmoteId = 50; // Default: groundsit
        public List<ushort> RandomIdleEmotes = new List<ushort>();
        public ushort VictoryPoseEmoteId = 0; // Default: none

        // Appearance mode
        public bool UseMcdfAppearance = false;
        public string McdfFilePath = "";

        public bool UseMonsterModel = false;
        public uint MonsterModelId = 0;

        public bool UsePenumbraCollection = false;
        public string PenumbraCollection = "";

        // Model Choice
        public string ModelChoice = "";

        // Encounter tracking (keyed by player/NPC name)
        // How many times this NPC has had a conversation with each person
        public Dictionary<string, int> EncounterCounts = new Dictionary<string, int>();
        // When this NPC last saw each person (UTC ticks for serialization)
        public Dictionary<string, long> LastSeenTimestamps = new Dictionary<string, long>();
        // Whether the player left this NPC behind at their stay location
        public bool WasLeftBehind = false;

        public void RecordEncounter(string personName)
        {
            if (EncounterCounts.ContainsKey(personName))
                EncounterCounts[personName]++;
            else
                EncounterCounts[personName] = 1;

            LastSeenTimestamps[personName] = DateTime.UtcNow.Ticks;
        }

        /// <summary>
        /// Updates the last-seen timestamp without incrementing the encounter count.
        /// Used for dismiss/zone-leave events.
        /// </summary>
        public void UpdateLastSeen(string personName)
        {
            LastSeenTimestamps[personName] = DateTime.UtcNow.Ticks;
        }

        public string GetEncounterContext(string personName)
        {
            string context = "";
            if (EncounterCounts.TryGetValue(personName, out int count) && count > 0)
            {
                context += $" They have met {personName} {count} time{(count > 1 ? "s" : "")} before.";

                if (LastSeenTimestamps.TryGetValue(personName, out long ticks))
                {
                    var lastSeen = new DateTime(ticks, DateTimeKind.Utc);
                    var elapsed = DateTime.UtcNow - lastSeen;

                    string timeAgo;
                    if (elapsed.TotalMinutes < 2)
                        timeAgo = "just moments ago";
                    else if (elapsed.TotalMinutes < 60)
                        timeAgo = $"about {(int)elapsed.TotalMinutes} minutes ago";
                    else if (elapsed.TotalHours < 24)
                        timeAgo = $"about {(int)elapsed.TotalHours} hour{((int)elapsed.TotalHours > 1 ? "s" : "")} ago";
                    else
                        timeAgo = $"about {(int)elapsed.TotalDays} day{((int)elapsed.TotalDays > 1 ? "s" : "")} ago";

                    context += $" They last saw {personName} {timeAgo}.";
                }

                // Add emotional context if left behind
                if (WasLeftBehind)
                {
                    context += $" {personName} left them behind at their current location when they departed.";
                }
            }
            else
            {
                context += $" They have never met {personName} before — this is their first encounter.";
            }
            return context;
        }

        public string GetFullLore()
        {
            string lore = NpcPersonality;
            if (!string.IsNullOrWhiteSpace(NpcBirthDate)) lore += $" They were born on {NpcBirthDate}.";
            if (!string.IsNullOrWhiteSpace(NpcBirthLocation)) lore += $" They were born in {NpcBirthLocation}.";
            if (!string.IsNullOrWhiteSpace(NpcJob)) lore += $" They work as a {NpcJob}.";
            if (!string.IsNullOrWhiteSpace(NpcHobbies)) lore += $" They enjoy {NpcHobbies}.";
            return lore;
        }
    }
}
