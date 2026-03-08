# 🛡️ Umbrella Spoofer — Hardware Identity Manager

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows-blue?style=for-the-badge&logo=windows" alt="Platform">
  <img src="https://img.shields.io/badge/Language-C%23-512BD4?style=for-the-badge&logo=csharp" alt="Language">
  <img src="https://img.shields.io/badge/Framework-.NET%208-512BD4?style=for-the-badge&logo=dotnet" alt="Framework">
</p>

<p align="center">
  <img src="https://img.shields.io/github/stars/NikolisSecurity/UmbrellaSpoofer?style=social">
  <img src="https://img.shields.io/github/v/release/NikolisSecurity/UmbrellaSpoofer">
  <img src="https://img.shields.io/github/downloads/NikolisSecurity/UmbrellaSpoofer/total">
  <a href="https://discord.gg/rfWdrewbAz" target="_blank">
    <img src="https://img.shields.io/badge/Discord-Join%20Server-7289DA?logo=discord&logoColor=white" alt="Join our Discord">
  </a>
</p>

## 📋 Overview
Umbrella Spoofer is a Windows desktop application for managing and masking hardware identity values, with system inventory, history tracking, and update support.

## ✨ Features
- Hardware inventory view with live identifiers
- Masked preview generation before apply
- History and backup tracking
- Tray integration
- Discord Rich Presence integration
- Configurable update checks

## 🔧 Requirements
- Windows 10/11
- .NET 8 Desktop Runtime or SDK
- Administrator privileges (for hardware identity operations)

## 📥 Installation
1. Download the latest release (recommended):

[![Download latest.rar](https://img.shields.io/badge/Download-latest.rar-00A6FF?style=for-the-badge&logo=github)](https://github.com/NikolisSecurity/UmbrellaSpoofer/releases/latest/download/latest.rar)

2. Clone the repository:

```bash
git clone https://github.com/NikolisSecurity/UmbrellaSpoofer.git
```

3. Build:

```bash
dotnet build
```

4. Run:

```bash
dotnet run --project .\UmbrellaSpoofer\UmbrellaSpoofer.csproj
```

## ⚙️ Configuration

### Discord Presence
Discord settings are stored in the local SQLite database and can be configured via the UI.

## 🧱 Project Structure
```
UmbrellaSpoofer/
  assets/
  UI/
  Services/
  Data/
  App.xaml
  App.xaml.cs
  Program.cs
  TrayService.cs
  updater.json
  app.manifest
```

Recommended release artifacts:
- latest.rar containing the published app
- Optional SHA256 checksum file (latest.rar.sha256)

## ❓ FAQ
- Does this bypass anti-cheat? No guarantees. Use responsibly and respect TOS.
- Will this harm my PC? Changes should be used with care and proper backups.

## ⚠️ Warning
- Use this tool responsibly and at your own risk
- Modifying hardware identifiers may affect system functionality
- Some changes require administrator privileges
- Always create a system restore point before making changes

## 🔍 Troubleshooting
- Run as administrator for hardware identifier operations
- If update checks fail, verify updater.json is configured with a valid owner/repo

## 📜 License
See LICENSE.

## 📞 Contact
For issues or feature requests, please open an issue on the GitHub repository.

Made with ❤️ for privacy enthusiasts
