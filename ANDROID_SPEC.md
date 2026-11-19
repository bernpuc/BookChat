# BookChat for Android — Design Specification

**Status:** Design complete — see [ANDROID_IMPLEMENTATION_PLAN.md](ANDROID_IMPLEMENTATION_PLAN.md) for build phases
**Last updated:** 2026-05-05

### Design decisions log
| Date | Decision |
|---|---|
| 2026-05-03 | SearchScreen layout: persistent search bar top + results list + collapsible download strip bottom (Spotify mini-player pattern) |
| 2026-05-05 | No native library — all downloads pushed to Calibre Content Server; local temp files deleted after confirmed upload |
| 2026-05-06 | Calibre HTTP upload not viable (CDB API localhost-only in 9.8); saving to Downloads/BookChat/ instead |
| 2026-05-05 | LibraryScreen removed; bottom nav is Search + Downloads only |

---

## 1. Purpose

A native Android app that replicates the core BookChat workflow: connect to an IRC book-search network, search for e-books, download them via DCC, and push them automatically to a Calibre Content Server. The interface is redesigned for mobile from scratch rather than porting the desktop layout.

---

## 2. Core Features (MVP scope)

| Feature | Desktop equivalent | Mobile approach |
|---|---|---|
| IRC connection | Auto-connects on launch | Background service, connects on app open |
| Book search | Text field → bot query | Search bar with recent history |
| Search results | DataGrid with status column | Scrollable card list with status badges |
| DCC download | TCP socket transfer with progress | Foreground service with notification progress |
| Download queue | Implicit (one at a time) | Explicit queue with pause/cancel per item |
| Settings | WPF settings window | Dedicated Settings screen |
| Calibre import | CLI / HTTP API | HTTP upload to Calibre Content Server; local file deleted after confirmed add |

---

## 3. Tech Stack

- **Language:** Kotlin
- **UI:** Jetpack Compose (Material 3)
- **Architecture:** MVVM + Repository pattern (same philosophy as desktop)
- **DI:** Hilt
- **Async:** Kotlin Coroutines + Flow
- **Local storage:** DataStore for settings and recent searches (no Room — no persistent library)
- **Networking:** Raw sockets for IRC/DCC (same protocol as desktop)
- **Min SDK:** API 26 (Android 8.0)
- **Target SDK:** API 35

---

## 4. Screen Map

```
MainActivity (single-activity host)
├── SearchScreen          — default home
├── DownloadsScreen       — active + queued transfers
└── SettingsScreen        — IRC, Calibre, download config
```

Navigation: bottom nav bar with **Search** and **Downloads** only. Settings reachable via top-bar icon. No Library screen — books live in Calibre.

---

## 5. Screen Designs (TBD — to be filled in collaboratively)

### 5.1 SearchScreen

**Layout pattern:** Persistent search bar (top) · Results list (middle, scrollable) · Collapsible download strip (bottom, always visible when a download is active — Spotify mini-player pattern).

```
┌─────────────────────────────────┐
│  [IRC status dot] 🔍 Search...  │  ← TopAppBar with search field + connection indicator
├─────────────────────────────────┤
│                                 │
│  ┌─────────────────────────┐   │
│  │ 📖 Title                │   │  ← Result card
│  │    Author · EPUB · 2 MB │   │
│  │    bot_name  [Download] │   │
│  └─────────────────────────┘   │
│                                 │
│  ┌─────────────────────────┐   │
│  │ 📖 Title                │   │
│  │    Author · MOBI · 1 MB │   │
│  │    bot_name  [  Done  ] │   │  ← status badge replaces button after action
│  └─────────────────────────┘   │
│                                 │
│            ...                  │
│                                 │
├─────────────────────────────────┤
│  ▲  Book Title.epub  ████░░ 67%│  ← collapsible download strip (tap to expand)
└─────────────────────────────────┘
          [Search] [Downloads]  ← Bottom nav
```

**Search bar behavior:**
- Single query field (author + title combined, same as desktop `!search` command)
- Trailing IRC connection indicator dot: grey = disconnected, amber = connecting, green = ready
- Keyboard "Search" / IME action submits the query
- Recent searches dropdown (last 10, stored in DataStore)
- While connecting, show a subtle banner under the bar: "Connecting to IRC…" — bar is disabled until green

**Result cards:**
- Format badge (EPUB / MOBI / PDF / AZW3) — color-coded pill
- Title (bold), Author, file size, bot/server name
- Single primary action button per card — label changes with state:
  - `Download` → tapping sends the `!get` command and transitions to `Requesting…`
  - `Requesting…` → spinner, not tappable
  - `Downloading` → replaced by the collapsible strip (card shows a thin progress bar instead)
  - `Done` → green check, tapping opens the book in Library
  - `Failed` → red label `Retry`
- No confirm sheet — tap Download = immediate action (mobile users expect directness)

