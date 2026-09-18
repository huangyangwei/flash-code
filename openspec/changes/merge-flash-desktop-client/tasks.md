## 1. Build And Host Foundation

- [x] 1.1 Add a `ClientWeb` Vite workspace under `flash-code` with Node `>=20.19`, `mpegts.js`, deterministic build scripts, and copied pure playback decision modules; verify `npm ci`, `npm test`, and `npm run build` succeed on Node 20+.
- [x] 1.2 Add the WebView2 dependency and MSBuild integration that copies `ClientWeb/dist` into desktop build/publish output without starting a local server; verify `dotnet build` contains an `app` web asset directory and the executable starts without Node running.
- [x] 1.3 Define and unit-test a versioned JSON message envelope for all host/UI commands and events in design decision 3, including rejection of unknown types, malformed JSON, and out-of-range values.

## 2. Unified Configuration And Secrets

- [x] 2.1 Extend `AppConfig` with sanitized monitoring and playback settings while preserving existing OCR/clipboard defaults; verify logic tests cover missing, invalid, clamped, saved, and reloaded values.
- [x] 2.2 Add Windows-protected storage for the optional monitoring credential and expose only a configured boolean in snapshots; verify round-trip/clear tests pass and serialized config/status output never contains plaintext credentials.

## 3. Embedded Playback Experience

- [x] 3.1 Implement the embedded operational UI for live video, manual FLV input, monitor controls, capture controls, clipboard actions, compact status, and playback settings using the host message bridge; verify responsive desktop layouts and keyboard-accessible labels/actions with browser screenshots.
- [x] 3.2 Port the low-latency `mpegts.js` player, buffer watermark, catch-up thresholds, bounded 2s/4s/8s initial retry, stall detection, and replacement-address flow; verify frontend tests cover URL validation, catch-up decisions, retry exhaustion, settings application, and teardown.
- [x] 3.3 Implement the host-neutral frontend bridge so host `app.init`, `monitor.status`, `monitor.stream`, `capture.status`, `capture.code`, and `app.error` events render correctly and manual/setting actions emit validated commands; verify bridge tests prove monitored and manual streams use the same playback entry point.

## 4. Native Monitoring Service

- [x] 4.1 Implement room input normalization and room-page parsing in C#, including preferred/fallback FLV selection, explicit offline detection, verification-page detection, and fail-closed unknown formats; verify fixture tests match the existing JavaScript behavior.
- [x] 4.2 Implement a cancellable single-room monitor with request timeout, cold-start/live transition memory, no duplicate live events, failure preservation, and capped exponential backoff; verify deterministic tests with injected HTTP responses and delays cover start, update, stop, recovery, and hard timeout.
- [x] 4.3 Wire monitoring commands, protected credential use, sanitized status events, and fresh stream handoff into the host bridge; verify an integration test starts a fake room monitor, emits one stream event, omits secrets, and stops without leaving work running.

## 5. Native Capture And Clipboard Integration

- [x] 5.1 Refactor the desktop form into a WebView2 host while retaining tray lifecycle and global hotkeys; wire capture select/toggle commands and publish capture state without breaking existing region selection behavior, verified by host command tests and a WinForms smoke launch.
- [x] 5.2 Publish OCR debug/status and successful code/latency events to the embedded UI while preserving `CodeDetector`, cooldown, clipboard retry, fail-safe clearing, and non-blocking auto-paste behavior; verify existing logic tests plus new event payload tests pass.
- [x] 5.3 Wire manual clear and all OCR/playback/monitor setting changes through the unified host configuration, then remove runtime reliance on browser `localStorage` and `/__watch/api`; verify a restart restores effective values and frontend network inspection shows no local watch API calls.

## 6. Lifecycle, Packaging, And Documentation

- [x] 6.1 Implement coordinated shutdown for monitor cancellation, player disposal, capture thread stop, pending paste cancellation, WebView disposal, timers, hotkeys, and tray resources; verify repeated start/stop/close tests finish without background callbacks or unhandled exceptions.
- [x] 6.2 Add publish configuration and a release build check for WebView2 Runtime guidance and embedded assets; verify `dotnet publish` produces a runnable Windows client whose normal runtime does not require Node/Vite.
- [x] 6.3 Update README usage, prerequisites, configuration migration, manual fallbacks, known screen-capture limitations, and developer commands; verify documented commands run from a clean dependency install.

## 7. End-To-End Verification

- [x] 7.1 Run all C# logic/integration tests, frontend tests, frontend production build, desktop build, and OpenSpec strict validation; verify every command exits successfully with no build warnings introduced by this change.
- [x] 7.2 On Windows, verify a visible WebView2 test scene containing a known room code can be region-selected, OCR-recognized, copied, manually cleared, and optionally auto-pasted while capture stays responsive; record the measured capture-to-clipboard latency and fail the task if screen composition is blank.
- [x] 7.3 Verify the complete mocked live workflow in the packaged client: monitor offline -> live, one automatic stream start, low-latency status updates, no duplicate starts, simulated stream expiry, refreshed address recovery, and clean shutdown; capture screenshots for the normal, retry, capture-success, and error states.
