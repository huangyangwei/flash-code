export const DEFAULT_SETTINGS = {
  catchupEnabled: true,
  speedThresholdMs: 1000,
  speedRate: 1.1,
  jumpThresholdMs: 2000,
};

export const MIN_RATE = 1.05;
export const MAX_RATE = 2.0;

export function sanitizeSettings(input) {
  const settings = { ...DEFAULT_SETTINGS };
  if (!input || typeof input !== 'object') return settings;

  if (typeof input.catchupEnabled === 'boolean') {
    settings.catchupEnabled = input.catchupEnabled;
  }

  const rate = Number(input.speedRate);
  if (Number.isFinite(rate)) {
    settings.speedRate = Math.min(MAX_RATE, Math.max(MIN_RATE, rate));
  }

  const speedThreshold = Number(input.speedThresholdMs);
  if (Number.isFinite(speedThreshold) && speedThreshold >= 0) {
    settings.speedThresholdMs = speedThreshold;
  }

  if (input.jumpThresholdMs === null || input.jumpThresholdMs === undefined) {
    settings.jumpThresholdMs = null;
  } else {
    const jumpThreshold = Number(input.jumpThresholdMs);
    if (Number.isFinite(jumpThreshold) && jumpThreshold > 0) {
      settings.jumpThresholdMs = jumpThreshold;
    }
  }

  return settings;
}
