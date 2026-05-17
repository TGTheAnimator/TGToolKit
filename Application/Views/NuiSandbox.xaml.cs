using CefSharp;
using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ToolKitV.Views
{
    public partial class NuiSandbox : UserControl
    {
        private static string _lastDirectory = "";

        public NuiSandbox()
        {
            InitializeComponent();
            
            // Set a default payload for testing
            txtJsonPayload.Text = "{\n  \"action\": \"open\",\n  \"data\": {\n    \"playerId\": 1\n  }\n}";
        }

        private void btnLoad_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select NUI UI File (index.html)",
                Filter = "HTML Files (*.html;*.htm)|*.html;*.htm|All Files (*.*)|*.*"
            };

            if (!string.IsNullOrEmpty(_lastDirectory) && Directory.Exists(_lastDirectory))
            {
                dlg.InitialDirectory = _lastDirectory;
            }
            else
            {
                // Try to start in a common path if available, or just My Documents
                dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            if (dlg.ShowDialog() == true)
            {
                _lastDirectory = Path.GetDirectoryName(dlg.FileName) ?? "";
                txtNuiPath.Text = dlg.FileName;

                Browser.Address = dlg.FileName;
                LogTerminal($"[SYSTEM] Loading UI from: {dlg.FileName}");
            }
        }

        private void Browser_FrameLoadEnd(object sender, FrameLoadEndEventArgs e)
        {
            if (e.Frame.IsMain)
            {
                // ─── THE FIVEM POLYFILL INJECTION ───
                // This tricks the React/Vue UI into thinking it's inside GTA V.
                string polyfillScript = @"
                    // Mock the GetParentResourceName function
                    window.GetParentResourceName = function() { return 'tgtoolkit_mock'; };

                    // Intercept fetch requests (NUI Callbacks)
                    const originalFetch = window.fetch;
                    window.fetch = async function(resource, config) {
                        if (typeof resource === 'string' && resource.startsWith('https://')) {
                            
                            // Parse the data being sent to Lua
                            let bodyData = config && config.body ? config.body : '{}';
                            
                            // Print it to the CefSharp Console (which we catch in C#)
                            console.log('TG_INTERCEPT|' + resource + '|' + bodyData);
                            
                            // Return a mock OK response so the UI doesn't throw an error
                            return new Response('ok', { status: 200 });
                        }
                        return originalFetch(resource, config);
                    };
                ";

                e.Frame.ExecuteJavaScriptAsync(polyfillScript);
                
                Dispatcher.Invoke(() => LogTerminal("[SYSTEM] FiveM NUI Polyfill Injected Successfully."));
            }
        }

        // Catch console.log messages from the Chromium browser
        private void Browser_ConsoleMessage(object sender, ConsoleMessageEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.Message.StartsWith("TG_INTERCEPT|"))
                {
                    // Format intercepted callbacks from the UI
                    string[] parts = e.Message.Split(new[] { '|' }, 3);
                    string endpoint = parts.Length > 1 ? parts[1].Replace("https://", "") : "Unknown Endpoint";
                    string payload = parts.Length > 2 ? parts[2] : "{}";
                    
                    LogTerminal($"[NUI CALLBACK] Endpoint: {endpoint}\n   Payload: {payload}");
                }
                else
                {
                    // Standard JS errors or logs
                    LogTerminal($"[JS LOG] Line {e.Line}: {e.Message}");
                }
            });
        }

        private void btnFireEvent_Click(object sender, RoutedEventArgs e)
        {
            string json = txtJsonPayload.Text;
            
            // ─── EMIT EVENT TO UI ───
            // FiveM sends data to UIs using standard Window MessageEvents.
            // We format the user's JSON and dispatch it directly into the browser DOM.
            string jsCommand = $@"
                window.dispatchEvent(new MessageEvent('message', {{
                    data: {json}
                }}));
            ";

            Browser.ExecuteScriptAsync(jsCommand);
            LogTerminal("[EMITTED] Pushed JSON payload into NUI.");
        }

        private void btnDevTools_Click(object sender, RoutedEventArgs e)
        {
            Browser.ShowDevTools();
        }

        private void LogTerminal(string message)
        {
            txtConsoleLog.Text += message + "\n\n";
            LogScroller.ScrollToEnd();
        }
    }
}
