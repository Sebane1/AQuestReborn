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
using Lumina.Excel.Sheets;
using RoleplayingMediaCore;
using RoleplayingQuestCore;
using RoleplayingVoiceDalamudWrapper;
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
    byte[] _emptyBackground;
    byte[] _currentBackground;
    private bool _alreadyLoadingFrame;

    MediaManager _mediaManager;

    private ITextureProvider _textureProvider;
    private DummyObject _dummyObject;
    private ImGuiWindowFlags _rightClick;
    private ImGuiWindowFlags _defaultFlags;
    private IDalamudTextureWrap _frameToLoad;
    private byte[] _lastLoadedFrame;
    private bool taskAlreadyRunning;
    private QuestEvent.EventBackgroundType _currentBackgroundType;
    private bool _videoWasPlaying;
    private bool _videoNeedsToPlay;
    private bool _wasClosed;
    private float _currentBackgroundAspectRatio = 0.5625f;
    private bool _isPortrait = false;
    private byte[] _blackBars;
    private IDalamudTextureWrap _blackBarsFrame;
    private bool _rightClickDown;

    public event EventHandler ButtonClicked;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public DialogueBackgroundWindow(Plugin plugin, ITextureProvider textureProvider)
        : base("Dialogue Background Window##dialoguewindow", ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground, true)
    {
        //Size = new Vector2(600, 200);
        Plugin = plugin;
        InitializePlaceholders();
        _currentBackground = _emptyBackground;
        _textureProvider = textureProvider;
        _dummyObject = new DummyObject();
        _rightClick = ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar
    | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground;
        _defaultFlags = ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground;
    }

    private void InitializePlaceholders()
    {
        MemoryStream memoryData = new MemoryStream();
        Bitmap none = new Bitmap(1, 1);
        Graphics graphics = Graphics.FromImage(none);
        graphics.Clear(Color.Transparent);
        none.Save(memoryData, ImageFormat.Png);
        memoryData.Position = 0;
        _emptyBackground = memoryData.ToArray();

        memoryData = new MemoryStream();
        Bitmap blackBars = new Bitmap(1, 1);
        graphics = Graphics.FromImage(blackBars);
        graphics.Clear(Color.Black);
        blackBars.Save(memoryData, ImageFormat.Png);
        memoryData.Position = 0;
        _blackBars = memoryData.ToArray();
    }

    public QuestDisplayObject QuestTexts { get => questDisplayObject; set => questDisplayObject = value; }
    public MediaManager MediaManager { get => _mediaManager; set => _mediaManager = value; }

    public void Dispose() { }
    public override void OnClose()
    {
        if (!_videoWasPlaying)
        {
            _wasClosed = true;
            ClearBackground();
            base.OnClose();
        }
        else
        {
            IsOpen = true;
        }
    }
    public override void PreDraw()
    {
        base.PreDraw();
        if (_rightClickDown)
        {
            Flags = _rightClick;
        }
        else
        {
            Flags = _defaultFlags;
        }
    }
    public override void Draw()
    {
        var displaySize = ImGui.GetIO().DisplaySize;
        Size = displaySize * 1.1f;
        Position = new Vector2((displaySize.X / 2) - (Size.Value.X / 2), (displaySize.Y / 2) - (Size.Value.Y / 2));
        switch (_currentBackgroundType)
        {
            case QuestEvent.EventBackgroundType.None:
                _currentBackground = _emptyBackground;
                ImageFileDisplay();
                break;
            case QuestEvent.EventBackgroundType.Image:
            case QuestEvent.EventBackgroundType.ImageTransparent:
                ImageFileDisplay();
                break;
            case QuestEvent.EventBackgroundType.Video:
                VideoFilePlayback();
                break;
        }
    }

    public void CheckMouseDown(bool doClick = false)
    {
        if (!Plugin.RewardWindow.IsOpen)
        {
            var values = ImGui.GetIO().MouseDown;
            if (values[0] || doClick)
            {
                ButtonClicked?.Invoke(this, EventArgs.Empty);
            }
            if (values[1] || doClick)
            {
                _rightClickDown = true;
            }
        }
    }

    private void ImageFileDisplay()
    {
        var displaySize = Size.Value;
        CheckMouseDown();
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
                if (_blackBarsFrame == null)
                {
                    _blackBarsFrame = await _textureProvider.CreateFromImageAsync(_blackBars);
                }
                _alreadyLoadingFrame = false;
            });
        }
        if (_frameToLoad != null)
        {
            Vector2 scaledSize = new Vector2(displaySize.Y * _currentBackgroundAspectRatio, displaySize.Y);

            if (_blackBarsFrame != null && _currentBackgroundType == QuestEvent.EventBackgroundType.Image)
            {
                ImGui.Image(_blackBarsFrame.ImGuiHandle, displaySize);
            }
            ImGui.SetCursorPos(new Vector2((displaySize.X / 2) - (scaledSize.X / 2), (displaySize.Y / 2) - (scaledSize.Y / 2)));
            ImGui.Image(_frameToLoad.ImGuiHandle, scaledSize);
        }
        if (!Plugin.DialogueWindow.IsOpen)
        {
            IsOpen = false;
        }
    }

    private void VideoFilePlayback()
    {
        var displaySize = Size.Value;
        if (_mediaManager != null && _mediaManager.LastFrame != null && _mediaManager.LastFrame.Length > 0)
        {
            try
            {
                lock (_mediaManager.LastFrame)
                {
                    _videoNeedsToPlay = false;
                    _videoWasPlaying = true;
                    if (!taskAlreadyRunning)
                    {
                        _ = Task.Run(async () =>
                        {
                            taskAlreadyRunning = true;
                            ReadOnlyMemory<byte> bytes = new byte[0];
                            lock (_mediaManager.LastFrame)
                            {
                                bytes = _mediaManager.LastFrame;
                            }

                            if (bytes.Length > 0)
                            {
                                if (_lastLoadedFrame != _mediaManager.LastFrame)
                                {
                                    _frameToLoad = await _textureProvider.CreateFromImageAsync(bytes);
                                    _lastLoadedFrame = _mediaManager.LastFrame;
                                }
                            }
                            taskAlreadyRunning = false;
                        });
                    }
                    if (_frameToLoad != null)
                    {
                        float ratio = (float)1920 / (float)1080;
                        Vector2 scaledSize = new Vector2(displaySize.Y * ratio, displaySize.Y);
                        ImGui.SetCursorPos(new Vector2((displaySize.X / 2) - (scaledSize.X / 2), (displaySize.Y / 2) - (scaledSize.Y / 2)));
                        ImGui.Image(_frameToLoad.ImGuiHandle, scaledSize);
                    }
                }
            }
            catch (Exception e)
            {
            }
        }
        else
        {
            if (!_videoNeedsToPlay)
            {
                CheckMouseDown(_videoWasPlaying);
                _videoWasPlaying = false;
            }
        }
    }

    public void SetBackground(string path, QuestEvent.EventBackgroundType dialogueBackgroundType)
    {
        if (_wasClosed)
        {
            ClearBackground();
            _wasClosed = false;
        }
        _currentBackgroundType = dialogueBackgroundType;
        switch (dialogueBackgroundType)
        {
            case QuestEvent.EventBackgroundType.Image:
            case QuestEvent.EventBackgroundType.ImageTransparent:
                MemoryStream background = new MemoryStream();
                Bitmap newImage = new Bitmap(path);
                _currentBackgroundAspectRatio = (float)newImage.Width / newImage.Height;
                newImage.Save(background, ImageFormat.Png);
                background.Position = 0;
                _currentBackground = background.ToArray();
                break;
            case QuestEvent.EventBackgroundType.Video:
                _videoNeedsToPlay = true;
                Plugin.MediaManager?.PlayMedia(_dummyObject, path, SoundType.NPC, true);
                _videoWasPlaying = false;
                break;
            case QuestEvent.EventBackgroundType.None:
                ClearBackground();
                break;
        }
    }
    public void ClearBackground()
    {
        _currentBackgroundType = QuestEvent.EventBackgroundType.None;
        _currentBackground = _emptyBackground;
        _frameToLoad = null;
        Plugin.MediaManager?.StopAudio(_dummyObject);
    }
}
