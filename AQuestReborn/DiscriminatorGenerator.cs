using Dalamud.Plugin.Services;
using DragAndDropTexturing.ThreadSafeDalamudObjectTable;
using FFXIVClientStructs.FFXIV.Client.Game;
using SamplePlugin;
using System;

namespace AQuestReborn
{
    public static unsafe class DiscriminatorGenerator
    {
        public static unsafe string GetDiscriminator(ThreadSafeGameObjectManager objectTable)
        {
            string value = "";
            try
            {
                if (objectTable != null)
                {
                    if (objectTable.LocalPlayer != null)
                    {
                        var housingManager = HousingManager.Instance();
                        if (housingManager != null)
                        {
                            if (IsResidential())
                            {
                                value += objectTable.LocalPlayer.CurrentWorld.Value.Name.ExtractText() + "-" + housingManager->GetCurrentDivision() + "-"
                                    + housingManager->GetCurrentWard() + (housingManager->IsInside() ? "-" + housingManager->GetCurrentPlot() + "-" +
                                    housingManager->GetCurrentRoom() + "-" + (long)housingManager->GetCurrentIndoorHouseId() : "");
                            }
                            else
                            {
                                value += objectTable.LocalPlayer.CurrentWorld.Value.Name.ExtractText();
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
            var housingManager = HousingManager.Instance();
            return housingManager != null && (housingManager->IsInside() || housingManager->OutdoorTerritory != null);
        }
    }
}
