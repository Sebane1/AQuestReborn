using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AQuestReborn;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using NAudio.Wave;
using RoleplayingMediaCore;
using RoleplayingQuestCore;
using RoleplayingVoiceCore;

namespace SamplePlugin.Windows;

public class DialogueRecorderWindow : Window, IDisposable
{

    /// <summary>A flat record pointing at one QuestEvent in one Objectective.</summary>
    private class DialogueLine
    {
        public int ObjectectiveIndex;
        public int EventIndex;
        public QuestObjective Objectective;
        public QuestEvent Event;
    }

    private enum RecorderState { Idle, Recording, Transcribing, GeneratingTts }

    private readonly Plugin _plugin;

    // Data
    private RoleplayingQuest _quest;
    private string _questFolder;
    /// <summary>All lines grouped by NPC name (alias takes priority over name).</summary>
    private Dictionary<string, List<DialogueLine>> _linesByNpc = new();
    private List<string> _npcOrder = new();    
    private bool _dirty = true;       
    private DialogueLine _recordingLine;
    private WaveInEvent _waveIn;
    private MemoryStream _recordingStream;
    private WaveFileWriter _waveWriter;
    private RecorderState _state = RecorderState.Idle;
    private string _statusMessage = "";

    private HashSet<DialogueLine> _pendingTts = new();
    private bool _bulkRunning = false;

    private WaveOutEvent _playbackOut;
    private AudioFileReader _playbackReader;
    private DialogueLine _playingLine;

    private float _globalScale;
    private bool _collapseAll = false;
    private Dictionary<string, bool> _groupCollapsed = new();
    private bool _bringToFrontOnNextDraw = false;

    public DialogueRecorderWindow(Plugin plugin)
        : base("Dialogue Recorder##" + Guid.NewGuid().ToString(), ImGuiWindowFlags.NoScrollbar)
    {
        Size = new Vector2(1100, 650);
        SizeCondition = ImGuiCond.FirstUseEver;
        _plugin = plugin;
    }

    public void OpenForQuest(RoleplayingQuest quest, string questFolder)
    {
        _quest = quest;
        _questFolder = questFolder;
        _dirty = true;
        _bringToFrontOnNextDraw = true;
        IsOpen = true;
    }

    public override void OnOpen()
    {
        _dirty = true;
        _bringToFrontOnNextDraw = true;
    }

    public override void OnClose()
    {
        StopRecording(save: false);
        StopPlayback();
    }

    public void Dispose()
    {
        StopRecording(save: false);
        StopPlayback();
    }

