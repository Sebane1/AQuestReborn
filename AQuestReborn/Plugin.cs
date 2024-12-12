using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using SamplePlugin.Windows;
using RoleplayingQuestCore;
using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using DualSenseAPI;
using System.Linq;
using Controller_Wrapper;
using System.Diagnostics;
using Dalamud.Game.Gui.Toast;
using RoleplayingMediaCore;
using RoleplayingVoiceDalamudWrapper;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Threading.Tasks;
using Brio.Game.Actor;
using Brio.IPC;
using Lumina.Excel.Sheets;
using System.Threading;
using Anamnesis.GameData;
using EmbedIO.Authentication;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using SharpDX;
using AQuestReborn;
using ArtemisRoleplayingKit;
using AnamCore;

namespace SamplePlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;

    private const string CommandName = "/questreborn";
    private const string CommandName2 = "/questchat";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("A Quest Reborn");
    private IClientState _clientState;
    private IFramework _framework;
    private AnamcoreManager _anamcoreManager;
    private RoleplayingQuestManager _roleplayingQuestManager;
    private IToastGui _toastGui;
    private IGameGui _gameGui;
    private ITextureProvider _textureProvider;
    private IDataManager _dataManager;
    private MediaManager _mediaManager;
    private MediaGameObject _playerObject;
    private unsafe Camera* _camera;
    private MediaCameraObject _playerCamera;
    private IDalamudPluginInterface _dalamudPluginInterface;
    private AQuestReborn.AQuestReborn _aQuestReborn;
    private Brio.Brio _brio;

    private MainWindow MainWindow { get; init; }
    public DialogueBackgroundWindow DialogueBackgroundWindow { get; private set; }
    public ObjectiveWindow ObjectiveWindow { get; private set; }
    public QuestAcceptanceWindow QuestAcceptanceWindow { get; private set; }
    public RewardWindow RewardWindow { get; private set; }
    public EditorWindow EditorWindow { get; init; }
    public ChoiceWindow ChoiceWindow { get; private set; }
    public DialogueWindow DialogueWindow { get; init; }
    public IClientState ClientState { get => _clientState; set => _clientState = value; }
    public RoleplayingQuestManager RoleplayingQuestManager { get => _roleplayingQuestManager; set => _roleplayingQuestManager = value; }
    public IToastGui ToastGui { get => _toastGui; set => _toastGui = value; }
    public IGameGui GameGui { get => _gameGui; set => _gameGui = value; }
    public ITextureProvider TextureProviderReference { get => _textureProvider; set => _textureProvider = value; }
    public MediaManager MediaManager { get => _mediaManager; set => _mediaManager = value; }
    public IDataManager DataManager { get => _dataManager; set => _dataManager = value; }
    public IPluginLog PluginLog { get => _pluginLog; set => _pluginLog = value; }
    public IFramework Framework { get => _framework; set => _framework = value; }
    public EmoteReaderHooks EmoteReaderHook { get => _emoteReaderHook; set => _emoteReaderHook = value; }
    public IDalamudPluginInterface DalamudPluginInterface { get => _dalamudPluginInterface; set => _dalamudPluginInterface = value; }
    internal AQuestReborn.AQuestReborn AQuestReborn { get => _aQuestReborn; set => _aQuestReborn = value; }
    public AnamcoreManager AnamcoreManager { get => _anamcoreManager; set => _anamcoreManager = value; }
    public IGameConfig GameConfig { get => _gameConfig; set => _gameConfig = value; }

    private EmoteReaderHooks _emoteReaderHook;
    private IPluginLog _pluginLog;
    private IGameConfig _gameConfig;

    public Plugin(IClientState clientState, IFramework framework, IToastGui toastGui,
        ITextureProvider textureProvider, IGameGui gameGui, IDalamudPluginInterface dalamudPluginInterface,
        IGameInteropProvider gameInteropProvider, IObjectTable objectTable, IDataManager dataManager, IPluginLog pluginLog, IGameConfig gameConfig)
    {
        _brio = new Brio.Brio(dalamudPluginInterface);
        _clientState = clientState;
        _framework = framework;
        _toastGui = toastGui;
        _gameGui = gameGui;
        _textureProvider = textureProvider;
        _dataManager = dataManager;
        _pluginLog = pluginLog;
        _gameConfig = gameConfig;
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // you might normally want to embed resources and load them from the manifest stream
        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        ChoiceWindow = new ChoiceWindow(this);
        DialogueWindow = new DialogueWindow(this);
        EditorWindow = new EditorWindow(this);
        MainWindow = new MainWindow(this);
        DialogueBackgroundWindow = new DialogueBackgroundWindow(this, textureProvider);
        ObjectiveWindow = new ObjectiveWindow(this);
        QuestAcceptanceWindow = new QuestAcceptanceWindow(this);
        RewardWindow = new RewardWindow(this);

        WindowSystem.AddWindow(EditorWindow);
        WindowSystem.AddWindow(DialogueBackgroundWindow);
        WindowSystem.AddWindow(DialogueWindow);
        WindowSystem.AddWindow(ChoiceWindow);
        WindowSystem.AddWindow(ObjectiveWindow);
        WindowSystem.AddWindow(QuestAcceptanceWindow);
        WindowSystem.AddWindow(RewardWindow);
        WindowSystem.AddWindow(MainWindow);
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens settings."
        });
        CommandManager.AddHandler(CommandName2, new CommandInfo(OnCommandChat)
        {
            HelpMessage = "For chat objectives"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui

        // Adds another button that is doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
        _anamcoreManager = new AnamcoreManager(this);
        _roleplayingQuestManager = new RoleplayingQuestManager(Configuration.QuestChains, Configuration.QuestProgression, Configuration.QuestInstallFolder);
        _emoteReaderHook = new EmoteReaderHooks(gameInteropProvider, _clientState, objectTable);
        _dalamudPluginInterface = dalamudPluginInterface;
        _aQuestReborn = new AQuestReborn.AQuestReborn(this);
    }

    private void OnCommandChat(string command, string arguments)
    {
        if (!DialogueWindow.IsOpen && !ChoiceWindow.IsOpen)
        {
            _roleplayingQuestManager.ProgressTriggerQuestObjective(QuestObjective.ObjectiveTriggerType.SayPhrase, arguments);
        }
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
        _mediaManager.Dispose();
        _brio.Dispose();
        _aQuestReborn.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        // in response to the slash command, just toggle the display status of our main ui
        ToggleMainUI();
    }
    private void DrawUI() => WindowSystem.Draw();
    public void SaveProgress()
    {
        Configuration.QuestProgression = _roleplayingQuestManager.QuestProgression;
        Configuration.QuestChains = _roleplayingQuestManager.QuestChains;
        Configuration.Save();
    }
    public void ToggleMainUI() => MainWindow.Toggle();
}
