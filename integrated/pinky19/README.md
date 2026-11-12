# Pinky 19 sidebar patch

This patch targets Pinky commit `4002b94bd1be8c86ff150d461e6cba8b4f077da7` (`2026.1111.19`). The source patch includes the sidebar integration, input diagnostics, compact-panel typography and protected battery export, with build version `2026.1112.19+sidebar.20261112.9`. The base Pinky 18 to 19 comparison confirmed the four upstream release-note/module changes; the touched upstream source files were verified against the Pinky 19 snapshot.

The patch adds the Windows panel-host lease and attachment, same-width panel behavior, glass styling, successful-response handoff acknowledgement, demo wiring and English/Chinese/Japanese catalog entries. It also records sidebar input context and geometry in window DIPs. Logs identify whether input came from the main-window right panel or floating panel. Keyboard events report that pointer coordinates are unavailable and omit pointer-coordinate fields. Pointer events include element and window coordinates; card-local coordinates are included only when card bounds are known.

## Apply and build

Apply from a clean checkout pinned to the specified Pinky 19 commit.

```powershell
git switch --detach 4002b94bd1be8c86ff150d461e6cba8b4f077da7
git apply --check path\to\pinky-19-sidebar.patch
git apply path\to\pinky-19-sidebar.patch
go tool mygo generate cmd/pinky
$env:GOTOOLCHAIN = 'local'
$env:CGO_ENABLED = '0'
go build -tags=mygo_noinspector -ldflags="-H=windowsgui -X main.version=2026.1112.19+sidebar.20261112.9" -o PinkySidebar.exe ./cmd/pinky
```

Use Go 1.27.3. The host integration receives `PINKY_PANEL_HOST_DIR` from the local Status Island configuration. Keep `PINKY_HOME`, host lease files and build outputs in a local isolated directory; do not commit them. The sidebar holds the overlay window identity and DPI/rectangle lease checks; Pinky retains the gateway approval API as the decision authority.

Pinky preview and recording use FFmpeg 9.0.2. The Go9 runtime manifest has eight entries: the executable, four FFmpeg DLLs, build and LGPL notices, and the upstream Pinky license. Place `avcodec-63.dll`, `avformat-63.dll`, `avutil-61.dll` and `swscale-10.dll` beside the executable. Obtain them from the matching upstream Windows package or build them with `scripts/ffmpeg.sh`. Config-driven startup uses `sidebar.pinky-start.v1` and empty example values.

## Compact battery status

The compact panel uses smaller type while keeping the full content reachable by scrolling. Pinky writes real-device battery snapshots to `batteries.json` through its existing protected PinkyHost file channel. It reuses the existing online, open `PhoneVitals` path: collection does not call `phone_connect`, open or reopen a Pinky device, acquire a task lease, or capture the screen. The payload carries Gateway `batteryAtUtc` timestamps unchanged as UTC instants, distinguishes unavailable values from real zero, omits virtual devices, and uses atomic replacement. Device readings refresh every 60 seconds; the file heartbeat advances every 30 seconds. Offline, paused, unsupported, missing or stale readings remain unavailable.

## Verification status and limits

The Go9 stage passed the targeted fake battery collector and native-render/scroll checks, and a read-only Mac battery probe returned charge data. The five newly added Go files were checked at the exact Pinky 19 commit and are absent there. `LICENSE` is unchanged; `cmd/pinky/mygo.json` updates its existing upstream file to the `.9` build version. The regenerated patch passes apply-check, apply, normalized comparison against all 24 changed Go9-stage files, reverse-check and reverse on a reconstructed target-file baseline. This validates the touched files, not a full checkout of the Pinky repository. The live Pinky/iPad `.9` battery display still awaits deployment acceptance; the Mac probe does not establish iPad status. The recorded full Go suite has two known Windows symlink fixture failures. In `internal/apppkg.TestPackApp`, symlink creation reports success, but the test then finds that the refused `.ipa` remains. In `internal/mcp.TestDownloadLink`, creating the symlink fails with “A required privilege is not held by the client.” Do not treat the full suite as passing.

The earlier development installation ran the `.8` executable with its eight verified runtime entries. H.264/HEVC initialization passed. Four live pointer-input records showed the floating-panel render context and window-DIP coordinate labels. Keyboard events omitting pointer fields passed targeted fixtures. The sidebar completed a live parent-UI rollback and restoration through its normal updater, preserving layout, position, recent cities and AIHOT history. The Go9 executable is compiled and sealed; deployment and live iPad battery acceptance remain pending. Physical multi-monitor, mixed-DPI and cross-machine acceptance also remains pending.

## License

The upstream Pinky source and this derivative patch are under Apache License 2.0. See `LICENSE` copied from the pinned Pinky source. The surrounding Status Island source keeps its repository-level MIT license. Each directory's license applies to its own source.
