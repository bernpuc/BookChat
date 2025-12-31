# BookChat - IRC E-Book Downloader

A modern WPF desktop application for searching and downloading e-books from IRC networks with automatic Calibre library integration.

![.NET 9.0](https://img.shields.io/badge/.NET-9.0-512BD4)
![WPF](https://img.shields.io/badge/WPF-Windows-blue)
![License](https://img.shields.io/badge/license-Personal%20Use-green)

## Features

### ✅ Core Functionality
- **IRC Integration**: Automatic connection to IRCHighway #ebooks channel
- **Book Search**: Integrated SearchBot query interface with real-time status updates
- **DCC Downloads**: Full DCC file transfer protocol support with progress tracking
- **Smart Storage**: Books saved to Documents folder, temporary search results to system temp
- **Result Parsing**: Automatic extraction and display of search results from zip files

### ✅ Calibre Integration
- **Auto-Import**: Automatically add downloaded books to Calibre library
- **Content Server Support**: Works with running Calibre instances via HTTP API
- **Duplicate Handling**: Configurable options (ignore, overwrite, create new record)
- **Authentication**: Full username/password support for secured content servers
- **Settings Persistence**: All Calibre settings saved between sessions

### ✅ User Experience
- **Tab-Based Interface**:
  - **Find Books**: Search interface and results grid
  - **Settings**: IRC connection, Calibre integration, and chat messages
  - **About**: Version info, features, and system details
- **Auto-Connect**: Connects to IRC on startup for immediate use
- **Connection Validation**: Modal dialogs guide users if not connected
- **Real-Time Updates**: Live download progress, search status, and IRC chat
- **Persistent Settings**: All configurations saved to `%APPDATA%\BookChat\`

## Quick Start

### Prerequisites
- .NET 9 SDK ([Download](https://dotnet.microsoft.com/download/dotnet/9.0))
- Windows 10/11
- (Optional) Calibre installed and in system PATH for auto-import feature

### Build and Run

```bash
git clone <repository-url>
cd BookChat/BookChat
dotnet restore
dotnet build
dotnet run
```

### First Launch

1. **Automatic Connection**: The app auto-connects to IRC on startup
2. **Search for Books**:
   - Go to the **Find Books** tab
   - Enter author and/or book title
   - Click **Fetch** to search
   - Results appear in the grid below
   - Click **Download** on any result
3. **Configure Calibre** (Optional):
   - Go to the **Settings** tab
   - Set your Calibre Library Path
   - Enable "Auto-Import Downloads"
   - Configure credentials if Calibre content server is running
4. **View System Info**:
   - Go to the **About** tab for version and path information

## Configuration

### IRC Settings
Located in the **Settings** tab:
- **Server**: irc.irchighway.net (default)
- **Port**: 6667 (default)
- **Nickname**: ircuser (customizable)
- **Channel**: #ebooks (default)

### Calibre Settings
Located in the **Settings** tab:
- **Library Path**: Path to your Calibre library folder
- **Server URL**: Content server URL (default: http://localhost:8080)
- **Username/Password**: Credentials for content server authentication
- **Auto-Import**: Toggle automatic import of downloads
- **Duplicate Handling**: Choose how to handle existing books

### Storage Locations
- **Book Downloads**: `%USERPROFILE%\Documents\BookChat\Downloads`
- **Search Results (Temp)**: `%TEMP%\BookChat\SearchResults` (auto-cleaned on reboot)
- **Settings File**: `%APPDATA%\BookChat\calibre-settings.json`

## Project Structure

```
BookChat/
├── Models/
│   ├── FileOffer.cs              - DCC file offer model
│   ├── DownloadProgress.cs       - Download progress tracking
│   ├── SearchResult.cs           - Search result model
│   └── CalibreSettings.cs        - Settings persistence model
├── Services/
│   ├── Interfaces/
│   │   ├── IDccService.cs        - DCC service interface
│   │   ├── ICalibreService.cs    - Calibre integration interface
│   │   ├── ISearchResultParser.cs - Search parser interface
│   │   └── ISettingsService.cs   - Settings service interface
│   ├── DccService.cs             - DCC file transfer implementation
│   ├── CalibreService.cs         - Calibre CLI integration
│   ├── SearchResultParser.cs     - Search result parsing logic
│   └── SettingsService.cs        - JSON settings persistence
├── ViewModels/
│   └── MainWindowViewModel.cs    - Main application logic (MVVM)
├── Views/
│   ├── MainWindow.xaml           - Multi-tab UI layout
│   └── MainWindow.xaml.cs        - Code-behind and converters
├── App.xaml                       - Prism application definition
├── App.xaml.cs                    - Prism bootstrapper and DI
└── BookChat.csproj               - Project file with dependencies
```

## Technology Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Framework | .NET / WPF | 9.0 |
| MVVM | Prism with DryIoc | 9.0.537 |
| IRC Library | SmartIrc4net | 1.1.0 |
| E-Book Management | Calibre (CLI) | External |
| Settings | System.Text.Json | Built-in |

## Features in Detail

### Search and Download Workflow

1. **Search Request**: User enters author/title → App sends `@search` to SearchBot
2. **Results Download**: SearchBot sends zip file via DCC → Auto-accepted to temp folder
3. **Parsing**: Zip extracted → Results parsed → Displayed in grid with ranking
4. **Book Download**: User clicks Download → App sends request → Bot sends book
5. **Calibre Import**: (If enabled) Book automatically added to Calibre library

### DCC Protocol Implementation

- Full DCC SEND protocol support with quoted filename handling
- TCP socket-based file transfers with 8KB buffer
- Progress tracking with speed calculation (KB/s, MB/s)
- ACK acknowledgments sent every 4KB
- IPv4 decimal-to-dotted notation conversion
- Automatic file naming conflict resolution

### Calibre Integration Details

**Two-Stage Connection Strategy:**
1. **Direct Database Access** (when Calibre is closed):
   ```bash
   calibredb add "book.epub" --library-path "C:\Users\...\Calibre Library"
   ```

2. **Content Server API** (when Calibre is running):
   ```bash
   calibredb add "book.epub" --with-library http://localhost:8080 --username <user> --password <pass>
   ```

**Error Handling:**
- Detects database lock errors → Automatically retries with content server
- Clear error messages guide users to start content server if needed
- Validates paths and credentials before attempting import

### Settings Persistence

All settings automatically saved on change to:
```
%APPDATA%\BookChat\calibre-settings.json
```

Includes:
- Calibre library path and server URL
- Auto-import preferences and duplicate handling
- Username and password (stored locally, not encrypted)

## Known Limitations

- **Single Channel**: Only one IRC channel supported at a time
- **Windows Only**: WPF is Windows-specific
- **SmartIrc4net Warnings**: Package shows .NET Framework compatibility warnings (works fine)
- **Calibre Dependency**: Auto-import requires Calibre installed and `calibredb` in PATH
- **Password Storage**: Credentials stored in plain text JSON (local machine only)

## Troubleshooting

### Calibre Import Fails with "Not Connected"
- Ensure Calibre is either fully closed OR content server is running
- To start content server: Calibre → Preferences → Sharing over the net → Start server
- Verify `calibredb --version` works in command prompt

### Search Returns No Results
- Check IRC connection status in Settings tab
- Verify you're connected to #ebooks channel
- Try searching with just author name or just book title
- Check chat messages in Settings tab for SearchBot responses

### Build Errors
- Ensure .NET 9 SDK is installed: `dotnet --version`
- Run `dotnet restore` before building
- Close running BookChat.exe before rebuilding

## Development

### Adding a New Feature

1. Update models in `Models/` if needed
2. Create service interface in `Services/Interfaces/`
3. Implement service in `Services/`
4. Register service in `App.xaml.cs` DI container
5. Add properties/commands to `MainWindowViewModel.cs`
6. Update `Views/MainWindow.xaml` UI

### Debug Mode

```bash
# Build in Debug mode (default)
dotnet build

# Build in Release mode
dotnet build -c Release

# Run with verbose logging
dotnet run --project BookChat --verbosity detailed
```

## Version History

- **v1.0.0** (December 2025) - Full release with Calibre integration
  - Tab-based interface (Find Books, Settings, About)
  - Automatic Calibre library imports
  - Settings persistence
  - Auto-connect on startup
  - Smart temporary file management

- **v0.2.0** - Search integration
  - SearchBot query support
  - Result parsing and display
  - Ranked search results

- **v0.1.0** - Initial release
  - IRC connectivity
  - DCC file transfers
  - Basic UI

## Contributing

This is a personal project. Feel free to fork and modify for your own use.

## License

Personal use project. Not licensed for distribution.

## Credits

Developed with assistance from Claude (Anthropic)

---

**Version:** 1.0.0
**Last Updated:** December 30, 2025
**Status:** Production Ready ✅