**Collapsible download strip:**
- Collapsed: single-line bar showing filename, progress bar, speed (KB/s), tap to expand
- Expanded: slides up to a half-screen bottom sheet showing full DownloadsScreen content (active + queue)
- Strip is hidden when no download is active or queued
- Strip sits above the bottom nav bar, never overlaps it

**Decisions:**
- Sort order: preserved as received from bot (no user-facing sort)
- Long result lists: lazy-loaded as the user scrolls (`LazyColumn` with paging); no hard cap
- Empty state: see below

**Empty state (no search run yet):**
```
┌─────────────────────────────────┐
│                                 │
│                                 │
│         📚                      │  ← large book-stack icon, centered
│                                 │
│      Find your next read        │  ← headline, Material3 headlineSmall
│                                 │
│  Search by title or author and  │
│  BookChat will fetch it for you │  ← two-line subtext, bodyMedium, muted color
│                                 │
│                                 │
└─────────────────────────────────┘
```
- Shown only before the first search in the session; once results arrive they never revert to empty state mid-session
- If IRC is still connecting when the user opens the app, the connection banner replaces the subtext line until ready
- No "try these searches" suggestions (keeps it clean; this is a power-user app)

### 5.2 DownloadsScreen

**Dual-mode:** renders as a full screen (bottom nav tap) and as the expanded collapsible strip from SearchScreen. Layout is identical in both cases — the strip just hosts it in a `ModalBottomSheet`.

```
┌─────────────────────────────────┐
│  Downloads                  [×] │  ← top bar (× only in bottom-sheet mode)
├─────────────────────────────────┤
│  ACTIVE                         │  ← section header
│ ┌─────────────────────────────┐ │
│ │ Book Title.epub             │ │
│ │ from xBot_42                │ │
│ │ ████████████░░░░░  67%      │ │  ← prominent progress bar
│ │ 128 KB/s · ~14 s remaining  │ │  ← speed + ETA on one line
│ │                      [Stop] │ │  ← single destructive action
│ └─────────────────────────────┘ │
│                                 │
│  QUEUE  (2)                     │  ← section header with count
│ ┌─────────────────────────────┐ │
│ │ Another Book.mobi      [✕] │ │  ← queued item: title + remove button
│ └─────────────────────────────┘ │
│ ┌─────────────────────────────┐ │
│ │ Third Book.epub        [✕] │ │
│ └─────────────────────────────┘ │
│                                 │
│  RECENTLY COMPLETED             │  ← section header (last session only)
│ ┌─────────────────────────────┐ │
│ │ ✓ Finished Book.epub        │ │
│ │ ✗ Failed Book.pdf   [Retry] │ │
│ └─────────────────────────────┘ │
└─────────────────────────────────┘
```

**Active download card:**
- Large segmented progress bar (fills left to right), percentage label on right
- Speed in KB/s, ETA in seconds/minutes — both update every second via Flow
- `Stop` button: cancels transfer, moves item to Recently Completed with `✗ Cancelled` state
- No pause — DCC has no pause mechanism; stop = restart from zero

**Queue:**
- One download at a time (desktop behavior preserved)
- Queue items show title + format only — no progress bar
- `✕` removes item from queue before it starts; no confirmation dialog (easy to re-queue from Search)
- Queue order: FIFO (tap order from SearchScreen)

**Recently Completed:**
- Shown for the current app session only; cleared on app restart
- `✓` items: tap opens the book in Library
- `✗` items: show failure reason in small muted text (e.g., "Connection refused", "Bot timed out") + `Retry` button which re-queues the request
- Kept to last 10 entries max; oldest drop off silently

**Empty state (nothing active or queued):**
```
         ⬇
    All clear
  No downloads in progress
```
Minimal — icon, two lines. No illustration needed here.

**Decisions:**
- Completed items are session-only; cleared on app restart (no Room storage for history)
- One download at a time, always — no concurrency setting

### 5.3 LibraryScreen

**Removed from scope.** The app has no persistent local library. All downloaded books are pushed to Calibre and local artifacts are deleted. Users browse their library in Calibre directly.

The bottom nav therefore has two items only: **Search** and **Downloads**. Settings via top-bar icon.

### 5.4 SettingsScreen

Scrollable single-column list, Material 3 `ListItem` style, grouped by section with dividers. No nested screens — everything on one page; the list is short enough.

```
┌─────────────────────────────────┐
│  ←  Settings                    │  ← back arrow to return to Search
├─────────────────────────────────┤
│  IRC                            │  ← section header
│  Nickname          BookChatUser │  ← tappable → inline text field
│  Server       irc.irchighway.net│
│  Port                      6667 │
│  Channel                #ebooks │
│  Password                  ···· │  ← masked; tap to edit
├─────────────────────────────────┤
│  CALIBRE                        │
│  Server URL  http://192.168.1.x │  ← tappable → inline text field
│                  [Test connection]│  ← appears below URL field only
│  Username                       │  ← blank = no auth
│  Password                  ···· │
│  Duplicates              Ignore ▾│  ← dropdown: Ignore / Overwrite / Add new
├─────────────────────────────────┤
│  ABOUT                          │
│  Version                  1.0.0 │  ← read-only
│  Source code            GitHub ↗│  ← external link
└─────────────────────────────────┘
```

