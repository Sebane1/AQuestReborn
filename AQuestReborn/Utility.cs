using FFXIVClientStructs;
using System;
using System.Collections.Generic;

namespace AQuestReborn
{
    public static class Utility
    {
        public static string[] FillNewList(int count, string phrase)
        {
            List<string> values = new List<string>();
            for (int i = 0; i < count; i++)
            {
                values.Add(phrase + " " + i);
            }
            return values.ToArray();
        }
        public static string ArrayToString(this string[] strings)
        {
            string finalString = "";
            foreach (string s in strings)
            {
                finalString += s + ",";
            }
            return finalString.TrimEnd(',');
        }
        public static string[] StringToArray(this string strings)
        {
            string[] finalStrings = strings.Split(",");
            return finalStrings;
        }
    }
}
