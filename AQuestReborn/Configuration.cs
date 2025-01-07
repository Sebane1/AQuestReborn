using Dalamud.Configuration;
using FFXIVClientStructs.FFXIV.Common.Lua;
using McdfDataImporter;
using RoleplayingQuestCore;
using System;
using System.Collections.Generic;
using System.IO;

namespace SamplePlugin;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public string QuestInstallFolder
    {
        get
        {
            return _questInstallFolder;
        }
        set
        {
            if (!string.IsNullOrEmpty(value) && (!value.Contains("Program Files")
                || !value.Contains("FINAL FANTASY XIV - A Realm Reborn")))
            {
                _questInstallFolder = value;
                if (!string.IsNullOrEmpty(_questInstallFolder))
                {
                    McdfAccessUtils.CacheLocation = Path.Combine(Path.GetDirectoryName(_questInstallFolder + ".poop"), "QuestCache\\");
                    Directory.CreateDirectory(_questInstallFolder);
                    Directory.CreateDirectory(McdfAccessUtils.CacheLocation);
                }
            }
        }
    }
    private string _questInstallFolder = "";

    private Dictionary<string, RoleplayingQuest> _questChains = new Dictionary<string, RoleplayingQuest>();
    private Dictionary<string, int> _questProgression = new Dictionary<string, int>();
    private Dictionary<string, List<string>> _completedObjectives = new Dictionary<string, List<string>>();
    private Dictionary<string, Dictionary<string, NpcPartyMember>> _npcPartyMembers = new Dictionary<string, Dictionary<string, NpcPartyMember>>();
    private Dictionary<string, PlayerAppearanceData> _playerAppearances = new Dictionary<string, PlayerAppearanceData>();

    public Dictionary<string, int> QuestProgression { get => _questProgression; set => _questProgression = value; }
    public Dictionary<string, List<string>> CompletedObjectives { get { return _completedObjectives; } set { _completedObjectives = value; } }

    public Dictionary<string, RoleplayingQuest> QuestChains { get => _questChains; set => _questChains = value; }
    public Dictionary<string, Dictionary<string, NpcPartyMember>> NpcPartyMembers { get => _npcPartyMembers; set => _npcPartyMembers = value; }
    public Dictionary<string, PlayerAppearanceData> PlayerAppearances { get => _playerAppearances; set => _playerAppearances = value; }

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
