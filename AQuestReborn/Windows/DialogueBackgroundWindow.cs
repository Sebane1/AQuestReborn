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
    byte[] emptyBackground;
    byte[] _currentBackground;
    private bool _alreadyLoadingFrame;

    MediaManager _mediaManager;

    private ITextureProvider _textureProvider;
    private DummyObject _dummyObject;
    private IDalamudTextureWrap _frameToLoad;
    private byte[] _lastLoadedFrame;
    private bool taskAlreadyRunning;
    private QuestText.DialogueBackgroundType _currentBackgroundType;
    private bool _videoWasPlaying;
    private bool _videoNeedsToPlay;

    public event EventHandler ButtonClicked;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public DialogueBackgroundWindow(Plugin plugin, ITextureProvider textureProvider)
        : base("Dialogue Background Window##dialoguewindow", ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground)
    {
        //Size = new Vector2(600, 200);
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
        _dummyObject = new DummyObject();
    }

    public QuestDisplayObject QuestTexts { get => questDisplayObject; set => questDisplayObject = value; }
    public MediaManager MediaManager { get => _mediaManager; set => _mediaManager = value; }

    public void Dispose() { }
    public override void OnClose()
    {
        if (!_videoWasPlaying)
        {
            ClearBackground();
            base.OnClose();
        }
        else
        {
            IsOpen = true;
        }
    }
    public override void Draw()
    {
        var displaySize = ImGui.GetIO().DisplaySize;
        Size = displaySize * 1.1f;
        Position = new Vector2((displaySize.X / 2) - (Size.Value.X / 2), (displaySize.Y / 2) - (Size.Value.Y / 2));
        switch (_currentBackgroundType)
        {
            case QuestText.DialogueBackgroundType.None:
                _currentBackground = emptyBackground;
                ImageFileDisplay();
                break;
            case QuestText.DialogueBackgroundType.Image:
                ImageFileDisplay();
                break;
            case QuestText.DialogueBackgroundType.Video:
                VideoFilePlayback();
                break;
        }
    }

    public void CheckMouseDown(bool doClick = false)
    {
        var values = ImGui.GetIO().MouseDown;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] || doClick)
            {
                ButtonClicked?.Invoke(this, EventArgs.Empty);
                break;
            }
        }
    }

    private void ImageFileDisplay()
    {
        var displaySize = ImGui.GetIO().DisplaySize;
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
                _alreadyLoadingFrame = false;
            });
        }
        if (_frameToLoad != null)
        {
            ImGui.Image(_frameToLoad.ImGuiHandle, new Vector2(Size.Value.X, Size.Value.Y));
        }
        if (!Plugin.DialogueWindow.IsOpen)
        {
            IsOpen = false;
        }
    }

    private void VideoFilePlayback()
    {
        var displaySize = ImGui.GetIO().DisplaySize;
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
                        ImGui.Image(_frameToLoad.ImGuiHandle, new Vector2(Size.Value.X, Size.Value.Y));
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

    public void SetBackground(string path, QuestText.DialogueBackgroundType dialogueBackgroundType)
    {
        _currentBackgroundType = dialogueBackgroundType;
        switch (dialogueBackgroundType)
        {
            case QuestText.DialogueBackgroundType.Image:
                MemoryStream background = new MemoryStream();
                Bitmap none = new Bitmap(path);
                none.Save(background, ImageFormat.Png);
                background.Position = 0;
                _currentBackground = background.ToArray();
                break;
            case QuestText.DialogueBackgroundType.Video:
                _videoNeedsToPlay = true;
                Plugin.MediaManager?.PlayMedia(_dummyObject, path, SoundType.NPC, true);
                _videoWasPlaying = false;
                break;
            case QuestText.DialogueBackgroundType.None:
                ClearBackground();
                break;
        }
    }
    public void ClearBackground()
    {
        _currentBackground = emptyBackground;
        Plugin.MediaManager?.StopAudio(_dummyObject);
    }
}
