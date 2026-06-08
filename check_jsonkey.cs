using System;
using System.Reflection;
using System.IO;
using UnityEngine;

class Program {
    static void Main() {
        try {
            Assembly asm = Assembly.LoadFrom("C:/Program Files/Steam/steamapps/common/Nuclear Option/NuclearOption_Data/Managed/Assembly-CSharp.dll");
            Console.WriteLine("Done");
        } catch (Exception ex) { 
            Console.WriteLine(ex);
        }
    }
}
