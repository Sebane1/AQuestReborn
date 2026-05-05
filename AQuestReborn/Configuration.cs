using AQuestReborn.CustomNpc;
using Dalamud.Configuration;
using FFXIVClientStructs.FFXIV.Common.Lua;
using LanguageConversionProxy;
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
                    AppearanceAccessUtils.CacheLocation = Path.Combine(Path.GetDirectoryName(_questInstallFolder + ".poop"), "QuestCache\\");
                    Directory.CreateDirectory(_questInstallFolder);
                    Directory.CreateDirectory(AppearanceAccessUtils.CacheLocation);
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
    private LanguageEnum _questLanguage = LanguageEnum.English;
    private List<CustomNpcCharacter> _customNpcCharacters = new List<CustomNpcCharacter>();
    private bool _showNpcHitboxes = false;
    private bool _enableControllerInteraction = false;
    private bool _showCustomNameplates = true;
    private bool _enableAmbientChatter = true;

    // AI Provider settings (global for all NPCs)
    private string _aiProvider = "default"; // "default", "openai_compatible", "novelai"
    private string _openAiCompatibleUrl = ""; // Base URL for OpenAI-compatible endpoints
    private string _openAiCompatibleApiKey = "";
    private string _openAiCompatibleModelName = "";
    private string _novelAiApiToken = "";
    private string _novelAiModel = "kayra-v2";
    private string _geminiApiKey = "";
    private string _geminiModel = "gemini-2.0-flash";

    public Dictionary<string, int> QuestProgression { get => _questProgression; set => _questProgression = value; }
    public Dictionary<string, List<string>> CompletedObjectives { get { return _completedObjectives; } set { _completedObjectives = value; } }
    public Dictionary<string, RoleplayingQuest> QuestChains { get => _questChains; set => _questChains = value; }
    public Dictionary<string, Dictionary<string, NpcPartyMember>> NpcPartyMembers { get => _npcPartyMembers; set => _npcPartyMembers = value; }
    public Dictionary<string, PlayerAppearanceData> PlayerAppearances { get => _playerAppearances; set => _playerAppearances = value; }
    public LanguageEnum QuestLanguage { get => _questLanguage; set => _questLanguage = value; }
    public List<CustomNpcCharacter> CustomNpcCharacters { get => _customNpcCharacters; set => _customNpcCharacters = value; }
    public bool ShowNpcHitboxes { get => _showNpcHitboxes; set => _showNpcHitboxes = value; }
    public bool EnableControllerInteraction { get => _enableControllerInteraction; set => _enableControllerInteraction = value; }
    public bool ShowCustomNameplates { get => _showCustomNameplates; set => _showCustomNameplates = value; }
    public bool EnableAmbientChatter { get => _enableAmbientChatter; set => _enableAmbientChatter = value; }

    // AI Provider properties
    public string AiProvider { get => _aiProvider; set => _aiProvider = value; }
    public string OpenAiCompatibleUrl { get => _openAiCompatibleUrl; set => _openAiCompatibleUrl = value; }
    public string OpenAiCompatibleApiKey { get => _openAiCompatibleApiKey; set => _openAiCompatibleApiKey = value; }
    public string OpenAiCompatibleModelName { get => _openAiCompatibleModelName; set => _openAiCompatibleModelName = value; }
    public string NovelAiApiToken { get => _novelAiApiToken; set => _novelAiApiToken = value; }
    public string NovelAiModel { get => _novelAiModel; set => _novelAiModel = value; }
    public string GeminiApiKey { get => _geminiApiKey; set => _geminiApiKey = value; }
    public string GeminiModel { get => _geminiModel; set => _geminiModel = value; }

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
