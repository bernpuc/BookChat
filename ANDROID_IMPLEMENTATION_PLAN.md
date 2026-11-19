# BookChat Android — Implementation Plan

**Project location:** `C:\Users\bernp\source\repos\BookChatAndroid`
**Created:** 2026-05-05

---

## Architectural Overview

The entire app communicates through a single long-lived `DownloadService` (Android `ForegroundService`) that owns the IRC socket and DCC socket lifecycle. The UI layer never touches sockets directly — it communicates with the service via a bound service interface backed by `StateFlow` streams.

```
MainActivity
  └── NavHost (Compose Navigation)
        ├── SearchScreen      ← SearchViewModel
        ├── DownloadsScreen   ← DownloadViewModel
        └── SettingsScreen    ← SettingsViewModel

DownloadService (ForegroundService, bound + started)
  ├── IrcClient       (coroutine, raw socket)
  ├── DccDownloader   (coroutine, raw socket)
  ├── DownloadQueue   (StateFlow<List<QueueItem>>)
  └── CalibreUploader (OkHttp)

DataStore (settings + recent searches)
```

---

## Dependency Order

```
Phase 1 (Settings + Scaffold)
    ↓
Phase 2 (IRC Client)          ← depends on Phase 1 for settings
    ↓
Phase 3 (Search + Parser)     ← depends on Phase 2 for IRC + DCC (search zip)
    ↓
Phase 4 (Download Service)    ← depends on Phase 2 (IRC), Phase 3 (DccParser, results)
    ↓
Phase 5 (Polish)              ← depends on all previous phases
```

---

## Phase 1 — Project Scaffold and Settings Foundation

**Goal:** A compilable, runnable project with all dependencies wired, the settings data layer complete, and a SettingsScreen that reads/writes live data. No IRC. No DCC.

### Steps

1. Create new Android Studio project: Empty Activity, Kotlin, min SDK 26, package `com.bookchat`.
2. Add all Gradle dependencies (see reference at end of this file).
3. Add `AndroidManifest.xml` permissions: `INTERNET`, `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_DATA_SYNC`, `POST_NOTIFICATIONS`.
4. Create `BookChatApp : Application()` annotated with `@HiltAndroidApp`.
5. Define `AppSettings` data class: `ircServer`, `ircPort`, `ircChannel`, `ircNickname`, `ircPassword`, `calibreUrl`, `calibreUsername`, `calibrePassword`, `calibreDuplicates`.
6. Create `SettingsRepository` using `DataStore<Preferences>`. One `Flow<AppSettings>` getter, one `suspend fun save(settings: AppSettings)`. Apply first-launch defaults here (`BookChat_` + 4-digit random suffix for nickname, `irc.irchighway.net`, port `6667`, channel `#ebooks`).
7. Create `SettingsModule` (Hilt `@Module`) providing the `DataStore` singleton via `preferencesDataStore(name = "settings")` on `Context`.
8. Create `SettingsViewModel` injecting `SettingsRepository`. Expose `uiState: StateFlow<AppSettings>`, write immediately to DataStore on each field change — no Save button.
9. Build `SettingsScreen`: scrollable `LazyColumn`, `ListItem`-based rows, grouped by section (IRC / CALIBRE / ABOUT). Inline expand-to-edit: each row toggles a local `editing: Boolean`; when true, the trailing slot becomes a `TextField` with `ImeAction.Done`. IME done or focus loss calls `viewModel.update(...)` and collapses.
10. Port validation: `isError = port !in 1..65535`. URL validation: `isError = !url.startsWith("http://") && !url.startsWith("https://")`.
11. Add `Test Connection` button below the Calibre URL field: fire a lightweight OkHttp `HEAD` to the URL from `viewModel` scope, show a `Snackbar` ("Connected ✓" or error).
12. Wire `MainActivity` with `NavHost` (initial route = `"search"`). Settings via top-bar icon.

### Key Files

