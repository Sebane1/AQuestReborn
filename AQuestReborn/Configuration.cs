using Dalamud.Configuration;
using RoleplayingQuestCore;
using System;
using System.Collections.Generic;

namespace SamplePlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    private string questInstallFolder;

    public Dictionary<string, RoleplayingQuest> _questChains = new Dictionary<string, RoleplayingQuest>();
    public Dictionary<string, int> _questProgression = new Dictionary<string, int>();

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;
    public Dictionary<string, RoleplayingQuest> QuestChains { get => _questChains; set => _questChains = value; }
    public Dictionary<string, int> QuestProgression { get => _questProgression; set => _questProgression = value; }
    public string QuestInstallFolder { get => questInstallFolder; set => questInstallFolder = value; }

    // the below exist just to make saving less cumbersome
    public void Save()
    {
        bool persistenceSucceeded = false;
        while (!persistenceSucceeded)
        {
            try
            {
                lock (QuestChains)
                {
                    {
                        lock (QuestProgression)
                        {
                            Plugin.PluginInterface.SavePluginConfig(this);
                            persistenceSucceeded = true;
                        }
                    }
                }
            }
            catch
            {

            }
        }
    }
}
