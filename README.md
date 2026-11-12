# Status Island

This repository provides the following Windows status panels.

- `integrated/` is the current C# WinForms panel. It has compact/expanded layouts, music and lyrics, weather, local system activity, optional AIHOT, optional Autoloop, and an optional Pinky panel host.
- `src/` is the older PowerShell widget, retained for existing users. It is separate from the current panel and has its own optional tunnel and proxy probes.

## Run the current panel

Use Windows 10 version 1809 or later with .NET Framework 4.8 and Windows PowerShell 5.1. Start `integrated/Launch.cmd` for visible build/start errors, or `integrated/Launch.vbs` for a quiet start. The scripts build the helper assemblies with the installed .NET Framework compiler. Build outputs stay under ignored `integrated/state/build/`.

Runtime settings, layout, position and logs are stored under `%LOCALAPPDATA%\StatusIsland` by default. Set `STATUS_ISLAND_HOME` to use another local data directory. On first start, a blank `config.example.json` is copied there if no config exists. Optional integrations are disabled unless explicitly configured.

## Optional integrations

- Autoloop is disabled by default. Enable it in the local config only after filling `autoloopHost`, `autoloopAccount`, `autoloopKeyFile`, `autoloopNode` and `autoloopRemoteScript`. It uses Windows OpenSSH with batch mode and strict host key checking. Invalid or incomplete settings show as unavailable. Fill the connection fields using your own deployment settings.
- Pinky is disabled by default. `pinky-host-connection.example.json` and `pinky-start.example.json` contain empty placeholders only. Configure them under the local data directory before enabling the optional host/start hook. The start hook checks the configured candidate executable hash and a matching local host config, uses that candidate's normal `needs`/`quit` flow, and records its desktop startup result.
- Desktop switching is available from the status panel and sidebar menus with local, MacBook Pro, iPad Mac and overview actions. `desktop-switch-connection.example.json` initializes the local desktop controller setting only when the setting is missing; the example is disabled and empty, and an existing local config is preserved. The controller executable is read from that local configuration.
- Mac, iPad and phone battery slots remain visible in the compact status panel. Mac polling is opt-in through `device-battery-connection.example.json`; it uses existing OpenSSH trust and a fixed read-only `pmset` command once per minute. Mobile values use the independently authenticated Pinky battery lane without creating a task or lease. Paused, missing, ambiguous or stale devices show unavailable. Public connection examples are disabled with empty fields; existing local configuration is preserved.
- `CODEX_HOME`, `SIDEBAR_AGENT_LIFECYCLE_HOME`, `SIDEBAR_VOICE_HOME` and `AIHOT_ACTOR_ID` are optional local settings. Activity readers inspect the documented local files.
- The older PowerShell widget's network fields (`macHost`, `clashGroup`, `clashMacProxyName`, `watchdogLockPath`, `tunnelStartScript`) start empty. A user must configure their own values. Its tunnel menu stays disabled until a script is configured.

## Verification and acceptance

| Area | Current evidence |
|---|---|
| Public source build and fixtures | The public package build, AIHOT/unread tests, 25 app-integration checks, layout/controller checks, state migration fixtures and Pinky patch apply/reverse checks passed on the Windows development installation. Cross-machine and mixed-DPI acceptance remains pending. |
| AIHOT compact inbox | The user confirmed the 360×176 layout, size and three footer buttons. |
| Music and lyrics | The user confirmed playback, track changes and refresh.  |
| Activity and visuals | The user confirmed the Codex activity estimate, breathing indicator, website timing feedback and next right-edge expansion theme behavior. Activity uses a recent WAL modification-time estimate. |
| Autoloop | The user confirmed the active compact notification, details and answer submission. The same backend card completed, the fixed confirmation resumed successfully, and pending items returned to zero. Pending ask notifications stay visible and recover after temporary blockers. The notification passes 60 synthetic checks. Hidden polling runs every 15 seconds, visible polling every 5 seconds. Pinky requests take priority. Public configuration keeps the remote bridge disabled by default. |
| Pinky 19 | The user confirmed compact summary expansion, same-width details, an explicit Done click and the matching settled/release receipt. H.264/HEVC initialization and the iPad video preview passed on the development installation. |
| Battery and Pinky typography update | The new local trial was deployed through normal gateway quit/start and the guarded frontend updater, preserving layout, position, recent weather cities and AIHOT state. Live Mac and iPad values were observed; the paused phone stays unavailable. All 51 battery fixtures, state initialization checks, the six-output public build and exact-ref Pinky patch apply/reverse checks passed. Compact Pinky text is smaller and full details scroll. User visual confirmation, a real notification font check and this trial's same-version exit/restart and rollback acceptance remain pending. |
| Desktop switch menu | The current candidate is deployed as a pending trial with four actions from the existing menus. The six-output build, all18 layout/action checks and configuration initialization checks passed. Menu visuals and the local action through the running menu await manual acceptance. |
| Normal update baseline | The previous accepted deployment passed normal same-version menu exit/restart and stable registration. Six AfterRestart checks passed for layout, backups, city history, position and five runtime configs. A live rollback to its parent UI and restoration both completed through the normal updater. Both comparisons preserved layout, position, recent weather cities and AIHOT seen, inbox and read history. The newer battery trial has six runtime configs and retains the accepted baseline as its rollback target; it has not inherited those acceptance flags. Migration fixtures cover the AIHOT 32 MiB cap and other state files' 1 MiB cap. |

Physical multi-monitor, mixed-DPI and cross-machine acceptance remains pending. The Pinky full Go suite retains two Windows symlink-related fixture failures, described in its verification record. [Pinky verification](integrated/pinky19/README.md)

## Older PowerShell widget

`install.ps1` installs only the legacy widget, creates desktop/startup shortcuts, and generates a local config with network values blank. Review the optional network/tunnel settings before enabling them. To start it without installing shortcuts, run `powershell -File .\src\StatusIsland.ps1`.

## Licenses

Status Island source is MIT under the root `LICENSE`. The Pinky 19 derivative patch under `integrated/pinky19/` is Apache-2.0 under its bundled license file.
