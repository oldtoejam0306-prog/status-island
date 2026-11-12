# Integrated Status Island

This is the current C# WinForms panel. Build and start it with `Launch.cmd` (visible diagnostics) or `Launch.vbs` (quiet start) on Windows 10 version 1809 or later with .NET Framework 4.8 and Windows PowerShell 5.1. The project uses the installed Framework compiler and Windows Runtime metadata.

The helper assemblies are built under ignored `state/build`. Runtime configuration, layout, position, integration state and logs use `STATUS_ISLAND_HOME`, defaulting to `%LOCALAPPDATA%\StatusIsland`. `Candidate.State.ps1` creates the directory and copies empty example configuration files only when no local file exists. Existing local files are preserved.

## Local configuration

Configure the following files in the local data directory before enabling integrations.

- `config.example.json` → `config.json`
- `pinky-host-connection.example.json` → `pinky-host-connection.json`
- `pinky-start.example.json` → `pinky-start.json`
- `device-battery-connection.example.json` → `device-battery-connection.json`

The examples contain empty values. Do not commit local copies. `config.json` supports optional `ccSwitchLaunch` and Autoloop fields. Autoloop defaults disabled. To enable it, supply an explicit host, account, key-file path, remote node executable and remote script path. Only restricted host/account/path characters are accepted. Windows OpenSSH is invoked with batch mode and strict host key checking. If configuration is invalid, Autoloop reports unavailable; if disabled or absent, it stays offline.

Pinky startup is also disabled by default. The optional `Start-PinkyFusion.ps1` entry requires an explicit candidate executable path and SHA256, a profile directory and a host directory beneath `%LOCALAPPDATA%\StatusIsland\PinkyHost`. It uses the configured candidate itself for Pinky's `needs` and `quit` control commands; it does not depend on a separately installed official executable or a fixed `.8`/`.9` binary pin. The host trust root stays fixed there even when `STATUS_ISLAND_HOME` changes the panel's configuration and layout directory. With an enabled local config, it checks the host configuration and executable hash, defers while there is a pending request, and uses Pinky's normal control flow without sending phone actions.

Mac battery polling is opt-in. First-run setup creates `device-battery-connection.json` with `enabled: false` and empty host, account, SSH key, known-hosts and device-ID fields. Fill it locally only after configuring the Mac host and SSH trust; it is not committed or included in public packages. iPad and phone levels come from the separate battery lane of an enabled, trusted Pinky connection; Pinky itself remains disabled by default. This frontend lane does not start a task or lease. Paused or ambiguous devices remain unavailable. Synthetic fixtures do not represent live device acceptance.

The following environment settings configure local integrations.

| Setting | Purpose |
|---|---|
| `STATUS_ISLAND_HOME` | Local app data root. |
| `CODEX_HOME` | Directory whose selected Codex activity WAL timestamps are used for the activity estimate. Defaults to the current user's `.codex`. |
| `SIDEBAR_AGENT_LIFECYCLE_HOME` | Optional agent lifecycle receipt directory. Defaults under the app data root. |
| `SIDEBAR_VOICE_HOME` | Optional voice-pack directory. |
| `AIHOT_ACTOR_ID` | Optional AIHOT request identifier; if absent, the optional identity header is omitted. |

The activity estimate reads only recent modification times. The estimate can return unknown. Balance credential lookup uses an explicit local connection manifest. Weather, lyrics and AIHOT use public service endpoints. The AIHOT inbox stores cached stories, summaries and read/unread history in local `aihot-monitor.json`. Configure `AIHOT_ACTOR_ID` to send its optional identity header.

## Evidence and limits

- Candidate build, `ValidationRunner.exe` and `LayoutTests.exe` passed on the development machine.
- The user confirmed music playback, track changes, lyric refresh, the activity estimate, breathing indicator, website timing feedback and the next right-edge expansion theme behavior.
- AIHOT compact inbox checks passed with 124 UI assertions, 36 unread-model assertions and 25 app-integration checks. The card stays 360×176 in summary and list modes and exposes three interactive footer actions. The user confirmed the layout, size and three buttons. Multi-display/DPI acceptance remains pending.
- Pinky 19 build and targeted input tests passed. The user confirmed compact summary expansion, same-width details, an explicit Done click and the matching settled/release receipt. H.264/HEVC initialization and the iPad video preview passed on the development installation.
- The user confirmed Autoloop details and submission; the backend state/choice reconciled with a resumed fixed confirmation and the snapshot showed zero pending items. The acceptance rule returned a fixed confirmation. Public configuration keeps the remote bridge disabled by default.
- Normal update handoff backs up `layout.xml`, `layout.xml.bak`, `pos.json` and `aihot-monitor.json`. The AIHOT limit is 32 MiB and each other state file has a 1 MiB limit. The state import helper has fixtures for hashes, backups, oversized input and partial-failure prevention. Rollback verification covers the parent source pin and four state backups. Executed rollback remains pending.
- The development installation passed normal same-version menu exit/restart and stable registration. WinPS 5.1 AfterRestart passed six checks for layout, backups, recent city history, position and five runtime configs.

Stable registration passed on the development installation after normal same-version exit/restart. Cross-machine and multi-display/DPI behavior remains unverified.