    public override void Draw()
    {

        _globalScale = ImGuiHelpers.GlobalScale;

        if (_quest == null)
        {
            ImGui.TextDisabled("No quest loaded. Open a quest in the Quest Creator first.");
            return;
        }

        if (_dirty)
        {
            RebuildLines();
            _dirty = false;
        }

        DrawToolbar();
        ImGui.Separator();
        ImGui.BeginChild("##dialogueRecorder_scroll", new Vector2(0, 0), false, ImGuiWindowFlags.HorizontalScrollbar);
        DrawLineTable();
        ImGui.EndChild();
    }
    private void DrawToolbar()
    {
        int total = _linesByNpc.Values.Sum(l => l.Count);
        int filled = _linesByNpc.Values.SelectMany(l => l).Count(l => CheckAudioExists(l.Event.DialogueAudio));
        ImGui.Text($"Quest: {_quest.QuestName}   |   Lines: {filled} / {total} have audio");

        ImGui.SameLine(0, 20);

        // Refresh
        if (ImGui.Button("Refresh##dialogueRecorder_refresh"))
        {
            _dirty = true;
        }

        ImGui.SameLine();

        if (Plugin.Instance.Configuration.AccessibilityReaderActive)
        {
            // Bulk TTS
            bool busy = _state != RecorderState.Idle || _bulkRunning;
            if (busy)
            {
                ImGui.BeginDisabled();
            }
            if (ImGui.Button("Generate All Missing TTS##dialogueRecorder_alltts"))
            {
                _ = BulkGenerateTtsAsync(null);
            }
            if (busy)
            {
                ImGui.EndDisabled();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Generate TTS for every line that has no audio file yet.");
            }
        }

        ImGui.SameLine();

        // Status label
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.5f, 1f), _statusMessage);
        }

        // Collapse all toggle
        ImGui.SameLine(ImGui.GetContentRegionMax().X - 120 * _globalScale);
        if (ImGui.Button(_collapseAll ? "Expand All" : "Collapse All"))
        {
            _collapseAll = !_collapseAll;
            foreach (var k in _npcOrder)
                _groupCollapsed[k] = _collapseAll;
        }
    }
    private void DrawLineTable()
    {
        foreach (var npcName in _npcOrder)
        {
            var lines = _linesByNpc[npcName];
            if (!_groupCollapsed.ContainsKey(npcName))
                _groupCollapsed[npcName] = false;
            bool collapsed = _groupCollapsed[npcName];
            int filled = lines.Count(l => CheckAudioExists(l.Event.DialogueAudio));
            string headerLabel = $"{npcName}  ({filled}/{lines.Count})##grp_{npcName}";

            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.2f, 0.3f, 0.5f, 0.8f));
            bool open = ImGui.CollapsingHeader(headerLabel,
                collapsed ? ImGuiTreeNodeFlags.None : ImGuiTreeNodeFlags.DefaultOpen);
            ImGui.PopStyleColor();
            _groupCollapsed[npcName] = !open;

            if (!open) continue;
            if (Plugin.Instance.Configuration.AccessibilityReaderActive)
            {
                bool busy = _state != RecorderState.Idle || _bulkRunning;
                if (busy) ImGui.BeginDisabled();
                if (ImGui.Button($"Generate All TTS for {npcName}##npcgen_{npcName}"))
                    _ = BulkGenerateTtsAsync(npcName);
                if (busy) ImGui.EndDisabled();
                ImGui.Spacing();
            }
            if (!ImGui.BeginTable($"##table_{npcName}", 8,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollX | ImGuiTableFlags.SizingFixedFit))
                continue;

            ImGui.TableSetupColumn("Object#", ImGuiTableColumnFlags.WidthFixed, 40 * _globalScale);
            ImGui.TableSetupColumn("Event#", ImGuiTableColumnFlags.WidthFixed, 40 * _globalScale);
            ImGui.TableSetupColumn("Dialogue", ImGuiTableColumnFlags.WidthFixed, 480 * _globalScale);
            ImGui.TableSetupColumn("Audio Path", ImGuiTableColumnFlags.WidthFixed, 240 * _globalScale);
            ImGui.TableSetupColumn("Has Audio", ImGuiTableColumnFlags.WidthFixed, 70 * _globalScale);
            ImGui.TableSetupColumn("Record", ImGuiTableColumnFlags.WidthFixed, 65 * _globalScale);
            ImGui.TableSetupColumn("TTS", ImGuiTableColumnFlags.WidthFixed, 55 * _globalScale);
            ImGui.TableSetupColumn("Play", ImGuiTableColumnFlags.WidthFixed, 55 * _globalScale);
            ImGui.TableHeadersRow();

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                DrawLineRow(line, i);
            }

            ImGui.EndTable();
            ImGui.Spacing();
        }
    }

    private void DrawLineRow(DialogueLine line, int rowIdx)
    {
        ImGui.TableNextRow();
        string uid = $"{line.ObjectectiveIndex}_{line.EventIndex}_{rowIdx}";

        ImGui.TableSetColumnIndex(0);
        ImGui.Text((line.ObjectectiveIndex + 1).ToString());

        ImGui.TableSetColumnIndex(1);
        ImGui.Text((line.EventIndex + 1).ToString());

        ImGui.TableSetColumnIndex(2);
        string dlg = line.Event.Dialogue ?? "";

        string display = dlg.Length > 90 ? dlg.Substring(0, 87) + "..." : dlg;
        ImGui.TextUnformatted(display);
        if (dlg.Length > 90 && ImGui.IsItemHovered())
            ImGui.SetTooltip(dlg);

        ImGui.TableSetColumnIndex(3);
        string audioPath = line.Event.DialogueAudio ?? "";
        ImGui.SetNextItemWidth(230 * _globalScale);
        if (ImGui.InputText($"##audiopath_{uid}", ref audioPath, 512))
            line.Event.DialogueAudio = audioPath;

        ImGui.TableSetColumnIndex(4);
        bool hasAudio = CheckAudioExists(line.Event.DialogueAudio);
        if (hasAudio)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 1f, 0.4f, 1f));
            ImGui.Text("  ✓");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
            ImGui.Text("  ✗");
            ImGui.PopStyleColor();
        }

        ImGui.TableSetColumnIndex(5);
        bool isRecordingThis = _state == RecorderState.Recording && _recordingLine == line;
        bool isTranscribingThis = _state == RecorderState.Transcribing && _recordingLine == line;
        bool busy = (_state != RecorderState.Idle && !isRecordingThis) || _bulkRunning;

        if (isRecordingThis)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.85f, 0.2f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.95f, 0.3f, 0.3f, 1f));
        }
        if (busy) ImGui.BeginDisabled();

        ImGui.PushFont(UiBuilder.IconFont);
        if (ImGui.Button(FontAwesomeIcon.Microphone.ToIconString() + $"##rec_{uid}", new Vector2(28 * _globalScale, 0)))
            ToggleRecording(line);
        ImGui.PopFont();

        if (busy) ImGui.EndDisabled();
        if (isRecordingThis)
            ImGui.PopStyleColor(2);

        if (ImGui.IsItemHovered())
        {
            if (isRecordingThis) ImGui.SetTooltip("Recording… click to stop & save.");
            else if (isTranscribingThis) ImGui.SetTooltip("Saving audio…");
            else ImGui.SetTooltip("Record audio from your microphone.");
        }

        ImGui.TableSetColumnIndex(6);
        bool ttsInProgress = _pendingTts.Contains(line);
        bool ttsBusy = busy || ttsInProgress;
        if (ttsBusy) ImGui.BeginDisabled();

        ImGui.PushFont(UiBuilder.IconFont);
        if (ImGui.Button(FontAwesomeIcon.VolumeUp.ToIconString() + $"##tts_{uid}", new Vector2(28 * _globalScale, 0)))
            _ = GenerateTtsForLineAsync(line);
        ImGui.PopFont();

        if (ttsBusy) ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
        {
            if (ttsInProgress) ImGui.SetTooltip("Generating TTS…");
            else ImGui.SetTooltip("Generate TTS audio using the configured provider.");
        }

        ImGui.TableSetColumnIndex(7);
        bool hasAudioForPlay = CheckAudioExists(line.Event.DialogueAudio);
        bool isPlayingThis = _playingLine == line && _playbackOut != null
                             && _playbackOut.PlaybackState == PlaybackState.Playing;

        if (!hasAudioForPlay) ImGui.BeginDisabled();

        if (isPlayingThis)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.2f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.75f, 0.3f, 1f));
        }

        ImGui.PushFont(UiBuilder.IconFont);
        string playIcon = isPlayingThis
            ? FontAwesomeIcon.Stop.ToIconString()
            : FontAwesomeIcon.Play.ToIconString();
        if (ImGui.Button(playIcon + $"##play_{uid}", new Vector2(28 * _globalScale, 0)))
        {
            if (isPlayingThis)
                StopPlayback();
            else
                PlayLine(line);
        }
        ImGui.PopFont();

        if (isPlayingThis)
            ImGui.PopStyleColor(2);

        if (!hasAudioForPlay) ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
        {
            if (!hasAudioForPlay) ImGui.SetTooltip("No audio file set.");
            else if (isPlayingThis) ImGui.SetTooltip("Stop playback.");
            else ImGui.SetTooltip("Preview this line's audio.");
        }
    }


    private void RebuildLines()
    {
        _linesByNpc.Clear();
        _npcOrder.Clear();

        if (_quest == null) return;

        int ObjectectIndex = 0;
        foreach (var Object in _quest.QuestObjectives)
        {
            CollectFromObjectective(Object, ref ObjectectIndex);
        }
    }

    private void CollectFromObjectective(QuestObjective Object, ref int ObjectectIdx)
    {
        int myIdx = ObjectectIdx++;
        for (int EventIdx = 0; EventIdx < Object.QuestText.Count; EventIdx++)
        {
            var Event = Object.QuestText[EventIdx];
            // Skip events with no meaningful dialogue
            if (string.IsNullOrWhiteSpace(Event.Dialogue)) continue;

            // Key by alias if set, otherwise name
            string key = !string.IsNullOrWhiteSpace(Event.NpcAlias) ? Event.NpcAlias : Event.NpcName;
            if (!_linesByNpc.ContainsKey(key))
            {
                _linesByNpc[key] = new List<DialogueLine>();
                _npcOrder.Add(key);
            }
            _linesByNpc[key].Add(new DialogueLine
            {
                ObjectectiveIndex = myIdx,
                EventIndex = EventIdx,
                Objectective = Object,
                Event = Event
            });
        }

        foreach (var sub in Object.SubObjectives)
            CollectFromObjectective(sub, ref ObjectectIdx);
    }

    private static bool AudioPathIsSet(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        // Default placeholder values considered "not set"
        if (path.Equals("none.mp3", StringComparison.OrdinalIgnoreCase)) return false;
        return File.Exists(path);
    }

    private string ResolveAudioPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Equals("none.mp3", StringComparison.OrdinalIgnoreCase))
            return "";

        if (File.Exists(path))
            return path;

        if (!string.IsNullOrWhiteSpace(_questFolder))
        {
            string inQuest = Path.Combine(_questFolder, path);
            if (File.Exists(inQuest))
                return inQuest;
        }

        return path;
    }

    private bool CheckAudioExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Equals("none.mp3", StringComparison.OrdinalIgnoreCase))
            return false;

        string resolved = ResolveAudioPath(path);
        return File.Exists(resolved);
    }

    private void ToggleRecording(DialogueLine line)
    {
        if (_state == RecorderState.Recording && _recordingLine == line)
        {
            StopRecording(save: true);
        }
        else
        {
            StopRecording(save: false);
            StartRecording(line);
        }
    }

    private void StartRecording(DialogueLine line)
    {
        try
        {
            _recordingLine = line;
            _recordingStream = new MemoryStream();
            _waveIn = new WaveInEvent
            {
                DeviceNumber = -1, // Windows default mic
                WaveFormat = new WaveFormat(44100, 1)
            };
            _waveWriter = new WaveFileWriter(_recordingStream, _waveIn.WaveFormat);
            _waveIn.DataAvailable += OnWaveDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();
            _state = RecorderState.Recording;
            _statusMessage = $"Recording: {line.Event.NpcName}…";
        }
        catch (Exception ex)
        {
            _plugin.PluginLog.Warning(ex, "DialogueRecorder: failed to start recording.");
            _state = RecorderState.Idle;
            _statusMessage = "Mic error: " + ex.Message;
        }
    }

    private void StopRecording(bool save)
    {
        if (_waveIn == null) return;
        try
        {
            _waveIn.StopRecording();
        }
        catch { }

        if (save && _recordingLine != null)
        {
            _state = RecorderState.Transcribing;
            Task.Run(() => SaveRecording(_recordingLine));
        }
        else
        {
            CleanupRecorder();
            _state = RecorderState.Idle;
            _statusMessage = "";
        }
    }

    private void OnWaveDataAvailable(object sender, WaveInEventArgs e)
    {
        _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnRecordingStopped(object sender, StoppedEventArgs e)
    {

    }

    private void SaveRecording(DialogueLine line)
    {
        try
        {
            _waveWriter?.Flush();
            _waveWriter?.Dispose();
            _waveWriter = null;

            byte[] wavData = _recordingStream?.ToArray() ?? Array.Empty<byte>();
            if (wavData.Length < 100)
            {
                _statusMessage = "Recording was empty — not saved.";
                CleanupRecorder();
                _state = RecorderState.Idle;
                return;
            }

            string folder = GetAudioFolder();
            Directory.CreateDirectory(folder);

            string safeName = SanitizeFileName(
                (!string.IsNullOrWhiteSpace(line.Event.NpcAlias) ? line.Event.NpcAlias : line.Event.NpcName)
                + $"_Object{line.ObjectectiveIndex + 1}_Event{line.EventIndex + 1}");
            string outPath = Path.Combine(folder, safeName + ".wav");

            File.WriteAllBytes(outPath, wavData);
            line.Event.DialogueAudio = GetStorageAudioPath(outPath);
            _statusMessage = $"Saved: {Path.GetFileName(outPath)}";
        }
        catch (Exception ex)
        {
            _plugin.PluginLog.Warning(ex, "DialogueRecorder: failed to save recording.");
            _statusMessage = "Save error: " + ex.Message;
        }
        finally
        {
            CleanupRecorder();
            _state = RecorderState.Idle;
        }
    }

    private void CleanupRecorder()
    {
        try { _waveIn?.Dispose(); } catch { }
        try { _waveWriter?.Dispose(); } catch { }
        try { _recordingStream?.Dispose(); } catch { }
        _waveIn = null;
        _waveWriter = null;
        _recordingStream = null;
        _recordingLine = null;
    }

    private void PlayLine(DialogueLine line)
    {
        StopPlayback();

        string path = ResolveAudioPath(line.Event.DialogueAudio);
        if (!File.Exists(path)) return;

        try
        {
            _playbackReader = new AudioFileReader(path);
            _playbackOut = new WaveOutEvent();
            _playbackOut.Init(_playbackReader);
            _playbackOut.PlaybackStopped += OnPlaybackStopped;
            _playingLine = line;
            _playbackOut.Play();
        }
        catch (Exception ex)
        {
            _plugin.PluginLog.Warning(ex, "DialogueRecorder: playback failed.");
            _statusMessage = "Playback error: " + ex.Message;
            StopPlayback();
        }
    }

    private void StopPlayback()
    {
        try { _playbackOut?.Stop(); } catch { }
        try { _playbackOut?.Dispose(); } catch { }
        try { _playbackReader?.Dispose(); } catch { }
        _playbackOut = null;
        _playbackReader = null;
        _playingLine = null;
    }

    private void OnPlaybackStopped(object sender, StoppedEventArgs e)
    {
        try { _playbackOut?.Dispose(); } catch { }
        try { _playbackReader?.Dispose(); } catch { }
        _playbackOut = null;
        _playbackReader = null;
        _playingLine = null;
    }


    private async Task GenerateTtsForLineAsync(DialogueLine line)
    {
        if (_plugin.RoleplayingMediaManagerInstance == null)
        {
            _statusMessage = "TTS unavailable: no media manager.";
            return;
        }

        _pendingTts.Add(line);
        _statusMessage = $"Generating TTS for {line.Event.NpcName}…";
        try
        {
            string audioPath = await InvokeTts(line);
            if (!string.IsNullOrWhiteSpace(audioPath) && File.Exists(audioPath))
            {
                string destinationPath = MoveTtsAudioToQuestFolder(line, audioPath);
                line.Event.DialogueAudio = GetStorageAudioPath(destinationPath);
                _statusMessage = $"TTS saved: {Path.GetFileName(destinationPath)}";
            }
            else
            {
                _statusMessage = "TTS returned no audio.";
            }
        }
        catch (Exception ex)
        {
            _plugin.PluginLog.Warning(ex, "DialogueRecorder: TTS generation failed.");
            _statusMessage = "TTS error: " + ex.Message;
        }
        finally
        {
            _pendingTts.Remove(line);
        }
    }

    private async Task BulkGenerateTtsAsync(string npcFilter)
    {
        if (_plugin.RoleplayingMediaManagerInstance == null)
        {
            _statusMessage = "TTS unavailable: no media manager.";
            return;
        }

        _bulkRunning = true;
        try
        {
            var groups = npcFilter != null
                ? _linesByNpc.Where(kv => kv.Key == npcFilter)
                : _linesByNpc;

            var linesToProcess = groups
                .SelectMany(kv => kv.Value)
                .Where(l => !CheckAudioExists(l.Event.DialogueAudio))
                .ToList();

            int done = 0;
            foreach (var line in linesToProcess)
            {
                _statusMessage = $"TTS {++done}/{linesToProcess.Count}: {line.Event.NpcName}…";
                _pendingTts.Add(line);
                try
                {
                    string audioPath = await InvokeTts(line);
                    if (!string.IsNullOrWhiteSpace(audioPath) && File.Exists(audioPath))
                    {
                        string destinationPath = MoveTtsAudioToQuestFolder(line, audioPath);
                        line.Event.DialogueAudio = GetStorageAudioPath(destinationPath);
                    }
                }
                catch (Exception ex)
                {
                    _plugin.PluginLog.Warning(ex, $"DialogueRecorder bulk TTS failed for event {line.EventIndex}.");
                }
                finally
                {
                    _pendingTts.Remove(line);
                }
            }
            _statusMessage = $"{done} lines generated.";
        }
        finally
        {
            _bulkRunning = false;
        }
    }

    /// <summary>
    /// Routes to the correct TTS backend based on AccessibilityTextProvider.
    /// Uses the NPC's voice from NpcCustomizations if available.
    /// </summary>
    private Task<string> InvokeTts(DialogueLine line)
    {
        var mgr = _plugin.RoleplayingMediaManagerInstance;
        string npcName = !string.IsNullOrWhiteSpace(line.Event.NpcAlias)
            ? line.Event.NpcAlias : line.Event.NpcName;
        string text = line.Event.Dialogue;

        // Look up voice override from quest's NPC customizations
        string voiceOverride = GetNpcVoice(npcName);

        int provider = _plugin.Configuration.AccessibilityTextProvider;
        // 0 = Fish Audio, 1 = Microsoft Narrator, 2 = Self Hosted (CTTS)

        string effectiveSender = !string.IsNullOrWhiteSpace(voiceOverride) ? voiceOverride : npcName;

        return provider switch
        {
            0 => mgr.DoVoiceFishAudioWithSpecificVoice(effectiveSender, text, false, 1f, System.Numerics.Vector3.Zero, false),
            1 => mgr.DoVoiceMicrosoftNarratorWithSpecificVoice(effectiveSender, text, false, 1f, System.Numerics.Vector3.Zero, false),
            2 => mgr.DoCTTSWithSpecificVoice(effectiveSender, text, false, 1f, System.Numerics.Vector3.Zero, false),
            _ => mgr.DoVoiceMicrosoftNarratorWithSpecificVoice(effectiveSender, text, false, 1f, System.Numerics.Vector3.Zero, false),
        };
    }

    private string GetNpcVoice(string npcName)
    {
        if (_quest?.NpcCustomizations == null) return "";
        foreach (var kv in _quest.NpcCustomizations)
        {
            if (kv.Value.NpcName.Equals(npcName, StringComparison.OrdinalIgnoreCase)
                || kv.Value.NameplateAlias.Equals(npcName, StringComparison.OrdinalIgnoreCase))
                return kv.Value.Voice ?? "";
        }
        return "";
    }

    private string MoveTtsAudioToQuestFolder(DialogueLine line, string sourceAudioPath)
    {
        try
        {
            string folder = GetAudioFolder();
            Directory.CreateDirectory(folder);

            string ext = Path.GetExtension(sourceAudioPath);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".mp3";

            string safeName = SanitizeFileName(
                (!string.IsNullOrWhiteSpace(line.Event.NpcAlias) ? line.Event.NpcAlias : line.Event.NpcName)
                + $"_Object{line.ObjectectiveIndex + 1}_Event{line.EventIndex + 1}");

            string destPath = Path.Combine(folder, safeName + ext);

            // If already at destPath, do nothing
            if (string.Equals(Path.GetFullPath(sourceAudioPath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                return destPath;

            File.Copy(sourceAudioPath, destPath, true);
            return destPath;
        }
        catch (Exception ex)
        {
            _plugin.PluginLog.Warning(ex, "Failed to copy TTS voice file to quest directory.");
            return sourceAudioPath;
        }
    }

    private string GetStorageAudioPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return "";

        if (!string.IsNullOrWhiteSpace(_questFolder))
        {
            string normalizedFolder = Path.GetFullPath(_questFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedFull = Path.GetFullPath(fullPath);
            if (normalizedFull.StartsWith(normalizedFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFull.Substring(normalizedFolder.Length + 1);
            }
        }

        return fullPath;
    }

    private string GetAudioFolder()
    {
        if (!string.IsNullOrWhiteSpace(_questFolder))
        {
            return Path.Combine(_questFolder, "audio");
        }
        return Path.Combine(_plugin.Configuration.QuestInstallFolder, "recorded_audio");
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char character in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(character, '_');
        }
        return name.Length > 80 ? name.Substring(0, 80) : name;
    }
}