**Editing behavior:**
- Tapping a field expands it to an inline text input in-place (no separate edit dialog)
- Keyboard "Done" / IME action saves and collapses the field
- Tapping away (focus lost) also saves
- Changes are written to DataStore immediately on save — no global Save button

**IRC defaults (pre-filled on first launch):**
- Server: `irc.irchighway.net`
- Port: `6667`
- Channel: `#ebooks`
- Nickname: `BookChat_` + 4 random digits (user can change it)
- Password: blank

**Calibre defaults:**
- Server URL: blank (user must fill in; app prompts on first download attempt if empty)
- Username / Password: blank
- Duplicates: `Ignore`

**Validation:**
- Port must be numeric 1–65535; show inline error if not
- Server URL must start with `http://` or `https://`; show inline error
- All other fields: free text, no validation
- IRC reconnects automatically when Server / Port / Channel / Nickname change and a connection is active — show a brief snackbar: "Reconnecting…"

**Decisions:**
- IRC password: NickServ (`/msg NickServ IDENTIFY <password>`) — consistent with desktop implementation
- Calibre URL field has a `Test connection` button inline; fires a lightweight HTTP request and shows a snackbar: "Connected ✓" or the error message (e.g., "Connection refused", "401 Unauthorized")

---

## 6. Calibre Integration

**Decision (revised 2026-05-06):** Calibre Content Server HTTP upload is not available in Calibre 9.8 — the CDB API (`/cdb/`) is restricted to localhost only. The desktop app uses the `calibredb` CLI subprocess which cannot be run from Android. Current approach: save downloaded books to `Downloads/BookChat/` on the Android device. Future: implement Calibre wireless device protocol (port 9090) for direct sync.

**Workflow:**
1. DCC download completes → file saved to app-private temp storage
2. App HTTP-POSTs the file to the Calibre Content Server (`/cdb/add-book` endpoint)
3. On HTTP 200, local temp file is deleted
4. On failure, temp file is retained and the download card shows `Calibre upload failed` + `Retry`

**Configuration (in SettingsScreen):**
- Content Server URL (e.g., `http://192.168.1.10:8080`)
- Username and password (optional, if Calibre server auth is enabled)
- Duplicate handling: ignore / overwrite / add new (mirrors desktop behavior)

**Constraints:**
- App and Calibre must be on the same network (or the server must be reachable)
- No offline mode — if Calibre is unreachable, temp file is kept and user is notified
- Temp storage lives in app-private cache dir (`context.cacheDir`); Android can reclaim it if space is low, so upload should happen immediately after download completes

---

## 7. IRC / DCC Protocol Notes

The desktop DCC implementation is in `MainWindowViewModel`. Key behaviors to preserve:

- IPv4 decimal-to-dotted conversion for DCC SEND offers
- 8 KB read buffer, 4 KB ACK cadence
- Quoted filename handling (`"filename with spaces.epub"`)
- Conflict resolution for duplicate filenames

These must be re-implemented in Kotlin using raw `Socket` / `ServerSocket`. The logic is well-understood from the desktop code and should not be abstracted differently.

---

## 8. Permissions Required

- `INTERNET` — IRC and DCC sockets
- `FOREGROUND_SERVICE` — download service
- `FOREGROUND_SERVICE_DATA_SYNC` — Android 14+ foreground service type
- `POST_NOTIFICATIONS` — download progress notifications (Android 13+)
- No external storage permissions needed — all file I/O uses app-private `cacheDir`

---

## 9. Decisions (formerly open questions)

1. **UI design** — SearchScreen ✓, DownloadsScreen ✓, LibraryScreen removed, SettingsScreen ✓
2. **In-app reader** — moot; books go to Calibre, reading happens there
3. **IRC nicknames** — `BookChat_` + 4 random digits generated on first launch, editable in Settings; no forced login flow
4. **Simultaneous downloads** — one at a time, always; no user setting
5. **Connection resilience** — auto-reconnect silently on disconnect; exponential backoff starting at 5 s, doubling up to 60 s cap; connection dot returns to amber during reconnect attempts; no user-facing reconnect button (it just happens)

**No open questions remain.**

---

## 10. Out of Scope (MVP)

- XDCC batch requests
- IRC chat (the app uses IRC only as a search/download mechanism)
- Book metadata editing
- Cloud backup of library
- iPad / tablet optimized layout (design for phone first)
- Native local library (all books live in Calibre)
- In-app e-book reader
