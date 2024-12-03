using System;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Numerics;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace SamplePlugin.Windows;

public class ObjectiveWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;
    private FileDialogManager _fileDialogManager;
    private byte[] emptyBackground;
    private byte[] _currentBackground;
    private bool _alreadyLoadingFrame;
    private IDalamudTextureWrap _frameToLoad;
    private byte[] _lastLoadedFrame;
    private ImGuiWindowFlags _hoverFlags;
    private ImGuiWindowFlags _defaultFlags;
    private RangeAccessor<bool> mouseDownValues;
    private bool _mouseDistanceIsCloseToObjective;

    public event EventHandler OnSelectionAttempt;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public ObjectiveWindow(Plugin plugin)
        : base("Objective Display##mainwindow", ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
        MemoryStream blank = new MemoryStream();
        Bitmap none = new Bitmap(1, 1);
        Graphics graphics = Graphics.FromImage(none);
        graphics.Clear(Color.Red);
        none.Save(blank, ImageFormat.Png);
        blank.Position = 0;
        emptyBackground = blank.ToArray();
        _currentBackground = emptyBackground;
        AllowClickthrough = true;
        _defaultFlags = ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground;
        _hoverFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground;
    }

    public void Dispose() { }

    public void OnOpen()
    {

    }

    public void OnClose()
    {
        IsOpen = true;
    }
    public override void PreDraw()
    {
        base.PreDraw();
        if (_mouseDistanceIsCloseToObjective)
        {
            Flags = _hoverFlags;
        }
        else
        {
            Flags = _defaultFlags;
        }
    }
    public override void Draw()
    {
        mouseDownValues = ImGui.GetIO().MouseDown;
        Size = new Vector2(ImGui.GetMainViewport().Size.X, ImGui.GetMainViewport().Size.Y);
        Position = new Vector2(0, 0);
        if (!_alreadyLoadingFrame)
        {
            Task.Run(async () =>
            {
                _alreadyLoadingFrame = true;
                if (_lastLoadedFrame != _currentBackground)
                {
                    _frameToLoad = await Plugin.TextureProvider.CreateFromImageAsync(_currentBackground);
                    _lastLoadedFrame = _currentBackground;
                }
                _alreadyLoadingFrame = false;
            });
        }
        if (!Plugin.DialogueWindow.IsOpen && !Plugin.ChoiceWindow.IsOpen)
        {
            var questChains = Plugin.RoleplayingQuestManager.GetActiveQuestChainObjectives(Plugin.ClientState.TerritoryType);
            _mouseDistanceIsCloseToObjective = false;
            foreach (var item in questChains)
            {
                Vector2 screenPosition = new Vector2();
                bool inView = false;
                Plugin.GameGui.WorldToScreen(item.Coordinates + new Vector3(0, 2.5f, 0), out screenPosition, out inView);
                if (inView)
                {
                    if (_frameToLoad != null)
                    {
                        var value = ImGui.GetIO().MousePos;
                        var distance = Vector2.Distance(new Vector2(screenPosition.X / Size.Value.X, 0),
                            new Vector2(value.X / Size.Value.X, 0));
                        var playerDistance = Vector3.Distance(Plugin.ClientState.LocalPlayer.Position, item.Coordinates);
                        if (distance < 0.1f && playerDistance < Plugin.RoleplayingQuestManager.MinimumDistance)
                        {
                            _mouseDistanceIsCloseToObjective = true;
                            for (int i = 0; i < mouseDownValues.Count; i++)
                            {
                                if (mouseDownValues[i])
                                {
                                    OnSelectionAttempt?.Invoke(this, EventArgs.Empty);
                                    _mouseDistanceIsCloseToObjective = false;
                                    break;
                                }
                            }
                        }
                        ImGui.SetCursorPos(screenPosition);
                        ImGui.Image(_frameToLoad.ImGuiHandle, new Vector2(50, 50));
                    }
                }
            }
        }
    }
}
