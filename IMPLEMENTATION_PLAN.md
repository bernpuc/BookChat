# BookChat - IRC E-Book Search and Download Application
## Implementation Plan

## Overview
BookChat is an IRC client designed specifically for searching and downloading e-books from IRC channels using SearchBot. The workflow involves sending search queries, receiving zipped search results, parsing them, and automatically requesting the best match for download.

---

## Phase 1: Basic IRC Foundation (Week 1)

### 1.1 Basic IRC Connection ✓
**Goal:** Connect to IRC server and join a channel

**Tasks:**
- [x] Create WPF solution with Prism MVVM
- [x] Add ChatSharp and Prism.DryIoc packages
- [ ] Create MainWindow with basic IRC UI
- [ ] Implement MainWindowViewModel with connection logic
- [ ] Add server/port/nickname/channel configuration
- [ ] Display connection status
- [ ] Show basic chat messages

**Deliverable:** Can connect to IRC server, join channel, see messages

---

### 1.2 Basic Chat Functionality
**Goal:** Send and receive text messages

**Tasks:**
- [ ] Add message display (ListBox with messages)
- [ ] Add message input (TextBox)
- [ ] Implement send message command
- [ ] Show join/part notifications
- [ ] Add timestamp to messages
- [ ] Implement auto-scroll for messages

**Deliverable:** Fully functional basic IRC chat client

**Files to Create:**
- `Views/MainWindow.xaml` - Main UI
- `ViewModels/MainWindowViewModel.cs` - Chat logic
- `App.xaml.cs` - Prism bootstrapper

---

## Phase 2: File Download Infrastructure (Week 2)

### 2.1 DCC File Transfer Detection
**Goal:** Detect when a file is offered via DCC SEND

**Tasks:**
- [ ] Create `Services/DccService.cs`
- [ ] Hook into ChatSharp's DCC events (if available) or raw IRC messages
- [ ] Parse DCC SEND messages (format: `DCC SEND filename ip port filesize`)
- [ ] Display file offer notifications in UI
- [ ] Add accept/reject file transfer options

**Deliverable:** Can detect file offers and show them to user

**Files to Create:**
- `Services/Interfaces/IDccService.cs`
- `Services/DccService.cs`
- `Models/FileOffer.cs`

---

### 2.2 File Download Implementation
**Goal:** Actually download files offered via DCC

**Tasks:**
- [ ] Implement TCP socket connection to DCC host
- [ ] Implement file transfer protocol (receive bytes, send ACK)
- [ ] Add progress tracking (bytes downloaded / total size)
- [ ] Show download progress in UI (ProgressBar)
- [ ] Save files to configured download directory
- [ ] Handle download errors and cancellation

**Deliverable:** Can download files from IRC bots

**Files to Create:**
- `Services/FileDownloadService.cs`
- `Models/DownloadProgress.cs`
- `ViewModels/DownloadViewModel.cs` (for download list)

---

## Phase 3: Search Bot Integration (Week 3)

### 3.1 Search Command Interface
**Goal:** Send @search commands and identify responses

**Tasks:**
- [ ] Add search input UI (TextBox for "Author Title")
- [ ] Create `@search <query>` command builder
- [ ] Send search to channel
- [ ] Detect when SearchBot offers the zip file response
- [ ] Auto-accept zip file from SearchBot
- [ ] Show search status (searching, results received, etc.)

**Deliverable:** Can send search queries and receive result zip files

**Files to Create:**
- `ViewModels/SearchViewModel.cs`
- `Services/SearchBotService.cs`
- `Models/SearchQuery.cs`

---

### 3.2 Zip File Processing
**Goal:** Extract and parse search results from zip file

**Tasks:**
- [ ] Add `System.IO.Compression` reference
- [ ] Create zip extraction service
- [ ] Extract .txt file from zip
- [ ] Parse search results text file
- [ ] Identify result lines (starting with `!BotName`)
- [ ] Extract metadata (author, title, format, size, tags)
- [ ] Create `SearchResult` model with all fields

**Deliverable:** Can extract and parse search results

**Files to Create:**
- `Services/ZipProcessingService.cs`
- `Services/SearchResultParser.cs`
- `Models/SearchResult.cs` (BotName, Hash, Author, Title, Series, Format, Size, Tags)

---

## Phase 4: Smart Result Selection (Week 4)

### 4.1 Result Ranking Algorithm
**Goal:** Automatically select the best match from search results

