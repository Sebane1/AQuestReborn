using System;
using System.Reflection;

class Program
{
    static void Main()
    {
        try {
            Assembly asm = Assembly.LoadFrom(@"C:\Users\stel9\AppData\Roaming\XIVLauncher\addon\Hooks\dev\FFXIVClientStructs.dll");
            Type type = asm.GetType("FFXIVClientStructs.FFXIV.Client.Game.Character.Character");
            if (type != null) {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                    Console.WriteLine(field.Name + " : " + field.FieldType.Name);
                }
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                    Console.WriteLine(prop.Name + " : " + prop.PropertyType.Name);
                }
            } else {
                Console.WriteLine("Type not found");
            }
        } catch (Exception ex) {
            Console.WriteLine(ex.ToString());
        }
    }
}
