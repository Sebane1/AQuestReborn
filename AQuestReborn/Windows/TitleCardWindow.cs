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
using FFXIVLooseTextureCompiler.ImageProcessing;
using Dalamud.Bindings.ImGui;
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
    private string _soundPath;
    private bool _isEnd;

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
            _alreadyLoadingFrame = true;
            Task.Run(async () =>
            {
                if (IsOpen)
                {
                    try
                    {
                        if (_titleCardImage != null)
                        {
                            if (_lastLoadedFrame != _titleCardImage)
                            {
                                _frameToLoad = await _textureProvider.CreateFromImageAsync(_titleCardImage);
                                _lastLoadedFrame = _titleCardImage;
                                if (!titleTimer.IsRunning)
                                {
                                    if (!string.IsNullOrEmpty(_soundPath) && File.Exists(_soundPath))
                                    {
                                        Plugin.MediaManager.PlayMedia(_dummyObject, _soundPath, SoundType.NPC, true);
                                    }
                                    else
                                    {
                                        InitializeSound();
                                        Plugin.MediaManager.PlayAudioStream(new DummyObject(),
                                        new Mp3FileReader(!_isEnd ? _questStartSound : _questEndSound), SoundType.NPC, false, false, 1, 0, false, null, null, 1, 2f);
                                    }
                                    titleTimer.Restart();
                                }
                            }
                        }
                    }
                    catch
                    {

                    }
                }
                _alreadyLoadingFrame = false;
            });
        }
        if (_frameToLoad != null)
        {
            Vector2 scaledSize = new Vector2(displaySize.Y * titleCardRatio, displaySize.Y);
            ImGui.SetCursorPos(new Vector2((displaySize.X / 2) - (scaledSize.X / 2), (displaySize.Y / 2) - (scaledSize.Y / 2)));
            ImGui.Image(_frameToLoad.Handle, scaledSize);
        }
        if (titleTimer.ElapsedMilliseconds > 4000)
        {
            IsOpen = false;
            titleTimer.Reset();
        }
    }

    public void DisplayCard(string imagePath = "", string soundPath = "", bool isEnd = false)
    {
        Task.Run(() =>
        {
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                MemoryStream background = new MemoryStream();
                Bitmap newImage = TexIO.ResolveBitmap(imagePath);
                titleCardRatio = (float)newImage.Width / newImage.Height;
                TexIO.SaveBitmap(newImage, background);
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
            _soundPath = soundPath;
            _isEnd = isEnd;
            IsOpen = true;
        });
    }

    public void ClearBackground()
    {
        _titleCardImage = null;
        _frameToLoad = null;
        _lastLoadedFrame = null;
    }
}
