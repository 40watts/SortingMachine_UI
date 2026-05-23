using System;
using System.IO;
using System.Windows;

namespace SortingMachineDesktop
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            var state = new MachineState();
            var webRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");
            var apiServer = new ApiServer(state, 8050, webRoot);

            try
            {
                apiServer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Démarrage API impossible : " + ex.Message,
                    "TriCell Pilot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                app.Run(new MainWindow(state, apiServer));
            }
            finally
            {
                apiServer.Stop();
            }
        }
    }
}
