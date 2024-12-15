using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using EmbedIO.Authentication;
using ImGuiNET;
using Lumina.Excel.Sheets;
using RoleplayingQuestCore;

namespace SamplePlugin.Windows;

public class QuestAcceptanceWindow : Window, IDisposable
{
    private RoleplayingQuest _questToDisplay;
    private bool _alreadyLoadingData;
    private byte[] _currentThumbnail;
    private IDalamudTextureWrap _frameToLoad;
    private byte[] _lastLoadedFrame;
    public event EventHandler OnQuestAccepted;
    private Stopwatch _timeSinceLastQuestAccepted = new Stopwatch();
    private float _thumbnailRatio;
    private IDalamudTextureWrap _seperatorSection;
    private IDalamudTextureWrap _rewardSection;
    private IDalamudTextureWrap _biggerBoxSection;
    private IDalamudTextureWrap _topPiece;
    private IDalamudTextureWrap _bottomPiece;
    private IDalamudTextureWrap _sidePiece;
    private IDalamudTextureWrap _sidePieceRepeated;
    private IDalamudTextureWrap _topCornerPiece;
    private IDalamudTextureWrap _bottomCornerPiece;
    private IDalamudTextureWrap _topCenterPiece;
    private object _reward;
    private object _description;
    private object _rating;
    private float _globalScale;
    private byte[] _backgroundFill;
    private IDalamudTextureWrap _backgroundImage;

