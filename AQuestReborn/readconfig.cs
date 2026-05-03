using System;
using System.IO;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        string json = File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "pluginConfigs", "AQuestReborn.json"));
        var matches = Regex.Matches(json, @"""NpcName"":\s*""([^""]+)"",\s*""NPCGreeting"":\s*""([^""]+)""");
        foreach (Match match in matches)
        {
            Console.WriteLine("NPC: " + match.Groups[1].Value);
            Console.WriteLine("Greeting: " + match.Groups[2].Value);
            Console.WriteLine("---");
        }
    }
}
