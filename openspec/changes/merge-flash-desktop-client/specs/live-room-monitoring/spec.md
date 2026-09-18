## Purpose

让统一客户端持续观察指定抖音直播间，在开播沿提取可播放流并自动交给播放器，同时对风控、解析失败、网络失败和重复状态保持可预期的退避与提示行为。

## ADDED Requirements

### Requirement: User can configure and control one room monitor

The system SHALL allow the user to enter a numeric room identifier or supported room URL, configure a positive polling interval, optionally provide a credential, and start or stop monitoring from the desktop client.

#### Scenario: Start with a room URL

- **WHEN** the user submits a supported live-room URL and a valid interval
- **THEN** the client normalizes the room identifier, starts monitoring, and displays the effective room and interval

#### Scenario: Invalid room input

- **WHEN** the user submits an empty or unsupported room value
- **THEN** the client refuses to start monitoring, explains the input error, and does not issue polling requests

#### Scenario: Stop monitoring

- **WHEN** the user stops an active monitor
- **THEN** polling stops, the monitor status is disabled, and any previously exposed live URL is cleared from the active playback handoff state

### Requirement: Monitor reports live transitions without duplicate starts

The system SHALL parse the room page for a valid stream address, treat a cold-start live room as a live transition, and trigger automatic playback only once per continuous live period.

#### Scenario: Cold start already live

- **WHEN** monitoring starts and the first successful room-page response contains a valid stream address
- **THEN** the client reports the room as live and hands the selected stream address to the player once

#### Scenario: Offline to live

- **WHEN** a previously offline room becomes live and a valid stream address is available
- **THEN** the client reports the transition and hands the new address to the player once

#### Scenario: Live remains live

- **WHEN** consecutive successful polls remain live for the same continuous period
- **THEN** the client keeps the live status but does not restart playback for every poll

### Requirement: Monitor failures do not create false live events

The system SHALL distinguish parse failures, blocked/verification pages, network failures, and explicit offline responses; failures SHALL preserve the previous transition memory and SHALL use bounded backoff before retrying.

#### Scenario: Verification or parse failure

- **WHEN** a room response is a verification page or lacks sufficient fields to prove live playback
- **THEN** the client reports a monitor failure, does not hand a stream to the player, and schedules a backoff retry

#### Scenario: Failure while live

- **WHEN** a polling attempt fails after the room was already known to be live
- **THEN** the client reports the failure and does not emit a second live transition when a later successful poll confirms the same live period

#### Scenario: Failure recovery from offline

- **WHEN** a polling failure occurs while the previous stable state was offline and a later successful response proves live playback
- **THEN** the client emits one live transition after recovery

### Requirement: Selected stream address is refreshed for playback lifecycle

The system SHALL expose the selected available quality stream to the player and SHALL allow a later live poll to replace an expired address without requiring the user to restart the desktop client.

#### Scenario: Preferred quality available

- **WHEN** the room page contains the preferred quality and one or more fallback stream addresses
- **THEN** the monitor selects the preferred quality and provides it to the player

#### Scenario: Active address expires

- **WHEN** playback reports that its signed address is no longer usable while monitoring remains live
- **THEN** the client can hand the next newly observed valid address to the player and resume from the live edge
