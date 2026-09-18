import test from 'node:test';
import assert from 'node:assert/strict';
import {
  DEFAULT_SETTINGS,
  MAX_RATE,
  MIN_RATE,
  sanitizeSettings,
} from '../src/lib/settings.js';

test('invalid input falls back to defaults', () => {
  assert.deepEqual(sanitizeSettings(null), DEFAULT_SETTINGS);
  assert.deepEqual(sanitizeSettings('invalid'), DEFAULT_SETTINGS);
});

test('numeric settings are bounded and jump can be disabled', () => {
  assert.equal(sanitizeSettings({ speedRate: 99 }).speedRate, MAX_RATE);
  assert.equal(sanitizeSettings({ speedRate: 0 }).speedRate, MIN_RATE);
  assert.equal(sanitizeSettings({ jumpThresholdMs: null }).jumpThresholdMs, null);
});
