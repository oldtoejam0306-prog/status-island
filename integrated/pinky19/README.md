# Pinky 19 sidebar patch

This source patch targets the exact Pinky 19 commit `4002b94bd1be8c86ff150d461e6cba8b4f077da7` (`2026.1111.19`) from `DIYgod/pinky`. GitHub's comparison from the verified 18 commit reports only the four release-note/module files changed between those commits. The source files touched by this patch have the same upstream blobs in both versions.

The patch adds the Windows panel-host lease and attachment, same-width panel behavior, glass styling, successful-response handoff acknowledgement, demo wiring, and English/Chinese/Japanese catalog entries. It includes the panel-host and handoff tests plus a short implementation note. Regenerate `cmd/pinky/locales/catalog_gen.go` from the catalogs after applying the patch.

## Apply and build

Apply the patch from a clean checkout pinned to the specified Pinky 19 commit.

```powershell
git switch --detach 4002b94bd1be8c86ff150d461e6cba8b4f077da7
git apply --check path\to\pinky-19-sidebar.patch
git apply path\to\pinky-19-sidebar.patch
go tool mygo generate cmd/pinky
$env:GOTOOLCHAIN = 'local'
$env:CGO_ENABLED = '0'
go build -tags=mygo_noinspector -ldflags="-H=windowsgui -X main.version=2026.1111.19+sidebar.summary-hitfix.20261112.6" -o PinkySidebar.exe ./cmd/pinky
```

Use Go 1.27.3. The host integration receives `PINKY_PANEL_HOST_DIR` from the local Status Island configuration. Keep `PINKY_HOME`, host lease files, and build outputs in a local isolated directory; do not commit them. The sidebar holds the overlay window identity and DPI/rectangle lease checks; Pinky retains the gateway approval API as the decision authority.

Pinky preview and recording use FFmpeg 9.0.2. Place `avcodec-63.dll`, `avformat-63.dll`, `avutil-61.dll` and `swscale-10.dll` beside the Pinky executable. Obtain them from the matching upstream Windows package or build them with `scripts/ffmpeg.sh`. Keep the matching build and LGPL license notices with the runtime. The config-driven startup uses `sidebar.pinky-start.v1` and empty example values.

## Evidence and limits

The base Pinky 19 adaptation passed `go build -tags=mygo_noinspector ./...`, `go vet -tags=mygo_noinspector ./...`, and `go test -tags=mygo_noinspector ./ui ./cmd/pinky/locales`. The final Need-specific input fix passed targeted pointer/hidden-window regressions, `go vet ./ui` and a `./cmd/pinky` build. The exported patch passed apply and reverse checks.

The user confirmed compact summary expansion, same-width details, an explicit Done click and the matching settled/release receipt. The development installation also passed H.264/HEVC initialization, iPad video preview, normal same-version sidebar exit/restart and stable registration. Six AfterRestart checks passed for layout, backups, city history, position and five runtime configs. Cross-machine and mixed-DPI acceptance remains pending.

The patch package contains source changes, documentation and the upstream license. Rollback verification covers the parent source pin and four state backups. Executed rollback remains pending.

## License

The upstream Pinky source and this derivative patch are under Apache License 2.0. See `LICENSE` copied from the pinned Pinky source. The surrounding Status Island source keeps its repository-level MIT license. Each directory's license applies to its own source.
