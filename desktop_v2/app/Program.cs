using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace SortingMachineDesktop
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var singleInstance = new Mutex(true, "Global\\TriCellPilot_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "TriCell Pilot est déjà lancé. Une seule instance peut piloter la machine (port COM et API locale). Fermer l'instance existante avant d'en lancer une nouvelle.",
                        "TriCell Pilot",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

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
                    state.Shutdown();
                }
            }

            // Garantie anti-zombie: aucun thread restant ne doit garder COM1/8050 apres fermeture.
            Environment.Exit(0);
        }
    }
}
