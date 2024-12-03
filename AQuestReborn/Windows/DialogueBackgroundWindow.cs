using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using RoleplayingQuestCore;
using static FFXIVClientStructs.FFXIV.Component.GUI.AtkComponentButton.Delegates;

namespace SamplePlugin.Windows;

public class DialogueBackgroundWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;
    QuestDisplayObject questDisplayObject;
    int _index = 0;
    private bool _settingNewText;
    int _currentCharacter = 0;
    string _targetText = "";
    string _currentText = "";
    string _currentName = "";
    Stopwatch textTimer = new Stopwatch();
    private bool _choicesAreNext;
    byte[] emptyBackground;
    byte[] _currentBackground;
    private bool _alreadyLoadingFrame;

    private ITextureProvider _textureProvider;
    private IDalamudTextureWrap _frameToLoad;
    private byte[] _lastLoadedFrame;

    public event EventHandler ButtonClicked;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public DialogueBackgroundWindow(Plugin plugin, ITextureProvider textureProvider)
        : base("Dialogue Background Window##dialoguewindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground)
    {
        Size = new Vector2(600, 200);
        Plugin = plugin;
        MemoryStream blank = new MemoryStream();
        Bitmap none = new Bitmap(1, 1);
        Graphics graphics = Graphics.FromImage(none);
        graphics.Clear(Color.Transparent);
        none.Save(blank, ImageFormat.Png);
        blank.Position = 0;
        emptyBackground = blank.ToArray();
        _currentBackground = emptyBackground;
        _textureProvider = textureProvider;
    }

    public QuestDisplayObject QuestTexts { get => questDisplayObject; set => questDisplayObject = value; }

    public void Dispose() { }

    public override void Draw()
    {
        Size = new Vector2(ImGui.GetMainViewport().Size.X, ImGui.GetMainViewport().Size.Y);
        Position = new Vector2(0, 0);
        if (!_alreadyLoadingFrame)
        {
            Task.Run(async () =>
            {
                _alreadyLoadingFrame = true;
                if (_lastLoadedFrame != _currentBackground)
                {
                    _frameToLoad = await _textureProvider.CreateFromImageAsync(_currentBackground);
                    _lastLoadedFrame = _currentBackground;
                }
                _alreadyLoadingFrame = false;
            });
        }
        var values = ImGui.GetIO().MouseDown;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i])
            {
                ButtonClicked?.Invoke(this, EventArgs.Empty);
            }
        }
        if (_frameToLoad != null)
        {
            ImGui.Image(_frameToLoad.ImGuiHandle, new Vector2(Size.Value.X, Size.Value.Y));/*))*/
        }
        if (!Plugin.DialogueWindow.IsOpen)
        {
            IsOpen = false;
        }
    }
    public void SetBackground(string path)
    {
        MemoryStream background = new MemoryStream();
        Bitmap none = new Bitmap(path);
        background.Position = 0;
        _currentBackground = background.ToArray();
    }
    public void ClearBackground()
    {
        _currentBackground = emptyBackground;
    }
}
