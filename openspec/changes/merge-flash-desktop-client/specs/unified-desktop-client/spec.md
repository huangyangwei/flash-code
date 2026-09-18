## Purpose

为直播抢码流程提供一个可直接启动的 Windows 客户端，将播放、监测、OCR、剪贴板和配置放在同一窗口与生命周期内，避免用户维护两个独立运行时。

## ADDED Requirements

### Requirement: Client starts as one integrated desktop experience

The system SHALL provide one Windows desktop application entry point that exposes the live video, room monitoring, room-code capture status, playback settings, and clipboard actions without requiring the user to start a separate development server or command-line watcher.

#### Scenario: Fresh launch

- **WHEN** the user launches the packaged application
- **THEN** the application opens one desktop window and presents the playback and monitoring controls without requiring Node.js, Vite, or a second local process

#### Scenario: Graceful shutdown

- **WHEN** the user exits the application
- **THEN** active monitoring, playback, OCR capture, timers, and tray resources stop without leaving a background watcher or pending paste action

### Requirement: Host and UI exchange typed user-visible state

The system SHALL synchronize playback state, monitoring state, capture state, and user actions between the desktop host and the embedded playback UI, and SHALL report errors as actionable user-visible status rather than silently dropping them.

#### Scenario: Monitoring discovers a stream

- **WHEN** the host obtains a new playable stream address for the configured room
- **THEN** the playback UI receives the address, starts the same playback path as manual entry, and shows the monitoring and playback state to the user

#### Scenario: Host operation fails

- **WHEN** monitoring, playback handoff, OCR initialization, or clipboard writing fails
- **THEN** the client exposes a readable error or warning, preserves the manual fallback relevant to that operation, and remains usable for unrelated controls

### Requirement: Configuration has one persisted source of truth

The system SHALL persist playback, monitoring, OCR, clipboard, and auto-paste settings through one client configuration model, SHALL restore valid values on the next launch, and SHALL fall back to documented defaults for missing or invalid values.

#### Scenario: Restore settings

- **WHEN** the user changes supported settings and restarts the client
- **THEN** the next launch restores those settings and reflects the effective values in the UI

#### Scenario: Invalid persisted data

- **WHEN** persisted settings are missing, malformed, or outside supported bounds
- **THEN** the client uses safe defaults for the invalid fields and continues launching without exposing secrets in diagnostics

### Requirement: Sensitive monitoring credentials are not exposed in status

The system SHALL keep optional room-monitoring credentials out of status snapshots, logs, error messages, and UI text except for a non-secret configured/not-configured indicator.

#### Scenario: Credential configured

- **WHEN** the user starts monitoring with an optional credential
- **THEN** requests can use the credential while status and logs report only that a credential is configured

#### Scenario: Credential cleared

- **WHEN** the user clears the optional credential
- **THEN** subsequent requests omit it and the status returns to the not-configured state
