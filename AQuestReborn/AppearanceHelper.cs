using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using McdfDataImporter;
using RoleplayingVoiceDalamud.Glamourer;

namespace AQuestReborn
{
    public static class AppearanceHelper
    {
        public static CharacterCustomization GetCustomization(IPlayerCharacter playerCharacter)
        {
            try
            {
                return AppearanceAccessUtils.AppearanceManager.GetGlamourerCustomization();
            }
            catch
            {
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
                        Clan = new Clan() { Value = playerCharacter.Customize[(int)CustomizeIndex.Tribe] },
                        Race = new Race() { Value = playerCharacter.Customize[(int)CustomizeIndex.Race] },
                        BodyType = new BodyType() { Value = playerCharacter.Customize[(int)CustomizeIndex.ModelType] }
                    }
                };
            }
        }
    }
}
