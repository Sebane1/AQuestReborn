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
    }
}
