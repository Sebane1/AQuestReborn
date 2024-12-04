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

namespace SamplePlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;

    private const string CommandName = "/questreborn";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("A Quest Reborn");
    private IClientState _clientState;
    private IFramework _framework;
    private RoleplayingQuestManager _roleplayingQuestManager;
    private bool _waitingForSelectionRelease;
    private DualSense _dualSense;
    private Controller xboxController;
    private Stopwatch _pollingTimer;
    private Stopwatch _controllerCheckTimer;
    private IToastGui _toastGui;
    private IGameGui _gameGui;
    private ITextureProvider _textureProvider;
    private bool _screenButtonClicked;
    private MediaManager _mediaManager;
    private MediaGameObject _playerObject;
    private unsafe Camera* _camera;
    private MediaCameraObject _playerCamera;
    private IDalamudPluginInterface _dalamudPluginInterface;

    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    public DialogueBackgroundWindow DialogueBackgroundWindow { get; private set; }
    public ObjectiveWindow ObjectiveWindow { get; private set; }
    public QuestAcceptanceWindow QuestAcceptanceWindow { get; private set; }
    public EditorWindow EditorWindow { get; init; }
    public ChoiceWindow ChoiceWindow { get; private set; }
    public DialogueWindow DialogueWindow { get; init; }
    public IClientState ClientState { get => _clientState; set => _clientState = value; }
    public RoleplayingQuestManager RoleplayingQuestManager { get => _roleplayingQuestManager; set => _roleplayingQuestManager = value; }
    public IToastGui ToastGui { get => _toastGui; set => _toastGui = value; }
    public IGameGui GameGui { get => _gameGui; set => _gameGui = value; }
    public ITextureProvider TextureProvider1 { get => _textureProvider; set => _textureProvider = value; }
    public MediaManager MediaManager { get => _mediaManager; set => _mediaManager = value; }

    public Plugin(IClientState clientState, IFramework framework, IToastGui toastGui,
        ITextureProvider textureProvider, IGameGui gameGui, IDalamudPluginInterface dalamudPluginInterface)
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // you might normally want to embed resources and load them from the manifest stream
        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        ConfigWindow = new ConfigWindow(this);
        ChoiceWindow = new ChoiceWindow(this);
        DialogueWindow = new DialogueWindow(this);
        EditorWindow = new EditorWindow(this);
        MainWindow = new MainWindow(this);
        DialogueBackgroundWindow = new DialogueBackgroundWindow(this, textureProvider);
        ObjectiveWindow = new ObjectiveWindow(this);
        QuestAcceptanceWindow = new QuestAcceptanceWindow(this);

        DialogueBackgroundWindow.ButtonClicked += DialogueBackgroundWindow_buttonClicked;
        ObjectiveWindow.OnSelectionAttempt += DialogueBackgroundWindow_buttonClicked;
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(EditorWindow);
        WindowSystem.AddWindow(DialogueBackgroundWindow);
        WindowSystem.AddWindow(DialogueWindow);
        WindowSystem.AddWindow(ChoiceWindow);
        WindowSystem.AddWindow(ObjectiveWindow);
        WindowSystem.AddWindow(QuestAcceptanceWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens settings."
        });

        PluginInterface.UiBuilder.Draw += DrawUI;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;

        // Adds another button that is doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
        _clientState = clientState;
        _framework = framework;
        _pollingTimer = new Stopwatch();
        _pollingTimer.Start();
        _controllerCheckTimer = new Stopwatch();
        _controllerCheckTimer.Start();
        _toastGui = toastGui;
        _gameGui = gameGui;
        _textureProvider = textureProvider;
        _roleplayingQuestManager = new RoleplayingQuestManager(Configuration.QuestChains, Configuration.QuestProgression);
        _roleplayingQuestManager.OnQuestTextTriggered += _roleplayingQuestManager_OnQuestTextTriggered;
        _roleplayingQuestManager.OnQuestStarted += _roleplayingQuestManager_OnQuestStarted;
        _roleplayingQuestManager.OnQuestCompleted += _roleplayingQuestManager_OnQuestCompleted;
        _roleplayingQuestManager.OnObjectiveCompleted += _roleplayingQuestManager_OnObjectiveCompleted;
        _roleplayingQuestManager.OnQuestAcceptancePopup += _roleplayingQuestManager_OnQuestAcceptancePopup;
        _roleplayingQuestManager.LoadMainQuestGameObject(new QuestGameObject(_clientState));
        QuestAcceptanceWindow.OnQuestAccepted += QuestAcceptanceWindow_OnQuestAccepted;
        _dalamudPluginInterface = dalamudPluginInterface;
        _framework.Update += _framework_Update;
        _clientState.Login += _clientState_Login;
        if (_clientState.IsLoggedIn)
        {
            InitializeMediaManager();
        }
    }

    private void _clientState_Login()
    {
        if (_clientState.IsLoggedIn)
        {
            InitializeMediaManager();
        }
    }

    public unsafe void InitializeMediaManager()
    {
        if (_playerObject == null)
        {
            _playerObject = new MediaGameObject(_clientState.LocalPlayer);
        }

        if (_playerCamera != null)
        {
            _playerCamera = new MediaCameraObject(_camera);
        }
        _camera = CameraManager.Instance()->GetActiveCamera();
        _mediaManager = new MediaManager(_playerObject, _playerCamera,
        Path.GetDirectoryName(_dalamudPluginInterface.AssemblyLocation.FullName));
        DialogueBackgroundWindow.MediaManager = _mediaManager;
    }
    private void _roleplayingQuestManager_OnQuestAcceptancePopup(object? sender, RoleplayingQuest e)
    {
        QuestAcceptanceWindow.PromptQuest(e);
    }

    private void QuestAcceptanceWindow_OnQuestAccepted(object? sender, EventArgs e)
    {
        _roleplayingQuestManager.ProgressTriggerQuestObjective();
    }

    private void _roleplayingQuestManager_OnObjectiveCompleted(object? sender, QuestObjective e)
    {
        ToastGui.ShowQuest(e.Objective,
        new Dalamud.Game.Gui.Toast.QuestToastOptions()
        {
            DisplayCheckmark = e.ObjectiveStatus == QuestObjective.ObjectiveStatusType.Complete
        });
    }

    private void _roleplayingQuestManager_OnQuestCompleted(object? sender, EventArgs e)
    {
        QuestToastOptions questToastOptions = new QuestToastOptions();
        ToastGui.ShowQuest("Quest Completed");
        Configuration.Save();
    }

    private void _roleplayingQuestManager_OnQuestStarted(object? sender, EventArgs e)
    {
        ToastGui.ShowQuest("Quest Started");
    }

    private void DialogueBackgroundWindow_buttonClicked(object? sender, EventArgs e)
    {
        _screenButtonClicked = true;
    }

    private void _roleplayingQuestManager_OnQuestTextTriggered(object? sender, QuestDisplayObject e)
    {
        if (e.QuestObjective.QuestText.Count > 0)
        {
            DialogueWindow.IsOpen = true;
            DialogueWindow.NewText(e);
        }
        else
        {
            e.QuestEvents.Invoke(this, EventArgs.Empty);
        }
    }

    private void _framework_Update(IFramework framework)
    {
        if (_controllerCheckTimer.ElapsedMilliseconds > 5000)
        {
            ControllerConnectionCheck();
            _controllerCheckTimer.Restart();
        }
        if (_pollingTimer.ElapsedMilliseconds > 100)
        {
            ObjectiveWindow.IsOpen = true;
            if ((CheckXboxInput() || CheckDualsenseInput() || _screenButtonClicked))
            {
                _screenButtonClicked = false;
                if (!_waitingForSelectionRelease)
                {
                    if (!DialogueWindow.IsOpen && !ChoiceWindow.IsOpen)
                    {
                        _roleplayingQuestManager.ProgressTriggerQuestObjective();
                    }
                    else
                    {
                        DialogueWindow.NextText();
                    }
                    _waitingForSelectionRelease = true;
                }
            }
            else
            {
                _waitingForSelectionRelease = false;
            }
            _pollingTimer.Restart();
        }
    }

    private bool CheckXboxInput()
    {
        if (xboxController != null)
        {
            return xboxController.A;
        }
        return false;
    }

    private void ControllerConnectionCheck()
    {
        if (xboxController == null)
        {
            Task.Run(() =>
            {
                var controllers = Controller_Wrapper.Controller.GetConnectedControllers();
                if (controllers.Length > 0)
                {
                    xboxController = controllers[0];
                }
            });
        }
        if (_dualSense == null)
        {
            Task.Run(() =>
            {
                var controllers = DualSense.EnumerateControllers();
                if (controllers.Count() > 0)
                {
                    _dualSense = controllers.First();
                    _dualSense.Acquire();
                    _dualSense.BeginPolling(20);
                }
            });
        }
    }
    private bool CheckDualsenseInput()
    {
        if (_dualSense != null)
        {
            return _dualSense.InputState.CrossButton;
        }
        return false;
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
        if (_dualSense != null)
        {
            _dualSense.EndPolling();
            _dualSense.Release();
        }
        _mediaManager.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        // in response to the slash command, just toggle the display status of our main ui
        ToggleMainUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();
}
