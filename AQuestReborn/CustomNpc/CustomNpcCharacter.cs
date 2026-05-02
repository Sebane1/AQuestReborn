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
        public ushort IdleEmoteId = 0;
    }
}
