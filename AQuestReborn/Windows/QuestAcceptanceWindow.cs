using System;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using RoleplayingQuestCore;

namespace SamplePlugin.Windows;

public class QuestAcceptanceWindow : Window, IDisposable
{
    private RoleplayingQuest _questToDisplay;
    private bool _alreadyLoadingFrame;
    private byte[] _currentThumbnail;
    private IDalamudTextureWrap _frameToLoad;
    private byte[] _lastLoadedFrame;
    public event EventHandler OnQuestAccepted;

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public QuestAcceptanceWindow(Plugin plugin) : base("Quest Details###With a constant ID")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(500, 500);
        SizeCondition = ImGuiCond.Always;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
    }

    public override void Draw()
    {
        string questName = _questToDisplay.QuestName;
        string questReward = AddSpacesToSentence(_questToDisplay.TypeOfReward.ToString(), false);
        string description = _questToDisplay.QuestDescription;
        string thumbnailPath = _questToDisplay.QuestThumbnailPath;
        string contentRating = AddSpacesToSentence(_questToDisplay.ContentRating.ToString(), false);
        ImGui.SetWindowFontScale(1.5f);
        ImGui.LabelText("", questName);
        ImGui.LabelText("", "Content Rating: " + contentRating);

        if (!string.IsNullOrEmpty(_questToDisplay.QuestThumbnailPath))
        {
            if (!_alreadyLoadingFrame)
            {
                Task.Run(async () =>
                {
                    _alreadyLoadingFrame = true;
                    if (_lastLoadedFrame != _currentThumbnail)
                    {
                        _frameToLoad = await Plugin.TextureProvider.CreateFromImageAsync(_currentThumbnail);
                        _lastLoadedFrame = _currentThumbnail;
                    }
                    _alreadyLoadingFrame = false;
                });
            }
            if (_frameToLoad != null)
            {
                ImGui.Image(_frameToLoad.ImGuiHandle, new Vector2(500, 200));
            }
        }
        ImGui.LabelText("", "Reward: " + questReward);
        ImGui.LabelText("", "Description: ");
        ImGui.TextWrapped(description);
        ImGui.SetWindowFontScale(2);
        if (ImGui.Button("Accept"))
        {
            _questToDisplay.HasQuestAcceptancePopup = false;
            OnQuestAccepted?.Invoke(this, EventArgs.Empty);
            IsOpen = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Decline"))
        {
            IsOpen = false;
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
        if (!string.IsNullOrEmpty(_questToDisplay.QuestThumbnailPath))
        {
            SetThumbnail(_questToDisplay.QuestThumbnailPath);
        }
        IsOpen = true;
    }
    public void SetThumbnail(string path)
    {
        MemoryStream background = new MemoryStream();
        Bitmap none = new Bitmap(path);
        background.Position = 0;
        _currentThumbnail = background.ToArray();
    }
}
