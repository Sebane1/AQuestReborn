using AQuestReborn;
using AQuestReborn.CustomNpc;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using FFXIVLooseTextureCompiler.ImageProcessing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace SamplePlugin.Windows;

public class NpcChatWindow : Window, IDisposable
{
    private Plugin _plugin;
    private float _globalScale;

    // Active conversation state
    private string _activeNpcName;
    private NPCConversationManager _activeConversationManager;
    private ICharacter _activeNpcCharacter;
    private CustomNpcCharacter _activeNpcData;

    // Current display state — one response at a time
    private string _displayText = "";
    private bool _isWaitingForResponse;
    private string _inputText = "";
    private bool _focusInput;

    // Typewriter
    private string _typewriterTarget = "";
    private int _typewriterIndex = 0;
    private Stopwatch _typewriterTimer = new Stopwatch();
    private bool _typewriterActive;

    // Farewell auto-close
    private bool _farewellDetected;
    private Stopwatch _farewellTimer = new Stopwatch();

    // Dialogue box visuals (same as EventWindow)
    private List<byte[]> _dialogueBoxStyles = new List<byte[]>();
    private ConcurrentDictionary<int, IDalamudTextureWrap> _dialogueStylesToLoad = new ConcurrentDictionary<int, IDalamudTextureWrap>();
    private bool _alreadyLoadingFrame;
    private byte[] _nameTitleStyle;
    private IDalamudTextureWrap _dialogueTitleStyleToLoad;
    private bool _alreadyLoadingTitleFrame;
    private Bitmap _titleBitmap;
    private int _dialogueBoxIndex = 0;

