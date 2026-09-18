import test from 'node:test';
import assert from 'node:assert/strict';
import { CONNECT_RETRY_DELAYS_MS, retryPlan } from '../src/lib/connect-retry.js';

test('initial network failures retry with a bounded sequence', () => {
  assert.deepEqual(CONNECT_RETRY_DELAYS_MS, [2000, 4000, 8000]);
  assert.deepEqual(
    retryPlan({ failedAttempts: 1, hasMedia: false, isNetworkError: true }),
    { retry: true, delayMs: 2000, attempt: 1 },
  );
  assert.equal(
    retryPlan({ failedAttempts: 4, hasMedia: false, isNetworkError: true }).retry,
    false,
  );
});

test('non-network and mid-stream failures do not use initial retry', () => {
  assert.equal(retryPlan({ failedAttempts: 1, hasMedia: true, isNetworkError: true }).retry, false);
  assert.equal(retryPlan({ failedAttempts: 1, hasMedia: false, isNetworkError: false }).retry, false);
});
