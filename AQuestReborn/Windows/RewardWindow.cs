using System;
using System.Diagnostics;
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

public class RewardWindow : Window, IDisposable
{
    private RoleplayingQuest _questToDisplay;
    private bool _alreadyLoadingFrame;
    private byte[] _currentThumbnail;
    private IDalamudTextureWrap _frameToLoad;
    private byte[] _lastLoadedFrame;
    public event EventHandler OnRewardClosed;
    public Plugin Plugin { get; private set; }

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public RewardWindow(Plugin plugin) : base("Quest Reward###With a constant ID")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(600, 250);
        SizeCondition = ImGuiCond.Always;
        Plugin = plugin;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
    }

    public override void Draw()
    {
        var screen = ImGui.GetIO().DisplaySize;
        Position = new Vector2((screen.X / 2) - (Size.Value.X / 2), (screen.Y / 2) - (Size.Value.Y / 2));
        string questName = _questToDisplay.QuestName;
        string questReward = AddSpacesToSentence(_questToDisplay.TypeOfReward.ToString(), false);
        string description = _questToDisplay.QuestDescription;
        string thumbnailPath = _questToDisplay.QuestThumbnailPath;
        string contentRating = AddSpacesToSentence(_questToDisplay.ContentRating.ToString(), false);
        ImGui.SetWindowFontScale(2f);
        ImGui.LabelText("", questName);
        ImGui.SetWindowFontScale(1.5f);
        switch (_questToDisplay.TypeOfReward)
        {
            case RoleplayingQuest.QuestRewardType.None:
                ImGui.TextUnformatted("You were rewarded with a journey of finishing this quest!");
                break;
            case RoleplayingQuest.QuestRewardType.SecretMessage:
                ImGui.TextUnformatted("You have been awarded the following message:");
                ImGui.TextWrapped(_questToDisplay.QuestReward);
                break;
            case RoleplayingQuest.QuestRewardType.DownloadLink:
                if (ImGui.Button("Awarded A Download"))
                {
                    OpenFile(_questToDisplay.QuestReward);
                }
                break;
            case RoleplayingQuest.QuestRewardType.MediaFile:
                if (ImGui.Button("Awarded A Media File"))
                {
                    OpenFile(_questToDisplay.QuestReward);
                }
                break;
        }
        ImGui.SetWindowFontScale(2);
        if (ImGui.Button("Accept"))
        {
            IsOpen = false;
            OnRewardClosed?.Invoke(this, EventArgs.Empty);
        }
    }
    void OpenFile(string relativePath)
    {
        string foundPath = _questToDisplay.FoundPath;
        string fullPath = "";
        if (string.IsNullOrEmpty(foundPath))
        {
            fullPath = Path.GetDirectoryName(foundPath);
        }
        else
        {
            fullPath = Path.Combine(Plugin.Configuration.QuestInstallFolder, _questToDisplay.QuestName);
        }
        if (File.Exists(fullPath))
        {
            ProcessStartInfo ProcessInfo;
            Process Process;
            ProcessInfo = new ProcessStartInfo("explorer.exe", @"""" + fullPath + @"""");
            ProcessInfo.UseShellExecute = true;
            Process = Process.Start(ProcessInfo);
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
    public void PromptReward(RoleplayingQuest quest)
    {
        _questToDisplay = quest;
        IsOpen = true;
    }
}