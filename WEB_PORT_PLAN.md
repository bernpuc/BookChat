# BookChat Web Port — Design & Porting Plan

Status: **investigation only — nothing in this document has been implemented.**

Goal: stop depending on the Windows desktop (Bernards-NUC) being on in order to search/download
books over IRC. Host the IRC/DCC "book finder" as an always-on service on **kassandra**
(Ubuntu Server, Intel NUC), the same way Calibre was just moved there, with a web UI reachable
from any browser/phone on the LAN.

This plan is based on a direct code survey of:
- `C:\local\source\BookChat` (WPF desktop app, Prism + DryIoc, Meebey.SmartIrc4net)
- `C:\local\source\BookChatAndroid` (Kotlin/Compose, hand-rolled IRC/DCC, Drive handoff)
- `C:\Users\bernp\KassandraWiki\Services\*.md` (docker-compose conventions for the existing stack)

---

## 1. What exists today

### 1.1 BookChat (WPF desktop)

- Single project, .NET 9 / WPF, **Prism.DryIoc** for MVVM+DI, **SmartIrc4net 1.1.0** for IRC.
- All IRC/DCC/VM logic lives in one class, `ViewModels/MainWindowViewModel.cs` (~1000+ lines) —
  there is no separable "IRC core" today; the IrcClient is a field on the ViewModel itself.
- **IRC**: connects to `irc.irchighway.net:6667`, `#ebooks`, default nick `ircuser`.
  `AutoReconnect=false` — no automatic reconnect on drop, user must click Connect again. No
  NickServ identify. Connect/Login/Join/Listen runs on a dedicated background `Thread`; all
  SmartIrc4net event callbacks fire off-UI-thread and are marshalled back via
  `Application.Current.Dispatcher.Invoke(...)` (~15 call sites) — this is the one thing a web port
  removes entirely (there's no WPF dispatcher on a server; it becomes "push state to SignalR
  clients" instead).
- **DCC**: hand-rolled in `Services/DccService.cs` — no DCC NuGet package. Parses `CTCP DCC SEND`
  via regex, always connects **out** as a TCP client to the sender's advertised IP:port (classic
  active DCC; no passive/reverse DCC support). Manual 4-byte big-endian ACKs every 4KB. All offers
  are **auto-accepted**, no user confirmation step.
- **Search**: sends `@search <query>` to the channel; a bot named like `*Search*` DCC-sends a
  `.zip` of results; `Services/SearchResultParser.cs` unzips/parses several known XDCC bot
  result-line formats and ranks them (`RankResults`, scores EPUB/MOBI/author/title/language
  matches). Selecting a result sends `!<BotName> <FileHash>` to the channel, which triggers the
  real DCC SEND for the book, through the same DCC pipeline.
- **Config/credentials**: one JSON file, `%APPDATA%\BookChat\app-settings.json`
  (`Services/SettingsService.cs`), auto-saved on property change. Holds IRC server/port/nick/
  channel, library path, Calibre content-server URL + **username/password stored in plaintext**.
  No encryption/DPAPI anywhere.
- **Persistence**: none beyond the settings file. Chat log capped at 1000 in-memory lines; search
  results and active downloads are in-memory `ObservableCollection`s, lost on exit. No SQLite, no
  download history.
- **Known rough edges worth not carrying forward**: dead "ChatSharp" exception-suppression cruft
  in `App.xaml.cs`; an unused filename-based `UpdateSearchResultStatus` helper suggests result
  status is currently correlated loosely (by object reference, not a stable key) — a multi-user
  web UI needs a real correlation key (hash/request id) for concurrent downloads.

### 1.2 BookChatAndroid (for comparison — already solved "no always-on machine")

This is the single most useful reference for the web port, because it already had to solve "the
process is not always running" and "the download needs to end up somewhere else automatically":

- **No IRC/DCC library** either — raw `Socket`/`SSLSocket`, hand-rolled, same as the WPF app will
  need to become once ported off SmartIrc4net (SmartIrc4net doesn't fit a server as cleanly —
  see §3).
