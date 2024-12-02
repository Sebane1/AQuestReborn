using Dalamud.Configuration;
using Dalamud.Plugin;
using RoleplayingQuestCore;
using System;
using System.Collections.Generic;

namespace SamplePlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public Dictionary<string, RoleplayingQuest> _questChains = new Dictionary<string, RoleplayingQuest>();
    public Dictionary<string, int> _questProgression = new Dictionary<string, int>();

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;
    public Dictionary<string, RoleplayingQuest> QuestChains { get => _questChains; set => _questChains = value; }
    public Dictionary<string, int> QuestProgression { get => _questProgression; set => _questProgression = value; }

    // the below exist just to make saving less cumbersome
    public void Save()
    {
        try
        {
            lock (QuestChains)
            {
                {
                    lock (QuestProgression)
                    {
                        Plugin.PluginInterface.SavePluginConfig(this);
                    }
                }
            }
        }
        catch
        {

        }
    }
}
