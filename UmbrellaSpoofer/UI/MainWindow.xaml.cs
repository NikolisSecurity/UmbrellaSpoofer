using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using UmbrellaCore;
using UmbrellaCore.Models;
using UmbrellaSpoofer.Data;
using UmbrellaSpoofer.Services;

namespace UmbrellaSpoofer.UI
{
    public partial class MainWindow : Window
    {
        private readonly SqliteStore store;
        private readonly SystemInfo sys;
        private Dictionary<string, string>? masked;
        private readonly DispatcherTimer autoBackupTimer = new DispatcherTimer();
        private readonly DispatcherTimer autoRefreshTimer = new DispatcherTimer();
        private readonly string backupFolder;
        private bool closeToTrayEnabled = true;
        private bool startMinimizedEnabled;
        private bool rememberLastTabEnabled;
        private bool startWithWindowsEnabled;

        public MainWindow()
        {
            InitializeComponent();
            SetWindowIcon();
            store = new SqliteStore();
            store.EnsureCreated();
            sys = new SystemInfo();
            backupFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UmbrellaSpoofer", "Backups");
            Directory.CreateDirectory(backupFolder);
            BackupFolderText.Text = $"Backup folder: {backupFolder}";
            AutoBackupInterval.SelectionChanged += AutoBackupInterval_SelectionChanged;
            AutoBackupToggle.Checked += AutoBackupToggle_Checked;
            AutoBackupToggle.Unchecked += AutoBackupToggle_Unchecked;
            autoBackupTimer.Tick += AutoBackupTimer_Tick;
            MainTabs.SelectionChanged += MainTabs_SelectionChanged;
            AutoRefreshInterval.SelectionChanged += AutoRefreshInterval_SelectionChanged;
            AutoRefreshToggle.Checked += AutoRefreshToggle_Checked;
            AutoRefreshToggle.Unchecked += AutoRefreshToggle_Unchecked;
            autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
            LoadSettings();
            Loaded += MainWindow_Loaded;

            LoadSystemInfo();
            LoadNetworkAdapters();
            Log("Umbrella Spoofer initialized");
            
            if (sys.IsDriverConnected())
            {
                Log("Kernel driver connection established (Ring 0 access active)");
            }
            else
            {
                Log("Warning: Kernel driver not detected. Some low-level spoofing may be restricted.");
            }
            
            Log("System ready for inventory tracking operations");
            
            this.Focus();
            this.Activate();
            
            LogsBox.Text = "Umbrella Spoofer v1.0\n";
            Log("Application initialized successfully");
            Log("Ready for system inventory tracking operations");

            LogsBox.PreviewMouseWheel += (s, e) =>
            {
                if (e.Delta > 0)
                    LogsScroll.LineUp();
                else
                    LogsScroll.LineDown();
                e.Handled = true;
            };
        }

        void SetWindowIcon()
        {
            try
            {
                var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "logo.png");
                if (!File.Exists(logoPath)) return;
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(logoPath);
                image.DecodePixelWidth = 256;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                Icon = image;
            }
            catch
            {
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        }