    public NpcChatWindow(Plugin plugin)
        : base("NPC Chat##npcchatwindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBackground, true)
    {
        Size = new Vector2(1088, 340);
        _plugin = plugin;
        LoadBubbleBackgrounds();
    }

    public bool IsConversationActive => IsOpen && _activeNpcName != null;

    public void OpenConversation(string npcName, NPCConversationManager conversationManager,
        ICharacter npcCharacter, CustomNpcCharacter npcData)
    {
        _activeNpcName = npcName;
        _activeConversationManager = conversationManager;
        _activeNpcCharacter = npcCharacter;
        _activeNpcData = npcData;
        _inputText = "";
        _isWaitingForResponse = false;
        _typewriterActive = false;
        _farewellDetected = false;
        _farewellTimer.Reset();
        _displayText = "";
        _focusInput = true;
        _plugin.Movement.EnableMovementLock();
        IsOpen = true;

        // Ask the AI to generate its own greeting
        SendMessage("*approaches and waves hello*");
    }

    public override void OnClose()
    {
        _activeNpcName = null;
        _activeConversationManager = null;
        _activeNpcCharacter = null;
        _activeNpcData = null;
        _typewriterActive = false;
        _displayText = "";
        _plugin.Movement.DisableMovementLock();
        base.OnClose();
    }

    public void Dispose() { }

    private byte[] ImageToBytes(Bitmap image)
    {
        MemoryStream ms = new MemoryStream();
        image.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        return ms.ToArray();
    }

    private void LoadBubbleBackgrounds()
    {
        try
        {
            _titleBitmap = ImageManipulation.Crop(TexIO.TexToBitmap(
                new MemoryStream(_plugin.DataManager.GetFile("ui/uld/talk_hr1.tex").Data)), new Vector2(575, 72));
            var data2 = TexIO.TexToBitmap(
                new MemoryStream(_plugin.DataManager.GetFile("ui/uld/talk_basic_hr1.tex").Data));
            var data3 = TexIO.TexToBitmap(
                new MemoryStream(_plugin.DataManager.GetFile("ui/uld/talk_other_hr1.tex").Data));
            _nameTitleStyle = ImageToBytes(_titleBitmap);
            foreach (var item in ImageManipulation.DivideImageVertically(data2, 3))
                _dialogueBoxStyles.Add(ImageToBytes(item));
            foreach (var item in ImageManipulation.DivideImageVertically(data3, 6))
                _dialogueBoxStyles.Add(ImageToBytes(item));
        }
        catch { }
    }

    public override void Draw()
    {
        if (_activeNpcName == null) return;

        _globalScale = ImGuiHelpers.GlobalScale * 0.95f;
        var displaySize = ImGui.GetIO().DisplaySize;
        Size = new Vector2(1088 * _globalScale, 340 * _globalScale);
        Position = new Vector2((displaySize.X / 2) - (Size.Value.X / 2), displaySize.Y - Size.Value.Y);

        // Update typewriter
        if (_typewriterActive && _typewriterIndex < _typewriterTarget.Length)
        {
            if (_typewriterTimer.ElapsedMilliseconds > 30)
            {
                _typewriterIndex = Math.Min(_typewriterIndex + 2, _typewriterTarget.Length);
                _displayText = _typewriterTarget.Substring(0, _typewriterIndex);
                _typewriterTimer.Restart();
            }
            if (_typewriterIndex >= _typewriterTarget.Length)
            {
                _typewriterActive = false;
                _focusInput = true;

                // Check for farewell keywords
                string lower = _typewriterTarget.ToLower();
                if (lower.Contains("goodbye") || lower.Contains("farewell") || lower.Contains("see you")
                    || lower.Contains("take care") || lower.Contains("bye") || lower.Contains("until next time"))
                {
                    _farewellDetected = true;
                    _farewellTimer.Restart();
                }
            }
        }

        // Auto-close after farewell
        if (_farewellDetected && _farewellTimer.ElapsedMilliseconds > 10000)
        {
            IsOpen = false;
            return;
        }

        // Load dialogue background textures
        if (!_alreadyLoadingFrame)
        {
            _alreadyLoadingFrame = true;
            Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < _dialogueBoxStyles.Count; i++)
                    {
                        if (!_dialogueStylesToLoad.ContainsKey(i) || _dialogueStylesToLoad[i] == null)
                            _dialogueStylesToLoad[i] = await Plugin.TextureProvider.CreateFromImageAsync(_dialogueBoxStyles[i]);
                    }
                }
                finally { _alreadyLoadingFrame = false; }
            });
        }

        // Draw dialogue background
        if (_dialogueStylesToLoad.ContainsKey(_dialogueBoxIndex) && _dialogueStylesToLoad[_dialogueBoxIndex] != null)
        {
            ImGui.Image(_dialogueStylesToLoad[_dialogueBoxIndex].Handle, new Vector2(Size.Value.X, Size.Value.Y - 52 * _globalScale));
        }

        // Draw name title bar
        if (!_alreadyLoadingTitleFrame)
        {
            _alreadyLoadingTitleFrame = true;
            Task.Run(async () =>
            {
                try
                {
                    if (_dialogueTitleStyleToLoad == null && _nameTitleStyle != null)
                        _dialogueTitleStyleToLoad = await Plugin.TextureProvider.CreateFromImageAsync(_nameTitleStyle);
                }
                finally { _alreadyLoadingTitleFrame = false; }
            });
        }
        if (_dialogueTitleStyleToLoad != null && _titleBitmap != null)
        {
            ImGui.SetCursorPos(new Vector2(50 * _globalScale, 8 * _globalScale));
            ImGui.Image(_dialogueTitleStyleToLoad.Handle, new Vector2(_titleBitmap.Width * _globalScale, _titleBitmap.Height * _globalScale));
        }

        // Draw content
        ImGui.SetCursorPos(new Vector2(0, 0));
        ImGui.BeginTable("##NpcChatTable", 3);
        ImGui.TableSetupColumn("Pad1", ImGuiTableColumnFlags.WidthFixed, 100 * _globalScale);
        ImGui.TableSetupColumn("Center", ImGuiTableColumnFlags.WidthFixed, 888 * _globalScale);
        ImGui.TableSetupColumn("Pad2", ImGuiTableColumnFlags.WidthFixed, 100 * _globalScale);
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TableSetColumnIndex(1);
        DrawDialogueContent();
        ImGui.TableSetColumnIndex(2);
        ImGui.EndTable();
    }

    private void DrawDialogueContent()
    {
        // NPC name (same style as EventWindow)
        ImGui.SetCursorPosY(22 * _globalScale);
        ImGui.SetWindowFontScale(2.2f);
        ImGui.LabelText("##npcChatName", _activeNpcName);

        // NPC response text (single response)
        ImGui.SetWindowFontScale(2f);
        ImGui.SetCursorPosY(75 * _globalScale);

        // Use dark text for default box styles (same logic as EventWindow)
        if (_dialogueBoxIndex != 8 && _dialogueBoxIndex != 2 && _dialogueBoxIndex != 3)
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 0, 0, 255));
        else
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(255, 255, 255, 255));

        ImGui.TextWrapped(_displayText);

        ImGui.PopStyleColor();

        // Chat input at the bottom
        ImGui.SetCursorPosY(Size.Value.Y - 48 * _globalScale);
        ImGui.SetWindowFontScale(1.4f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.1f, 0.1f, 0.1f, 0.9f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 1, 1));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.4f, 0.4f, 0.4f, 0.8f));

        bool busy = _isWaitingForResponse || _typewriterActive || _farewellDetected;

        ImGui.SetNextItemWidth(620 * _globalScale);
        if (_focusInput && !busy)
        {
            ImGui.SetKeyboardFocusHere();
            _focusInput = false;
        }

        if (busy)
        {
            // Show disabled input while waiting
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.15f, 0.15f, 0.15f, 0.6f));
            string placeholder = "";
            ImGui.InputText("##npcChatInput", ref placeholder, 500, ImGuiInputTextFlags.ReadOnly);
            ImGui.PopStyleColor();
        }
        else
        {
            bool submitted = ImGui.InputText("##npcChatInput", ref _inputText, 500,
                ImGuiInputTextFlags.EnterReturnsTrue);

            ImGui.SameLine();
            bool sendClicked = ImGui.Button("Send", new Vector2(80 * _globalScale, 0));

            if ((submitted || sendClicked) && !string.IsNullOrWhiteSpace(_inputText))
            {
                SendMessage(_inputText.Trim());
                _inputText = "";
            }
        }

        ImGui.PopStyleColor(3);

        // Exit button — always visible
        ImGui.SameLine();
        if (ImGui.Button("Exit", new Vector2(60 * _globalScale, 0)))
        {
            IsOpen = false;
        }

        // Escape key to close
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            IsOpen = false;
        }
    }

    private void SendMessage(string message)
    {
        if (_activeConversationManager == null || _activeNpcCharacter == null || _activeNpcData == null)
            return;

        var sender = _plugin.ObjectTable.LocalPlayer;
        if (sender == null) return;

        _isWaitingForResponse = true;

        // Capture refs for the closure
        var convManager = _activeConversationManager;
        var npcChar = _activeNpcCharacter;
        var npcData = _activeNpcData;
        var npcName = _activeNpcName;

        Task.Run(async () =>
        {
            try
            {
                var sw = Stopwatch.StartNew();


                string response = await convManager.SendMessage(
                    sender, npcChar,
                    npcData.NpcName,
                    npcData.NPCGreeting,
                    message,
                    _plugin.GetEnvironmentContext(),
                    npcData.NpcPersonality);

                _plugin.PluginLog.Information($"NPC Chat: Got response in {sw.ElapsedMilliseconds}ms, length={response?.Length ?? 0}");
                _plugin.PluginLog.Information($"NPC Chat: Raw response text: [{response}]");

                if (!string.IsNullOrEmpty(response))
                {
                    // Clean up formatting artifacts from GPT pipeline
                    // WordFilter leaves "says, " / "asks, " prefix — strip it for dialogue display
                    string cleanResponse = response;
                    foreach (var prefix in new[] { "says, ", "asks, ", "exclaims, " })
                    {
                        if (cleanResponse.StartsWith(prefix))
                        {
                            cleanResponse = cleanResponse.Substring(prefix.Length);
                            break;
                        }
                    }

                    // Strip asterisk actions for the UI display
                    cleanResponse = System.Text.RegularExpressions.Regex.Replace(cleanResponse, @"\*[^*]+\*", "").Trim();
                    if (string.IsNullOrWhiteSpace(cleanResponse))
                    {
                        cleanResponse = "...";
                    }

                    // Strip surrounding quotes if present
                    if (cleanResponse.StartsWith("\"") && cleanResponse.EndsWith("\"") && cleanResponse.Length > 2)
                    {
                        cleanResponse = cleanResponse.Substring(1, cleanResponse.Length - 2);
                    }
                    // Always strip any trailing quote
                    cleanResponse = cleanResponse.TrimEnd('"').Trim();

                    _plugin.PluginLog.Information($"NPC Chat: Clean display text: [{cleanResponse}]");

                    // Start typewriter
                    _typewriterTarget = cleanResponse;
                    _typewriterIndex = 0;
                    _displayText = "";
                    _typewriterActive = true;
                    _typewriterTimer.Restart();

                    // Log to FFXIV chat
                    try
                    {
                        _plugin.ChatGui.Print(new Dalamud.Game.Text.XivChatEntry
                        {
                            Message = $"{_activeNpcName}: {cleanResponse}",
                            Type = Dalamud.Game.Text.XivChatType.NPCDialogue,
                        });
                    }
                    catch { }

                    // Trigger lip sync
                    try
                    {
                        _plugin.AnamcoreManager.TriggerLipSync(npcChar, 0);
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(Math.Max(response.Length * 50, 3000));
                            try { _plugin.AnamcoreManager.StopLipSync(npcChar); } catch { }
                        });
                    }
                    catch { }
                }
                else
                {
                    _displayText = "(No response)";
                }
            }
            catch (Exception ex)
            {
                _displayText = "Failed to get response.";
                _plugin.PluginLog.Warning(ex, "NPC Chat error: " + ex.Message);
            }
            finally
            {
                _isWaitingForResponse = false;
            }
        });
    }
}

