using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;

namespace UmbrellaSpoofer.Services
{
    public class TrayService : IDisposable
    {
        private Forms.NotifyIcon? _notifyIcon;
        private Forms.ContextMenuStrip? _contextMenu;
        private Icon? _customIcon;
        private Forms.ToolStripMenuItem? _discordToggle;

        public void Show()
        {
            if (_notifyIcon != null) return;

            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Umbrella Spoofer",
                Visible = true
            };
            TrySetIconFromAssets();

            _contextMenu = new Forms.ContextMenuStrip();
            _contextMenu.Items.Add("Show", null, OnShow);
            _discordToggle = new Forms.ToolStripMenuItem("Discord Presence: On", null, OnToggleDiscord);
            _contextMenu.Items.Add(_discordToggle);
            _contextMenu.Items.Add(new Forms.ToolStripSeparator());
            _contextMenu.Items.Add("Exit", null, OnExit);

            _notifyIcon.ContextMenuStrip = _contextMenu;
            _notifyIcon.DoubleClick += OnShow;
        }

        private void OnShow(object? sender, EventArgs e)
        {
            if (System.Windows.Application.Current?.MainWindow is { } w)
            {
                w.Show();
                w.WindowState = WindowState.Normal;
                w.Activate();
            }
        }

        private void OnExit(object? sender, EventArgs e)
        {
            if (System.Windows.Application.Current is App app)
                app.RequestExit();
            else
                System.Windows.Application.Current?.Shutdown();
        }

        private void OnToggleDiscord(object? sender, EventArgs e)
        {
            if (System.Windows.Application.Current is App app)
                app.ToggleDiscordPresence();
        }

        public void Hide()
        {
        }

        public void Dispose()
        {
            _customIcon?.Dispose();
            _notifyIcon?.Dispose();
            _contextMenu?.Dispose();
        }

        public void UpdateIdentity(string text, string? iconPath)
        {
            if (_notifyIcon == null) return;
            if (!string.IsNullOrWhiteSpace(text))
                _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                try
                {
                    _customIcon?.Dispose();
                    _customIcon = iconPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                        ? LoadIconFromPng(iconPath)
                        : new Icon(iconPath);
                    if (_customIcon == null) return;
                    _notifyIcon.Icon = _customIcon;
                }
                catch { }
            }
        }

        public void UpdateDiscordMenu(bool enabled)
        {
            if (_discordToggle == null) return;
            _discordToggle.Text = enabled ? "Discord Presence: On" : "Discord Presence: Off";
        }

        void TrySetIconFromAssets()
        {
            if (_notifyIcon == null) return;
            var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "logo.png");
            if (!File.Exists(logoPath)) return;
            var icon = LoadIconFromPng(logoPath);
            if (icon == null) return;
            _customIcon?.Dispose();
            _customIcon = icon;
            _notifyIcon.Icon = _customIcon;
        }

        static Icon? LoadIconFromPng(string path)
        {
            try
            {
                using var bmp = new Bitmap(path);
                using var scaled = new Bitmap(bmp, 48, 48);
                var hIcon = scaled.GetHicon();
                var icon = (Icon)Icon.FromHandle(hIcon).Clone();
                DestroyIcon(hIcon);
                return icon;
            }
            catch
            {
                return null;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyIcon(IntPtr hIcon);
    }
}