**Tasks:**
- [ ] Create scoring algorithm for search results
- [ ] Prioritize EPUB over other formats
- [ ] Match query terms (author, title) against results
- [ ] Consider file size (reasonable size for books)
- [ ] Prefer specific bots (configurable bot preference)
- [ ] Rank by relevance score
- [ ] Select top result automatically

**Deliverable:** Can identify best match from search results

**Files to Create:**
- `Services/ResultRankingService.cs`
- `Models/RankedSearchResult.cs`

---

### 4.2 Automatic Download Request
**Goal:** Send book request command and download the book

**Tasks:**
- [ ] Extract bot command from selected result (e.g., `!Ashurbanipal <hash>`)
- [ ] Send command to IRC channel
- [ ] Wait for file offer from bot
- [ ] Auto-accept book file download
- [ ] Track download progress
- [ ] Notify when book is downloaded
- [ ] Open file location option

**Deliverable:** End-to-end search → download workflow

**Files to Create:**
- `Services/BookDownloadService.cs` (orchestrates search → parse → request → download)

---

## Phase 5: User Interface Enhancements (Week 5)

### 5.1 Search Results Display
**Goal:** Show search results and allow manual selection

**Tasks:**
- [ ] Create search results view (DataGrid or ListView)
- [ ] Display all parsed results with columns: Author, Title, Format, Size
- [ ] Highlight auto-selected result
- [ ] Allow user to override and select different result
- [ ] Double-click to download alternative result
- [ ] Add filter/sort options

**Deliverable:** Rich search results UI

**Files to Create:**
- `Views/SearchResultsView.xaml`
- `ViewModels/SearchResultsViewModel.cs`

---

### 5.2 Download Manager
**Goal:** Track all downloads in one place

**Tasks:**
- [ ] Create downloads view showing all active/completed downloads
- [ ] Show download status (Pending, Downloading, Completed, Failed)
- [ ] Display progress bars for active downloads
- [ ] Allow cancellation of active downloads
- [ ] Show download history
- [ ] Add "Open Folder" button for completed downloads
- [ ] Clear completed downloads option

**Deliverable:** Complete download management UI

**Files to Create:**
- `Views/DownloadsView.xaml`
- `ViewModels/DownloadsViewModel.cs`
- `Models/Download.cs` (status, progress, file info)

---

## Phase 6: Library Management (Week 6)

### 6.1 Book Library
**Goal:** Organize and manage downloaded books

**Tasks:**
- [ ] Create library database (SQLite with EF Core)
- [ ] Store book metadata (author, title, format, file path, download date)
- [ ] Create library view (grid of books)
- [ ] Display book covers (if available in metadata)
- [ ] Add search/filter within library
- [ ] Sort by author, title, date added
- [ ] Tag management
- [ ] Open book in default reader

**Deliverable:** Local library for downloaded books

**Files to Create:**
- `Data/BookChatDbContext.cs`
- `Models/Book.cs`
- `Views/LibraryView.xaml`
- `ViewModels/LibraryViewModel.cs`
- `Services/LibraryService.cs`

---

### 6.2 Metadata Extraction
**Goal:** Extract metadata from EPUB files

**Tasks:**
- [ ] Add EPUB parsing library (VersOne.Epub or EpubSharp)
- [ ] Extract author, title, publisher from EPUB metadata
- [ ] Extract cover image
- [ ] Update library with extracted metadata
- [ ] Display cover thumbnails in library view

**Deliverable:** Rich metadata in library

**NuGet Packages:**
- `VersOne.Epub.NetCore` or `EpubSharp`

---

## Phase 7: Configuration & Settings (Week 7)

### 7.1 Application Settings
**Goal:** Persistent configuration

**Tasks:**
- [ ] Create settings model (server, port, nickname, channel, download path)
- [ ] Add settings persistence (JSON file or app settings)
- [ ] Create settings view/dialog
- [ ] Allow server presets (common e-book IRC servers)
- [ ] Download directory selection
- [ ] Auto-connect option
- [ ] Preferred format selection (EPUB, MOBI, AZW3, PDF)
- [ ] Bot preference order

**Deliverable:** Configurable application settings

**Files to Create:**
- `Models/AppSettings.cs`
- `Services/SettingsService.cs`
- `Views/SettingsWindow.xaml`
- `ViewModels/SettingsViewModel.cs`

---

## Phase 8: Advanced Features (Week 8+)

### 8.1 Multiple Search Bots
**Goal:** Support different search bot commands

