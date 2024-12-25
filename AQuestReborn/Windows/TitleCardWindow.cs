using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Lumina.Excel.Sheets;
using NAudio.Wave;
using RoleplayingMediaCore;
using RoleplayingQuestCore;
using RoleplayingVoiceDalamudWrapper;
using static FFXIVClientStructs.FFXIV.Client.System.Scheduler.Resource.SchedulerResource;
using static FFXIVClientStructs.FFXIV.Component.GUI.AtkComponentButton.Delegates;

namespace SamplePlugin.Windows;

public class TitleCardWindow : Window, IDisposable
{
    private Plugin Plugin;
    QuestDisplayObject questDisplayObject;
    int _index = 0;
    private bool _settingNewText;
    int _currentCharacter = 0;
    Stopwatch textTimer = new Stopwatch();

    MediaManager _mediaManager;

    private ITextureProvider _textureProvider;
    private DummyObject _dummyObject;
    private IDalamudTextureWrap _frameToLoad;
    private byte[] _lastLoadedFrame;
    private bool taskAlreadyRunning;
    private float titleCardRatio = 1.777777777777778f;
    private bool _isPortrait = false;
    private byte[] _titleCardImage;


    Stopwatch titleTimer = new Stopwatch();
    private byte[] _questStartImage;
    private byte[] _questEndImage;
    private bool _alreadyLoadingFrame;
    private bool _wasClosed;
    private MemoryStream _questStartSound;
    private MemoryStream _questEndSound;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public TitleCardWindow(Plugin plugin, ITextureProvider textureProvider)
        : base("Title Card Window##dialoguewindow", ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground, true)
    {
        //Size = new Vector2(600, 200);
        Plugin = plugin;
        InitializePlaceholders();
        _textureProvider = textureProvider;
        _dummyObject = new DummyObject();
    }

    private void InitializePlaceholders()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string resourceName = assembly.GetManifestResourceNames().Single(str => str.EndsWith("questStart.png"));
        var questImage = assembly.GetManifestResourceStream(resourceName);
        var questStart = new MemoryStream();
        questImage.CopyTo(questStart);

        _questStartImage = questStart.ToArray();

        assembly = Assembly.GetExecutingAssembly();
        resourceName = assembly.GetManifestResourceNames().Single(str => str.EndsWith("questEnd.png"));
        questImage = assembly.GetManifestResourceStream(resourceName);
        var questEnd = new MemoryStream();
        questImage.CopyTo(questEnd);

        _questEndImage = questEnd.ToArray();
    }

    private void InitializeSound()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string resourceName = assembly.GetManifestResourceNames().Single(str => str.EndsWith("QuestStart.mp3"));
        var questSound = assembly.GetManifestResourceStream(resourceName);
        _questStartSound = new MemoryStream();
        questSound.CopyTo(_questStartSound);
        _questStartSound.Position = 0;

        assembly = Assembly.GetExecutingAssembly();
        resourceName = assembly.GetManifestResourceNames().Single(str => str.EndsWith("QuestEnd.mp3"));
        questSound = assembly.GetManifestResourceStream(resourceName);
        _questEndSound = new MemoryStream();
        questSound.CopyTo(_questEndSound);
        _questEndSound.Position = 0;
    }

    public QuestDisplayObject QuestTexts { get => questDisplayObject; set => questDisplayObject = value; }
    public MediaManager MediaManager { get => _mediaManager; set => _mediaManager = value; }

    public void Dispose() { }
    public override void OnClose()
    {
        _wasClosed = true;
        ClearBackground();
        base.OnClose();
    }
    public override void Draw()
    {
        var displaySize = ImGui.GetIO().DisplaySize;
        Size = displaySize * 1.1f;
        Position = new Vector2((displaySize.X / 2) - (Size.Value.X / 2), (displaySize.Y / 2) - (Size.Value.Y / 2));
        ImageFileDisplay();
    }

    private void ImageFileDisplay()
    {
        var displaySize = Size.Value;
        if (!_alreadyLoadingFrame)
        {
            Task.Run(async () =>
            {
                _alreadyLoadingFrame = true;
                if (_lastLoadedFrame != _titleCardImage)
                {
                    _frameToLoad = await _textureProvider.CreateFromImageAsync(_titleCardImage);
                    _lastLoadedFrame = _titleCardImage;
                }
                _alreadyLoadingFrame = false;
            });
        }
        if (_frameToLoad != null)
        {
            Vector2 scaledSize = new Vector2(displaySize.Y * titleCardRatio, displaySize.Y);
            ImGui.SetCursorPos(new Vector2((displaySize.X / 2) - (scaledSize.X / 2), (displaySize.Y / 2) - (scaledSize.Y / 2)));
            ImGui.Image(_frameToLoad.ImGuiHandle, scaledSize);
        }
        if (titleTimer.ElapsedMilliseconds > 4000)
        {
            IsOpen = false;
        }
    }

    public void DisplayCard(string imagePath = "", string soundPath = "", bool isEnd = false)
    {
        if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
        {
            MemoryStream background = new MemoryStream();
            Bitmap newImage = new Bitmap(imagePath);
            titleCardRatio = (float)newImage.Width / newImage.Height;
            newImage.Save(background, ImageFormat.Png);
            background.Position = 0;
            _titleCardImage = background.ToArray();
        }
        else if (isEnd)
        {
            _titleCardImage = _questEndImage;
        }
        else
        {
            _titleCardImage = _questStartImage;
        }
        if (!string.IsNullOrEmpty(soundPath) && File.Exists(soundPath))
        {
            Plugin.MediaManager.PlayMedia(_dummyObject, soundPath, SoundType.MainPlayerVoice, true);
        }
        else
        {
            InitializeSound();
            Plugin.MediaManager.PlayAudioStream(_dummyObject,
            new Mp3FileReader(!isEnd ? _questStartSound : _questEndSound), SoundType.MainPlayerVoice, false, false, 1, 0, false, null, null, 1, 2f);
        }
        IsOpen = true;
        titleTimer.Restart();
    }

    public void ClearBackground()
    {
        _titleCardImage = null;
        Plugin.MediaManager?.StopAudio(_dummyObject);
    }
}
