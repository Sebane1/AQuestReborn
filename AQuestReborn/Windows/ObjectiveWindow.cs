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
using FFXIVLooseTextureCompiler.ImageProcessing;

namespace SamplePlugin.Windows;

public class ObjectiveWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;
    private FileDialogManager _fileDialogManager;
    private byte[] emptyBackground;
    private bool _alreadyLoadingQuestStartIcon;
    private IDalamudTextureWrap _questStartIconTextureWrap;
    private byte[] _lastQuestStartIconData;
    private ImGuiWindowFlags _hoverFlags;
    private ImGuiWindowFlags _defaultFlags;
    private RangeAccessor<bool> mouseDownValues;
    private bool _mouseDistanceIsCloseToObjective;
    private byte[] _questStartIconData;
    private byte[] _questObjectiveIconData;
    private bool _alreadyLoadingQuestObjectiveIcon;
    private byte[] _lastQuestStartObjectiveData;
    private IDalamudTextureWrap _questObjectiveIconTextureWrap;

    public event EventHandler OnSelectionAttempt;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public ObjectiveWindow(Plugin plugin)
        : base("Objective Display##mainwindow", ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground)
    {
        Plugin = plugin;
        AllowClickthrough = true;
        _defaultFlags = ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground;
        _hoverFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground;
        Flags = _defaultFlags;
        LoadQuestIcons();
    }
    private void LoadQuestIcons()
    {
        // Quest start icon
        var data1 = Plugin.DataManager.GetFile("ui/icon/061000/061411_hr1.tex");
        // Quest complete icon
        var data2 = Plugin.DataManager.GetFile("ui/icon/061000/061421_hr1.tex");

        MemoryStream questStartIcon = new MemoryStream();
        Grayscale.MakeGrayscale(TexIO.TexToBitmap(new MemoryStream(data1.Data))).Save(questStartIcon, ImageFormat.Png);

        MemoryStream questObjectiveIcon = new MemoryStream();
        Grayscale.MakeGrayscale(TexIO.TexToBitmap(new MemoryStream(data2.Data))).Save(questObjectiveIcon, ImageFormat.Png);

        questStartIcon.Position = 0;
        questObjectiveIcon.Position = 0;
        _questStartIconData = questStartIcon.ToArray();
        _questObjectiveIconData = questObjectiveIcon.ToArray();
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
        if (!_alreadyLoadingQuestStartIcon)
        {
            Task.Run(async () =>
            {
                _alreadyLoadingQuestStartIcon = true;
                if (_lastQuestStartIconData != _questStartIconData)
                {
                    if (_questStartIconData != null)
                    {
                        _questStartIconTextureWrap = await Plugin.TextureProvider.CreateFromImageAsync(_questStartIconData);
                    }
                    _lastQuestStartIconData = _questStartIconData;
                }
                _alreadyLoadingQuestStartIcon = false;
            });
        }
        if (!_alreadyLoadingQuestObjectiveIcon)
        {
            Task.Run(async () =>
            {
                _alreadyLoadingQuestObjectiveIcon = true;
                if (_lastQuestStartObjectiveData != _questObjectiveIconData)
                {
                    if (_questObjectiveIconData != null)
                    {
                        _questObjectiveIconTextureWrap = await Plugin.TextureProvider.CreateFromImageAsync(_questObjectiveIconData);
                    }
                    _lastQuestStartObjectiveData = _questObjectiveIconData;
                }
                _alreadyLoadingQuestObjectiveIcon = false;
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
                Vector3 offset = new Vector3();
                switch (item.Value.Item2.TypeOfQuestPoint)
                {
                    case RoleplayingQuestCore.QuestObjective.QuestPointType.NPC:
                        offset = new Vector3(0, 2.5f, 0);
                        break;
                    case RoleplayingQuestCore.QuestObjective.QuestPointType.GroundItem:
                        // To do: Display something unique?
                        break;
                    case RoleplayingQuestCore.QuestObjective.QuestPointType.StandAndWait:
                        // To do: Display something unique?
                        break;
                }
                Plugin.GameGui.WorldToScreen(item.Value.Item2.Coordinates + offset, out screenPosition, out inView);
                if (inView)
                {
                    if (_questStartIconTextureWrap != null)
                    {
                        var value = ImGui.GetIO().MousePos;
                        var distance = Vector2.Distance(new Vector2(screenPosition.X / Size.Value.X, 0),
                            new Vector2(value.X / Size.Value.X, 0));
                        var playerDistance = Vector3.Distance(Plugin.ClientState.LocalPlayer.Position, item.Value.Item2.Coordinates);
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
                        var iconDimensions = new Vector2(100, 100);
                        ImGui.SetCursorPos(new Vector2(screenPosition.X - (iconDimensions.X / 2), screenPosition.Y - (iconDimensions.Y / 2)));
                        if (_questStartIconTextureWrap != null && _questObjectiveIconTextureWrap != null)
                        {
                            ImGui.Image(item.Value.Item1 == 0 ? _questStartIconTextureWrap.ImGuiHandle : _questObjectiveIconTextureWrap.ImGuiHandle, iconDimensions);
                        }
                    }
                }
            }
        }
    }

    public void Dispose()
    {

    }
}