| File | Purpose |
|---|---|
| `BookChatApp.kt` | Hilt application class |
| `data/settings/AppSettings.kt` | Settings data class |
| `data/settings/SettingsRepository.kt` | DataStore wrapper |
| `di/SettingsModule.kt` | Hilt module |
| `ui/settings/SettingsViewModel.kt` | |
| `ui/settings/SettingsScreen.kt` | |
| `ui/MainActivity.kt` | NavHost scaffold |

### Gotchas

- `preferencesDataStore` is a top-level property delegate on `Context` — declare it once at file level, never inside a class. The Hilt module must be `@Singleton` scoped.
- `FocusRequester` needed for inline-edit: request focus via `LaunchedEffect` after composing the text field, otherwise the keyboard does not appear on first tap.
- `ImeAction.Done` inside a `LazyColumn` can cause unexpected scroll. Pin items with `key = { item.id }`.

---

## Phase 2 — IRC Client with Connection State Machine

**Goal:** Working IRC connection to `irc.irchighway.net #ebooks`. Exposes connection state and raw message events as `Flow`. Settings changes trigger reconnect. UI: connection status dot on SearchScreen top bar.

### Steps

1. Create `IrcConnectionState` sealed class: `Disconnected`, `Connecting(attempt: Int)`, `Connected`.
2. Create `IrcClient`. Takes `Flow<AppSettings>`. Internal `MutableStateFlow<IrcConnectionState>` and `Channel<String>` for inbound lines.
3. Implement `suspend fun connect()`:
   - `Socket(host, port)` with 30-second connect timeout.
   - Send `NICK`, `USER`, then `JOIN` after receiving `001` numeric.
   - If `ircPassword` non-blank, send `/msg NickServ IDENTIFY <password>` after `001`.
   - Read lines in a loop via `BufferedReader.readLine()`. Respond to `PING` with `PONG` directly (not via SharedFlow — latency sensitive).
   - Emit all other lines to a `MutableSharedFlow<String>(extraBufferCapacity = 64)`.
   - On `IOException`, throw to trigger reconnect.
4. Exponential backoff reconnect loop: `while(true)` catching failures; delay = `min(5_000L * 2.pow(attempt), 60_000L)`. Reset `attempt = 0` on successful join.
5. Create `IrcRepository` (`@Singleton`). Owns `IrcClient` and its `CoroutineScope(SupervisorJob() + Dispatchers.IO)`. Exposes `connectionState: StateFlow<IrcConnectionState>`, `inboundLines: SharedFlow<String>`, `fun sendRaw(line: String)`.
6. Settings-change reconnect: collect `settingsRepository.settings` with `distinctUntilChangedBy { listOf(it.ircServer, it.ircPort, it.ircChannel, it.ircNickname) }`. On change: `job.cancelAndJoin()`, re-launch, emit snackbar event "Reconnecting…".
7. `SearchScreen` stub: `TopAppBar` with IRC status dot (grey / amber / green `Canvas` circle). Connecting banner via `AnimatedVisibility`.

### Key Files

| File | Purpose |
|---|---|
| `irc/IrcConnectionState.kt` | Sealed class |
| `irc/IrcClient.kt` | Raw socket coroutine |
| `irc/IrcRepository.kt` | Hilt singleton, state + send |
| `di/IrcModule.kt` | |
| `ui/search/SearchViewModel.kt` | Consumes connection state |
| `ui/search/SearchScreen.kt` | Status dot + banner stub |

### Gotchas

- Sockets must run on `Dispatchers.IO`. Close the socket explicitly in a `finally` block on cancellation so `readLine()` unblocks immediately.
- Trim `\r` from each line: `BufferedReader.readLine()` strips `\n` but may leave `\r` on some JVM implementations.
- PING/PONG handled directly in the read loop, not routed through `SharedFlow`, to avoid latency-based server disconnects.
- Settings-change reconnect must `cancelAndJoin()` the old job before starting a new one — otherwise both coroutines race to write to the socket.

---

## Phase 3 — Search Flow (Query, Results Parsing, Results UI)

**Goal:** User types a query, app sends `@search <query>`, SearchBot DCC-sends a `.zip` of results, app downloads it (minimal DCC receiver), parses the text file, displays result cards.

### Steps

