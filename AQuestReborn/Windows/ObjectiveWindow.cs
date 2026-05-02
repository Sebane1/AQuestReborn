using System;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Numerics;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVLooseTextureCompiler.ImageProcessing;
using AQuestReborn;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Game.ClientState.Objects.Types;

namespace SamplePlugin.Windows;

public class ObjectiveWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;
    private FileDialogManager _fileDialogManager;
    private byte[] emptyBackground;
    private bool _alreadyLoadingQuestStartIcon;
    private IDalamudTextureWrap _questStartIconTextureWrap;
    private byte[] _lastQuestStartIconData;
    private bool _mouseDistanceIsCloseToObjective;
    private byte[] _questStartIconData;
    private byte[] _questObjectiveIconData;
    private bool _alreadyLoadingQuestObjectiveIcon;
    private byte[] _lastQuestStartObjectiveData;
    private IDalamudTextureWrap _questObjectiveIconTextureWrap;

    public event EventHandler OnSelectionAttempt;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public ObjectiveWindow(Plugin plugin)
        : base("Objective Display##mainwindow", ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground, true)
    {
        Plugin = plugin;
        AllowClickthrough = true;
        LoadQuestIcons();
    }
    private void LoadQuestIcons()
    {
        // Quest start icon
        var data1 = Plugin.DataManager.GetFile("ui/icon/061000/061411_hr1.tex");
        // Quest complete icon
        var data2 = Plugin.DataManager.GetFile("ui/icon/061000/061421_hr1.tex");

        MemoryStream questStartIcon = new MemoryStream();
        Grayscale.MakeGrayscale(TexIO.TexToBitmap(new MemoryStream(data1.Data))).Save(questStartIcon, ImageFormat.Png);

        MemoryStream questObjectiveIcon = new MemoryStream();
        Grayscale.MakeGrayscale(TexIO.TexToBitmap(new MemoryStream(data2.Data))).Save(questObjectiveIcon, ImageFormat.Png);

        questStartIcon.Position = 0;
        questObjectiveIcon.Position = 0;
        _questStartIconData = questStartIcon.ToArray();
        _questObjectiveIconData = questObjectiveIcon.ToArray();
    }

    public void OnClose()
    {
        IsOpen = true;
    }
    public override void PreDraw()
    {
        base.PreDraw();
    }
    public override void Draw()
    {
        bool mouseDown = false;
        unsafe
        {
            mouseDown = UIInputData.Instance()->CursorInputs.MouseButtonPressedFlags.HasFlag(MouseButtonFlags.LBUTTON);
        }
        Size = new Vector2(ImGui.GetMainViewport().Size.X, ImGui.GetMainViewport().Size.Y);
        Position = new Vector2(0, 0);
        if (!Plugin.EventWindow.IsOpen && !Plugin.ChoiceWindow.IsOpen && Plugin.ClientState.IsLoggedIn && Plugin.ObjectTable.LocalPlayer != null)
        {
            var questChainObjectives = Plugin.RoleplayingQuestManager.GetActiveQuestChainObjectivesInZone((int)Plugin.ClientState.TerritoryType, Plugin.AQuestReborn.Discriminator);
            if (!_alreadyLoadingQuestStartIcon)
            {
                Task.Run(async () =>
                {
                    _alreadyLoadingQuestStartIcon = true;
                    if (_lastQuestStartIconData != _questStartIconData)
                    {
                        if (_questStartIconData != null)
                        {
                            _questStartIconTextureWrap = await Plugin.TextureProvider.CreateFromImageAsync(_questStartIconData);
                        }
                        _lastQuestStartIconData = _questStartIconData;
                    }
                    _alreadyLoadingQuestStartIcon = false;
                });
            }
            if (!_alreadyLoadingQuestObjectiveIcon)
            {
                Task.Run(async () =>
                {
                    _alreadyLoadingQuestObjectiveIcon = true;
                    if (_lastQuestStartObjectiveData != _questObjectiveIconData)
                    {
                        if (_questObjectiveIconData != null)
                        {
                            _questObjectiveIconTextureWrap = await Plugin.TextureProvider.CreateFromImageAsync(_questObjectiveIconData);
                        }
                        _lastQuestStartObjectiveData = _questObjectiveIconData;
                    }
                    _alreadyLoadingQuestObjectiveIcon = false;
                });
            }
            _mouseDistanceIsCloseToObjective = false;
            foreach (var item in questChainObjectives)
            {
                if (!item.Item2.ObjectiveCompleted)
                {
                    Vector2 screenPosition = new Vector2();
                    bool inView = false;
                    Vector3 offset = new Vector3();
                    switch (item.Item2.TypeOfQuestPoint)
                    {
                        case RoleplayingQuestCore.QuestObjective.QuestPointType.NPC:
                            // Use actual head bone position — marker goes slightly above
                            var headWorldPos = GetNpcHeadPosition(item.Item2.Coordinates);
                            Plugin.GameGui.WorldToScreen(headWorldPos + new Vector3(0, 0.3f, 0), out screenPosition, out inView);
                            break;
                        case RoleplayingQuestCore.QuestObjective.QuestPointType.GroundItem:
                            // To do: Display something unique?
                            break;
                        case RoleplayingQuestCore.QuestObjective.QuestPointType.StandAndWait:
                            // To do: Display something unique?
                            break;
                    }
                    // For non-NPC types, use coordinate + offset
                    if (item.Item2.TypeOfQuestPoint != RoleplayingQuestCore.QuestObjective.QuestPointType.NPC)
                        Plugin.GameGui.WorldToScreen(item.Item2.Coordinates + offset, out screenPosition, out inView);
                    if (inView)
                    {
                        if (_questStartIconTextureWrap != null)
                        {
                            try
                            {
                                var value = ImGui.GetIO().MousePos;
                                var distance = Vector2.Distance(new Vector2(screenPosition.X / Size.Value.X, 0),
                                    new Vector2(value.X / Size.Value.X, 0));
                                var playerDistance = Vector3.Distance(Plugin.ObjectTable.LocalPlayer.Position, item.Item2.Coordinates);
                                if (distance < 0.02f && playerDistance < Plugin.RoleplayingQuestManager.MinimumDistance
                                    && item.Item2.TypeOfObjectiveTrigger == RoleplayingQuestCore.QuestObjective.ObjectiveTriggerType.NormalInteraction)
                                {
                                    _mouseDistanceIsCloseToObjective = true;
                                    if (mouseDown)
                                    {
                                        OnSelectionAttempt?.Invoke(this, EventArgs.Empty);
                                        _mouseDistanceIsCloseToObjective = false;
                                        break;
                                    }
                                }
                                if (playerDistance < item.Item2.Maximum3dIndicatorDistance)
                                {
                                    var iconDimensions = new Vector2(100, 100);
                                    ImGui.SetCursorPos(new Vector2(screenPosition.X - (iconDimensions.X / 2), screenPosition.Y - (iconDimensions.Y / 2)));
                                    if (_questStartIconTextureWrap != null && _questObjectiveIconTextureWrap != null
                                        && item.Item2.TypeOfObjectiveTrigger != RoleplayingQuestCore.QuestObjective.ObjectiveTriggerType.BoundingTrigger)
                                    {
                                        ImGui.Image(item.Item1 == 0 ? _questStartIconTextureWrap.Handle : _questObjectiveIconTextureWrap.Handle, iconDimensions);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Plugin.PluginLog.Warning(e, e.Message);
                            }
                        }
                    }
                }
            }

            // Custom NPC click-to-chat detection
            if (!Plugin.NpcChatWindow.IsConversationActive && Plugin.AQuestReborn != null)
            {
                foreach (var kvp in Plugin.AQuestReborn.CustomNpcCharacters)
                {
                    if (kvp.Value == null) continue;

                    // Project feet and top of character to get full vertical coverage
                    Vector2 feetScreenPos, topScreenPos;
                    bool feetInView, topInView;
                    Plugin.GameGui.WorldToScreen(kvp.Value.Position, out feetScreenPos, out feetInView);
                    Plugin.GameGui.WorldToScreen(kvp.Value.Position + new Vector3(0, 1.8f, 0), out topScreenPos, out topInView);

                    if (feetInView || topInView)
                    {
                        // Center click zone between feet and top
                        var npcScreenPos = new Vector2(
                            (feetScreenPos.X + topScreenPos.X) / 2f,
                            (feetScreenPos.Y + topScreenPos.Y) / 2f);
                        float verticalExtent = MathF.Abs(feetScreenPos.Y - topScreenPos.Y) / 2f;
                        float horizontalExtent = MathF.Max(verticalExtent * 0.4f, 30f); // Narrower than tall

                        var mousePos = ImGui.GetIO().MousePos;
                        // Elliptical hit test: check if mouse is inside the character-shaped zone
                        float dx = (mousePos.X - npcScreenPos.X) / horizontalExtent;
                        float dy = (mousePos.Y - npcScreenPos.Y) / verticalExtent;
                        float ellipseDist = dx * dx + dy * dy;

                        var playerDist = Vector3.Distance(Plugin.ObjectTable.LocalPlayer.Position, kvp.Value.Position);

                        // Draw clickable area debug visual
                        var drawList = ImGui.GetWindowDrawList();
                        bool inRange = playerDist < 5f;
                        uint circleColor = inRange
                            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 1f, 0.3f, 0.35f))
                            : ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0.2f, 0.2f));
                        // Draw ellipse debug visual using path
                        for (int seg = 0; seg < 32; seg++)
                        {
                            float angle = (seg / 32f) * MathF.PI * 2f;
                            drawList.PathLineTo(new Vector2(
                                npcScreenPos.X + MathF.Cos(angle) * horizontalExtent,
                                npcScreenPos.Y + MathF.Sin(angle) * verticalExtent));
                        }
                        drawList.PathFillConvex(circleColor);
                        for (int seg = 0; seg < 32; seg++)
                        {
                            float angle = (seg / 32f) * MathF.PI * 2f;
                            drawList.PathLineTo(new Vector2(
                                npcScreenPos.X + MathF.Cos(angle) * horizontalExtent,
                                npcScreenPos.Y + MathF.Sin(angle) * verticalExtent));
                        }
                        drawList.PathStroke(ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.5f)), ImDrawFlags.Closed, 2f);

                        if (ellipseDist <= 1f && inRange)
                        {
                            if (mouseDown)
                            {
                                // Find NPC data and conversation manager
                                string npcName = kvp.Key;
                                AQuestReborn.CustomNpc.CustomNpcCharacter npcData = null;
                                foreach (var npc in Plugin.Configuration.CustomNpcCharacters)
                                {
                                    if (npc.NpcName == npcName)
                                    {
                                        npcData = npc;
                                        break;
                                    }
                                }
                                if (npcData != null && Plugin.AQuestReborn.CustomNpcConversationManagers.ContainsKey(npcName))
                                {
                                    Plugin.NpcChatWindow.OpenConversation(npcName,
                                        Plugin.AQuestReborn.CustomNpcConversationManagers[npcName],
                                        kvp.Value, npcData);
                                }
                                break;
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Finds the nearest spawned NPC at the given position and returns their head world position
    /// using actual bone data. Falls back to position + 1.6y if bone access fails.
    /// </summary>
    private unsafe Vector3 GetNpcHeadPosition(Vector3 position)
    {
        ICharacter closest = null;
        float closestDist = float.MaxValue;

        // Search spawned quest NPCs
        foreach (var questKvp in Plugin.AQuestReborn.SpawnedNPCs)
        {
            foreach (var npcKvp in questKvp.Value)
            {
                if (npcKvp.Value != null)
                {
                    float dist = Vector3.Distance(npcKvp.Value.Position, position);
                    if (dist < closestDist && dist < 3f)
                    {
                        closestDist = dist;
                        closest = npcKvp.Value;
                    }
                }
            }
        }

        if (closest == null) return position + new Vector3(0, 1.6f, 0);

        try
        {
            var gameObject = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)closest.Address;
            if (gameObject != null && gameObject->DrawObject != null)
            {
                // Bone 6 = head bone in FFXIV skeleton
                var headPos = Hypostasis.Game.Common.GetBoneWorldPosition(gameObject, 6);
                if (headPos != Vector3.Zero)
                    return headPos;
            }
        }
        catch { }

        // Fallback
        return position + new Vector3(0, 1.6f, 0);
    }

    public void Dispose()
    {

    }
}
