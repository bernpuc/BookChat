using Prism.Mvvm;
using Prism.Commands;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Meebey.SmartIrc4net;
using System;
using System.Windows;
using System.IO;
using BookChat.Services.Interfaces;
using BookChat.Models;
using System.Linq;
using System.Threading;
using System.Text.RegularExpressions;

namespace BookChat.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        private readonly IDccService _dccService;
        private readonly ISearchResultParser _searchResultParser;
        private readonly ICalibreService _calibreService;
        private readonly ISettingsService _settingsService;
        private IrcClient? _client;
        private Thread? _listenerThread;
        private string _server = "irc.irchighway.net";
        private int _port = 6667;
        private string _nickname = "ircuser";
        private string _channel = "#ebooks";
        private string _messageToSend = string.Empty;
        private bool _isConnected;
        private string _statusMessage = "Not connected";
        private string _windowTitle = "BookChat - IRC E-Book Downloader";
        private string _downloadDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BookChat", "Downloads");
        private string _searchResultsTempDirectory = Path.Combine(Path.GetTempPath(), "BookChat", "SearchResults");
        private string _authorName = string.Empty;
        private string _bookTitle = string.Empty;
        private string _lastAuthorQuery = string.Empty;
        private string _lastTitleQuery = string.Empty;
        private string _searchStatus = string.Empty;
        private string _calibreLibraryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Calibre Library");
        private bool _autoImportToCalibre = false;
        private string _calibreDuplicateHandling = "ignore";
        private string _calibreContentServerUrl = "http://localhost:8080";
        private string _calibreUsername = string.Empty;
        private string _calibrePassword = string.Empty;

        public ObservableCollection<string> Messages { get; } = new();
        public ObservableCollection<FileOffer> FileOffers { get; } = new();
        public ObservableCollection<DownloadProgress> ActiveDownloads { get; } = new();
        public ObservableCollection<SearchResult> SearchResults { get; } = new();

        public string Server
        {
            get => _server;
            set => SetProperty(ref _server, value);
        }

        public int Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        public string Nickname
        {
            get => _nickname;
            set => SetProperty(ref _nickname, value);
        }

        public string Channel
        {
            get => _channel;
            set => SetProperty(ref _channel, value);
        }

        public string MessageToSend
        {
            get => _messageToSend;
            set => SetProperty(ref _messageToSend, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                SetProperty(ref _isConnected, value);
                UpdateWindowTitle();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        public string AuthorName
        {
            get => _authorName;
            set => SetProperty(ref _authorName, value);
        }

        public string BookTitle
        {
            get => _bookTitle;
            set => SetProperty(ref _bookTitle, value);
        }

        public string SearchStatus
        {
            get => _searchStatus;
            set => SetProperty(ref _searchStatus, value);
        }

        public string CalibreLibraryPath
        {
            get => _calibreLibraryPath;
            set => SetProperty(ref _calibreLibraryPath, value);
        }

        public bool AutoImportToCalibre
        {
            get => _autoImportToCalibre;
            set => SetProperty(ref _autoImportToCalibre, value);
        }

        public string CalibreDuplicateHandling
        {
            get => _calibreDuplicateHandling;
            set => SetProperty(ref _calibreDuplicateHandling, value);
        }

        public string CalibreContentServerUrl
        {
            get => _calibreContentServerUrl;
            set => SetProperty(ref _calibreContentServerUrl, value);
        }

        public string CalibreUsername
        {
            get => _calibreUsername;
            set => SetProperty(ref _calibreUsername, value);
        }

        public string CalibrePassword
        {
            get => _calibrePassword;
            set => SetProperty(ref _calibrePassword, value);
        }

        public string DownloadDirectory => _downloadDirectory;

        public string SearchResultsTempDirectory => _searchResultsTempDirectory;

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand SendMessageCommand { get; }
        public ICommand FetchCommand { get; }
        public ICommand DownloadBookCommand { get; }

        public MainWindowViewModel(IDccService dccService, ISearchResultParser searchResultParser, ICalibreService calibreService, ISettingsService settingsService)
        {
            _dccService = dccService ?? throw new ArgumentNullException(nameof(dccService));
            _searchResultParser = searchResultParser ?? throw new ArgumentNullException(nameof(searchResultParser));
            _calibreService = calibreService ?? throw new ArgumentNullException(nameof(calibreService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

            // Set up DCC event handlers
            _dccService.FileOffered += OnFileOffered;
            _dccService.ProgressUpdated += OnProgressUpdated;
            _dccService.DownloadCompleted += OnDownloadCompleted;

            ConnectCommand = new DelegateCommand(Connect, CanConnect)
                .ObservesProperty(() => IsConnected)
                .ObservesProperty(() => Server)
                .ObservesProperty(() => Nickname)
                .ObservesProperty(() => Channel);

            DisconnectCommand = new DelegateCommand(Disconnect, () => IsConnected)
                .ObservesProperty(() => IsConnected);

            SendMessageCommand = new DelegateCommand(SendMessage, CanSendMessage)
                .ObservesProperty(() => MessageToSend)
                .ObservesProperty(() => IsConnected);

            FetchCommand = new DelegateCommand(FetchBook, CanFetchBook)
                .ObservesProperty(() => IsConnected)
                .ObservesProperty(() => AuthorName)
                .ObservesProperty(() => BookTitle);

            DownloadBookCommand = new DelegateCommand<SearchResult>(DownloadBook);

            AddMessage("Welcome to BookChat - IRC E-Book Downloader");
            AddMessage($"Book downloads will be saved to: {_downloadDirectory}");
            AddMessage($"Search results (temporary): {_searchResultsTempDirectory}");

            // Ensure directories exist
            Directory.CreateDirectory(_downloadDirectory);
            Directory.CreateDirectory(_searchResultsTempDirectory);

            // Load saved settings
            _ = LoadSettingsAsync();

            // Subscribe to property changes to auto-save settings
            PropertyChanged += OnCalibreSettingChanged;

            // Auto-connect on startup
            AddMessage($"Auto-connecting to {Server} {Channel}...");
            Connect();
        }

        private bool CanConnect()
        {
            return !IsConnected &&
                   !string.IsNullOrWhiteSpace(Server) &&
                   !string.IsNullOrWhiteSpace(Nickname) &&
                   !string.IsNullOrWhiteSpace(Channel);
        }

        private void Connect()
        {
            try
            {
                // Clean up any existing client
                if (_client != null)
                {
                    try
                    {
                        if (_client.IsConnected)
                        {
                            _client.RfcQuit("Reconnecting");
                            _client.Disconnect();
                        }
                    }
                    catch { }
                    finally
                    {
                        _client = null;
                    }

                    // Small delay to ensure old connection is fully closed
                    Thread.Sleep(500);
                }

                AddMessage($"Connecting to {Server}:{Port} as {Nickname}...");
                StatusMessage = $"Connecting to {Server}...";

                // Create new IRC client
                _client = new IrcClient
                {
                    AutoReconnect = false,
                    AutoRejoin = false,
                    AutoRetry = false,
                    SupportNonRfc = true
                };

                // Set up event handlers
                _client.OnChannelMessage += OnChannelMessage;
                _client.OnQueryMessage += OnPrivateMessage;
                _client.OnJoin += OnJoin;
                _client.OnPart += OnPart;
                _client.OnError += OnError;
                _client.OnConnected += OnConnected;
                _client.OnDisconnected += OnDisconnected;
                _client.OnCtcpRequest += OnCtcpRequest;
                _client.OnRawMessage += OnRawMessage;

                // Connect in a background thread
                _listenerThread = new Thread(() =>
                {
                    try
                    {
                        _client.Connect(Server, Port);
                        _client.Login(Nickname, Nickname);
                        _client.RfcJoin(Channel);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            IsConnected = true;
                            StatusMessage = $"Connected to {Channel}";
                            AddMessage($"*** Joined {Channel}");
                        });

                        // Start listening loop
                        _client.Listen();
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            AddMessage($"*** ERROR: Connection failed: {ex.Message}");
                            StatusMessage = "Connection failed";
                            IsConnected = false;
                        });
                    }
                })
                {
                    IsBackground = true,
                    Name = "IRC Listener Thread"
                };

                _listenerThread.Start();
            }
            catch (Exception ex)
            {
                AddMessage($"*** ERROR: Failed to connect: {ex.Message}");
                StatusMessage = "Connection failed";
                IsConnected = false;
            }
        }

        private void OnConnected(object? sender, EventArgs e)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AddMessage($"*** Connected to {Server}");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnConnected: {ex.Message}");
            }
        }

        private void OnDisconnected(object? sender, EventArgs e)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsConnected = false;
                    StatusMessage = "Disconnected";
                    AddMessage("*** Disconnected from server");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnDisconnected: {ex.Message}");
            }
        }

        private void OnChannelMessage(object? sender, IrcEventArgs e)
        {
            try
            {
                var msg = $"[{e.Data.Nick}] {e.Data.Message}";
                Application.Current.Dispatcher.Invoke(() => AddMessage(msg));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnChannelMessage: {ex.Message}");
            }
        }

        private void OnPrivateMessage(object? sender, IrcEventArgs e)
        {
            try
            {
                var msg = $"[PRIVATE from {e.Data.Nick}] {e.Data.Message}";
                Application.Current.Dispatcher.Invoke(() => AddMessage(msg));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnPrivateMessage: {ex.Message}");
            }
        }

        private void OnJoin(object? sender, JoinEventArgs e)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (e.Data.Nick == Nickname)
                    {
                        AddMessage($"*** You joined {e.Channel}");
                    }
                    else
                    {
                        AddMessage($"*** {e.Who} joined {e.Channel}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnJoin: {ex.Message}");
            }
        }

        private void OnPart(object? sender, PartEventArgs e)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AddMessage($"*** {e.Who} left {e.Channel}");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnPart: {ex.Message}");
            }
        }

        private void OnError(object? sender, Meebey.SmartIrc4net.ErrorEventArgs e)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AddMessage($"*** ERROR: {e.ErrorMessage}");
                    StatusMessage = "Error occurred";
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnError: {ex.Message}");
            }
        }

        private void OnCtcpRequest(object? sender, CtcpEventArgs e)
        {
            try
            {
                // Log CTCP requests for debugging
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AddMessage($"[DEBUG CTCP] From: {e.Data.Nick}, Command: {e.CtcpCommand}, Parameter: {e.CtcpParameter}");
                });

                // Handle CTCP requests (including DCC)
                if (e.CtcpCommand.Equals("DCC", StringComparison.OrdinalIgnoreCase))
                {
                    // DCC request - parse and handle
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // Reconstruct the full DCC message format that DccService expects
                            var fullDccMessage = $"DCC {e.CtcpParameter}";
                            AddMessage($"[DEBUG DCC] Parsing: {fullDccMessage}");
                            AddMessage($"[DEBUG DCC] From Nick: {e.Data.Nick}");

                            _dccService.ParseDccOffer(fullDccMessage, e.Data.Nick);
                            AddMessage($"[DEBUG DCC] ParseDccOffer completed");
                        }
                        catch (Exception parseEx)
                        {
                            AddMessage($"[DEBUG DCC ERROR] ParseDccOffer failed: {parseEx.Message}");
                            AddMessage($"[DEBUG DCC ERROR] StackTrace: {parseEx.StackTrace}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AddMessage($"[DEBUG ERROR] OnCtcpRequest: {ex.Message}");
                });
                System.Diagnostics.Debug.WriteLine($"Error in OnCtcpRequest: {ex.Message}");
            }
        }

        private void OnRawMessage(object? sender, IrcEventArgs e)
        {
            try
            {
                // Enable debugging to see all IRC protocol messages
                var rawMsg = e.Data.RawMessage;

                // Parse search status messages
                if (rawMsg.Contains("SearchBot", StringComparison.OrdinalIgnoreCase))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AddMessage($"[RAW] {rawMsg}");

                        // Extract search status messages
                        if (rawMsg.Contains("has been accepted", StringComparison.OrdinalIgnoreCase))
                        {
                            SearchStatus = "Search request accepted, waiting for results...";
                        }
                        else if (rawMsg.Contains("returned no matches", StringComparison.OrdinalIgnoreCase))
                        {
                            SearchStatus = "Search returned no matches";
                        }
                        else if (rawMsg.Contains("found", StringComparison.OrdinalIgnoreCase) && rawMsg.Contains("match", StringComparison.OrdinalIgnoreCase))
                        {
                            // Extract match count from message like "found 62 matches"
                            var matchCountMatch = Regex.Match(rawMsg, @"found\s+(\d+)\s+match", RegexOptions.IgnoreCase);
                            if (matchCountMatch.Success)
                            {
                                var count = matchCountMatch.Groups[1].Value;
                                SearchStatus = $"Search found {count} matches";
                            }
                        }
                    });
                }
                else if (rawMsg.Contains("DCC", StringComparison.OrdinalIgnoreCase))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        AddMessage($"[RAW] {rawMsg}"));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnRawMessage: {ex.Message}");
            }
        }

        private void Disconnect()
        {
            if (_client != null)
            {
                try
                {
                    AddMessage($"*** Disconnecting from {Server}...");

                    if (_client.IsConnected)
                    {
                        _client.RfcQuit("Goodbye!");
                        _client.Disconnect();
                    }

                    _client = null;
                }
                catch (Exception ex)
                {
                    AddMessage($"*** ERROR disconnecting: {ex.Message}");
                    _client = null;
                }
            }

            IsConnected = false;
            StatusMessage = "Disconnected";
            AddMessage("*** Disconnected");
        }

        private bool CanSendMessage()
        {
            return IsConnected && !string.IsNullOrWhiteSpace(MessageToSend);
        }

        private void SendMessage()
        {
            if (_client != null && !string.IsNullOrWhiteSpace(MessageToSend))
            {
                try
                {
                    _client.SendMessage(SendType.Message, Channel, MessageToSend);
                    AddMessage($"[{Nickname}] {MessageToSend}");
                    MessageToSend = string.Empty;
                }
                catch (Exception ex)
                {
                    AddMessage($"*** ERROR sending message: {ex.Message}");
                }
            }
        }

        private bool CanFetchBook()
        {
            return IsConnected &&
                   (!string.IsNullOrWhiteSpace(AuthorName) || !string.IsNullOrWhiteSpace(BookTitle));
        }

        private void FetchBook()
        {
            // Check if connected first
            if (!IsConnected)
            {
                MessageBox.Show(
                    "You must be connected to IRC before searching for books.\n\n" +
                    "Go to the Settings tab to configure your connection, then click Connect.",
                    "Not Connected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_client != null && CanFetchBook())
            {
                try
                {
                    // Build search query: @search [author name] [book title]
                    var searchQuery = "@search";

                    if (!string.IsNullOrWhiteSpace(AuthorName))
                    {
                        searchQuery += $" {AuthorName.Trim()}";
                    }

                    if (!string.IsNullOrWhiteSpace(BookTitle))
                    {
                        searchQuery += $" {BookTitle.Trim()}";
                    }

                    // Send the search command
                    _client.SendMessage(SendType.Message, Channel, searchQuery);
                    AddMessage($"[{Nickname}] {searchQuery}");
                    AddMessage($"*** Searching for: {AuthorName} {BookTitle}");
                    AddMessage($"*** Waiting for SearchBot response...");

                    // Update search status
                    SearchStatus = "Sending search request...";

                    // Save the search query for ranking results later
                    _lastAuthorQuery = AuthorName.Trim();
                    _lastTitleQuery = BookTitle.Trim();

                    // Clear previous search results
                    SearchResults.Clear();
                }
                catch (Exception ex)
                {
                    AddMessage($"*** ERROR sending search: {ex.Message}");
                }
            }
        }

        private void DownloadBook(SearchResult? result)
        {
            if (result == null || _client == null || !IsConnected)
                return;

            try
            {
                // Send the download command (e.g., "!Ashurbanipal mM43v4QB0mLu6KmIh4zXhQ")
                var downloadCommand = result.DownloadCommand;
                _client.SendMessage(SendType.Message, Channel, downloadCommand);

                AddMessage($"[{Nickname}] {downloadCommand}");
                AddMessage($"*** Requesting download: {result.Author} - {result.Title} ({result.Format})");
                AddMessage($"*** Waiting for {result.BotName} to offer file...");

                // Update search status
                SearchStatus = $"Requesting: {result.Title}";
            }
            catch (Exception ex)
            {
                AddMessage($"*** ERROR requesting book: {ex.Message}");
                SearchStatus = "Download request failed";
            }
        }

        private void AddMessage(string message)
        {
            Messages.Add($"[{DateTime.Now:HH:mm:ss}] {message}");

            // Keep only last 1000 messages to avoid memory issues
            while (Messages.Count > 1000)
            {
                Messages.RemoveAt(0);
            }
        }

        private void UpdateWindowTitle()
        {
            if (IsConnected)
            {
                WindowTitle = $"BookChat - {Server} {Channel} ({Nickname})";
            }
            else
            {
                WindowTitle = "BookChat - IRC E-Book Downloader";
            }
        }

        #region DCC Event Handlers

        private void OnFileOffered(object? sender, FileOffer fileOffer)
        {
            try
            {
                AddMessage($"[DEBUG] OnFileOffered triggered!");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AddMessage($"[DEBUG] FileOffer details - Name: {fileOffer.FileName}, Sender: {fileOffer.SenderNickname}");
                    FileOffers.Add(fileOffer);
                    AddMessage($"*** FILE OFFER: {fileOffer.FileName} ({fileOffer.FileSizeFormatted}) from {fileOffer.SenderNickname}");
                    AddMessage($"*** Automatically accepting file download...");

                    // Update search status
                    SearchStatus = $"Downloading: {fileOffer.FileName}";

                    // Auto-accept the file
                    _ = AutoAcceptFileAsync(fileOffer);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnFileOffered: {ex.Message}");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AddMessage($"*** ERROR: Failed to process file offer: {ex.Message}");
                });
            }
        }

        private async System.Threading.Tasks.Task AutoAcceptFileAsync(FileOffer fileOffer)
        {
            try
            {
                // Determine download directory based on file type
                // SearchBot results go to temp directory, actual books go to Downloads
                bool isSearchResults = fileOffer.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                                      fileOffer.SenderNickname.Contains("Search", StringComparison.OrdinalIgnoreCase);

                string targetDirectory = isSearchResults ? _searchResultsTempDirectory : _downloadDirectory;

                // Ensure target directory exists
                Directory.CreateDirectory(targetDirectory);

                await _dccService.AcceptFileAsync(fileOffer, targetDirectory);
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AddMessage($"*** ERROR downloading file: {ex.Message}");
                });
            }
        }

        private void OnProgressUpdated(object? sender, DownloadProgress progress)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Update or add progress to collection
                var existing = ActiveDownloads.FirstOrDefault(d => d.FileOffer.FileName == progress.FileOffer.FileName);
                if (existing != null)
                {
                    var index = ActiveDownloads.IndexOf(existing);
                    ActiveDownloads[index] = progress;
                }
                else
                {
                    ActiveDownloads.Add(progress);
                }

                // Show progress every 10%
                var percentRounded = Math.Floor(progress.PercentComplete / 10) * 10;
                if (percentRounded % 10 == 0 && progress.Status == DownloadStatus.Downloading)
                {
                    AddMessage($"*** Downloading {progress.FileOffer.FileName}: {progress.PercentComplete:0.0}% ({progress.SpeedFormatted})");
                }

                // Update search status with progress
                if (progress.Status == DownloadStatus.Downloading)
                {
                    SearchStatus = $"Downloading: {progress.PercentComplete:0.0}% ({progress.SpeedFormatted})";
                }
            });
        }

        private void OnDownloadCompleted(object? sender, DownloadProgress progress)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (progress.Status == DownloadStatus.Completed)
                {
                    AddMessage($"*** DOWNLOAD COMPLETE: {progress.FileOffer.FileName}");
                    AddMessage($"*** Saved to: {progress.FileOffer.LocalFilePath}");
                    AddMessage($"*** Download took {progress.ElapsedTime.TotalSeconds:0.0} seconds at {progress.SpeedFormatted}");

                    // Update search status
                    SearchStatus = $"Download complete: {progress.FileOffer.FileName}";

                    // Check if this is a SearchBot results zip file
                    bool isSearchResultsZip = progress.FileOffer.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                        progress.FileOffer.SenderNickname.Contains("Search", StringComparison.OrdinalIgnoreCase);

                    if (isSearchResultsZip)
                    {
                        _ = ProcessSearchResultsAsync(progress.FileOffer.LocalFilePath!);
                    }
                    else if (AutoImportToCalibre && !string.IsNullOrWhiteSpace(CalibreLibraryPath))
                    {
                        // Auto-import to Calibre (only for actual books, not search results)
                        _ = ImportToCalibreAsync(progress.FileOffer.LocalFilePath!, progress.FileOffer.FileName);
                    }
                }
                else if (progress.Status == DownloadStatus.Failed)
                {
                    AddMessage($"*** DOWNLOAD FAILED: {progress.FileOffer.FileName} - {progress.ErrorMessage}");
                    SearchStatus = $"Download failed: {progress.ErrorMessage}";
                }
                else if (progress.Status == DownloadStatus.Cancelled)
                {
                    AddMessage($"*** DOWNLOAD CANCELLED: {progress.FileOffer.FileName}");
                    SearchStatus = "Download cancelled";
                }

                // Remove from active downloads
                var existing = ActiveDownloads.FirstOrDefault(d => d.FileOffer.FileName == progress.FileOffer.FileName);
                if (existing != null)
                {
                    ActiveDownloads.Remove(existing);
                }
            });
        }

        private async System.Threading.Tasks.Task ProcessSearchResultsAsync(string zipFilePath)
        {
            try
            {
                AddMessage($"*** Processing search results from {Path.GetFileName(zipFilePath)}...");

                var results = await _searchResultParser.ParseSearchResultsFromZipAsync(zipFilePath);

                if (results.Count == 0)
                {
                    AddMessage($"*** No results found in search file");
                    return;
                }

                AddMessage($"*** Found {results.Count} results");

                // Rank results by relevance
                var rankedResults = _searchResultParser.RankResults(results, _lastAuthorQuery, _lastTitleQuery);

                // Update UI with results
                Application.Current.Dispatcher.Invoke(() =>
                {
                    SearchResults.Clear();
                    foreach (var result in rankedResults)
                    {
                        SearchResults.Add(result);
                    }
                });

                AddMessage($"*** Search results loaded! Check the results tab for {results.Count} books");

                if (rankedResults.Count > 0)
                {
                    var topResult = rankedResults[0];
                    AddMessage($"*** Top result: {topResult.Author} - {topResult.Title} ({topResult.Format})");
                }
            }
            catch (Exception ex)
            {
                AddMessage($"*** ERROR processing search results: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task ImportToCalibreAsync(string bookFilePath, string fileName)
        {
            try
            {
                AddMessage($"*** Adding to Calibre library: {fileName}...");
                SearchStatus = "Adding to Calibre library...";

                var success = await _calibreService.AddBookToLibraryAsync(
                    bookFilePath,
                    CalibreLibraryPath,
                    CalibreDuplicateHandling,
                    CalibreContentServerUrl,
                    CalibreUsername,
                    CalibrePassword);

                if (success)
                {
                    AddMessage($"*** Successfully added to Calibre: {fileName}");
                    SearchStatus = "Added to Calibre library";
                }
                else
                {
                    AddMessage($"*** Failed to add to Calibre: {_calibreService.LastError}");
                    SearchStatus = $"Calibre import failed: {_calibreService.LastError}";
                }
            }
            catch (Exception ex)
            {
                AddMessage($"*** ERROR importing to Calibre: {ex.Message}");
                SearchStatus = $"Calibre import error: {ex.Message}";
            }
        }

        private async System.Threading.Tasks.Task LoadSettingsAsync()
        {
            try
            {
                var settings = await _settingsService.LoadCalibreSettingsAsync();
                if (settings != null)
                {
                    // Temporarily unsubscribe to avoid triggering saves during load
                    PropertyChanged -= OnCalibreSettingChanged;

                    CalibreLibraryPath = settings.LibraryPath;
                    AutoImportToCalibre = settings.AutoImport;
                    CalibreDuplicateHandling = settings.DuplicateHandling;
                    CalibreContentServerUrl = settings.ContentServerUrl;
                    CalibreUsername = settings.Username;
                    CalibrePassword = settings.Password;

                    // Re-subscribe
                    PropertyChanged += OnCalibreSettingChanged;

                    AddMessage("*** Calibre settings loaded");
                }
            }
            catch (Exception ex)
            {
                AddMessage($"*** Failed to load Calibre settings: {ex.Message}");
            }
        }

        private async void OnCalibreSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Save settings when any Calibre property changes
            if (e.PropertyName is "CalibreLibraryPath" or "AutoImportToCalibre" or "CalibreDuplicateHandling"
                or "CalibreContentServerUrl" or "CalibreUsername" or "CalibrePassword")
            {
                await SaveSettingsAsync();
            }
        }

        private async System.Threading.Tasks.Task SaveSettingsAsync()
        {
            try
            {
                var settings = new CalibreSettings
                {
                    LibraryPath = CalibreLibraryPath,
                    AutoImport = AutoImportToCalibre,
                    DuplicateHandling = CalibreDuplicateHandling,
                    ContentServerUrl = CalibreContentServerUrl,
                    Username = CalibreUsername,
                    Password = CalibrePassword
                };

                await _settingsService.SaveCalibreSettingsAsync(settings);
            }
            catch (Exception)
            {
                // Silently fail - settings won't be persisted but app continues
            }
        }

        #endregion
    }
}
