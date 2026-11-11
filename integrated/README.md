# Integrated Status Island

This is the current C# WinForms panel. Build and start it with `Launch.cmd` (visible diagnostics) or `Launch.vbs` (quiet start) on Windows 10 version 1809 or later with .NET Framework 4.8 and Windows PowerShell 5.1. The project uses the installed Framework compiler and Windows Runtime metadata.

The helper assemblies are built under ignored `state/build`. Runtime configuration, layout, position, integration state and logs use `STATUS_ISLAND_HOME`, defaulting to `%LOCALAPPDATA%\StatusIsland`. `Candidate.State.ps1` creates the directory and copies empty example configuration files only when no local file exists. Existing local files are preserved.

## Local configuration

Configure the following files in the local data directory before enabling integrations.

- `config.example.json` → `config.json`
- `pinky-host-connection.example.json` → `pinky-host-connection.json`
- `pinky-start.example.json` → `pinky-start.json`

The examples contain empty values. Do not commit local copies. `config.json` supports optional `ccSwitchLaunch` and Autoloop fields. Autoloop defaults disabled. To enable it, supply an explicit host, account, key-file path, remote node executable and remote script path. Only restricted host/account/path characters are accepted. Windows OpenSSH is invoked with batch mode and strict host key checking. If configuration is invalid, Autoloop reports unavailable; if disabled or absent, it stays offline.

Pinky startup is also disabled by default. The optional `Start-PinkyFusion.ps1` entry requires explicit candidate/official executable paths and SHA256 values, a profile directory and a host directory beneath `%LOCALAPPDATA%\StatusIsland\PinkyHost`. The host trust root stays fixed there even when `STATUS_ISLAND_HOME` changes the panel's configuration and layout directory. With an enabled local config, it checks the host configuration and executable hashes, defers while there is a pending request, and uses Pinky's normal control flow without sending phone actions.

The following environment settings configure local integrations.

| Setting | Purpose |
|---|---|
| `STATUS_ISLAND_HOME` | Local app data root. |
| `CODEX_HOME` | Directory whose selected Codex activity WAL timestamps are used for the activity estimate. Defaults to the current user's `.codex`. |
| `SIDEBAR_AGENT_LIFECYCLE_HOME` | Optional agent lifecycle receipt directory. Defaults under the app data root. |
| `SIDEBAR_VOICE_HOME` | Optional voice-pack directory. |
| `AIHOT_ACTOR_ID` | Optional AIHOT request identifier; if absent, the optional identity header is omitted. |

The activity estimate reads only recent modification times. The estimate can return unknown. Balance credential lookup uses an explicit local connection manifest. Weather and lyrics use public service endpoints; AIHOT polling is tied to its local card state. See source adapters for exact request behavior.

## Evidence and limits

- Candidate build, `ValidationRunner.exe` and `LayoutTests.exe` passed on the development machine.
- The user confirmed music playback, track changes, lyric refresh, the activity estimate, breathing indicator, website timing feedback and the next right-edge expansion theme behavior.
- Pinky 19 build and targeted UI/locales tests passed. The integrated client is switched to 19; the live panel lease identity matched in the hidden idle state at 360×80, with zero phone actions. The real handoff button callback and detail presentation remain pending after phone testing paused on 2026-11-12.
- The user confirmed Autoloop details and submission; the backend state/choice reconciled with a resumed fixed confirmation and the snapshot showed zero pending items. The acceptance rule returned a fixed confirmation. Public configuration keeps the remote bridge disabled by default.
- Normal update handoff evidence covers a limited two-version process-birth/receipt loop. Other upgrade and rollback cases remain pending.

Final stable-release registration awaits the final desktop exit/restart and visual acceptance record.
