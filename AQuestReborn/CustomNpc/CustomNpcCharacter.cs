using System;
using System.Collections.Generic;
using System.Linq;
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
        // Accumulated sentiment modifier per person (positive = kind, negative = hostile)
        public Dictionary<string, int> SentimentModifiers = new Dictionary<string, int>();

        // Places this NPC has visited (persisted across sessions)
        public List<string> VisitedLocations = new List<string>();

        /// <summary>
        /// Records a location visit if it hasn't been recorded already.
        /// </summary>
        public void RecordVisit(string locationName)
        {
            if (!string.IsNullOrWhiteSpace(locationName) && !VisitedLocations.Contains(locationName))
            {
                VisitedLocations.Add(locationName);
            }
        }

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

        private string CleanName(string personName)
        {
            if (string.IsNullOrEmpty(personName)) return "";
            if (personName.Contains("::")) return personName.Split(new[] { "::" }, StringSplitOptions.None).Last();
            return personName;
        }

        public string GetEncounterContext(string personName)
        {
            string context = "";
            string cleanName = CleanName(personName);
            
            if (EncounterCounts.TryGetValue(personName, out int count) && count > 0)
            {
                string familiarity = count >= 10 ? "extremely well" : "already";
                context += $" They {familiarity} know {cleanName} (having met {count} time{(count > 1 ? "s" : "")} before). DO NOT act like this is a first meeting or introduce yourself. Acknowledge them familiarly.";

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

                    context += $" They last saw {cleanName} {timeAgo}.";
                }

                // Add emotional context if left behind
                if (WasLeftBehind)
                {
                    context += $" {cleanName} abruptly left them behind at their current location when they departed.";
                }
            }
            else
            {
                context += $" They have never met {cleanName} before — this is their first encounter. They should introduce themselves.";
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

            // Travel history
            if (VisitedLocations.Count > 0)
            {
                lore += $" They have previously traveled to: {string.Join(", ", VisitedLocations)}.";
            }

            return lore;
        }
        /// <summary>
        /// Calculates a relationship level with a given person based on encounters, recency, and emotional state.
        /// Returns a tuple of (Label, Description, Score 0-100).
        /// </summary>
        public (string Label, string Description, int Score) GetRelationshipWith(string personName)
        {
            int score = 0;

            // Base familiarity from encounter count (each meeting adds 5, max 50)
            int encounters = 0;
            if (EncounterCounts.TryGetValue(personName, out int count))
                encounters = count;
            score += Math.Min(encounters * 5, 50);

            // Recency bonus (seen recently = warmer)
            if (LastSeenTimestamps.TryGetValue(personName, out long ticks))
            {
                var elapsed = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
                if (elapsed.TotalMinutes < 10)
                    score += 20;       // Currently together
                else if (elapsed.TotalHours < 1)
                    score += 15;       // Just parted
                else if (elapsed.TotalHours < 24)
                    score += 10;       // Seen today
                else if (elapsed.TotalDays < 7)
                    score += 5;        // Seen this week
                // Older than a week = no bonus
            }

            // Travel bonus — shared adventures build bonds (each zone together adds 2, max 20)
            score += Math.Min(VisitedLocations.Count * 2, 20);

            // Sentiment modifier from conversation tone
            if (SentimentModifiers.TryGetValue(personName, out int sentiment))
                score += Math.Clamp(sentiment, -40, 20);

            // Left behind penalty
            if (WasLeftBehind)
                score -= 15;

            score = Math.Clamp(score, 0, 100);

            // Map score to label
            string label;
            string description;
            if (score >= 80)
            {
                label = "Soulmate";
                description = "A deep, unshakable bond forged through countless shared experiences.";
            }
            else if (score >= 60)
            {
                label = "Close Friend";
                description = "A warm, reliable friendship built on trust and many meetings.";
            }
            else if (score >= 40)
            {
                label = "Friendly";
                description = "A comfortable familiarity — they know each other well enough to chat freely.";
            }
            else if (score >= 20)
            {
                label = "Acquaintance";
                description = "They've met a few times and are getting to know each other.";
            }
            else if (score > 0)
            {
                label = "Stranger";
                description = "They've barely met — still forming first impressions.";
            }
            else
            {
                label = "Unknown";
                description = "No relationship data recorded yet.";
            }

            if (WasLeftBehind && score < 60)
            {
                description += " Feels abandoned after being left behind.";
            }

            return (label, description, score);
        }

        // --- Sentiment keyword tiers ---
        // Mild hostility: insults, dismissiveness (-3 per hit)
        private static readonly string[] MildNegativeKeywords = new[]
        {
            "stupid", "idiot", "ugly", "dumb", "useless", "trash", "annoying",
            "moron", "loser", "pathetic", "lame", "boring", "weirdo", "creep",
            "shut up", "go away", "leave me alone", "get lost", "get out",
            "buzz off", "whatever", "don't care", "nobody asked", "who cares",
            "you suck", "you're awful", "you're terrible", "you're the worst",
            "hate you", "can't stand you", "sick of you", "tired of you"
        };

        // Harsh hostility: profanity, threats, slurs (-5 per hit)
        private static readonly string[] HarshNegativeKeywords = new[]
        {
            "fuck", "shit", "ass", "bitch", "bastard", "damn", "hell",
            "dick", "piss", "crap", "whore", "slut", "cock",
            "fuck you", "fuck off", "piss off", "screw you",
            "piece of shit", "son of a bitch", "go to hell",
            "eat shit", "kiss my ass", "suck my", "blow me",
            "stfu", "gtfo", "kys"
        };

        // Severe hostility: death threats, extreme cruelty (-8 per hit)
        private static readonly string[] SevereNegativeKeywords = new[]
        {
            "kill you", "murder you", "hope you die", "wish you were dead",
            "kill yourself", "end yourself", "neck yourself",
            "i'll destroy you", "i'll end you", "burn in hell",
            "worthless piece", "waste of space", "should be deleted",
            "nobody loves you", "everyone hates you", "you deserve to die"
        };

        // Words that indicate warmth or kindness (+1 per hit, +2 for strong)
        private static readonly string[] MildPositiveKeywords = new[]
        {
            "thank you", "thanks", "appreciate", "nice", "cool", "awesome",
            "great", "good job", "well done", "not bad", "impressive",
            "glad to see you", "happy to see you", "fun", "enjoy",
            "helpful", "kind", "sweet"
        };

        private static readonly string[] StrongPositiveKeywords = new[]
        {
            "love you", "adore you", "you're the best", "best friend",
            "missed you", "care about you", "proud of you", "wonderful",
            "beautiful", "amazing", "incredible", "you mean the world",
            "couldn't do it without you", "you're everything", "my hero",
            "i treasure you", "you're special"
        };

        /// <summary>
        /// Analyzes a message for sentiment keywords and adjusts the per-person modifier.
        /// Uses tiered severity: mild (-3), harsh (-5), severe (-8), mild positive (+1), strong positive (+2).
        /// </summary>
        public void RecordSentiment(string personName, string message)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(personName)) return;

            string lower = message.ToLower();
            int delta = 0;

            // Check severity tiers in order (most severe first)
            foreach (var word in SevereNegativeKeywords)
            {
                if (lower.Contains(word)) { delta = -8; break; }
            }

            if (delta == 0)
            {
                foreach (var word in HarshNegativeKeywords)
                {
                    if (lower.Contains(word)) { delta = -5; break; }
                }
            }

            if (delta == 0)
            {
                foreach (var word in MildNegativeKeywords)
                {
                    if (lower.Contains(word)) { delta = -3; break; }
                }
            }

            // Only check positive if no negative found
            if (delta == 0)
            {
                foreach (var word in StrongPositiveKeywords)
                {
                    if (lower.Contains(word)) { delta = 2; break; }
                }
            }

            if (delta == 0)
            {
                foreach (var word in MildPositiveKeywords)
                {
                    if (lower.Contains(word)) { delta = 1; break; }
                }
            }

            if (delta != 0)
            {
                if (SentimentModifiers.ContainsKey(personName))
                    SentimentModifiers[personName] += delta;
                else
                    SentimentModifiers[personName] = delta;
            }
        }

        /// <summary>
        /// Returns true if the NPC's relationship with this person is so low they refuse to talk.
        /// </summary>
        public bool ShouldRefuseConversation(string personName)
        {
            var rel = GetRelationshipWith(personName);
            return rel.Score <= 0;
        }

        /// <summary>
        /// Returns a mood context string to inject into the AI prompt based on how this NPC
        /// currently feels about the person they're talking to.
        /// </summary>
        public string GetMoodContext(string personName)
        {
            var rel = GetRelationshipWith(personName);
            string cleanName = CleanName(personName);

            if (rel.Score <= 5)
                return $" {NpcName} deeply resents {cleanName.Split(" ")[0]} and wants nothing to do with them. They are cold, hostile, and dismissive.";
            else if (rel.Score <= 15)
                return $" {NpcName} is upset and hurt by how {cleanName.Split(" ")[0]} has treated them. They are guarded and short-tempered.";
            else if (rel.Score <= 25)
                return $" {NpcName} is somewhat wary of {cleanName.Split(" ")[0]}. They're cautious and not fully comfortable.";
            else if (rel.Score >= 80)
                return $" {NpcName} adores {cleanName.Split(" ")[0]} and considers them a soulmate. They are warm, affectionate, and deeply trusting.";
            else if (rel.Score >= 60)
                return $" {NpcName} considers {cleanName.Split(" ")[0]} a close friend. They are open, cheerful, and supportive.";
            else
                return ""; // Neutral — no special mood injection
        }
    }
}
