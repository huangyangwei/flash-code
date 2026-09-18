## Purpose

让用户在统一桌面客户端内手动或自动播放抖音 FLV 直播流，并用低缓冲和可控追帧尽快看到最新画面，在地址失效或网络异常时提供可恢复的操作路径。

## ADDED Requirements

### Requirement: User can start live playback from a valid FLV address

The system SHALL accept a valid HTTP(S) FLV stream address from manual input or host automation and start live playback with the client playback settings applied.

#### Scenario: Manual playback

- **WHEN** the user submits a valid, unexpired FLV address
- **THEN** the client starts playback, shows the player, and reports the current buffer watermark and playback action

#### Scenario: Invalid address

- **WHEN** the user submits an empty, non-HTTP(S), or non-FLV address
- **THEN** the client rejects it before playback and keeps the manual input available for correction

### Requirement: Playback minimizes and controls live latency

The system SHALL keep live buffering bounded and SHALL provide a user-configurable catch-up policy with normal playback, temporary speed-up, and latest-edge seeking actions.

#### Scenario: Buffer within target

- **WHEN** the measured buffer watermark is below the configured speed threshold
- **THEN** playback runs at normal speed and the UI reports the normal action

#### Scenario: Buffer exceeds thresholds

- **WHEN** the watermark reaches the configured speed or jump threshold
- **THEN** the client applies the configured speed-up or seeks toward the latest playable edge according to threshold priority, and reports the applied action

#### Scenario: Catch-up disabled

- **WHEN** the user disables catch-up
- **THEN** the client restores normal playback speed and does not perform automatic edge seeking

### Requirement: Initial connection failures have bounded recovery

The system SHALL retry transient initial connection failures with a bounded increasing delay, show retry progress, and stop retrying after the configured finite attempt limit.

#### Scenario: Stream not ready immediately

- **WHEN** the first connection fails before media has been received with a retryable network error
- **THEN** the client retries using the documented backoff sequence and shows the current retry number and delay

#### Scenario: Retry limit reached

- **WHEN** all initial connection retries fail
- **THEN** the client stops automatic retries and shows an actionable error that allows the user to provide a fresh address

### Requirement: Expired or stalled streams can be replaced

The system SHALL detect a playback error or a stalled buffer, stop using the unusable stream, and provide a replacement-address path that starts the new stream from its current live edge.

#### Scenario: Signed address expires

- **WHEN** the active stream reports an unrecoverable error or its buffer stops advancing beyond the stall limit
- **THEN** the client identifies the stream as interrupted and presents a replacement input without crashing the application

#### Scenario: Replacement stream submitted

- **WHEN** the user submits a new valid FLV address from the interruption state
- **THEN** the previous player instance is released and playback resumes from the new stream's live edge
