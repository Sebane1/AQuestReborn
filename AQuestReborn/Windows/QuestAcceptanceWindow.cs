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
    private float _globalScale;
    private byte[] _backgroundFill;


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
        Size = new Vector2(450, 450);
        SizeCondition = ImGuiCond.Always;
        _timeSinceLastQuestAccepted.Start();

    }

    public void Dispose() { }

    public override void PreDraw()
    {
    }

    public override void Draw()
    {
        _globalScale = ImGuiHelpers.GlobalScale;
        string questName = _questToDisplay.QuestName;
        string questReward = AddSpacesToSentence(_questToDisplay.TypeOfReward.ToString(), false);
        string description = _questToDisplay.QuestDescription;
        string thumbnailPath = _questToDisplay.QuestThumbnailPath;
        string contentRating = AddSpacesToSentence(_questToDisplay.ContentRating.ToString(), false);
        if (_currentThumbnail != null)
        {
            Size = new Vector2(450, 520) * _globalScale;
        }
        else
        {
            Size = new Vector2(450, 320) * _globalScale;
        }
        Plugin.UiAtlasManager.CheckImageAssets();
        Plugin.UiAtlasManager.DrawBackground(Size.Value);
        ImGui.SetCursorPosY(40 * _globalScale);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 0, 0, 255));
        Plugin.UiAtlasManager.TextCentered(questName, Size.Value);
        ImGui.PopStyleColor();
        ImGui.SetCursorPosY(90 * _globalScale);
        Plugin.UiAtlasManager.DrawSeperator(Size.Value.X);
        if (_currentThumbnail != null)
        {
            if (!_alreadyLoadingData)
            {
                Task.Run(async () =>
            {
                _alreadyLoadingData = true;
                if (_lastLoadedFrame != _currentThumbnail)
                {
                    _frameToLoad = await Plugin.TextureProvider.CreateFromImageAsync(_currentThumbnail);
                    _lastLoadedFrame = _currentThumbnail;
                }
                _alreadyLoadingData = false;
            });
            }
            if (_frameToLoad != null)
            {
                var thumbnailSize = new Vector2(_thumbnailRatio * 200, 200) * _globalScale;
                ImGui.SetCursorPosX((Size.Value.X / 2) - (thumbnailSize.X / 2));
                ImGui.Image(_frameToLoad.ImGuiHandle, thumbnailSize);
            }
        }
        var value = new Vector2(30, 30) * _globalScale;
        float offset = 30f;
        ImGui.SetWindowFontScale(1.5f);

        ImGui.SetCursorPosX(offset * _globalScale);
        Plugin.UiAtlasManager.DrawRatingImage(value);
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 0, 0, 255));
        ImGui.LabelText("", "Content Rating: " + contentRating);
        ImGui.PopStyleColor();

        ImGui.SetCursorPosX(offset * _globalScale);
        Plugin.UiAtlasManager.DrawRewardImage(value);
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 0, 0, 255));
        ImGui.LabelText("", "Reward: " + questReward);
        ImGui.PopStyleColor();
        ImGui.SetCursorPosX(offset * _globalScale);
        Plugin.UiAtlasManager.DrawDescriptionImage(value);
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 0, 0, 255));
        ImGui.LabelText("", "Description: ");
        ImGui.PopStyleColor();
        //ImGui.SetCursorPosX((offset + 40) * _globalScale);
        var tableOffset = (offset + 40);
        ImGui.SetCursorPosX(0);
        ImGui.BeginTable("##Dialogue Table", 3);
        ImGui.TableSetupColumn("Padding 1", ImGuiTableColumnFlags.WidthFixed, tableOffset * _globalScale);
        ImGui.TableSetupColumn("Center", ImGuiTableColumnFlags.WidthFixed, (Size.Value.X - (tableOffset * 2)) * _globalScale);
        ImGui.TableSetupColumn("Padding 2", ImGuiTableColumnFlags.WidthFixed, tableOffset * _globalScale);
        //ImGui.TableHeadersRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TableSetColumnIndex(1);


        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 0, 0, 255));
        ImGui.SetWindowFontScale(1.2f);
        ImGui.TextWrapped(description);
        ImGui.SetWindowFontScale(1.5f);
        ImGui.PopStyleColor();


        ImGui.TableSetColumnIndex(2);

        ImGui.EndTable();
        ImGui.SetCursorPosY(Size.Value.Y - (65f * _globalScale));
        Plugin.UiAtlasManager.DrawSeperator(Size.Value.X);
        ImGui.SetCursorPosY(Size.Value.Y - (55 * _globalScale));
        ImGui.SetCursorPosX(70 * _globalScale);
        ImGui.SetWindowFontScale(1.2f);
        var buttonSize = new Vector2(150, 25) * _globalScale;
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3137254901960784f, 0.3215686274509804f, 0.3215686274509804f, 255));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 1, 255));
        if (ImGui.Button("Accept", buttonSize))
        {
            _timeSinceLastQuestAccepted.Restart();
            _questToDisplay.HasQuestAcceptancePopup = false;
            OnQuestAccepted?.Invoke(this, EventArgs.Empty);
            IsOpen = false;
            _currentThumbnail = null;
            _frameToLoad = null;
        }
        ImGui.SameLine();
        if (ImGui.Button("Decline", buttonSize))
        {
            IsOpen = false;
            Plugin.Movement.DisableMovementLock();
            _currentThumbnail = null;
            _frameToLoad = null;
        }
        ImGui.PopStyleColor();
        ImGui.PopStyleColor();
        Plugin.UiAtlasManager.DrawFrameBorders(Size.Value);
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
        if (quest.QuestObjectives.Count > 0)
        {
            if (quest.QuestObjectives[0].QuestText.Count > 0)
            {
                Plugin.Movement.EnableMovementLock();
            }
        }
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
            _frameToLoad = null;
            _lastLoadedFrame = null;
        }
    }
}
