export const CONNECT_RETRY_DELAYS_MS = [2000, 4000, 8000];

export function retryPlan({ failedAttempts, hasMedia, isNetworkError }) {
  if (hasMedia || !isNetworkError) return { retry: false };
  if (failedAttempts < 1 || failedAttempts > CONNECT_RETRY_DELAYS_MS.length) {
    return { retry: false };
  }

  return {
    retry: true,
    delayMs: CONNECT_RETRY_DELAYS_MS[failedAttempts - 1],
    attempt: failedAttempts,
  };
}
