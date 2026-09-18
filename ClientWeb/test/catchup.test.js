import test from 'node:test';
import assert from 'node:assert/strict';
import { ACTION, decideAction, getWatermarkMs } from '../src/lib/catchup.js';

const settings = {
  catchupEnabled: true,
  speedThresholdMs: 1000,
  speedRate: 1.1,
  jumpThresholdMs: 2000,
};

test('catch-up selects normal, speed, and jump actions', () => {
  assert.equal(decideAction(500, settings, ACTION.NORMAL), ACTION.NORMAL);
  assert.equal(decideAction(1100, settings, ACTION.NORMAL), ACTION.SPEED);
  assert.equal(decideAction(2100, settings, ACTION.SPEED), ACTION.JUMP);
});

test('speed action uses hysteresis', () => {
  assert.equal(decideAction(600, settings, ACTION.SPEED), ACTION.SPEED);
  assert.equal(decideAction(499, settings, ACTION.SPEED), ACTION.NORMAL);
});

test('watermark uses the latest buffered range', () => {
  const ranges = { length: 2, end: (index) => (index === 0 ? 3 : 5) };
  assert.equal(getWatermarkMs(ranges, 3), 2000);
  assert.equal(getWatermarkMs({ length: 0 }, 0), null);
});