**Tasks:**
- [ ] Support `@search`, `@find`, and other search commands
- [ ] Detect different bot response formats
- [ ] Create parser factory for different bot types
- [ ] Allow manual bot selection

---

### 8.2 Batch Downloads
**Goal:** Download multiple books from search results

**Tasks:**
- [ ] Multi-select in search results
- [ ] Queue multiple downloads
- [ ] Throttle requests (avoid flooding/ban)
- [ ] Progress tracking for batch operations

---

### 8.3 Smart Features
**Goal:** Enhanced user experience

**Tasks:**
- [ ] Duplicate detection (don't download same book twice)
- [ ] Reading list / wish list
- [ ] Book series detection and grouping
- [ ] Export library to CSV
- [ ] Statistics (books downloaded, total size, etc.)
- [ ] Dark mode theme

---

## Technical Architecture

### Project Structure
```
BookChat/
├── Models/
│   ├── SearchQuery.cs
│   ├── SearchResult.cs
│   ├── FileOffer.cs
│   ├── Download.cs
│   ├── Book.cs
│   └── AppSettings.cs
├── Services/
│   ├── Interfaces/
│   │   ├── IIrcService.cs
│   │   ├── IDccService.cs
│   │   ├── ISearchBotService.cs
│   │   ├── IFileDownloadService.cs
│   │   ├── IZipProcessingService.cs
│   │   ├── IResultRankingService.cs
│   │   ├── ILibraryService.cs
│   │   └── ISettingsService.cs
│   └── [Implementations]
├── ViewModels/
│   ├── MainWindowViewModel.cs
│   ├── SearchViewModel.cs
│   ├── SearchResultsViewModel.cs
│   ├── DownloadsViewModel.cs
│   ├── LibraryViewModel.cs
│   └── SettingsViewModel.cs
├── Views/
│   ├── MainWindow.xaml
│   ├── SearchResultsView.xaml
│   ├── DownloadsView.xaml
│   ├── LibraryView.xaml
│   └── SettingsWindow.xaml
├── Data/
│   └── BookChatDbContext.cs
└── App.xaml.cs
```

### Key Technologies
- **UI Framework:** WPF with .NET 9
- **MVVM:** Prism 9.0 with DryIoc
- **IRC:** ChatSharp
- **File Compression:** System.IO.Compression
- **Database:** SQLite with EF Core 9
- **EPUB Parsing:** VersOne.Epub
- **Testing:** xUnit, Moq, FluentAssertions

---

## Development Approach

### Iteration 1: Minimal Viable Product (Phases 1-4)
**Goal:** Basic search and download functionality
**Duration:** 4 weeks
**Outcome:** Can search for books and download them

### Iteration 2: Enhanced UX (Phases 5-6)
**Goal:** Rich UI and library management
**Duration:** 2 weeks
**Outcome:** Professional-looking application with library

### Iteration 3: Polish (Phases 7-8)
**Goal:** Configuration and advanced features
**Duration:** 2+ weeks
**Outcome:** Production-ready application

---

## Testing Strategy

### Unit Tests
- Service layer (search parsing, result ranking, zip processing)
- Use Moq for IRC client mocking
- Test edge cases (malformed results, network errors)

### Integration Tests
- End-to-end search workflow (mock IRC responses)
- File download simulation
- Database operations

### Manual Testing
- Real IRC server connection
- Actual SearchBot interactions
- Download verification

---

## Risk Mitigation

### Potential Issues
1. **ChatSharp compatibility with .NET 9** - May need alternative library (IrcDotNet)
2. **IRC server bans** - Implement rate limiting, respect channel rules
3. **DCC firewall issues** - May need passive DCC or NAT traversal
4. **SearchBot format changes** - Make parser configurable/flexible
5. **Legal concerns** - Add disclaimer, user responsible for content

### Contingencies
- Have backup IRC library options
- Implement robust error handling
- Graceful degradation for missing features
- Clear user documentation

---

## Success Metrics

### MVP Success (Phase 4 Complete)
- Can connect to IRC
- Can send @search command
- Can parse search results
- Can download selected book

### Full Success (All Phases)
- Intuitive UI
- Reliable downloads
- Organized library
- < 5% error rate on searches
- Positive user feedback

---

## Next Steps

1. **Start with Phase 1.1** - Complete basic IRC connection and chat
2. **Verify IRC server** - Identify actual server/channel for e-books
3. **Test SearchBot** - Manually test search workflow to understand exact protocol
4. **Iterate rapidly** - Get feedback after each phase

Ready to begin implementation?
