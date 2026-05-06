using Lumina.Excel;
using Lumina.Data;
using System;
using System.Reflection;

class Program
{
    static void Main()
    {
        try {
            var lumina = new Lumina.GameData("C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack");
            var sheet = lumina.GetExcelSheet<Lumina.Excel.Sheets.ModelChara>();
            foreach (var prop in typeof(Lumina.Excel.Sheets.ModelChara).GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                Console.WriteLine(prop.Name + " : " + prop.PropertyType.Name);
            }
        } catch (Exception ex) {
            Console.WriteLine(ex.ToString());
        }
    }
}
