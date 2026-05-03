using System;
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
        public string NpcHobbies = "";
        
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
        public ushort VictoryPoseEmoteId = 0; // Default: none

        // Appearance mode
        // Appearance mode
        public bool UseMcdfAppearance = false;
        public string McdfFilePath = "";

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
