using System;
using System.Windows.Forms;

namespace TaskbarPet
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new ControllerForm());
        }
    }
}