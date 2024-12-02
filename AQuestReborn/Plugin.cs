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
    private IToastGui _toastGui;
    private IGameGui _gameGui;
    private ITextureProvider _textureProvider;
    private bool _screenButtonClicked;

    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    public DialogueBackgroundWindow DialogueBackgroundWindow { get; private set; }
    public ObjectiveWindow ObjectiveWindow { get; private set; }
    public EditorWindow EditorWindow { get; init; }
    public ChoiceWindow ChoiceWindow { get; private set; }
    public DialogueWindow DialogueWindow { get; init; }
    public IClientState ClientState { get => _clientState; set => _clientState = value; }
    public RoleplayingQuestManager RoleplayingQuestManager { get => _roleplayingQuestManager; set => _roleplayingQuestManager = value; }
    public IToastGui ToastGui { get => _toastGui; set => _toastGui = value; }
    public IGameGui GameGui { get => _gameGui; set => _gameGui = value; }
    public ITextureProvider TextureProvider1 { get => _textureProvider; set => _textureProvider = value; }

    public Plugin(IClientState clientState, IFramework framework, IToastGui toastGui, ITextureProvider textureProvider, IGameGui gameGui)
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
        DialogueBackgroundWindow.ButtonClicked += DialogueBackgroundWindow_buttonClicked;
        ObjectiveWindow.OnSelectionAttempt += DialogueBackgroundWindow_buttonClicked;
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(EditorWindow);
        WindowSystem.AddWindow(DialogueBackgroundWindow);
        WindowSystem.AddWindow(DialogueWindow);
        WindowSystem.AddWindow(ChoiceWindow);
        WindowSystem.AddWindow(ObjectiveWindow);
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
        _toastGui = toastGui;
        _gameGui = gameGui;
        _textureProvider = textureProvider;
        _roleplayingQuestManager = new RoleplayingQuestManager(Configuration.QuestChains, Configuration.QuestProgression);
        _roleplayingQuestManager.OnQuestTextTriggered += _roleplayingQuestManager_OnQuestTextTriggered;
        _roleplayingQuestManager.LoadMainQuestGameObject(new QuestGameObject(_clientState));
        _framework.Update += _framework_Update;
    }

    private void DialogueBackgroundWindow_buttonClicked(object? sender, EventArgs e)
    {
        _screenButtonClicked = true;
    }

    private void _roleplayingQuestManager_OnQuestTextTriggered(object? sender, QuestDisplayObject e)
    {
        DialogueWindow.IsOpen = true;
        if (e.QuestObjective.QuestText.Count > 0)
        {
            DialogueWindow.NewText(e);
        }
        else
        {
            _toastGui.ShowQuest(e.QuestObjective.Objective,
            new Dalamud.Game.Gui.Toast.QuestToastOptions()
            {
                DisplayCheckmark = e.QuestObjective.ObjectiveStatus == QuestObjective.ObjectiveStatusType.Complete
            });
        }
    }

    private void _framework_Update(IFramework framework)
    {
        if (_pollingTimer.ElapsedMilliseconds > 100)
        {
            ObjectiveWindow.IsOpen = true;
            if ((CheckXboxInput() || CheckDualsenseInput() || _screenButtonClicked))
            {
                _screenButtonClicked = false;
                if (!_waitingForSelectionRelease)
                {
                    if (!DialogueWindow.IsOpen)
                    {
                        _roleplayingQuestManager.ProgressNearestQuest();
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
        if (xboxController == null)
        {
            var controllers = Controller_Wrapper.Controller.GetConnectedControllers();
            if (controllers.Length > 0)
            {
                xboxController = controllers[0];
            }
        }
        if (xboxController != null)
        {
            return xboxController.A;
        }
        return false;
    }
    private bool CheckDualsenseInput()
    {
        if (_dualSense == null)
        {
            var controllers = DualSense.EnumerateControllers();
            if (controllers.Count() > 0)
            {
                _dualSense = controllers.First();
                _dualSense.Acquire();
                _dualSense.BeginPolling(20);
            }
        }
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
