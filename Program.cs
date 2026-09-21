using System;
using System.Windows.Forms;

namespace Chubby
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