- **Liveness policy is a two-state rule, not just "foreground":** `IrcRepository` disconnects IRC
  on `ON_STOP` (app backgrounded) *unless* `hasActiveDownload` is true, in which case a foreground
  Service + persistent notification keeps the process alive for just the transfer. **This is
  exactly the right model for the kassandra port**, inverted: a server has no "foreground" concept,
  but the same signal — "is there a pending search or an active/queued download?" — should drive
  whether the hosted background service stays connected to IRC at all, versus connecting on demand.
- **Reconnect-with-backoff** (`IrcClient.connectLoop`, capped exponential backoff 2s→15s) is
  self-contained and directly reusable as a design pattern (the WPF app has no equivalent at all —
  this is a straight improvement to carry into the web port).
- **State machine**: `DownloadItemState` (Queued → RequestSent → DccOffered → Downloading →
  Done/SavedLocally/Failed/Cancelled) is a clean, reusable shape for a job/download row in a web
  UI and maps well onto a DB table + SignalR push model.
- **Google Drive handoff** is the direct analogue of "get the file from where it landed to where
  the user wants it" — immediate (not batched) upload right after download/unzip, via a raw OkHttp
  multipart POST with a bearer token (not the full Drive SDK), to one fixed folder ID, with a
  local-save fallback on failure. For the web port, the equivalent "destination" problem is simpler
  (see §4.4) because the server *is* the machine with the library, but the Drive code is a good
  reference for a robust "upload, verify, fall back to local on failure" pattern if ever needed.
- **Known gaps, don't repeat these**: no persisted job queue (everything is in-memory, lost on
  process death — only the foreground service protects against the OS reaping it, not an actual
  crash); DCC receive loop is duplicated (once for book files, once inline for search-result
  zips) instead of a single shared primitive; IRC/NickServ password stored as a plain DataStore
  preference (no encryption, same gap as the WPF app).

### 1.3 Shared "IRC/DCC core" concept

Neither existing app actually has a reusable core today — each hand-rolled its own IRC+DCC code
independently (SmartIrc4net on WPF; raw sockets on Android). But their *behavior* has converged on
the same shape, which is good news for designing one:

1. Connect/login/join, with reconnect-with-backoff (Android's pattern, not WPF's).
2. Watch inbound lines for `CTCP DCC SEND`, parse filename/ip/port/size.
3. Distinguish "search-result zip" vs. "real book" DCC offers by sender-nick heuristic
   (`nick.Contains("Search")`) in both apps — brittle but consistent; worth keeping for bot
   compatibility, but isolating behind one function.
4. DCC receive = connect out, read loop with periodic 4-byte ACKs, write to a destination.
5. Search = send `@search`, wait for the results zip, unzip, parse multiple known bot formats,
   rank.
6. Download = send `!<bot> <hash>`, wait for the matching DCC offer, receive.
7. A job/result needs a stable correlation id (both apps are weak here) so multiple concurrent
   downloads/searches don't cross-contaminate state — important for a *multi-client* web UI in a
   way it never was for a single-user desktop/phone app.

A C# **class library** implementing steps 1–7, independent of WPF/ASP.NET, is the natural "core"
— see §3.1.

---

## 2. Recommended architecture

**Blazor Server**, hosted as an ASP.NET Core app with a background **Hosted Service** owning the
IRC/DCC connection, pushing UI updates in-process (Blazor Server's own SignalR-based render
pipeline does this for free — no separate SignalR hub needed unless you want other clients, like
a future mobile app, to subscribe too).

### 2.1 Why Blazor Server fits this app specifically (not just "it's the stated stack")

- The IRC connection is **single, long-lived, server-owned, shared across all viewers** — there is
  exactly one IRC identity/nick for the household, one DCC endpoint, one set of in-flight
  downloads. This is fundamentally a "one background service, many thin viewers" app, which is
  what Blazor Server's circuit model is built for: render state lives server-side, the browser is
  a dumb terminal getting diffs. You don't need to invent a client-side state sync protocol —
  Blazor Server's own circuit *is* that protocol.
  - Mapping to the Android app's `hasActiveDownload` liveness rule (§1.2) is direct: it becomes
    "does the hosted service have anything in its job queue?" with zero per-viewer logic needed,
    because there's only one server-side job queue, not one per browser tab.
