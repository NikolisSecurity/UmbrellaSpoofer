using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace UmbrellaSpoofer.Data
{
    public class SqliteStore
    {
        readonly string dbPath;

        public SqliteStore()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UmbrellaSpoofer");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            dbPath = Path.Combine(dir, "app.db");
        }

        SqliteConnection Open()
        {
            var c = new SqliteConnection($"Data Source={dbPath}");
            c.Open();
            return c;
        }

        public void EnsureCreated()
        {
            using var c = Open();
            using var cmd1 = c.CreateCommand();
            cmd1.CommandText = "create table if not exists backups(id integer primary key, ts text, mode text, key text, value text)";
            cmd1.ExecuteNonQuery();
            using var cmd2 = c.CreateCommand();
            cmd2.CommandText = "create table if not exists history(id integer primary key, ts text, key text, value text)";
            cmd2.ExecuteNonQuery();
            using var cmd3 = c.CreateCommand();
            cmd3.CommandText = "create table if not exists settings(key text primary key, value text)";
            cmd3.ExecuteNonQuery();
        }

        public void EnsureDiscordDefaults(string appId, string details, string state)
        {
            if (string.IsNullOrWhiteSpace(GetSecureSetting("discord.appId")))
                SetSecureSetting("discord.appId", appId);
            if (string.IsNullOrWhiteSpace(GetSecureSetting("discord.details")))
                SetSecureSetting("discord.details", details);
            if (string.IsNullOrWhiteSpace(GetSecureSetting("discord.state")))
                SetSecureSetting("discord.state", state);
            if (string.IsNullOrWhiteSpace(GetSecureSetting("discord.enabled")))
                SetSecureSetting("discord.enabled", "1");
        }

        public void AddBackup(string key, string value, string mode)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "insert into backups(ts, mode, key, value) values($ts,$m,$k,$v)";
            cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$m", mode);
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }

        public void AddHistory(string key, string value)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "insert into history(ts, key, value) values($ts,$k,$v)";
            cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }

        public string? GetSetting(string key)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "select value from settings where key=$k";
            cmd.Parameters.AddWithValue("$k", key);
            var v = cmd.ExecuteScalar();
            return v?.ToString();
        }

        public void SetSetting(string key, string? value)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "insert into settings(key,value) values($k,$v) on conflict(key) do update set value=$v";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value ?? "");
            cmd.ExecuteNonQuery();
        }

        public string? GetSecureSetting(string key)
        {
            var raw = GetSetting(key);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try
            {
                var data = Convert.FromBase64String(raw);
                var plain = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return null;
            }
        }

        public void SetSecureSetting(string key, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            SetSetting(key, Convert.ToBase64String(protectedBytes));
        }

        public (DateTime? timestamp, string? key, string? value) GetLastHistoryEntry()
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT ts, key, value FROM history ORDER BY id DESC LIMIT 1";
            
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var tsValue = reader["ts"];
                var keyValue = reader["key"];
                var valueValue = reader["value"];
                
                if (tsValue != null && tsValue != DBNull.Value && 
                    keyValue != null && keyValue != DBNull.Value &&
                    valueValue != null && valueValue != DBNull.Value)
                {
                    var tsString = tsValue.ToString();
                    if (!string.IsNullOrEmpty(tsString))
                    {
                        var timestamp = DateTime.Parse(tsString);
                        var key = keyValue.ToString();
                        var value = valueValue.ToString();
                        return (timestamp, key, value);
                    }
                }
            }
            
            return (null, null, null);
        }
    }
}
