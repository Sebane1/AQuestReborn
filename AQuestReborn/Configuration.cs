using Dalamud.Configuration;
using FFXIVClientStructs.FFXIV.Common.Lua;
using McdfDataImporter;
using RoleplayingQuestCore;
using System;
using System.Collections.Generic;
using System.IO;

namespace SamplePlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    private string _questInstallFolder = "";

    public Dictionary<string, RoleplayingQuest> _questChains = new Dictionary<string, RoleplayingQuest>();
    public Dictionary<string, int> _questProgression = new Dictionary<string, int>();
    private Dictionary<string, List<string>> _completedObjectives = new Dictionary<string, List<string>>();

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;
    public Dictionary<string, RoleplayingQuest> QuestChains { get => _questChains; set => _questChains = value; }
    public Dictionary<string, int> QuestProgression { get => _questProgression; set => _questProgression = value; }
    public string QuestInstallFolder
    {
        get
        {
            if (!string.IsNullOrEmpty(_questInstallFolder) && (_questInstallFolder.Contains("Program Files")
                || _questInstallFolder.Contains("FINAL FANTASY XIV - A Realm Reborn")))
            {
                _questInstallFolder = "";
            }
            if (!string.IsNullOrEmpty(_questInstallFolder))
            {
                try
                {
                    McdfAccessUtils.CacheLocation = Path.Combine(Path.GetDirectoryName(_questInstallFolder + ".poop"), "QuestCache\\");
                    Directory.CreateDirectory(McdfAccessUtils.CacheLocation);
                }
                catch
                {
                    _questInstallFolder = "";
                }
            }
            return _questInstallFolder;
        }
        set
        {
            _questInstallFolder = value;
            if (!string.IsNullOrEmpty(_questInstallFolder))
            {
                McdfAccessUtils.CacheLocation = Path.Combine(Path.GetDirectoryName(value + ".poop"), "QuestCache\\");
                McdfAccessUtils.CacheLocation = Path.Combine(Path.GetDirectoryName(_questInstallFolder + ".poop"), "QuestCache\\");
            }
        }
    }

    public Dictionary<string, List<string>> CompletedObjectives { get { return _completedObjectives; } set { _completedObjectives = value; } }

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