- Blazor Server keeps full C# — the DCC byte-shuffling and SmartIrc4net-derived logic ports with
  much less rewriting than a Blazor WASM + separate API approach would need (WASM can't own a
  background TCP connection at all; you'd need the hosted-service backend anyway, plus a second
  client app, for no benefit here).
- Reasonable to run on a NUC: Blazor Server's per-connected-browser server memory cost is fine for
  a household-scale app (a handful of concurrent viewers at most), and avoids WASM's slower cold
  start and larger download for an app that's used in short bursts ("go check if a book came in").
- The main downside of Blazor Server — it needs a persistent connection to the server and doesn't
  tolerate flaky networks well — is a non-issue on a home LAN. (It would matter if this were
  exposed over the open internet; it isn't being proposed as such here — see §5 open question on
  remote access.)

### 2.2 Why not the alternatives

- **Blazor WASM**: wrong shape — the IRC/DCC connection must live server-side regardless (a browser
  can't hold a raw TCP DCC socket or stay connected when the tab is closed), so WASM would only add
  a second app and an API layer between the UI and the thing doing the real work, with no payoff.
- **Plain ASP.NET Core Web API + separate SPA (React/etc.)**: more moving parts (two codebases,
  a build pipeline for JS) for no capability WASM/Blazor Server doesn't already lack; abandons the
  user's stated C# preference for no architectural benefit here.
- **Minimal API + SignalR hub, no Blazor**: viable, and basically "Blazor Server minus the
  server-rendered UI framework" — you'd end up hand-building the client-side state sync Blazor
  Server gives for free. Only worth it if a rich, highly interactive client-rendered UI were a hard
  requirement; this app's UI (a search box, a results table, a downloads list) does not need that.

**Recommendation: ASP.NET Core host, with:**
- A `BookChat.Core` class library (new) holding the IRC/DCC logic, UI-framework-agnostic (see 3.1).
- An `IHostedService` (`IrcHostedService`) owning one `BookChatSession`/`IrcCoreClient` instance,
  started at app startup, implementing the Android app's reconnect-with-backoff pattern (not the
  WPF app's manual-only reconnect).
- A small persistence layer — **SQLite via EF Core** (matches the user's stated secondary stack)
  for: job/download history (state machine from §1.3 item 7) and settings (replacing the plaintext
  JSON file — see §4.3 on secrets).
- Blazor Server components inject the singleton `BookChatSession` directly and subscribe to its
  plain C# events (`MessageLogged`, `JobsChanged`, `ConnectionStateChanged`,
  `SearchResultsReady`), calling `InvokeAsync(StateHasChanged)` — no separate SignalR hub required
  since Blazor Server already multiplexes state over one per browser circuit.

**Status: implemented.** `BookChat.Core` and `BookChat.Web` now exist in this repo (added to
`BookChat.sln`) and build/run — see §6 for what's there and what's still open.

### 2.3 What's reused vs. rewritten from the WPF app

| Component | Verdict | Why |
|---|---|---|
| `Services/DccService.cs` (DCC parse/receive/ACK logic) | **Reuse with light edits** | Pure byte-level logic, no WPF dependency already — `TcpClient`/`NetworkStream`/async, ports almost as-is into `BookChat.Core`. |
| `Services/SearchResultParser.cs` | **Reuse almost as-is** | Pure string/regex parsing, zero UI coupling. |
| `Services/CalibreService.cs` (calibredb CLI shim) | **Reuse as-is** | Shells out to `calibredb`; works identically from a Linux container calling the containerized Calibre's `calibredb`, or via its content-server API — see §4.4. |
| `Meebey.SmartIrc4net` usage in `MainWindowViewModel` | **Rewrite** | Tightly coupled to the VM; also note SmartIrc4net itself is tripping `PlatformNotSupportedException` workarounds under .NET 9 already (`Cleanup()`'s swallowed exception) — a good opportunity to replace it with a small hand-rolled async socket client in the new core, following the Android app's cleaner connect/reconnect-loop design instead. SmartIrc4net's threading model (raw `Thread` + blocking `Listen()`) doesn't fit an `IHostedService`'s async lifecycle well anyway. |
| All WPF-specific code (`MainWindow.xaml(.cs)`, `Dispatcher.Invoke` marshalling, Prism `BindableBase`/`DelegateCommand`, `PasswordBox` workaround) | **Discard** | No web equivalent needed; Blazor's own render model replaces all of it. |
| `Services/SettingsService.cs` (plaintext JSON) | **Rewrite** | Replace with SQLite-backed settings + proper secret handling (§4.3). |
| `Models/*` (SearchResult, DownloadProgress, FileOffer, AppSettings) | **Reuse concepts, adjust** | Good shapes; add a stable correlation id per §1.3 item 7 so concurrent jobs don't collide; `AppSettings` becomes DB-backed, not a POCO round-tripped to JSON. |

---

## 3. Proposed project layout

```
BookChat.sln
  BookChat/                 (existing WPF app — left as-is, or deprecated later; user's call)
  BookChat.Core/             (NEW: class library, no WPF/ASP.NET refs)
    Irc/IrcCoreClient.cs       — connect/login/join/reconnect-with-backoff, line event stream
    Irc/DccOffer.cs            — parse CTCP DCC SEND (ported from DccService.cs)
    Irc/DccReceiver.cs         — the one shared "receive N bytes with periodic ACKs" primitive
                                 (both BookChat and BookChatAndroid independently wrote this twice;
                                 do it once here, correctly, with a destination Stream, not a path,
                                 so callers decide file vs. memory — fixes the Android app's
                                 duplication noted in §1.2)
    Search/SearchResultParser.cs   — ported from SearchResultParser.cs
    Search/ResultRanker.cs
    Jobs/DownloadJob.cs         — state machine (Queued/RequestSent/DccOffered/Downloading/
                                   Done/Failed/Cancelled), with a stable Guid/hash correlation id
    CalibreImportService.cs     — ported from CalibreService.cs
  BookChat.Web/              (NEW: ASP.NET Core + Blazor Server host)
    Services/IrcConnectionService.cs   — IHostedService wrapping BookChat.Core.Irc.IrcCoreClient
    Services/IrcStateBroker.cs         — pushes connection/job/search-result updates to components
    Data/BookChatDbContext.cs          — EF Core + SQLite: Settings, DownloadHistory, (optional) SearchCache
    Pages/Search.razor, Downloads.razor, Settings.razor, Log.razor
    Dockerfile
```

Splitting out `BookChat.Core` is what lets the *existing* WPF app keep working unmodified during
the transition (it can keep referencing its own `Services/*`, or be migrated onto `BookChat.Core`
later) while the new web app is built against the same core — and it's the natural home for
anything eventually shared with BookChatAndroid's Kotlin code too (not literally shared code across
languages, but a single source of truth for *protocol behavior* — bot heuristics, result formats,
ACK framing — that both can be kept in sync against).

---

## 4. Running it on kassandra

### 4.1 Containerization

Multi-stage Dockerfile, `mcr.microsoft.com/dotnet/sdk:9.0` build stage →
`mcr.microsoft.com/dotnet/aspnet:9.0` runtime stage (standard .NET container pattern; no
linuxserver.io base exists for a custom .NET app, so this is a custom-built image — like
`robertlordhood/chaptarr` is the stack's existing precedent for "not every service is
linuxserver.io"). Build it on the dev machine or via GitHub Actions and push to a registry, or
build directly on kassandra from the repo — either is consistent with "custom-built image" being
already normal for this stack.

### 4.2 docker-compose.yml service block

Following the conventions the KassandraWiki survey found (PUID/PGID and `restart:` are **not**
actually used anywhere in this stack's documented services, so this plan doesn't invent them;
`network_mode: host` is the stack default but only for services needing LAN discovery or to reach
other host-networked services by native port — this app needs neither, so it follows **Calibre's
exception pattern**: bridge networking with explicit published ports):

```yaml
bookchat-web:
  build:
    context: ~/kassandra/bookchat/src              # repo root (BookChat.sln's directory)
    dockerfile: BookChat.Web/Dockerfile
  volumes:
    - ~/kassandra/bookchat/config:/config        # SQLite DB (settings + job history) lives here
    - /mnt/media/Calibre-AutoAdd:/auto-add         # drop completed books straight into Calibre's
                                                    # own "Automatically add to library" watch
                                                    # folder — picked up within seconds, no cron/
                                                    # polling layer needed (see §4.4 for why this
                                                    # is AutoAdd and not Calibre-Intake)
  ports:
    - "8085:8080"     # pick a free host port; 8080-8084/8789/8989/5076/3000/32400/1883 are taken
                       # (8085 is what was actually used on kassandra — see bookchat.md)
  environment:
    - BookChat__IrcNickServPassword=${BOOKCHAT_NICKSERV_PASSWORD}   # from .env, never in the repo
```

(No `BookChat__CalibrePassword`: there's no direct calibredb/content-server integration wired up
in `BookChat.Web` — see §6's "Calibre Settings UI removed" note. The `/auto-add` volume above is
the only Calibre integration this app actually has.)

(`BookChat__DbPath` and `BookChat__DownloadDirectory` are baked into the image's `Dockerfile` as
`/config/bookchat.db` and `/auto-add` respectively, matching the volumes above — no need to repeat
them here unless overriding.)

No `network_mode: host` needed (nothing here requires LAN discovery), no PUID/PGID (undocumented
anywhere else in the stack — if file-permission issues show up against `/mnt/media`, follow
Calibre's own fix of letting the entrypoint-owned process write rather than `docker exec`, per the
wiki's noted gotcha, instead of introducing a new convention unilaterally).

### 4.3 Secrets

Both existing apps store the Calibre/IRC credentials in **plaintext** (JSON file / DataStore
preference) — per the user's global CLAUDE.md, this should not be repeated. On kassandra, the
right-sized fix is: NickServ password and Calibre content-server credentials go into an `.env`
file alongside the real `~/kassandra/docker-compose.yml` (outside git, per the existing stack
convention that the compose file itself isn't committed anywhere), injected as container
environment variables, never written into the SQLite settings DB in plaintext. `.NET`'s
user-secrets mechanism is for *dev-time* only — on the deployed container, env vars from `.env`
are the stack's existing pattern and should be followed rather than introducing ASP.NET Core Data
Protection key management for a single-household app.

### 4.4 Getting files from the server to where they belong

This is the one place the Android app's "upload to Drive" problem and the web port's problem
genuinely differ, in the web port's favor: **the server already is the machine with the library**,
because Calibre now also runs on kassandra. So the destination problem collapses to: write
completed downloads to a path Calibre's existing import mechanism already watches.

**AutoAdd, not Calibre-Intake.** `calibre-mover.sh` (the Google-Drive→Calibre script, see
`C:\Users\bernp\KassandraWiki\Services\calibre.md`) uses `Calibre-Intake` purely as a staging area
for that one job: it polls Drive every 10 minutes, moves what it finds to `Calibre-AutoAdd`, then
polls up to 60s to confirm Calibre's own AutoAdder actually consumed the file *before* deleting
the source from Drive — a confirm-before-delete dance that exists only because deleting from the
user's Google Drive is a real, hard-to-undo action worth protecting against. BookChat.Web's
downloads have no such concern (the file only ever exists locally, nothing elsewhere needs
protecting), so routing through `Calibre-Intake` would just add up to a 10-minute cron delay for
no benefit. The right target is `Calibre-AutoAdd` directly — the same thing Chaptarr's event-driven
listener (`calibre-import-listener.py`) already does — so a download is visible in Calibre within
seconds of finishing, not after the next cron tick.

**One thing that has to be carried over from that listener's logic**: Calibre's AutoAdder silently
skips reprocessing any exact filename it has seen before, even across restarts and even if an
earlier attempt under that name failed (see calibre.md's AutoAdder gotcha) — a real risk once
books land here directly rather than through a staging step. `BookChatSession` handles this by
baking a short per-job uniqueifier into every destination filename (cheap to do: Calibre reads the
book's embedded metadata for its library entry, not this on-disk filename, and deletes the file
once consumed anyway).

### 4.5 Homepage dashboard

Per the wiki's observed pattern (native widget if Homepage has one, else a plain link — nzbhydra2
and chaptarr get plain links today because Homepage has no native widget for them): add
`bookchat-web` to `~/kassandra/homepage/config/services.yaml` as a plain link initially; a custom
widget isn't worth building for a single-household app with no existing Homepage integration
pattern to follow.

---

## 5. Open questions / risks to resolve before implementing

1. **SmartIrc4net replacement risk**: dropping SmartIrc4net in favor of a hand-rolled client (as
   BookChatAndroid already proves is viable) is the bigger of two rewrites in this plan. It's the
   right call long-term (threading model doesn't fit a hosted service; already hitting .NET 9
   compatibility workarounds) but is real, not mechanical, work — size it as its own milestone
   before touching DCC/search porting.
2. **Auto-accept-all DCC with no consent step** (both existing apps do this) becomes more sensitive
   once the receiving machine is a shared household server rather than one person's own PC/phone —
   worth deciding whether the web UI should require a confirm-click per offer, at least for the
   first release, even though neither existing app has this friction today.
3. **IRCHighway single-nick-per-connection**: only one IRC identity can be connected to `#ebooks`
   at a time; if the Windows desktop app and the new web service were ever run simultaneously
   (e.g. during a side-by-side transition period) they'd collide on nick/behavior from the bots'
   point of view. Plan the cutover (or give them distinct nicks) rather than running both
   long-term.
4. **Remote/off-LAN access**: this plan assumes LAN-only use (matching Calibre's bridge+port
   approach and the stack having no reverse proxy at all per the wiki). If the user wants to check
   downloads from outside the house, that's a separate decision (VPN vs. exposing a port) this plan
   doesn't make.
5. **Multi-viewer correlation**: once multiple browser tabs/people can trigger searches/downloads
   against the one shared IRC identity, the "stable correlation id per job" fix noted in §1.3/§3.1
   stops being a nice-to-have and becomes necessary — confirm the job/result model handles this
   before writing UI code that assumes a single active search, as both existing apps implicitly do.
6. **Where BookChatAndroid fits going forward**: it already solves "no always-on host," which the
   web port now also solves server-side — worth asking whether BookChatAndroid stays as a useful
   mobile-native frontend to the *same* server (hitting `bookchat-web`'s future API instead of
   IRC/Drive directly) once the server exists, rather than maintaining two independent IRC/DCC
   implementations indefinitely. Not in scope for this plan, but flagging it since the core-library
   split in §3 would make that consolidation easier later if wanted.

---

## 6. Implementation status

Built per this plan, in this repo, on `BookChat.sln`:

- **`BookChat.Core`** (net9.0 class library): `Irc/IrcCoreClient.cs` (hand-rolled socket client,
  replacing SmartIrc4net, with reconnect-with-backoff per §5 item 1 — this risk has been taken on),
  `Irc/DccParser.cs`, `Services/DccReceiver.cs` (the one shared receive-with-ACK primitive from
  §1.3/§3.1), `Services/SearchResultParser.cs` and `Services/CalibreImportService.cs` (ported from
  the WPF app's services), and `BookChatSession.cs` (the orchestrator: one IRC identity, a
  `ConcurrentQueue<DownloadJob>` correlated by `DownloadJob.Id` per §5 item 5, not a single
  "current" reference).
- **`BookChat.Web`** (Blazor Server, net9.0): `Services/IrcHostedService.cs` starts the one shared
  `BookChatSession` at app startup and persists completed/failed jobs to SQLite (via
  `Data/BookChatDbContext.cs`) as they finish — closing the "no persisted job queue" gap flagged
  against BookChatAndroid in §1.2. `Components/Pages/Home.razor` is a single page with the WPF
  app's three tabs (Find Books / Settings / About) reproduced directly, per the user's preference
  for the WPF UI over BookChatAndroid's. Builds and runs; manually verified it connects to
  `irc.irchighway.net` and joins `#ebooks` for real.
- **`BookChat.Web/Dockerfile`**: multi-stage `dotnet/sdk:9.0` → `dotnet/aspnet:9.0` build, matching
  §4.1/§4.2.
- **Deployed to kassandra (2026-11-18)**: built and running as the `bookchat-web` service in
  `~/kassandra/docker-compose.yml`, reachable at `http://kassandra:8085/`. Full details, including
  a real fix needed along the way (the image isn't linuxserver-based, so it ignores PUID/PGID —
  fixed with an explicit `user: "1000:1000"` in the compose entry) are in
  `C:\Users\bernp\KassandraWiki\Services\bookchat.md`. **Verified end-to-end**: searched,
  downloaded a real book, and confirmed it landed in the library.
- **Routed straight to Calibre-AutoAdd, not Calibre-Intake (2026-11-18, same night)**: the first
  cut mounted `Calibre-Intake` and relied on `calibre-mover.sh`'s 10-minute cron to eventually pick
  the file up — technically worked (confirmed above), but added a pointless delay and relied on a
  staging mechanism that exists only to protect a Drive-delete this app never does. Switched
  `BookChat__DownloadDirectory` to `/auto-add` (mounted from `/mnt/media/Calibre-AutoAdd`) so
  downloads are visible in Calibre within seconds, same as Chaptarr's listener — see §4.4. Added a
  per-job filename uniqueifier in `BookChatSession.BuildDestinationFileName` to avoid Calibre's
  AutoAdder silently skipping an exact filename it's seen before, now that nothing stages/dedupes
  on this app's behalf first.
- **Calibre Settings UI removed (2026-11-18)**: the Settings page originally ported the WPF app's
  whole "Calibre Integration" group (Library Path / Server URL / Username / Password /
  Auto-Import checkbox) verbatim, for calibredb-over-content-server integration. On kassandra
  that's all dead weight — the real delivery mechanism is just `BookChat__DownloadDirectory`
  pointing at Calibre's intake folder (§4.4), picked up independently by `calibre-mover.sh`. A
  live test flagged this as confusing (a "Server URL" field that does nothing), so those fields
  were removed from `Home.razor`, the backing columns removed from `SettingsEntity`, and
  `IrcHostedService` now passes `BookChatSession` an inert default `CalibreSettings` with a comment
  explaining why. The underlying `ICalibreImportService`/`CalibreSettings` capability stays in
  `BookChat.Core` for a future deployment that might actually want direct calibredb integration —
  it's just never wired up or exposed from `BookChat.Web` as it stands.
- **Settings-save bug found and fixed (2026-11-19)**: the Calibre Settings UI removal above left
  the live kassandra database's physical `Settings` table with five orphaned `NOT NULL` columns
  (EF's `EnsureCreatedAsync` never alters an already-existing table), which made the very first
  **Save Settings** click after that deploy crash the Blazor circuit with a SQLite `NOT NULL
  constraint failed` error. Fixed with `IrcHostedService.DropLegacyCalibreColumnsAsync`, an
  idempotent startup step that drops those columns via `ALTER TABLE ... DROP COLUMN`, ignoring
  "no such column" on installs that never had them. Verified against the real deployment, not just
  a build — clicked Save Settings twice in the running app (insert path, then update path), both
  succeeded.

**Not yet done** (left for a follow-up pass, deliberately out of scope for this first cut):
- The original `BookChat` (WPF) project is untouched — it still builds and runs standalone; no
  decision has been made yet about retiring it (§5 item 3 on single-nick collision still applies
  if both run against IRCHighway at once).
- No automated tests were added for `BookChat.Core` (the search-result-format parsing in
  particular would benefit from some, given how many bot-specific formats it has to handle).
- The "auto-accept all DCC offers" behavior (§5 item 2) was carried forward unchanged from the WPF
  app — no consent step was added.
- The Settings page writes to SQLite but does not yet trigger an automatic reconnect when IRC
  settings change (the user must manually Disconnect/Connect) — same manual-reconnect-after-edit
  behavior the WPF app had.
- No Homepage dashboard tile yet (§4.5) — `~/kassandra/homepage/config/services.yaml` untouched.
