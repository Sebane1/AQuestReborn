using System;
using System.Numerics;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;

namespace SamplePlugin.Windows;

public class MainWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;
    private FileDialogManager _fileDialogManager;
    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MainWindow(Plugin plugin)
        : base("A Quest Reborn##mainwindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
        _fileDialogManager = new FileDialogManager();
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.Button("Quest Creator"))
        {
            Plugin.EditorWindow.IsOpen = true;
        }
        _fileDialogManager.Draw();
        if (ImGui.Button("Load Quest"))
        {
            _fileDialogManager.Reset();
            ImGui.OpenPopup("OpenPathDialog##editorwindow");
        }
        if (ImGui.BeginPopup("OpenPathDialog##editorwindow"))
        {
            _fileDialogManager.OpenFileDialog("Select quest file", ".quest", (isOk, folder) =>
            {
                if (isOk)
                {
                    Plugin.RoleplayingQuestManager.AddQuest(folder[0]);
                    Plugin.Configuration.Save();
                }
            }, 0, null, true);
            ImGui.EndPopup();
        }
    }
}
