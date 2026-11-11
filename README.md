# Status Island

This repository provides the following Windows status panels.

- `integrated/` is the current C# WinForms panel. It has compact/expanded layouts, music and lyrics, weather, local system activity, optional AIHOT, optional Autoloop, and an optional Pinky panel host.
- `src/` is the older PowerShell widget, retained for existing users. It is separate from the current panel and has its own optional tunnel and proxy probes.

## Run the current panel

Use Windows 10 version 1809 or later with .NET Framework 4.8 and Windows PowerShell 5.1. Start `integrated/Launch.cmd` for visible build/start errors, or `integrated/Launch.vbs` for a quiet start. The scripts build the helper assemblies with the installed .NET Framework compiler. Build outputs stay under ignored `integrated/state/build/`.

Runtime settings, layout, position and logs are stored under `%LOCALAPPDATA%\StatusIsland` by default. Set `STATUS_ISLAND_HOME` to use another local data directory. On first start, a blank `config.example.json` is copied there if no config exists. Optional integrations are disabled unless explicitly configured.

## Optional integrations

- Autoloop is disabled by default. Enable it in the local config only after filling `autoloopHost`, `autoloopAccount`, `autoloopKeyFile`, `autoloopNode` and `autoloopRemoteScript`. It uses Windows OpenSSH with batch mode and strict host key checking. Invalid or incomplete settings show as unavailable. Fill the connection fields using your own deployment settings.
- Pinky is disabled by default. `pinky-host-connection.example.json` and `pinky-start.example.json` contain empty placeholders only. Configure them under the local data directory before enabling the optional host/start hook. The start hook checks both executable hashes and a matching local host config, uses Pinky's normal `needs`/`quit` flow, and records its desktop startup result.
- `CODEX_HOME`, `SIDEBAR_AGENT_LIFECYCLE_HOME`, `SIDEBAR_VOICE_HOME` and `AIHOT_ACTOR_ID` are optional local settings. Activity readers inspect the documented local files.
- The older PowerShell widget's network fields (`macHost`, `clashGroup`, `clashMacProxyName`, `watchdogLockPath`, `tunnelStartScript`) start empty. A user must configure their own values. Its tunnel menu stays disabled until a script is configured.

## Verification and acceptance

| Area | Current evidence |
|---|---|
| Build and synthetic layout/controller checks | Candidate build, validation runner and layout suite pass on the development machine. Installation and cross-machine acceptance remain pending. |
| Music and lyrics | The user confirmed playback, track changes and refresh.  |
| Activity and visuals | The user confirmed the Codex activity estimate, breathing indicator, website timing feedback and next right-edge expansion theme behavior. Activity uses a recent WAL modification-time estimate. |
| Autoloop | The user confirmed details and submitted a selection. The backend state and choice reconciled with the resumed fixed confirmation, and the final snapshot showed zero pending items. The acceptance rule returned a fixed confirmation. Public configuration keeps the remote bridge disabled by default. |
| Pinky 19 | The integrated client is switched to 19. The live panel lease identity matched in its hidden idle state at 360×80, with zero phone actions. Detail presentation and the real handoff-button callback remain pending after phone testing paused on 2026-11-12. |
| Normal update | A limited two-version process-birth/receipt handoff loop completed. Other upgrade and rollback cases remain pending. |

Final stable-release registration also awaits the final desktop exit/restart and visual acceptance record.

## Older PowerShell widget

`install.ps1` installs only the legacy widget, creates desktop/startup shortcuts, and generates a local config with network values blank. Review the optional network/tunnel settings before enabling them. To start it without installing shortcuts, run `powershell -File .\src\StatusIsland.ps1`.

## Licenses

Status Island source is MIT under the root `LICENSE`. The Pinky 19 derivative patch under `integrated/pinky19/` is Apache-2.0 under its bundled license file.
