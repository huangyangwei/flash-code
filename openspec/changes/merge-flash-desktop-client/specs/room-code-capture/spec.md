## Purpose

让用户在统一客户端的可见直播画面中框选房间码区域，由本地 OCR 快速识别符合格式的新码，并以低延迟完成剪贴板复制与可选自动粘贴。

## ADDED Requirements

### Requirement: User can select and control a visible capture region

The system SHALL let the user select a screen region containing the room code, start or stop capture, and reselect the region without restarting the desktop client.

#### Scenario: Select a valid region

- **WHEN** the user completes a region selection meeting the minimum size
- **THEN** the client stores the region, begins capture when enabled, and displays the active capture state

#### Scenario: Cancel selection

- **WHEN** the user cancels region selection
- **THEN** the previous valid region and capture state remain unchanged

#### Scenario: Region is not visible

- **WHEN** the selected region is covered, minimized, or otherwise unavailable to screen capture
- **THEN** the client keeps running, reports that capture is unavailable or producing no result, and does not fabricate a room code

### Requirement: Capture recognizes only valid new room codes

The system SHALL normalize OCR text, apply the configured room-code format constraints and bounded confusion corrections, and emit a code only when it is a newly eligible code according to confirmation, disappearance, and cooldown rules.

#### Scenario: Valid code appears

- **WHEN** a valid room code appears in the selected region and satisfies the configured confirmation policy
- **THEN** the client emits the normalized uppercase code once for that visible occurrence

#### Scenario: OCR text is ambiguous

- **WHEN** OCR output cannot be corrected into a valid room-code format within the allowed correction bounds
- **THEN** the client discards the candidate and leaves the previous clipboard content unchanged

#### Scenario: Same code remains visible

- **WHEN** the same normalized code remains visible across subsequent frames
- **THEN** the client does not emit duplicate copy or paste actions for that continuous occurrence

### Requirement: Successful capture writes the clipboard with bounded latency

The system SHALL write a newly emitted code to the system clipboard synchronously enough to preserve the configured capture latency budget and SHALL report the code and measured capture-to-write latency in the client UI.

#### Scenario: Clipboard available

- **WHEN** a new code is emitted and the clipboard is available
- **THEN** the code is written, the UI displays the code and measured latency, and any configured clear timer is scheduled

#### Scenario: Clipboard temporarily unavailable

- **WHEN** the clipboard cannot be opened after the bounded retry policy
- **THEN** the client reports the write failure, does not trigger automatic paste for that code, and keeps capture running

### Requirement: Optional auto-paste follows successful writes only

The system SHALL support an opt-in auto-paste action targeted at the current focused window, with a bounded configurable delay, and SHALL skip injection when clipboard writing failed, the feature is disabled, or the required modifier state makes injection unsafe.

#### Scenario: Auto-paste enabled

- **WHEN** a code is written successfully and auto-paste is enabled
- **THEN** the client waits the configured delay without blocking capture and sends one paste action to the current focused window

#### Scenario: Auto-paste disabled or unsafe

- **WHEN** auto-paste is disabled, the write failed, or the user is holding the conflicting modifier key
- **THEN** the client does not inject a paste action and continues monitoring normally

### Requirement: Clipboard clearing is fail-safe

The system SHALL support both manual clearing and optional delayed automatic clearing, and automatic clearing SHALL occur only when the clipboard still contains the exact code written by this client.

#### Scenario: Delayed clear with unchanged content

- **WHEN** the configured clear delay expires and the clipboard still contains the last client-written code
- **THEN** the client clears the clipboard

#### Scenario: Clipboard changed by the user

- **WHEN** the configured clear delay expires after the user has copied different content
- **THEN** the client leaves the clipboard unchanged

#### Scenario: Manual clear

- **WHEN** the user invokes manual clear from the client UI or tray menu
- **THEN** the clipboard is cleared immediately and the client shows a short confirmation without stopping OCR capture
