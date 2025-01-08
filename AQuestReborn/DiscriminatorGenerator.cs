using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using SamplePlugin;
using System;

namespace AQuestReborn
{
    public static unsafe class DiscriminatorGenerator
    {
        static HousingManager* housingManager;
        public static unsafe string GetDiscriminator(IClientState clientState)
        {
            string value = "";
            try
            {
                if (clientState != null)
                {
                    if (clientState.LocalPlayer != null)
                    {
                        if (housingManager == null)
                        {
                            housingManager = HousingManager.Instance();
                        }
                        if (housingManager != null)
                        {
                            if (IsResidential())
                            {
                                value += clientState.LocalPlayer.CurrentWorld.Value.Name.ExtractText() + "-" + housingManager->GetCurrentDivision() + "-"
                                    + housingManager->GetCurrentWard() + (housingManager->IsInside() ? "-" + housingManager->GetCurrentPlot() + "-" +
                                    housingManager->GetCurrentRoom() + "-" + housingManager->GetCurrentIndoorHouseId() : "");
                            }
                            else
                            {
                                value += clientState.LocalPlayer.CurrentWorld.Value.Name.ExtractText();
                            }
                        }
                    }

                }
            }
            catch (Exception e)
            {
                Plugin.Instance.PluginLog.Warning(e, e.Message);
            }
            return value;
        }
        private static unsafe bool IsResidential()
        {
            return housingManager->IsInside() || housingManager->OutdoorTerritory != null;
        }
    }
}