        void UpdateLastSpoofDisplay()
        {
            try
            {
                var (timestamp, key, value) = store.GetLastHistoryEntry();
                
                if (timestamp.HasValue)
                {
                    LastSpoofTimeText.Text = timestamp.Value.ToString("MMM dd, yyyy HH:mm");
                    LastSpoofStatusText.Text = $"Last {key} identifier generated";
                    LastSpoofTimeText.Foreground = (Brush)FindResource("SuccessBrush");
                }
                else
                {
                    LastSpoofTimeText.Text = "Never";
                    LastSpoofStatusText.Text = "No previous activity";
                    LastSpoofTimeText.Foreground = (Brush)FindResource("TextSecondaryBrush");
                }
            }
            catch (Exception ex)
            {
                LastSpoofTimeText.Text = "Error";
                LastSpoofStatusText.Text = "Failed to load history";
                LastSpoofTimeText.Foreground = (Brush)FindResource("ErrorBrush");
                Log($"Failed to update last spoof display: {ex.Message}");
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (closeToTrayEnabled && Application.Current is App app && !app.ExitRequested)
            {
                e.Cancel = true;
                WindowState = WindowState.Minimized;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        void LoadSystemInfo()
        {
            var info = sys.GetAll();
            CheckerGrid.ItemsSource = null;
            var list = new List<Row>();
            foreach (var kv in info)
                list.Add(new Row { Key = kv.Key, Current = kv.Value, Masked = "", Status = "Original" });
            CheckerGrid.ItemsSource = list;
        }

        void LoadNetworkAdapters()
        {
            try
            {
                var adapters = sys.GetNetworkAdapters();
                AdapterCombo.ItemsSource = adapters;
                
                if (adapters.Count > 0)
                {
                    AdapterCombo.SelectedIndex = 0;
                    Log($"Loaded {adapters.Count} network adapter(s)");
                }
                else
                {
                    Log("No network adapters found");
                }
            }
            catch (Exception ex)
            {
                Log($"Error loading network adapters: {ex.Message}");
            }
        }

        void Log(string text)
        {
            var timestamp = System.DateTime.Now.ToString("HH:mm:ss");
            LogsBox.AppendText($"[{timestamp}] {text}\n");
            LogsScroll.ScrollToBottom();
            
            if (LogsBox.LineCount > 100)
            {
                var lines = LogsBox.Text.Split('\n');
                if (lines.Length > 100)
                {
                    var newText = string.Join("\n", lines[^100..]);
                    LogsBox.Text = newText;
                }
            }
        }

        private void GenerateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (CheckerGrid.ItemsSource is not List<Row> rows || rows.Count == 0)
            {
                MessageBox.Show("No system data loaded yet. Click Refresh in System Info first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                Log("Generate preview skipped: no system data available");
                return;
            }

            masked = new Dictionary<string, string>();

            foreach (var r in rows)
            {
                var generated = GenerateRandomIdentifier(r.Key, r.Current);
                masked[r.Key] = generated;
                store.AddHistory(r.Key, generated);
                r.Masked = generated;
                r.Status = "Generated";
            }

            CheckerGrid.ItemsSource = null;
            CheckerGrid.ItemsSource = rows;
            UpdateLastSpoofDisplay();
            Log("Generated randomized tracking identifiers");
            Log("Identifiers ready for system inventory tracking");
        }

        static readonly System.Random Rng = new System.Random();

        static string GenerateRandomIdentifier(string key, string original)
        {
            return GenerateSpoofedValue(key, original);
        }

        static string GenerateSpoofedValue(string key, string original)
        {
            if (key.Contains("MAC_Addresses")) return GenerateRandomMacAddress();
            if (key.Contains("MachineGuid")) return GenerateSpoofedGuid(original);
            if (key.Contains("Volume_Serials")) return GenerateSpoofedVolumeSerials(original);
            if (key.Contains("Disk_Serials")) return GenerateSpoofedMultiField(original, GenerateSpoofedDiskSerial);
            if (key.Contains("GPU_Identifiers")) return GenerateSpoofedMultiField(original, GenerateSpoofedPnpId);
            if (key.Contains("CPU_Identifier")) return GenerateSpoofedMultiField(original, GenerateSpoofedCpuId);
            if (key.Contains("RAM_Serials")) return GenerateSpoofedMultiField(original, GenerateSpoofedRamSerial);
            if (key.Contains("Monitor_Serials")) return GenerateSpoofedMultiField(original, GenerateSpoofedMonitorSerial);
            if (key.Contains("EFI_Boot")) return GenerateSpoofedPath(original);
            if (key.Contains("BIOS_Serial")) return GetStylePreservedRandom(original);
            if (key.Contains("BaseBoard_Serial")) return GetStylePreservedRandom(original);
            if (key.Contains("EFI_Version")) return GetStylePreservedRandom(original);
            if (key.Contains("TPM_Identity")) return GenerateComponentSerial("TPM", 20);
            return GetStylePreservedRandom(original);
        }

        /// <summary>
        /// Splits comma-separated multi-values, generates a spoofed value per item, rejoins.
        /// </summary>
        static string GenerateSpoofedMultiField(string original, Func<string, string> generator)
        {
            if (string.IsNullOrEmpty(original)) return generator(original);

            var parts = original.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1) return generator(original);

            var spoofed = parts.Select(p => generator(p.Trim())).ToList();
            return string.Join(", ", spoofed);
        }

        static string GenerateSpoofedGuid(string original) => Guid.NewGuid().ToString();

        static string GenerateRandomMacAddress()
        {
            var rng = RandomNumberGenerator.Create();
            var bytes = new byte[6];
            rng.GetBytes(bytes);
            bytes[0] = (byte)((bytes[0] & 0xFE) | 0x02); // locally administered
            return string.Join(":", bytes.Select(b => b.ToString("X2")));
        }

        static string GenerateSpoofedVolumeSerials(string original)
        {
            if (string.IsNullOrEmpty(original)) return original;
            var parts = original.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(", ", parts.Select(p =>
            {
                var match = System.Text.RegularExpressions.Regex.Match(p, @"^([A-Z])::", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                    return $"{match.Groups[1].Value}::{GenerateRandomHex(8)}";
                return GenerateRandomHex(8);
            }));
        }

        static string GenerateSpoofedDiskSerial(string original)
        {
            if (string.IsNullOrEmpty(original)) return GenerateComponentSerial("DSK", 16);
            return GetStylePreservedRandom(original);
        }

        static string GenerateSpoofedPnpId(string original)
        {
            if (string.IsNullOrEmpty(original)) return GenerateComponentSerial("GPU", 16);
            var spo = original;
            var matchVen = System.Text.RegularExpressions.Regex.Match(original, @"VEN_([0-9A-Fa-f]{4})");
            var matchDev = System.Text.RegularExpressions.Regex.Match(original, @"DEV_([0-9A-Fa-f]{4})");
            if (matchVen.Success) spo = spo.Replace(matchVen.Value, "VEN_" + GenerateRandomHex(4));
            if (matchDev.Success) spo = spo.Replace(matchDev.Value, "DEV_" + GenerateRandomHex(4));
            return spo;
        }

        static string GenerateSpoofedCpuId(string original)
        {
            if (string.IsNullOrEmpty(original)) return GenerateRandomHex(16);
            return GetStylePreservedRandom(original);
        }

        static string GenerateSpoofedRamSerial(string original)
        {
            if (string.IsNullOrEmpty(original)) return GenerateRandomHex(8);
            return GetStylePreservedRandom(original);
        }

        static string GenerateSpoofedMonitorSerial(string original)
        {
            if (string.IsNullOrEmpty(original)) return GenerateComponentSerial("MON", 12);
            return GetStylePreservedRandom(original);
        }

        static string GenerateSpoofedPath(string original)
        {
            if (string.IsNullOrWhiteSpace(original)) return "\\\\Device\\\\Harddisk0\\\\Partition1";
            var match = System.Text.RegularExpressions.Regex.Match(original, @"(?i)(Harddisk)(\d+)");
            if (match.Success)
            {
                int currentDisk = int.Parse(match.Groups[2].Value);
                int newDisk;
                do { newDisk = Rng.Next(0, 5); } while (newDisk == currentDisk);
                original = System.Text.RegularExpressions.Regex.Replace(original, @"(?i)Harddisk\d+", $"Harddisk{newDisk}");
            }
            match = System.Text.RegularExpressions.Regex.Match(original, @"(?i)(Partition)(\d+)");
            if (match.Success)
            {
                int currentPart = int.Parse(match.Groups[2].Value);
                int newPart;
                do { newPart = Rng.Next(0, 5); } while (newPart == currentPart);
                original = System.Text.RegularExpressions.Regex.Replace(original, @"(?i)Partition\d+", $"Partition{newPart}");
            }
            return original;
        }

        static string GetStylePreservedRandom(string original)
        {
            if (string.IsNullOrEmpty(original)) return GenerateRandomHex(10);
            var rng = RandomNumberGenerator.Create();
            var sb = new System.Text.StringBuilder();
            foreach (char c in original)
            {
                var bytes = new byte[1];
                rng.GetBytes(bytes);
                int randVal = bytes[0];
                if (char.IsDigit(c))
                    sb.Append((randVal % 10).ToString());
                else if (char.IsUpper(c))
                    sb.Append((char)('A' + (randVal % 26)));
                else if (char.IsLower(c))
                    sb.Append((char)('a' + (randVal % 26)));
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        static string GenerateRandomHex(int length)
        {
            var rng = RandomNumberGenerator.Create();
            const string chars = "ABCDEF0123456789";
            var bytes = new byte[length];
            rng.GetBytes(bytes);
            return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
        }

        static string GenerateComponentSerial(string prefix, int randomLength)
        {
            var cleanPrefix = new string((prefix ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            var tail = GenerateRandomHex(Math.Max(randomLength, 4));
            return $"{cleanPrefix}-{tail}";
        }

        private void ApplyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (masked == null || masked.Count == 0)
            {
                MessageBox.Show("Generate tracking identifiers first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? macInterfaceIndex = null;
            if (AdapterCombo?.SelectedItem is NetworkAdapter selectedAdapter)
                macInterfaceIndex = selectedAdapter.InterfaceIndex;

            bool success;
            if (sys.IsDriverConnected())
            {
                success = sys.SpoofAll(masked, MacToggle?.IsChecked == true, macInterfaceIndex);
                if (success)
                {
                    // Reload system info to show new values from driver
                    LoadSystemInfo();
                    if (CheckerGrid.ItemsSource is List<Row> rows)
                    {
                        foreach (var r in rows)
                        {
                            if (masked.TryGetValue(r.Key, out var maskedVal))
                            {
                                r.Current = maskedVal;
                                r.Masked = maskedVal;
                                r.Status = "Applied";
                            }
                        }
                        CheckerGrid.ItemsSource = null;
                        CheckerGrid.ItemsSource = rows;
                    }
                }
            }
            else
            {
                success = sys.SpoofAll(masked, MacToggle?.IsChecked == true, macInterfaceIndex);
                if (success && CheckerGrid.ItemsSource is List<Row> rows2)
                {
                    foreach (var r in rows2)
                    {
                        if (masked.TryGetValue(r.Key, out var maskedVal))
                        {
                            r.Current = maskedVal;
                            r.Masked = maskedVal;
                            r.Status = "Applied";
                        }
                    }
                    CheckerGrid.ItemsSource = null;
                    CheckerGrid.ItemsSource = rows2;
                }
            }

            if (success)
            {
                MessageBox.Show("Identifiers sent to kernel driver successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                Log("Hardware identifiers spoofed successfully via Kernel Driver.");
                UpdateLastSpoofDisplay();
            }
            else
            {
                MessageBox.Show("Failed to communicate with Kernel Driver. Ensure the driver is running.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Log("Failed to apply spoofing via Kernel Driver.");
            }
        }

        private void QuickSpoofBtn_Click(object sender, RoutedEventArgs e)
        {
            Log("Quick Spoof: Generating and applying new identifiers...");

            if (sys.IsDriverConnected())
            {
                var result = sys.SpoofAll(masked ?? new Dictionary<string, string>(), false, null);
                if (result)
                {
                    Log("Quick Spoof completed successfully via kernel driver");
                    LoadSystemInfo();
                    UpdateLastSpoofDisplay();
                    MessageBox.Show("Hardware identifiers spoofed successfully via Kernel Driver.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    Log("Quick Spoof failed.");
                    MessageBox.Show("Quick spoof failed. Check driver status.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            // Fallback: legacy flow
            GenerateBtn_Click(sender, e);
            if (masked != null && masked.Count > 0)
            {
                ApplyBtn_Click(sender, e);
                Log("Quick Spoof completed (legacy mode).");
            }
            else
            {
                Log("Quick Spoof aborted: identifier generation failed.");
            }
        }

        private void RestoreOriginalsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!sys.IsDriverConnected())
            {
                MessageBox.Show("Kernel driver not detected. Cannot restore originals.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Log("Restore originals skipped: driver not connected");
                return;
            }

            var confirm = MessageBox.Show(
                "This will restore all original hardware identifiers from saved backups.\nContinue?",
                "Confirm Restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var result = sys.RestoreAll();
                if (result)
                {
                    Log("All original hardware identifiers restored");
                    LoadSystemInfo();
                    MessageBox.Show("Original identifiers restored successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    Log("Restore originals returned no results");
                    MessageBox.Show("No original values available to restore.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to restore originals: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"Restore originals failed: {ex.Message}");
            }
        }

        private void ViewSerialsBtn_Click(object sender, RoutedEventArgs e)
        {
            MainTabs.SelectedIndex = 2;
            Log("Navigated to System Inventory view");

            var button = sender as System.Windows.Controls.Button;
            if (button != null)
            {
                var originalContent = button.Content;
                button.Content = "Viewed";
                button.IsEnabled = false;

                System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(1);
                timer.Tick += (s, args) =>
                {
                    button.Content = originalContent;
                    button.IsEnabled = true;
                    timer.Stop();
                };
                timer.Start();
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) 
        {
            if (closeToTrayEnabled)
            {
                Log("Minimized to tray");
                WindowState = WindowState.Minimized;
                Hide();
            }
            else
            {
                Log("Shutting down");
                Close();
            }
        }
        
        private void MinBtn_Click(object sender, RoutedEventArgs e) 
        {
            Log("Minimizing application");
            WindowState = WindowState.Minimized;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void ClearLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            LogsBox.Clear();
            Log("Logs cleared");
        }

        private void ExportLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            var logs = LogsBox.Text;
            if (string.IsNullOrWhiteSpace(logs))
            {
                MessageBox.Show("No logs to export.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = $"anatoly_logs_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, logs);
                Log($"Logs exported to {dlg.FileName}");
                MessageBox.Show("Logs exported successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
        {
            Log("Checking for updates...");
            var updater = new UpdaterService();
            var check = await updater.CheckForUpdatesAsync(true);
            if (check.InvalidConfig)
            {
                MessageBox.Show("Updater is not configured. Fill updater.json with owner and repo.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                Log("Update check skipped: invalid config");
                return;
            }
            if (!check.UpdateAvailable || check.Release == null)
            {
                MessageBox.Show("You are running the latest version.", "Up to date", MessageBoxButton.OK, MessageBoxImage.Information);
                Log("Update check completed");
                return;
            }
            var cfg = updater.LoadConfig();
            var releaseTag = check.Release.Tag ?? "new version";
            var prompt = MessageBox.Show($"Update {releaseTag} is available. Download now?", "Update available", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (prompt != MessageBoxResult.Yes)
            {
                if (!string.IsNullOrWhiteSpace(check.Release.HtmlUrl))
                {
                    Process.Start(new ProcessStartInfo { FileName = check.Release.HtmlUrl, UseShellExecute = true });
                }
                return;
            }
            var stage = await updater.DownloadAndStageAsync(cfg, check.Release);
            if (!stage.Success || string.IsNullOrWhiteSpace(stage.PendingPath))
            {
                MessageBox.Show(stage.Error ?? "Failed to download update.", "Update", MessageBoxButton.OK, MessageBoxImage.Error);
                Log("Update download failed");
                return;
            }
            var installNow = MessageBox.Show("Update downloaded. Install now?", "Update ready", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (installNow == MessageBoxResult.Yes)
            {
                UpdaterService.RestartToApplyUpdate(stage.PendingPath);
                return;
            }
            Log("Update downloaded and staged");
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            Log("Refreshing system information...");
            LoadSystemInfo();
            Log("System information refreshed");
        }

        private void ExportReportBtn_Click(object sender, RoutedEventArgs e)
        {
            var rows = CheckerGrid.ItemsSource as List<Row>;
            if (rows == null || rows.Count == 0)
            {
                MessageBox.Show("No data to export.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json",
                FileName = $"anatoly_report_{System.DateTime.Now:yyyyMMdd_HHmmss}.json"
            };
            if (dlg.ShowDialog() == true)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(rows);
                File.WriteAllText(dlg.FileName, json);
                Log($"Report exported to {dlg.FileName}");
                MessageBox.Show("Report exported successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BackupBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Backup files (*.bak)|*.bak|All files (*.*)|*.*",
                DefaultExt = "bak",
                FileName = $"umbrella_backup_{System.DateTime.Now:yyyyMMdd_HHmmss}.bak"
            };
            if (dlg.ShowDialog() == true)
            {
                var options = GetBackupOptions();
                if (!HasAnyBackupOption(options))
                {
                    MessageBox.Show("Select at least one backup item.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                WriteBackupFile(dlg.FileName, options);
                Log($"Backup saved to {dlg.FileName}");
                MessageBox.Show("Backup created successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RestoreBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Backup files (*.bak)|*.bak|All files (*.*)|*.*",
                DefaultExt = "bak",
                Title = "Select backup file to restore"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var snapshot = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (snapshot == null || snapshot.Count == 0)
                {
                    MessageBox.Show("Backup file is empty or invalid.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                var confirm = MessageBox.Show(
                    "This will restore identifiers from the selected backup. Continue?",
                    "Confirm Restore",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;

                string? GetValue(JsonElement element)
                {
                    if (element.ValueKind == JsonValueKind.String) return element.GetString();
                    if (element.ValueKind == JsonValueKind.Number) return element.ToString();
                    if (element.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in element.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String) return item.GetString();
                            if (item.ValueKind == JsonValueKind.Number) return item.ToString();
                        }
                    }
                    return null;
                }

                var restored = new List<string>();
                var failed = new List<string>();
                var skipped = new List<string>();

                foreach (var kv in snapshot)
                {
                    var value = GetValue(kv.Value);
                    bool ok;
                    switch (kv.Key)
                    {
                        case "MachineGuid":
                            ok = !string.IsNullOrWhiteSpace(value) && !string.IsNullOrEmpty(sys.SpoofMachineGuid(value));
                            break;
                        case "BIOS_Serial":
                            ok = !string.IsNullOrWhiteSpace(value) && !string.IsNullOrEmpty(sys.SpoofBiosSerial(value));
                            break;
                        case "BaseBoard_Serial":
                            ok = !string.IsNullOrWhiteSpace(value) && !string.IsNullOrEmpty(sys.SpoofBaseBoardSerial(value));
                            break;
                        case "EFI_Version":
                            ok = !string.IsNullOrWhiteSpace(value) && !string.IsNullOrEmpty(sys.SpoofEfiVersion(value));
                            break;
                        case "Monitor_Serials":
                            ok = !string.IsNullOrWhiteSpace(value) && !string.IsNullOrEmpty(sys.SpoofMonitorSerials(value));
                            break;
                        case "RAM_Serials":
                            ok = !string.IsNullOrWhiteSpace(value) && !string.IsNullOrEmpty(sys.SpoofRamSerials(value));
                            break;
                        case "CapturedAt":
                        case "MAC_Addresses":
                        case "Monitor_Registry_EDID":
                            skipped.Add(kv.Key);
                            continue;
                        default:
                            skipped.Add(kv.Key);
                            continue;
                    }
                    if (ok) restored.Add(kv.Key); else failed.Add(kv.Key);
                }

                var msg = $"Restored {restored.Count} items.";
                if (failed.Count > 0) msg += $"\nFailed: {string.Join(", ", failed)}";
                if (skipped.Count > 0) msg += $"\nSkipped: {string.Join(", ", skipped)}";
                MessageBox.Show(msg, "Restore Result", MessageBoxButton.OK, failed.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                Log($"Restored backup from {dlg.FileName}: {restored.Count} OK, {failed.Count} failed, {skipped.Count} skipped");
                LoadSystemInfo();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to restore backup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"Restore backup failed: {ex.Message}");
            }
        }

        private void AutoBackupToggle_Checked(object sender, RoutedEventArgs e)
        {
            autoBackupTimer.Interval = GetAutoBackupInterval();
            autoBackupTimer.Start();
            Log("Auto backup enabled");
        }

        private void AutoBackupToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            autoBackupTimer.Stop();
            Log("Auto backup disabled");
        }

        private void AutoBackupInterval_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (AutoBackupToggle.IsChecked == true)
            {
                autoBackupTimer.Interval = GetAutoBackupInterval();
                Log("Auto backup interval updated");
            }
        }

        private void AutoBackupTimer_Tick(object? sender, EventArgs e)
        {
            var options = GetBackupOptions();
            if (!HasAnyBackupOption(options)) return;
            var filePath = Path.Combine(backupFolder, $"umbrella_backup_{System.DateTime.Now:yyyyMMdd_HHmmss}.bak");
            WriteBackupFile(filePath, options);
            Log($"Auto backup saved to {filePath}");
        }

        TimeSpan GetAutoBackupInterval()
        {
            return AutoBackupInterval.SelectedIndex switch
            {
                0 => TimeSpan.FromMinutes(15),
                1 => TimeSpan.FromHours(1),
                2 => TimeSpan.FromHours(6),
                3 => TimeSpan.FromHours(24),
                _ => TimeSpan.FromHours(1)
            };
        }

        BackupOptions GetBackupOptions()
        {
            return new BackupOptions
            {
                MachineGuid = BackupMachineGuid.IsChecked == true,
                BiosSerial = BackupBios.IsChecked == true,
                BaseBoardSerial = BackupBaseBoard.IsChecked == true,
                EfiVersion = BackupEfi.IsChecked == true,
                MonitorSerials = BackupMonitor.IsChecked == true,
                RamSerials = BackupRam.IsChecked == true,
                MacAddresses = BackupMac.IsChecked == true,
                RegistryEdid = BackupEdid.IsChecked == true
            };
        }

        static bool HasAnyBackupOption(BackupOptions options)
        {
            return options.MachineGuid || options.BiosSerial || options.BaseBoardSerial || options.EfiVersion ||
                   options.MonitorSerials || options.RamSerials || options.MacAddresses || options.RegistryEdid;
        }

        void WriteBackupFile(string filePath, BackupOptions options)
        {
            var snapshot = sys.GetBackupSnapshot(options);
            var json = System.Text.Json.JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        private void SaveSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            store.SetSetting("ui.startMinimized", StartMinimizedToggle.IsChecked == true ? "1" : "0");
            store.SetSetting("ui.closeToTray", CloseToTrayToggle.IsChecked == true ? "1" : "0");
            store.SetSetting("ui.rememberTab", RememberTabToggle.IsChecked == true ? "1" : "0");
            store.SetSetting("ui.startWithWindows", StartWithWindowsToggle.IsChecked == true ? "1" : "0");
            store.SetSetting("ui.autoRefresh", AutoRefreshToggle.IsChecked == true ? "1" : "0");
            store.SetSetting("ui.autoRefreshInterval", AutoRefreshInterval.SelectedIndex.ToString());
            closeToTrayEnabled = CloseToTrayToggle.IsChecked == true;
            startMinimizedEnabled = StartMinimizedToggle.IsChecked == true;
            rememberLastTabEnabled = RememberTabToggle.IsChecked == true;
            startWithWindowsEnabled = StartWithWindowsToggle.IsChecked == true;
            SetStartupEnabled(startWithWindowsEnabled);
            Log("Settings saved");
            MessageBox.Show("Settings saved.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        void LoadSettings()
        {
            StartMinimizedToggle.IsChecked = GetBoolSetting("ui.startMinimized", false);
            CloseToTrayToggle.IsChecked = GetBoolSetting("ui.closeToTray", true);
            RememberTabToggle.IsChecked = GetBoolSetting("ui.rememberTab", false);
            StartWithWindowsToggle.IsChecked = GetBoolSetting("ui.startWithWindows", GetStartupEnabledFromRegistry());
            AutoRefreshToggle.IsChecked = GetBoolSetting("ui.autoRefresh", false);
            AutoRefreshInterval.SelectedIndex = GetIntSetting("ui.autoRefreshInterval", 1);
            closeToTrayEnabled = CloseToTrayToggle.IsChecked == true;
            startMinimizedEnabled = StartMinimizedToggle.IsChecked == true;
            rememberLastTabEnabled = RememberTabToggle.IsChecked == true;
            startWithWindowsEnabled = StartWithWindowsToggle.IsChecked == true;
            if (AutoRefreshToggle.IsChecked == true)
            {
                autoRefreshTimer.Interval = GetAutoRefreshInterval();
                autoRefreshTimer.Start();
            }
            if (rememberLastTabEnabled)
            {
                var idx = GetIntSetting("ui.lastTabIndex", 0);
                if (idx >= 0 && idx < MainTabs.Items.Count)
                    MainTabs.SelectedIndex = idx;
            }
        }

        bool GetBoolSetting(string key, bool fallback)
        {
            var v = store.GetSetting(key);
            if (string.IsNullOrWhiteSpace(v)) return fallback;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (startMinimizedEnabled)
            {
                WindowState = WindowState.Minimized;
                Hide();
            }
            UpdateLastSpoofDisplay();
        }

        private void AutoRefreshToggle_Checked(object sender, RoutedEventArgs e)
        {
            autoRefreshTimer.Interval = GetAutoRefreshInterval();
            autoRefreshTimer.Start();
            Log("Auto refresh enabled");
        }

        private void AutoRefreshToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            autoRefreshTimer.Stop();
            Log("Auto refresh disabled");
        }

        private void AutoRefreshInterval_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (AutoRefreshToggle.IsChecked == true)
            {
                autoRefreshTimer.Interval = GetAutoRefreshInterval();
                Log("Auto refresh interval updated");
            }
        }

        private void AutoRefreshTimer_Tick(object? sender, EventArgs e)
        {
            LoadSystemInfo();
            Log("System information auto refreshed");
        }

        TimeSpan GetAutoRefreshInterval()
        {
            return AutoRefreshInterval.SelectedIndex switch
            {
                0 => TimeSpan.FromMinutes(1),
                1 => TimeSpan.FromMinutes(5),
                2 => TimeSpan.FromMinutes(15),
                3 => TimeSpan.FromMinutes(30),
                _ => TimeSpan.FromMinutes(5)
            };
        }


        private void MainTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!rememberLastTabEnabled) return;
            store.SetSetting("ui.lastTabIndex", MainTabs.SelectedIndex.ToString());
        }

        int GetIntSetting(string key, int fallback)
        {
            var v = store.GetSetting(key);
            return int.TryParse(v, out var i) ? i : fallback;
        }

        bool GetStartupEnabledFromRegistry()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            var v = key?.GetValue("UmbrellaSpoofer")?.ToString();
            return !string.IsNullOrWhiteSpace(v);
        }

        void SetStartupEnabled(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key == null) return;
            if (enabled)
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrWhiteSpace(exe))
                    key.SetValue("UmbrellaSpoofer", $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue("UmbrellaSpoofer", false);
            }
        }

        void StoreTrackingIdentifiersInRegistry(Dictionary<string, string> identifiers)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\UmbrellaSpoofer\TrackingIDs");
                if (key == null) return;
                
                key.SetValue("GeneratedAt", DateTime.UtcNow.ToString("O"));
                
                foreach (var kv in identifiers)
                {
                    var safeKey = kv.Key.Replace("_", "").Replace(" ", "");
                    key.SetValue(safeKey, kv.Value);
                }
                
                Log("Tracking identifiers stored in registry for Umbrella Spoofer");
            }
            catch (Exception ex)
            {
                Log($"Warning: Could not store tracking identifiers: {ex.Message}");
            }
        }

        TempRestorePayload BuildTempRestorePayload(Dictionary<string, string> current, NetworkAdapter? adapter)
        {
            var values = new Dictionary<string, string>();
            if (HwidToggle?.IsChecked == true && current.TryGetValue("MachineGuid", out var v1)) values["MachineGuid"] = v1;
            if (BiosToggle?.IsChecked == true && current.TryGetValue("BIOS_Serial", out var v2)) values["BIOS_Serial"] = v2;
            if (BiosToggle?.IsChecked == true && current.TryGetValue("BaseBoard_Serial", out var v3)) values["BaseBoard_Serial"] = v3;
            if (EfiToggle?.IsChecked == true && current.TryGetValue("EFI_Version", out var v4)) values["EFI_Version"] = v4;
            if (MonitorToggle?.IsChecked == true && current.TryGetValue("Monitor_Serials", out var v5)) values["Monitor_Serials"] = v5;
            if (RamToggle?.IsChecked == true && current.TryGetValue("RAM_Serials", out var v6)) values["RAM_Serials"] = v6;
            if (DiskToggle?.IsChecked == true && current.TryGetValue("Disk_Serials", out var v7)) values["Disk_Serials"] = v7;
            if (GpuToggle?.IsChecked == true && current.TryGetValue("GPU_Identifiers", out var v8)) values["GPU_Identifiers"] = v8;
            if (VolumeToggle?.IsChecked == true && current.TryGetValue("Volume_Serials", out var v9)) values["Volume_Serials"] = v9;
            if (TpmToggle?.IsChecked == true && current.TryGetValue("TPM_Identity", out var v10)) values["TPM_Identity"] = v10;
            if (EfiBootToggle?.IsChecked == true && current.TryGetValue("EFI_Boot", out var v11)) values["EFI_Boot"] = v11;
            if (ArpToggle?.IsChecked == true && current.TryGetValue("ARP_Cache", out var v12)) values["ARP_Cache"] = v12;
            var payload = new TempRestorePayload { Values = values };
            if (MacToggle?.IsChecked == true && adapter != null)
            {
                payload.MacInterfaceIndex = adapter.InterfaceIndex;
                payload.MacAddress = adapter.MacAddress;
            }
            return payload;
        }

        string SaveTempRestorePayload(TempRestorePayload payload)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmbrellaSpoofer");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "temp_restore.json");
                File.WriteAllText(path, JsonSerializer.Serialize(payload));
                return path;
            }
            catch
            {
                return "";
            }
        }

        bool ScheduleTempRestore(string payloadPath)
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrWhiteSpace(exe)) return false;
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce");
                if (key == null) return false;
                var cmd = $"\"{exe}\" --restore-temp \"{payloadPath}\"";
                key.SetValue("UmbrellaSpooferTempRestore", cmd);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public class Row
        {
            public string Key { get; set; } = "";
            public string Current { get; set; } = "";
            public string Masked { get; set; } = "";
            public string Status { get; set; } = "";
        }
    }
}
