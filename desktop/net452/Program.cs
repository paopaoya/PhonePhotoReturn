using System;
using System.Windows.Forms;

namespace PhonePhotoReturn
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            AssemblyLoader.Register();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
