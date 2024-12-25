using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
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
    private float _globalScale;

    public event EventHandler<RoleplayingQuest> OnRewardClosed;
    public Plugin Plugin { get; private set; }

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public RewardWindow(Plugin plugin) : base("Quest Reward###With a constant ID")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground;

        Size = new Vector2(600, 300);
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
        _globalScale = ImGuiHelpers.GlobalScale;
        Size = new Vector2(600, 250) * _globalScale;
        Position = new Vector2((screen.X / 2) - (Size.Value.X / 2), (screen.Y / 2) - (Size.Value.Y / 2));
        string questName = _questToDisplay.QuestName;
        string questReward = AddSpacesToSentence(_questToDisplay.TypeOfReward.ToString(), false);
        string description = _questToDisplay.QuestDescription;
        string thumbnailPath = _questToDisplay.QuestThumbnailPath;
        string contentRating = AddSpacesToSentence(_questToDisplay.ContentRating.ToString(), false);
        Plugin.UiAtlasManager.CheckImageAssets();
        Plugin.UiAtlasManager.DrawBackground(Size.Value*0.99f);
        ImGui.SetCursorPos(new Vector2(0, 0));
        ImGui.SetCursorPosY(40 * _globalScale);
        ImGui.SetWindowFontScale(2f);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 0, 0, 255));
        Plugin.UiAtlasManager.TextCentered(questName, Size.Value);
        ImGui.PopStyleColor();
        ImGui.SetCursorPosY(90 * _globalScale);
        Plugin.UiAtlasManager.DrawSeperator(Size.Value.X);
        var tableOffset = 100 * _globalScale;
        var contentArea = (Size.Value.X - (tableOffset * 2));
        var buttonSize = new Vector2(contentArea, 50 * _globalScale);
        ImGui.SetCursorPosX(0);
        ImGui.BeginTable("##Dialogue Table", 3);
        ImGui.TableSetupColumn("Padding 1", ImGuiTableColumnFlags.WidthFixed, tableOffset);
        ImGui.TableSetupColumn("Center", ImGuiTableColumnFlags.WidthFixed, contentArea);
        ImGui.TableSetupColumn("Padding 2", ImGuiTableColumnFlags.WidthFixed, tableOffset);
        //ImGui.TableHeadersRow();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TableSetColumnIndex(1);
        ImGui.SetWindowFontScale(1.5f);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3137254901960784f, 0.3215686274509804f, 0.3215686274509804f, 255));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 1, 255));
        switch (_questToDisplay.TypeOfReward)
        {
            case RoleplayingQuest.QuestRewardType.None:
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 0, 0, 255));
                ImGui.TextWrapped("You were rewarded with a journey of finishing this quest!");
                ImGui.PopStyleColor();
                break;
            case RoleplayingQuest.QuestRewardType.SecretMessage:
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 0, 0, 255));
                ImGui.TextUnformatted("You have been awarded this message:");
                ImGui.TextWrapped(_questToDisplay.QuestReward);
                ImGui.PopStyleColor();
                break;
            case RoleplayingQuest.QuestRewardType.DownloadLink:
                ImGui.SetWindowFontScale(0.9f);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 0, 0, 255));
                ImGui.TextWrapped("Check URL: " + _questToDisplay.QuestReward);
                ImGui.PopStyleColor();
                ImGui.SetWindowFontScale(1.2f);
                if (ImGui.Button("Awarded A Download", buttonSize))
                {
                    OpenFile(_questToDisplay.QuestReward, _questToDisplay.TypeOfReward);
                }
                break;
            case RoleplayingQuest.QuestRewardType.MediaFile:
                ImGui.SetWindowFontScale(1.2f);
                if (ImGui.Button("Awarded A Media File", buttonSize))
                {
                    OpenFile(_questToDisplay.QuestReward, _questToDisplay.TypeOfReward);
                }
                break;
        }
        ImGui.SetWindowFontScale(1.2f);
        if (ImGui.Button("Accept", buttonSize))
        {
            IsOpen = false;
            OnRewardClosed?.Invoke(this, _questToDisplay);
            Plugin.Movement.DisableMovementLock();
        }
        ImGui.PopStyleColor();
        ImGui.PopStyleColor();
        ImGui.TableSetColumnIndex(2);
        ImGui.EndTable();

        Plugin.UiAtlasManager.DrawFrameBorders(Size.Value);
    }
    void OpenFile(string relativePath, RoleplayingQuest.QuestRewardType questRewardType)
    {
        string foundPath = _questToDisplay.FoundPath;
        string fullPath = "";

        switch (questRewardType)
        {
            case RoleplayingQuest.QuestRewardType.MediaFile:
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
                    ProcessStartInfo processInfo = new ProcessStartInfo("explorer.exe", @"""" + fullPath + @"""");
                    processInfo.UseShellExecute = true;
                    Process.Start(processInfo);
                }
                break;
            case RoleplayingQuest.QuestRewardType.DownloadLink:
                ProcessStartInfo processInfo2 = new ProcessStartInfo(relativePath);
                processInfo2.UseShellExecute = true;
                Process.Start(processInfo2);
                break;
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
