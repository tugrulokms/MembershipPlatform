import http from 'k6/http';
import { check } from 'k6';

// Write path load test — POST /subscriptions.
//
// userId isolation across runs:
//   setup() runs once before VUs start and returns a runSlot (0-2099).
//   runSlot = (seconds since epoch) % 2100 — changes every second.
//   Each slot owns 50,000 userIds: runSlot * 50000 + __VU * 1000 + __ITER.
//   Max userId: 2099 * 50000 + 50 * 1000 + 999 = 105,000,999 — within int range.
//   Two consecutive runs (>1s apart) always land in different slots.
//
// Thresholds:
//   p(95) < 500ms — write path includes two DB reads + one write transaction
//   error rate < 1%

export const options = {
  vus: 50,
  duration: '30s',
  thresholds: {
    http_req_duration: ['p(95)<500'],
    http_req_failed: ['rate<0.01'],
  },
};

export function setup() {
  return { runSlot: Math.floor(Date.now() / 1000) % 2100 };
}

export default function (data) {
  // Unique userId per run, per VU, per iteration.
  const userId = data.runSlot * 50000 + __VU * 1000 + __ITER;

  const payload = JSON.stringify({
    userId: userId,
    planId: 2,
  });

  const res = http.post('http://localhost:5268/subscriptions', payload, {
    headers: { 'Content-Type': 'application/json' },
  });

  check(res, {
    'status is 201': (r) => r.status === 201,
  });
}
