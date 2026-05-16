using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace ToolKitV.Views
{
    public partial class Credits : UserControl
    {
        // Update these to point to your own GitHub / Discord if desired.
        private readonly string GitHubUrl = "https://github.com/TGTheAnimator";
        private readonly string DiscordUrl = "";   // leave empty to disable button

        public string Version { get; set; } = "";

        public Credits()
        {
            InitializeComponent();
            Version = "v" + GetType().Assembly.GetName().Version?.ToString() ?? "";
            DataContext = this;
        }

        private static void OpenLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            using Process proc = new();
            proc.StartInfo.FileName        = "cmd";
            proc.StartInfo.Arguments       = $"/c start {url}";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow  = true;
            proc.Start();
        }

        // Discord button
        private void Button_Click(object sender, RoutedEventArgs e)
            => OpenLink(DiscordUrl);

        // Site / GitHub button
        private void Button_Click_1(object sender, RoutedEventArgs e)
            => OpenLink(GitHubUrl);
    }
}
