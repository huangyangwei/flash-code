export const ACTION = {
  NORMAL: 'normal',
  SPEED: 'speed',
  JUMP: 'jump',
};

export function decideAction(watermarkMs, settings, previousAction) {
  if (!settings.catchupEnabled) return ACTION.NORMAL;

  const { speedThresholdMs, jumpThresholdMs } = settings;
  if (jumpThresholdMs !== null && watermarkMs >= jumpThresholdMs) {
    return ACTION.JUMP;
  }

  if (previousAction === ACTION.SPEED) {
    return watermarkMs >= speedThresholdMs / 2 ? ACTION.SPEED : ACTION.NORMAL;
  }

  return watermarkMs >= speedThresholdMs ? ACTION.SPEED : ACTION.NORMAL;
}

export function getWatermarkMs(buffered, currentTime) {
  if (!buffered || buffered.length === 0) return null;
  const watermark = (buffered.end(buffered.length - 1) - currentTime) * 1000;
  return Number.isFinite(watermark) && watermark >= 0 ? watermark : null;
}
