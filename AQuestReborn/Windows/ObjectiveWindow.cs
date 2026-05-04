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
        bool inCombat = false;
        unsafe
        {
            mouseDown = UIInputData.Instance()->CursorInputs.MouseButtonPressedFlags.HasFlag(MouseButtonFlags.LBUTTON);
            inCombat = Conditions.Instance()->InCombat;
        }
        Size = new Vector2(ImGui.GetMainViewport().Size.X, ImGui.GetMainViewport().Size.Y);
        Position = new Vector2(0, 0);
        if (!Plugin.EventWindow.IsOpen && !Plugin.ChoiceWindow.IsOpen && Plugin.ClientState.IsLoggedIn && Plugin.ObjectTable.LocalPlayer != null && Plugin.AQuestReborn != null)
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
            
            // Draw nameplates FIRST so they are rendered underneath the Quest Icons
            DrawNameplates();
            
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
                        // Offset marker up by 25px in screen space
                        screenPosition.Y -= 75f;
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
            bool playerDead = Plugin.ObjectTable.LocalPlayer != null && Plugin.ObjectTable.LocalPlayer.CurrentHp == 0;
            if (!Plugin.NpcChatWindow.IsConversationActive && Plugin.AQuestReborn != null && !inCombat && !playerDead)
            {
                foreach (var kvp in Plugin.AQuestReborn.CustomNpcCharacters)
                {
                    if (kvp.Value == null || kvp.Value.Address == 0) continue;
                    
                    var pos = kvp.Value.Position;
                    unsafe
                    {
                        var native = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)kvp.Value.Address;
                        if (native != null) pos = native->GameObject.Position;
                    }

                    // Project feet and top of character to get full vertical coverage
                    Vector2 feetScreenPos, topScreenPos;
                    bool feetInView, topInView;
                    Plugin.GameGui.WorldToScreen(pos, out feetScreenPos, out feetInView);
                    Plugin.GameGui.WorldToScreen(pos + new Vector3(0, 1.8f, 0), out topScreenPos, out topInView);

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


                        var player = Plugin.ObjectTable.LocalPlayer;
                        var playerDist = Vector3.Distance(player.Position, pos);
                        
                        // Calculate facing using player rotation
                        var toNpc = new Vector2(pos.X - player.Position.X, pos.Z - player.Position.Z);
                        toNpc = Vector2.Normalize(toNpc);
                        var playerForward = new Vector2((float)Math.Sin(player.Rotation), (float)Math.Cos(player.Rotation));
                        float dot = Vector2.Dot(toNpc, playerForward);
                        
                        // Must be close and facing within ~60 degrees (dot > 0.5)
                        bool inRange = playerDist < 3.5f && dot > 0.5f;

                        if (Plugin.Configuration.ShowNpcHitboxes)
                        {
                            var drawList = ImGui.GetWindowDrawList();
                            uint color = (ellipseDist <= 1f && inRange) 
                                ? ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 1f, 0f, 0.4f)) // Green if hovered and in range
                                : ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0f, 0f, 0.4f)); // Red otherwise
                            var min = new Vector2(npcScreenPos.X - horizontalExtent, npcScreenPos.Y - verticalExtent);
                            var max = new Vector2(npcScreenPos.X + horizontalExtent, npcScreenPos.Y + verticalExtent);
                            drawList.AddRectFilled(min, max, color, 8f);
                            drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f)), 8f, ImDrawFlags.None, 2f);
                        }

                        if (ellipseDist <= 1f && inRange)
                        {
                            if (mouseDown && Plugin.GamepadState.Raw(Dalamud.Game.ClientState.GamePad.GamepadButtons.South) == 0)
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

        // --- Draw ambient speech bubbles ---
        DrawSpeechBubbles();
    }

    private unsafe void DrawNameplates()
    {
        if (Plugin.AQuestReborn == null || Plugin.ObjectTable.LocalPlayer == null) return;
        if (!Plugin.Configuration.ShowCustomNameplates) return;

        var drawList = ImGui.GetWindowDrawList();

        // Custom NPCs
        foreach (var kvp in Plugin.AQuestReborn.CustomNpcCharacters)
        {
            if (kvp.Value == null || kvp.Value.Address == 0) continue;
            var pos = kvp.Value.Position;
            unsafe
            {
                var native = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)kvp.Value.Address;
                if (native != null) pos = native->GameObject.Position;
            }

            float dist = Vector3.Distance(Plugin.ObjectTable.LocalPlayer.Position, pos);
            if (dist < 40f)
            {
                DrawNameplate(drawList, kvp.Key, kvp.Value, dist, pos);
            }
        }

        // Quest NPCs
        foreach (var questKvp in Plugin.AQuestReborn.SpawnedNPCs)
        {
            foreach (var npcKvp in questKvp.Value)
            {
                var pos = npcKvp.Value.Position;
                unsafe
                {
                    var native = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)npcKvp.Value.Address;
                    if (native != null) pos = native->GameObject.Position;
                }

                float dist = Vector3.Distance(Plugin.ObjectTable.LocalPlayer.Position, pos);
                if (dist < 40f)
                {
                    DrawNameplate(drawList, npcKvp.Key, npcKvp.Value, dist, pos);
                }
            }
        }
    }

    private unsafe void DrawNameplate(ImDrawListPtr drawList, string name, ICharacter character, float distance, Vector3 actualPos)
    {
        Vector3 headPos;
        try
        {
            // Get the Y height from the head/neck bone to automatically adapt to Lalafell vs Roegadyn heights
            var rawBonePos = Hypostasis.Game.Common.GetBoneWorldPosition((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)character.Address, 6);
            
            // Lock X and Z to the character's root collision center so the nameplate doesn't sway horizontally during idle animations!
            headPos = new Vector3(actualPos.X, rawBonePos.Y, actualPos.Z);
        }
        catch
        {
            headPos = actualPos + new Vector3(0, 1.8f, 0);
        }

        // Project to screen, offset slightly above head (but lower than speech bubbles)
        bool inView;
        Vector2 screenPos;
        Plugin.GameGui.WorldToScreen(headPos + new Vector3(0, 0.2f, 0), out screenPos, out inView);
        if (!inView) return;

        // Push it at least 25 pixels up from the head projection
        screenPos.Y -= 25f;

        // Fade out at max distance
        float alpha = 1f;
        if (distance > 30f)
        {
            alpha = Math.Max(0f, 1f - ((distance - 30f) / 10f));
        }

        // FFXIV Friendly NPC style
        uint textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 1f, 0.9f, alpha));
        uint outlineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.8f * alpha));

        // Use larger font to emulate native nameplates
        float fontSize = 36f; // significantly bigger
        var font = ImGui.GetFont();
        
        // Calculate text size manually since ImGui.CalcTextSize uses current bound font size
        var textSize = ImGui.CalcTextSize(name) * (fontSize / font.FontSize);
        var textPos = new Vector2(screenPos.X - (textSize.X / 2), screenPos.Y - textSize.Y);

        // Draw heavy outline for readability
        drawList.AddText(font, fontSize, new Vector2(textPos.X - 2, textPos.Y), outlineColor, name);
        drawList.AddText(font, fontSize, new Vector2(textPos.X + 2, textPos.Y), outlineColor, name);
        drawList.AddText(font, fontSize, new Vector2(textPos.X, textPos.Y - 2), outlineColor, name);
        drawList.AddText(font, fontSize, new Vector2(textPos.X, textPos.Y + 2), outlineColor, name);
        
        drawList.AddText(font, fontSize, new Vector2(textPos.X - 2, textPos.Y - 2), outlineColor, name);
        drawList.AddText(font, fontSize, new Vector2(textPos.X + 2, textPos.Y + 2), outlineColor, name);
        drawList.AddText(font, fontSize, new Vector2(textPos.X + 2, textPos.Y - 2), outlineColor, name);
        drawList.AddText(font, fontSize, new Vector2(textPos.X - 2, textPos.Y + 2), outlineColor, name);

        // Draw actual text
        drawList.AddText(font, fontSize, textPos, textColor, name);
    }
    private unsafe void DrawSpeechBubbles()
    {
        if (Plugin.SpeechBubbleManager == null) return;
        var bubbles = Plugin.SpeechBubbleManager.ActiveBubbles;
        if (bubbles.Count == 0) return;

        var drawList = ImGui.GetWindowDrawList();

        foreach (var kvp in bubbles)
        {
            var bubble = kvp.Value;
            if (bubble.Character == null) continue;

            // Get head bone position
            Vector3 headPos;
            try
            {
                headPos = Hypostasis.Game.Common.GetBoneWorldPosition((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)bubble.Character.Address, 6);
            }
            catch
            {
                headPos = bubble.Character.Position + new Vector3(0, 1.6f, 0);
            }

            // Project to screen, offset above head
            bool inView;
            Vector2 screenPos;
            Plugin.GameGui.WorldToScreen(headPos + new Vector3(0, 0.5f, 0), out screenPos, out inView);
            if (!inView) continue;

            // Fade out in last 2 seconds
            float elapsed = bubble.Timer.ElapsedMilliseconds;
            float alpha = 1f;
            float fadeStart = bubble.DurationMs - 2000f;
            if (elapsed > fadeStart)
                alpha = Math.Max(0f, 1f - ((elapsed - fadeStart) / 2000f));

            // Word wrap text
            float maxWidth = 300f;
            string text = bubble.Text;
            var textSize = ImGui.CalcTextSize(text, false, maxWidth);

            // Bubble dimensions
            float padX = 16f, padY = 12f;
            float nameH = ImGui.CalcTextSize(kvp.Key).Y + 4f;
            float bubbleW = textSize.X + padX * 2;
            float bubbleH = nameH + textSize.Y + padY * 2;
            float tailH = 10f;

            // Center above head
            var topLeft = new Vector2(screenPos.X - bubbleW / 2, screenPos.Y - bubbleH - tailH);
            var bottomRight = new Vector2(topLeft.X + bubbleW, topLeft.Y + bubbleH);

            // Background
            uint bgColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.08f, 0.08f, 0.12f, 0.92f * alpha));
            uint borderColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.6f, 0.9f, 0.7f * alpha));
            uint textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, alpha));
            uint nameColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.6f, 0.8f, 1f, alpha));

            drawList.AddRectFilled(topLeft, bottomRight, bgColor, 8f);
            drawList.AddRect(topLeft, bottomRight, borderColor, 8f, ImDrawFlags.None, 1.5f);

            // Tail triangle
            var tailMid = new Vector2(screenPos.X, bottomRight.Y);
            var tailLeft = new Vector2(screenPos.X - 6, bottomRight.Y);
            var tailRight = new Vector2(screenPos.X + 6, bottomRight.Y);
            var tailBottom = new Vector2(screenPos.X, bottomRight.Y + tailH);
            drawList.AddTriangleFilled(tailLeft, tailRight, tailBottom, bgColor);
            drawList.AddLine(tailLeft, tailBottom, borderColor, 1.5f);
            drawList.AddLine(tailRight, tailBottom, borderColor, 1.5f);

            // Name
            var nameSize = ImGui.CalcTextSize(kvp.Key);
            drawList.AddText(new Vector2(topLeft.X + (bubbleW - nameSize.X) / 2, topLeft.Y + 2f), nameColor, kvp.Key);

            // Text (wrapped)
            drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(topLeft.X + padX, topLeft.Y + padY + 2f + nameSize.Y), textColor, text, maxWidth);
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