1. Create `SearchResult` data class: `botName`, `fileHash`, `author`, `title`, `series`, `language`, `format`, `fileSizeBytes: Long`, `fileName`, `downloadCommand`, `downloadState: DownloadState`.
2. Create `DownloadState` sealed class: `Idle`, `Requesting`, `Queued`, `Downloading(progress: Float)`, `Uploading`, `Done`, `Failed(reason: String)`.
3. Port `SearchResultParser` from C# to Kotlin as a pure `object`. All bot formats from the desktop implementation must be ported — do not simplify. Function signature: `fun parseResultLine(line: String): SearchResult?`.
4. Create `SearchRepository` (`@Singleton`). Exposes `suspend fun search(query: String)` (sends `@search <query>`, saves to recent searches), `resultsFlow: StateFlow<List<SearchResult>>`, `searchState: StateFlow<SearchUiState>`.
5. In `IrcRepository`, parse inbound PRIVMSG/CTCP lines for `DCC SEND` offers. Emit a `DccOffer` via `SharedFlow<DccOffer>`. CTCP markers (`\x01`) must be stripped before parsing.
6. Create `DccOffer` data class: `senderNick`, `fileName`, `ipDecimal: Long`, `port: Int`, `fileSize: Long`. Create `DccParser` with `fun parse(rawLine: String, senderNick: String): DccOffer?`.
   - Decimal-to-dotted IP: `InetAddress.getByAddress(ByteBuffer.allocate(4).putInt(decimalIp.toInt()).array()).hostAddress`
   - Quoted filename regex: `DCC SEND\s+"?([^"]+)"?\s+(\d+)\s+(\d+)\s+(\d+)`
7. Minimal DCC receiver in `SearchRepository` for search-result zips: open `Socket(ip, port)`, read `fileSize` bytes with 8 KB buffer into `cacheDir/search/`, send 4-byte big-endian ACK every 4096 bytes. On completion, call `parseZip()`, populate `resultsFlow`, delete temp file.
8. `parseZip`: stream-unzip, find `.txt` entry, read lines, call `parseResultLine`, collect non-null results. Run on `Dispatchers.Default` (CPU-bound).
9. `SearchViewModel`: exposes `searchText`, `results`, `searchState`, `recentSearches`. `fun onSearch()` delegates to `SearchRepository`.
10. Build `SearchScreen` UI:
    - `TopAppBar` with single-line `TextField` (disabled when not `Connected`).
    - Recent searches `ExposedDropdownMenuBox` when field focused.
    - Empty state: centered column, book icon, "Find your next read" headline, two-line subtext. Shown only before first search in session.
    - `LazyColumn` of `BookResultCard` composables.
    - `BookResultCard`: title (bold), author, format pill (color-coded), file size, bot name, single action button changing per `DownloadState`.
11. Wire bottom nav (Search / Downloads) in `MainActivity`.

### Key Files

| File | Purpose |
|---|---|
| `data/search/SearchResult.kt` | |
| `data/search/DownloadState.kt` | Sealed class |
| `data/search/SearchResultParser.kt` | Pure Kotlin port of all bot formats |
| `data/search/SearchRepository.kt` | |
| `irc/DccOffer.kt` | |
| `irc/DccParser.kt` | |
| `ui/search/SearchViewModel.kt` | |
| `ui/search/SearchScreen.kt` | |
| `ui/search/BookResultCard.kt` | |

### Gotchas

- DCC offer arrives as a private CTCP to your nick, not a channel message. IRC read loop must handle both.
- Search zip DCC download must run in a separate coroutine from the IRC read loop — collect `dccOffers` in `SearchRepository`'s own scope.
- Results list cleared on each new search. Do not accumulate across searches.
- Format pill colors: use `when(format.uppercase())` in a `@Composable` returning Material3 container colors — do not hardcode hex.

---

## Phase 4 — Download Service, DCC Book Downloads, Queue

**Goal:** Full FIFO queue, one download at a time, running in a `ForegroundService`. After DCC completes, file is HTTP-POSTed to Calibre. Local file deleted on success. Collapsible download strip on SearchScreen.

### Steps

