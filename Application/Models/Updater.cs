using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace ToolKitV.Models
{
    public class Updater
    {
        private const string RepoOwner = "TGTheAnimator";
        private const string RepoName = "ToolKitV";
        private const string ApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

        public static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "3.0.0";

        public class ReleaseInfo
        {
            public string tag_name { get; set; } = string.Empty;
            public string html_url { get; set; } = string.Empty;
            public List<Asset> assets { get; set; } = new();

            public class Asset
            {
                public string name { get; set; } = string.Empty;
                public string browser_download_url { get; set; } = string.Empty;
            }
        }

        public static async Task<ReleaseInfo?> CheckForUpdatesAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "TGToolKit-Updater");
                
                var response = await client.GetStringAsync(ApiUrl);
                var release = JsonSerializer.Deserialize<ReleaseInfo>(response);

                if (release != null)
                {
                    var latestVersion = release.tag_name.TrimStart('v');
                    if (IsNewerVersion(latestVersion, CurrentVersion))
                    {
                        return release;
                    }
                }
            }
            catch (Exception ex)
            {
                // Silently log to file for debugging if needed
                try { File.AppendAllText("log.txt", $"\n[{DateTime.Now}] Update check failed: {ex.Message}"); } catch { }
                Debug.WriteLine($"Update check failed: {ex.Message}");
            }

            return null;
        }

        private static bool IsNewerVersion(string latest, string current)
        {
            if (Version.TryParse(latest, out var vLatest) && Version.TryParse(current, out var vCurrent))
            {
                return vLatest > vCurrent;
            }
            return false;
        }

        public static async Task ApplyUpdateAsync(ReleaseInfo release)
        {
            var asset = release.assets.Find(a => a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null) return;

            string tempZip = Path.Combine(Path.GetTempPath(), "TGToolKit_Update.zip");
            string extractPath = Path.Combine(Path.GetTempPath(), "TGToolKit_Update_Extract");

            try
            {
                using var client = new HttpClient();
                var data = await client.GetByteArrayAsync(asset.browser_download_url);
                await File.WriteAllBytesAsync(tempZip, data);

                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, extractPath);

                string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                string currentDir = AppDomain.CurrentDomain.BaseDirectory;

                // Simple PowerShell script to wait for exit, move files, and restart
                string psScript = $@"
Start-Sleep -Seconds 2
Get-ChildItem -Path '{extractPath}' -Recurse | ForEach-Object {{
    $dest = Join-Path '{currentDir}' $_.FullName.Substring('{extractPath}'.Length).TrimStart('\')
    if ($_.PSIsContainer) {{
        if (!(Test-Path $dest)) {{ New-Item -ItemType Directory -Path $dest }}
    }} else {{
        Copy-Item -Path $_.FullName -Destination $dest -Force
    }}
}}
Start-Process '{currentExe}'
Remove-Item '{tempZip}' -Force
Remove-Item '{extractPath}' -Recurse -Force
";
                var psBase64 = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psScript));
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {psBase64}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to apply update: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
