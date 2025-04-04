using Dalamud.Interface.Windowing;
using ImGuiNET;
using RoleplayingQuestCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace SamplePlugin.Windows;

public class ChoiceWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;
    int _index = 0;
    int _currentCharacter = 0;
    string _targetText = "";
    string _currentText = "";
    string _currentName = "";
    Stopwatch _timeSinceLastChoiceMade = new Stopwatch();
    List<BranchingChoice> _branchingChoices = new List<BranchingChoice>();

    public Stopwatch TimeSinceLastChoiceMade { get => _timeSinceLastChoiceMade; set => _timeSinceLastChoiceMade = value; }

    public event EventHandler<int> OnChoiceMade;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public ChoiceWindow(Plugin plugin)
        : base("Choice Window##dialoguewindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize)
    {
        Size = new Vector2(800, 200);
        Plugin = plugin;
        _timeSinceLastChoiceMade.Start();
    }


    public void Dispose() { }

    public override void Draw()
    {
        var values = ImGui.GetWindowViewport().WorkSize;
        Position = new Vector2((values.X / 2) - (Size.Value.X / 2), (values.Y / 2) - (Size.Value.Y / 2) - (Size.Value.Y * 0.1f));
        int i = 0;
        foreach (var choice in _branchingChoices)
        {
            ImGui.SetWindowFontScale(1.5f);
            if (ImGui.Button(choice.ChoiceText + "##" + i, new Vector2(Size.Value.X, 40)) && IsOpen)
            {
                OnChoiceMade?.Invoke(this, i);
                _timeSinceLastChoiceMade.Restart();
                IsOpen = false;
                break;
            }
            i++;
        }
    }
    public void NewList(List<BranchingChoice> branchingChoiceList)
    {
        if (!IsOpen)
        {
            IsOpen = true;
            _branchingChoices = branchingChoiceList;
        }
    }
}
