using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace SortingMachineDesktop
{
    public class MainWindow : Window
    {
        private readonly MachineState _state;
        private readonly ApiServer _apiServer;
        private readonly DispatcherTimer _tickTimer;
        private readonly Grid _root;
        private readonly Border _fallbackPanel;
        private readonly TextBlock _fallbackText;
        private WebView2 _webView;

        public MainWindow(MachineState state, ApiServer apiServer)
        {
            _state = state;
            _apiServer = apiServer;

            Title = "TriCell Pilot";
            Width = 1600;
            Height = 980;
            MinWidth = 1320;
            MinHeight = 820;
            WindowState = WindowState.Maximized;
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF6, 0xF4));

            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath));
            }

            _root = new Grid();
            Content = _root;

            _fallbackText = new TextBlock
            {
                Text = "Initialisation de l’interface en cours...",
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x25, 0x29)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(32)
            };

            _fallbackPanel = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF6, 0xF4)),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MaxWidth = 720,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "TriCell Pilot",
                            FontSize = 42,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x13, 0x19)),
                            Margin = new Thickness(32, 0, 32, 8),
                            TextAlignment = TextAlignment.Center
                        },
                        _fallbackText
                    }
                }
            };

            _root.Children.Add(_fallbackPanel);

            Loaded += OnLoaded;
            Closed += OnClosed;

            _tickTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(40)
            };
            _tickTimer.Tick += OnTick;
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                _state.Tick();
            }
            catch
            {
                // keep UI alive; diagnostics stay available via snapshot/trace
            }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _tickTimer.Start();

            try
            {
                _webView = new WebView2
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    DefaultBackgroundColor = System.Drawing.Color.FromArgb(0xF7, 0xF6, 0xF4)
                };

                _root.Children.Add(_webView);

                var userDataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webview2_user_data");
                var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await _webView.EnsureCoreWebView2Async(environment);
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                _webView.CoreWebView2.Navigate(_apiServer.BaseUrl + "?v=" + DateTime.Now.Ticks.ToString());

                _fallbackPanel.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                _fallbackText.Text = "Impossible de charger WebView2.\n\n" + ex.Message +
                                     "\n\nL’API locale reste lancée, mais l’interface V2 n’a pas pu s’ouvrir.";
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _tickTimer.Stop();
        }
    }
}