    public Stopwatch TimeSinceLastQuestAccepted { get => _timeSinceLastQuestAccepted; set => _timeSinceLastQuestAccepted = value; }
    public Plugin Plugin { get; private set; }

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public QuestAcceptanceWindow(Plugin plugin) : base("Quest Details###With a constant ID")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground;
        Plugin = plugin;
        Size = new Vector2(630, 630);
        SizeCondition = ImGuiCond.Always;
        _timeSinceLastQuestAccepted.Start();
        Bitmap background = new Bitmap(1, 1);
        Graphics graphics = Graphics.FromImage(background);
        graphics.Clear(Color.FromArgb(255, 232, 225, 216));
        graphics.Save();
        MemoryStream memoryStream = new MemoryStream();
        background.Save(memoryStream, ImageFormat.Png);
        memoryStream.Position = 0;
        _backgroundFill = memoryStream.ToArray();
    }

    public void Dispose() { }

    public override void PreDraw()
    {
    }

    public override void Draw()
    {
        _globalScale = ImGuiHelpers.GlobalScale * 0.95f;
        string questName = _questToDisplay.QuestName;
        string questReward = AddSpacesToSentence(_questToDisplay.TypeOfReward.ToString(), false);
        string description = _questToDisplay.QuestDescription;
        string thumbnailPath = _questToDisplay.QuestThumbnailPath;
        string contentRating = AddSpacesToSentence(_questToDisplay.ContentRating.ToString(), false);
        CheckImageAssets();
        if (_backgroundImage != null)
        {
            var relativeSize = Size.Value * 0.98f;
            ImGui.SetCursorPos(new Vector2((Size.Value.X / 2) - (relativeSize.X / 2), 0));
            ImGui.Image(_backgroundImage.ImGuiHandle, relativeSize);
        }
        ImGui.SetCursorPosY(50 * _globalScale);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 0, 0, 255));
        TextCentered(questName);
        ImGui.SetCursorPosY(120 * _globalScale);
        if (_seperatorSection != null)
        {
            ImGui.Image(_seperatorSection.ImGuiHandle, new Vector2(Size.Value.X, _seperatorSection.Height));
        }
        if (_frameToLoad != null)
        {
            var thumbnailSize = new Vector2(_thumbnailRatio * 200, 200) * _globalScale;
            ImGui.SetCursorPosX((Size.Value.X / 2) - (thumbnailSize.X / 2));
            ImGui.Image(_frameToLoad.ImGuiHandle, thumbnailSize);
        }
        ImGui.SetWindowFontScale(1.5f);
        ImGui.SetCursorPosX(50 * _globalScale);
        ImGui.LabelText("", "Reward: " + questReward);
        ImGui.SetCursorPosX(50 * _globalScale);
        ImGui.LabelText("", "Description: ");
        ImGui.SetCursorPosX(50 * _globalScale);
        ImGui.TextWrapped(description);
        ImGui.SetCursorPosX(50 * _globalScale);
        ImGui.LabelText("", "Content Rating: " + contentRating);

        ImGui.SetCursorPosY(560 * _globalScale);
        if (_seperatorSection != null)
        {
            ImGui.Image(_seperatorSection.ImGuiHandle, new Vector2(Size.Value.X, _seperatorSection.Height));
        }
        ImGui.SetCursorPosY(580 * _globalScale);
        ImGui.SetWindowFontScale(2);
        ImGui.SetCursorPosX(220 * _globalScale);
        if (ImGui.Button("Accept"))
        {
            _timeSinceLastQuestAccepted.Restart();
            _questToDisplay.HasQuestAcceptancePopup = false;
            OnQuestAccepted?.Invoke(this, EventArgs.Empty);
            IsOpen = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Decline"))
        {
            IsOpen = false;
        }
        ImGui.PopStyleColor();
        DrawFrameBorders();
    }
    private void DrawFrameBorders()
    {
        //if (_rewardSection == null)
        //{
        //    _rewardSection = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.RewardSection);
        //}
        //if (_biggerBoxSection == null)
        //{
        //    _biggerBoxSection = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.BiggerBoxSection);
        //}
        if (_topCenterPiece != null)
        {
            var relativeScaling = _topCenterPiece.Size * _globalScale;
            ImGui.SetCursorPos(new Vector2((Size.Value.X / 2) - (relativeScaling.X / 2), 0));
            ImGui.Image(_topCenterPiece.ImGuiHandle, relativeScaling);
        }
        if (_topPiece != null)
        {
            var relativeScaling = _topPiece.Size * _globalScale;
            var pieceSize = new Vector2(Size.Value.X, relativeScaling.Y);
            ImGui.SetCursorPos(new Vector2((Size.Value.X / 2) - (pieceSize.X / 2), 0));
            ImGui.Image(_topPiece.ImGuiHandle, new Vector2(Size.Value.X, pieceSize.Y));
        }
        if (_sidePieceRepeated != null)
        {
            var relativeScaling = _sidePieceRepeated.Size * _globalScale;
            ImGui.SetCursorPos(new Vector2(0, 0));
            ImGui.Image(_sidePieceRepeated.ImGuiHandle, new Vector2(relativeScaling.X, Size.Value.Y - (20 * _globalScale)));

            ImGui.SetCursorPos(new Vector2(Size.Value.X - relativeScaling.X, 0));
            ImGui.Image(_sidePieceRepeated.ImGuiHandle, new Vector2(relativeScaling.X, Size.Value.Y - (20 * _globalScale)), new Vector2(1, 0), new Vector2(0, 1));
        }
        if (_sidePiece != null)
        {
            var relativeScaling = _sidePiece.Size * _globalScale;
            ImGui.SetCursorPos(new Vector2(0, 83 * _globalScale));
            ImGui.Image(_sidePiece.ImGuiHandle, relativeScaling);

            ImGui.SetCursorPos(new Vector2(Size.Value.X - relativeScaling.X, 83 * _globalScale));
            ImGui.Image(_sidePiece.ImGuiHandle, relativeScaling, new Vector2(1, 0), new Vector2(0, 1));
        }
        if (_bottomPiece != null)
        {
            var relativeScaling = _bottomPiece.Size * _globalScale;
            var pieceSize = new Vector2(Size.Value.X, relativeScaling.Y);
            ImGui.SetCursorPos(new Vector2((Size.Value.X / 2) - (pieceSize.X / 2), Size.Value.Y - relativeScaling.Y));
            ImGui.Image(_bottomPiece.ImGuiHandle, pieceSize);
        }
        if (_topCornerPiece != null)
        {
            var relativeScaling = _topCornerPiece.Size * _globalScale;
            ImGui.SetCursorPos(new Vector2(0, 0));
            ImGui.Image(_topCornerPiece.ImGuiHandle, relativeScaling);

            ImGui.SetCursorPos(new Vector2(Size.Value.X - relativeScaling.X, 0));
            ImGui.Image(_topCornerPiece.ImGuiHandle, relativeScaling, new Vector2(1, 0), new Vector2(0, 1));
        }
        if (_bottomCornerPiece != null)
        {
            var relativeScaling = _bottomCornerPiece.Size * _globalScale;
            ImGui.SetCursorPos(new Vector2(0, Size.Value.Y - relativeScaling.Y));
            ImGui.Image(_bottomCornerPiece.ImGuiHandle, relativeScaling);

            ImGui.SetCursorPos(new Vector2(Size.Value.X - relativeScaling.X, Size.Value.Y - relativeScaling.Y));
            ImGui.Image(_bottomCornerPiece.ImGuiHandle, relativeScaling, new Vector2(1, 0), new Vector2(0, 1));
        }
    }
    void TextCentered(string text)
    {
        var windowWidth = ImGui.GetWindowSize().X;
        var textWidth = ImGui.CalcTextSize(text).X;
        ImGui.SetWindowFontScale(2f);
        ImGui.SetCursorPosX((windowWidth - textWidth) * 0.5f);
        ImGui.Text(text);
    }
    private void CheckImageAssets()
    {
        if (!_alreadyLoadingData)
        {
            Task.Run(async () =>
        {
            _alreadyLoadingData = true;
            if (_currentThumbnail != null)
            {
                if (_lastLoadedFrame != _currentThumbnail)
                {
                    _frameToLoad = await Plugin.TextureProvider.CreateFromImageAsync(_currentThumbnail);
                    _lastLoadedFrame = _currentThumbnail;
                }
            }
            if (_reward == null)
            {
                _reward = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.Icons[2]);
            }
            if (_description == null)
            {
                _description = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.Icons[3]);
            }
            if (_rating == null)
            {
                _rating = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.Icons[0]);
            }
            if (_seperatorSection == null)
            {
                _seperatorSection = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.SeperatorSection);
            }
            if (_rewardSection == null)
            {
                _rewardSection = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.RewardSection);
            }
            if (_biggerBoxSection == null)
            {
                _biggerBoxSection = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.BiggerBoxSection);
            }
            if (_topPiece == null)
            {
                _topPiece = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.TopPiece);
            }
            if (_bottomPiece == null)
            {
                _bottomPiece = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.BottomPiece);
            }
            if (_sidePiece == null)
            {
                _sidePiece = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.SidePiece);
            }
            if (_sidePieceRepeated == null)
            {
                _sidePieceRepeated = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.SidePieceRepeated);
            }
            if (_topCornerPiece == null)
            {
                _topCornerPiece = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.TopCornerPiece);
            }
            if (_bottomCornerPiece == null)
            {
                _bottomCornerPiece = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.BottomCornerPiece);
            }
            if (_backgroundImage == null)
            {
                _backgroundImage = await Plugin.TextureProvider.CreateFromImageAsync(_backgroundFill);
            }
            //if (_topCenterPiece == null)
            //{
            //    _topCenterPiece = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.TopCenterPiece);
            //}
            _alreadyLoadingData = false;
        });
        }
    }
    string AddSpacesToSentence(string text, bool preserveAcronyms)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        StringBuilder newText = new StringBuilder(text.Length * 2);
        newText.Append(text[0]);
        for (int i = 1; i < text.Length; i++)
        {
            if (char.IsUpper(text[i]))
                if ((text[i - 1] != ' ' && !char.IsUpper(text[i - 1])) ||
                    (preserveAcronyms && char.IsUpper(text[i - 1]) &&
                     i < text.Length - 1 && !char.IsUpper(text[i + 1])))
                    newText.Append(' ');
            newText.Append(text[i]);
        }
        return newText.ToString();
    }
    public void PromptQuest(RoleplayingQuest quest)
    {
        _questToDisplay = quest;
        string thumbnailPath = Path.Combine(quest.FoundPath, _questToDisplay.QuestThumbnailPath);
        SetThumbnail(thumbnailPath);
        IsOpen = true;
    }
    public void SetThumbnail(string path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            MemoryStream thumbnail = new MemoryStream();
            Bitmap thumbnailBitmap = new Bitmap(path);
            _thumbnailRatio = (float)thumbnailBitmap.Width / (float)thumbnailBitmap.Height;
            thumbnailBitmap.Save(thumbnail, ImageFormat.Png);
            thumbnail.Position = 0;
            _currentThumbnail = thumbnail.ToArray();
        }
        else
        {
            _currentThumbnail = null;
        }
    }
}
