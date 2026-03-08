using System;
using System.Threading.Tasks;
using System.Windows;
using UmbrellaSpoofer.Data;
using UmbrellaSpoofer.Services;

namespace UmbrellaSpoofer
{
    public partial class App : Application
    {
        private TrayService? tray;
        private DiscordPresenceService? discord;
        internal bool ExitRequested { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length >= 2 && e.Args[0].Equals("--restore-temp", StringComparison.OrdinalIgnoreCase))
            {
                var path = e.Args[1];
                var sys = new SystemInfoService();
                sys.RestoreFromPayloadFile(path);
                Shutdown();
                return;
            }
            if (UpdaterService.TryHandleUpdateMode(e.Args))
            {
                Shutdown();
                return;
            }
            base.OnStartup(e);
            tray = new TrayService();
            tray.Show();
            var store = new SqliteStore();
            store.EnsureCreated();
            store.EnsureDiscordDefaults("1210910570669408266", "Umbrella Spoofer", "Made by Nikolis Security");
            var enabled = store.GetSecureSetting("discord.enabled") != "0";
            var appId = store.GetSecureSetting("discord.appId") ?? "";
            var details = store.GetSecureSetting("discord.details") ?? "Umbrella Spoofer";
            var state = store.GetSecureSetting("discord.state") ?? "Made by Nikolis Security";
            if (enabled && !string.IsNullOrWhiteSpace(appId))
            {
                discord = new DiscordPresenceService();
                discord.Initialize(appId);
                discord.SetPresence(details, state);
            }
            tray.UpdateDiscordMenu(enabled);
            var updater = new UpdaterService();
            _ = TryAutoUpdateAsync(updater);
            var w = new UI.MainWindow();
            MainWindow = w;
            w.Show();
        }

        async Task TryAutoUpdateAsync(UpdaterService updater)
        {
            var cfg = updater.LoadConfig();
            if (!cfg.AutoDownload && !cfg.AutoInstall) return;
            var check = await updater.CheckForUpdatesAsync();
            if (!check.UpdateAvailable || check.Release == null) return;
            var stage = await updater.DownloadAndStageAsync(cfg, check.Release);
            if (!stage.Success || string.IsNullOrWhiteSpace(stage.PendingPath)) return;
            if (cfg.AutoInstall)
            {
                await Dispatcher.InvokeAsync(() => UpdaterService.RestartToApplyUpdate(stage.PendingPath));
            }
        }

        internal void RequestExit()
        {
            ExitRequested = true;
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            discord?.Dispose();
            tray?.Dispose();
            base.OnExit(e);
        }

        internal void UpdateDiscordSettings(string appId, bool enabled, string details, string state)
        {
            discord?.Dispose();
            discord = null;
            if (enabled && !string.IsNullOrWhiteSpace(appId))
            {
                discord = new DiscordPresenceService();
                discord.Initialize(appId);
                discord.SetPresence(details, state);
            }
        }

        internal void ToggleDiscordPresence()
        {
            var store = new SqliteStore();
            store.EnsureCreated();
            var enabled = store.GetSecureSetting("discord.enabled") != "0";
            var next = !enabled;
            store.SetSecureSetting("discord.enabled", next ? "1" : "0");
            discord?.Dispose();
            discord = null;
            if (next)
            {
                var appId = store.GetSecureSetting("discord.appId") ?? "";
                var details = store.GetSecureSetting("discord.details") ?? "Umbrella Spoofer";
                var state = store.GetSecureSetting("discord.state") ?? "Made by Nikolis Security";
                if (!string.IsNullOrWhiteSpace(appId))
                {
                    discord = new DiscordPresenceService();
                    discord.Initialize(appId);
                    discord.SetPresence(details, state);
                }
            }
            tray?.UpdateDiscordMenu(next);
        }

        internal bool IsDiscordConnected => discord?.IsConnected == true;
        internal string DiscordStatus => discord?.Status ?? "Not connected";

        internal void UpdateTrayIdentity(string text, string? iconPath)
        {
            tray?.UpdateIdentity(text, iconPath);
        }
    }
}
