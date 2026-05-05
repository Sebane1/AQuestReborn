using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using McdfDataImporter;
using RoleplayingVoiceDalamud.Glamourer;

namespace AQuestReborn
{
    public static class AppearanceHelper
    {
        public static CharacterCustomization GetCustomization(Dalamud.Game.ClientState.Objects.Types.ICharacter playerCharacter)
        {
            try
            {
                // Only pull Glamourer IPC data if this is actually a player object,
                // because GetGlamourerCustomization() has no parameters and ALWAYS returns the LOCAL PLAYER's data.
                // If we don't check this, NPCs will inherit the player's appearance data.
                if (playerCharacter is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter)
                {
                    return AppearanceAccessUtils.AppearanceManager.GetGlamourerCustomization();
                }
            }
            catch { }
            
            // Fallback to reading the physical engine memory for the object (NPCs or if Glamourer fails)
            // Note: Dalamud memory indices are 0-indexed, but Lumina (EventWindow) expects 1-indexed values.
            return new CharacterCustomization()
            {
                Customize = new Customize()
                {
                    EyeColorLeft = new FacialValue() { Value = playerCharacter.Customize[(int)CustomizeIndex.EyeColor] },
                    EyeColorRight = new FacialValue() { Value = playerCharacter.Customize[(int)CustomizeIndex.EyeColor2] },
                    BustSize = new BustSize() { Value = playerCharacter.Customize[(int)CustomizeIndex.BustSize] },
                    LipColor = new LipColor() { Value = playerCharacter.Customize[(int)CustomizeIndex.LipColor] },
                    Gender = new Gender() { Value = playerCharacter.Customize[(int)CustomizeIndex.Gender] },
                    Height = new Height() { Value = playerCharacter.Customize[(int)CustomizeIndex.Height] },
                    Clan = new Clan() { Value = (byte)(playerCharacter.Customize[(int)CustomizeIndex.Tribe] + 1) },
                    Race = new Race() { Value = (byte)(playerCharacter.Customize[(int)CustomizeIndex.Race] + 1) },
                    BodyType = new BodyType() { Value = playerCharacter.Customize[(int)CustomizeIndex.ModelType] }
                }
            };
        }
    }
}
