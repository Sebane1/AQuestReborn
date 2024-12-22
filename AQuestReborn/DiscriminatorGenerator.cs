using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AQuestReborn
{
    public static class DiscriminatorGenerator
    {
        public static unsafe string GetDiscriminator(IClientState clientState)
        {
            string value = "";
            if (clientState != null)
            {
                if (clientState.LocalPlayer != null)
                {
                    if (IsResidential())
                    {
                        value += clientState.LocalPlayer.CurrentWorld.Value.Name.ExtractText() + "-" + HousingManager.Instance()->GetCurrentDivision() + "-"
                            + HousingManager.Instance()->GetCurrentWard() + (HousingManager.Instance()->IsInside() ? "-" + HousingManager.Instance()->GetCurrentPlot() + "-" +
                            HousingManager.Instance()->GetCurrentRoom() + "-" + HousingManager.Instance()->GetCurrentIndoorHouseId() : "");
                    }
                    else
                    {
                        value += clientState.LocalPlayer.CurrentWorld.Value.Name.ExtractText();
                    }
                }
            }
            return value;
        }
        private static unsafe bool IsResidential()
        {
            return HousingManager.Instance()->IsInside() || HousingManager.Instance()->OutdoorTerritory != null;
        }
    }
}
