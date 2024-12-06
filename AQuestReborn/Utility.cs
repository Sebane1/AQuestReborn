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
        public static float ConvertRadiansToDegrees(float radians)
        {
            double degrees = (180 / Math.PI) * radians;
            return (float)degrees;
        }
        public static float ConvertDegreesToRadians(float degrees)
        {
            double radians = degrees * (Math.PI / 180);
            return (float)radians;
        }
    }
}
