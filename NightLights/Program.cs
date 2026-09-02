using System;
using System.Threading;
using System.Windows.Forms;

namespace NightLights
{
    internal static class Program
    {
        // Prevents two copies of the tray app from running at once.
        private static Mutex _singleInstanceMutex;

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "NightLights.SingleInstance.Mutex", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("NightLights is already running (check the system tray).",
                    "NightLights", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.ThreadException += (s, e) =>
                Logger.Log("Unhandled UI exception: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Logger.Log("Unhandled exception: " + e.ExceptionObject);

            Application.Run(new TrayContext());

            GC.KeepAlive(_singleInstanceMutex);
        }
    }
}