1. Create `DownloadService : Service()`. Manifest: `android:foregroundServiceType="dataSync"`.
2. Binder pattern: `DownloadServiceBinder : Binder()` returns the service. `MainActivity` binds on `onCreate`, stores reference in `StateFlow<DownloadService?>`.
3. Move `IrcRepository` ownership into `DownloadService`. IRC lifetime = service lifetime.
4. Create `DownloadQueue`: `val queue: StateFlow<List<DownloadItem>>`, `val activeDownload: StateFlow<ActiveDownload?>`, `fun enqueue`, `fun cancel`, `fun retry`.
5. Create `DownloadItem` data class: `id: UUID`, `botName`, `fileHash`, `expectedFileName`, `displayTitle`, `format`, `state: DownloadItemState`.
6. `DownloadItemState` sealed class: `Queued`, `RequestSent`, `DccOffered`, `Downloading(bytesReceived, totalBytes, speedBps, etaSeconds)`, `Uploading`, `Done`, `Failed(reason)`, `Cancelled`.
7. Create `DccDownloader`. `suspend fun download(offer: DccOffer, destFile: File, onProgress: (Long) -> Unit): Result<File>`:
   - `Socket(offer.ippDotted, offer.port)`, 30-second connect timeout.
   - 8 KB `ByteArray` read buffer.
   - ACK: every `totalReceived - lastAcked >= 4096`, write 4-byte big-endian `totalReceived.toInt()` via `DataOutputStream.writeInt()`.
   - On `CancellationException`: close socket, delete partial file, return `Result.failure`.
   - Send final ACK on completion.
8. Speed/ETA: 3-second sliding window of `(timestamp, bytes)`. Update every 1 second via a parallel `delay(1000)` coroutine inside `coroutineScope { }`.
9. Create `CalibreUploader` (`@Singleton`). `suspend fun upload(file: File, settings: AppSettings): Result<Unit>` via OkHttp `multipart/form-data` POST to `<calibreUrl>/cdb/add-book/1/book`. Basic auth if credentials set. On HTTP 200: delete file, return success. On failure: return `Result.failure` with message, keep file.
10. Full download lifecycle in `DownloadService`: DCC offer matched → `DccDownloader.download()` → `CalibreUploader.upload()` → `Done` or `Failed`. After terminal state, dequeue and start next item.
11. Foreground notification: `NotificationCompat.Builder` with `setProgress`. Update from `activeDownload` Flow. Request `POST_NOTIFICATIONS` permission on API 33+.
12. `DownloadViewModel` (injected from binder): exposes `activeDownload`, `queue`, `completedDownloads`. Methods: `onCancel`, `onRemoveFromQueue`, `onRetry`.
13. Build `DownloadsScreen`: ACTIVE / QUEUE / RECENTLY COMPLETED sections per wireframe. Reuse as `ModalBottomSheet` content on SearchScreen.
14. Build `CollapsibleDownloadStrip`: `Surface` in `SearchScreen`'s `Scaffold` above `bottomBar`. Collapsed = 56 dp row (filename + progress + speed). Expanded = `ModalBottomSheet`. Hidden via `AnimatedVisibility` when nothing active or queued.

### Key Files

| File | Purpose |
|---|---|
| `service/DownloadService.kt` | Foreground service, owns IRC + DCC lifetime |
| `service/DownloadQueue.kt` | FIFO queue state machine |
| `service/DownloadItem.kt` | |
| `service/DccDownloader.kt` | Raw socket DCC receiver |
| `service/CalibreUploader.kt` | OkHttp multipart upload |
| `ui/downloads/DownloadViewModel.kt` | |
| `ui/downloads/DownloadsScreen.kt` | |
| `ui/search/CollapsibleDownloadStrip.kt` | |

### Gotchas

