# Pinky 19 sidebar patch

This patch targets Pinky commit `4002b94bd1be8c86ff150d461e6cba8b4f077da7` (`2026.1111.19`). The source patch includes the sidebar integration and the `.8` input diagnostics, with build version `2026.1112.19+sidebar.20261112.8`. The base Pinky 18 to 19 comparison confirmed the four upstream release-note/module changes; the touched upstream source files were verified against the Pinky 19 snapshot.

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
go build -tags=mygo_noinspector -ldflags="-H=windowsgui -X main.version=2026.1112.19+sidebar.20261112.8" -o PinkySidebar.exe ./cmd/pinky
```

Use Go 1.27.3. The host integration receives `PINKY_PANEL_HOST_DIR` from the local Status Island configuration. Keep `PINKY_HOME`, host lease files and build outputs in a local isolated directory; do not commit them. The sidebar holds the overlay window identity and DPI/rectangle lease checks; Pinky retains the gateway approval API as the decision authority.

Pinky preview and recording use FFmpeg 9.0.2. Place `avcodec-63.dll`, `avformat-63.dll`, `avutil-61.dll` and `swscale-10.dll` beside the Pinky executable. Obtain them from the matching upstream Windows package or build them with `scripts/ffmpeg.sh`. Keep the matching build and LGPL license notices with the runtime. Config-driven startup uses `sidebar.pinky-start.v1` and empty example values.

## Verification status and limits

The `.8` source stage records targeted pointer and keyboard input regressions, `go vet ./ui` and a `./cmd/pinky` build. All 20 patch paths were checked against GitHub at `4002b94bd1be8c86ff150d461e6cba8b4f077da7`: the 10 existing paths match their target blobs, and the 10 added paths are absent there. `LICENSE` already exists upstream and is unchanged by this patch; `cmd/pinky/mygo.json` is updated from the upstream file. Apply-check, apply and reverse-check pass on a reconstructed target-file baseline, and the 19 changed files match the `.8` stage. This verifies the patch against its touched target files, not a full checkout of the Pinky repository. The recorded full Go suite has two known Windows symlink fixture failures. In `internal/apppkg.TestPackApp`, symlink creation reports success, but the test then finds that the refused `.ipa` remains. In `internal/mcp.TestDownloadLink`, creating the symlink fails with “A required privilege is not held by the client.” Do not treat the full suite as passing.

The development installation now runs the `.8` executable with its eight verified runtime payloads. H.264/HEVC initialization passed. The sidebar completed a live parent-UI rollback and restoration through its normal updater, preserving layout, position, recent cities and AIHOT history. Live acceptance of the new diagnostic fields, physical multi-monitor, mixed-DPI and cross-machine behavior remains pending.

## License

The upstream Pinky source and this derivative patch are under Apache License 2.0. See `LICENSE` copied from the pinned Pinky source. The surrounding Status Island source keeps its repository-level MIT license. Each directory's license applies to its own source.
