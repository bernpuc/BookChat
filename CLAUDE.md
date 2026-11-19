## BookChat
Single-project WPF app. All logic in MainWindowViewModel — keep it that way unless explicitly refactoring.
- DCC protocol is sensitive: do not refactor socket/ACK logic without explicit instruction
- Calibre integration has two code paths (CLI vs content server) — preserve both
- IRC connection auto-starts on launch — do not add any blocking startup awaits