- **Foreground service type (API 34+):** Manifest must include `android:foregroundServiceType="dataSync"` and permission `FOREGROUND_SERVICE_DATA_SYNC`. Missing this crashes on Android 14+.
- **Binding timing:** `bindService` is async. ViewModels must handle `StateFlow<DownloadService?> = null` gracefully.
- **ACK overflow:** e-books are safely under 2 GB so `toInt()` is safe, but add `require(offer.fileSize < Int.MAX_VALUE)` to fail fast on pathological offers.
- **DCC offer matching:** match by filename (lowercase, strip path separators). One-at-a-time queue prevents race on identical filenames.
- **OkHttp on main thread:** all calls via `withContext(Dispatchers.IO)`.
- **Notification channel:** create in `Application.onCreate`, ID = `"download_progress"`, importance = `IMPORTANCE_LOW`.
- **`cacheDir` eviction:** upload immediately after DCC completes. Handle `FileNotFoundException` in `CalibreUploader` if file was evicted.

---

## Phase 5 — Polish and Production Readiness

**Goal:** Fill every gap between "it works" and "it ships."

### Steps

1. Calibre URL guard in `DownloadService.enqueue()`: if `calibreUrl.isBlank()`, emit `UserEvent.ShowSnackbar("Set Calibre URL in Settings first")` instead of enqueuing.
2. `UserEvent` sealed class + `SharedFlow(replay=0, extraBufferCapacity=1, onBufferOverflow=DROP_OLDEST)` collected in `MainActivity` for Snackbars.
3. Single `SnackbarHostState` hoisted to `MainActivity`, shared via `CompositionLocalProvider`.
4. Completed items capped at 10 in `DownloadQueue`; oldest dropped silently on add.
5. Recent searches: `SettingsRepository.addRecentSearch(query)` — prepend, deduplicate, trim to 10, store as pipe-delimited `String` in DataStore.
6. Dark mode: all colors from `MaterialTheme.colorScheme`. Format pills use tinted container colors, not hardcoded hex.
7. Accessibility: `contentDescription` on IRC status dot, format pills, action buttons.
8. ProGuard: OkHttp ships its own rules. No additional rules needed (DataStore uses `Preferences.Key`, not reflection).
9. End-to-end test checklist:
   - [ ] Connect to IRC, confirm green dot
   - [ ] Search, confirm result cards appear
   - [ ] Download a book, confirm DCC progress and Calibre upload
   - [ ] Confirm temp file deleted after upload
   - [ ] Confirm notification shown and dismissed
   - [ ] Settings changes trigger IRC reconnect
   - [ ] Calibre URL blank → snackbar, no enqueue
   - [ ] Stop a download → `Cancelled` state, next item starts
   - [ ] Retry a failed upload

### Key Files

| File | Purpose |
|---|---|
| `ui/common/UserEvent.kt` | Sealed class for one-shot events |
| `ui/MainActivity.kt` | Event bus + SnackbarHost |
| `data/settings/SettingsRepository.kt` | addRecentSearch |

---

## Gradle Dependencies (`build.gradle.kts`)

```kotlin
implementation(platform("androidx.compose:compose-bom:2024.09.00"))
implementation("androidx.compose.ui:ui")
implementation("androidx.compose.material3:material3")
implementation("androidx.compose.ui:ui-tooling-preview")

implementation("androidx.navigation:navigation-compose:2.8.0")

implementation("com.google.dagger:hilt-android:2.51")
kapt("com.google.dagger:hilt-android-compiler:2.51")
implementation("androidx.hilt:hilt-navigation-compose:1.2.0")

implementation("androidx.datastore:datastore-preferences:1.1.1")

implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.0")

implementation("com.squareup.okhttp3:okhttp:4.12.0")

implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.4")
implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.4")
```

No Room. No third-party IRC library. No image loading library.

---

## Highest-Risk Files

These are the files most likely to require debugging time and careful porting:

| File | Risk | Reason |
|---|---|---|
| `irc/IrcClient.kt` | High | Blocking socket + coroutine cancellation + reconnect backoff must all interact correctly |
| `service/DownloadService.kt` | High | Android service lifecycle + binder + foreground service type on API 34+ |
| `service/DccDownloader.kt` | High | ACK cadence, byte ordering, cancellation, partial file cleanup |
| `data/search/SearchResultParser.kt` | Medium | Many bot formats; port must be exact or results will silently drop |
| `ui/search/CollapsibleDownloadStrip.kt` | Medium | Bottom sheet state + animation + interaction with bottom nav |
