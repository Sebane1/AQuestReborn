using Dalamud.Interface.Windowing;
using ElevenLabs.Models;
using ImGuiNET;
using LanguageConversionProxy;
using RoleplayingQuestCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;

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
    List<string> _choiceText = new List<string>();
    public Stopwatch TimeSinceLastChoiceMade { get => _timeSinceLastChoiceMade; set => _timeSinceLastChoiceMade = value; }

    public event EventHandler<int> OnChoiceMade;

    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public ChoiceWindow(Plugin plugin)
        : base("Choice Window##dialoguewindow", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize)
    {
        Size = new Vector2(900, 200);
        Plugin = plugin;
        _timeSinceLastChoiceMade.Start();
    }


    public void Dispose() { }

    public override void Draw()
    {
        var values = ImGui.GetWindowViewport().WorkSize;
        Position = new Vector2((values.X / 2) - (Size.Value.X / 2), (values.Y / 2) - (Size.Value.Y / 2) - (Size.Value.Y * 0.1f));
        int i = 0;
        float textSize = 0;
        ImGui.SetWindowFontScale(1.5f);
        foreach (var choice in _choiceText)
        {
            var size = ImGui.CalcTextSize(choice).X * 1.1f;
            if (size > textSize)
            {
                textSize = size;
            }
        }
        Size = new Vector2(textSize, 200);
        foreach (var choice in _choiceText)
        {
            if (ImGui.Button(choice + "##" + i, new Vector2(Size.Value.X, 40)) && IsOpen)
            {
                OnChoiceMade?.Invoke(this, i);
                _timeSinceLastChoiceMade.Restart();
                IsOpen = false;
                break;
            }
            i++;
        }
        ImGui.SetWindowFontScale(1f);
    }
    public void NewList(List<BranchingChoice> branchingChoiceList, LanguageEnum language)
    {
        if (!IsOpen)
        {
            _choiceText.Clear();
            IsOpen = true;
            _branchingChoices = branchingChoiceList;
            Task.Run(async () =>
            {
                foreach (var choice in _branchingChoices)
                {
                    _choiceText.Add(await Translator.LocalizeText(choice.ChoiceText, Plugin.Configuration.QuestLanguage, language));
                }
            });
        }
    }
}
