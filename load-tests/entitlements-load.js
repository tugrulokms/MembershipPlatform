import http from 'k6/http';
import { check, sleep } from 'k6';

// Target: warm cache read path. userId=1 has entitlements seeded in DB and cached in Redis.
// Every request should hit the Redis cache — no DB round-trips.
//
// Thresholds:
//   p(95) < 50ms  — the goal from Phase 6
//   error rate < 1%

export const options = {
  vus: 50,
  duration: '30s',
  thresholds: {
    http_req_duration: ['p(95)<50'],
    http_req_failed: ['rate<0.01'],
  },
};

export default function () {
  const res = http.get('http://localhost:5268/users/1/entitlements');

  check(res, {
    'status is 200': (r) => r.status === 200,
    'response is array': (r) => r.body !== null && Array.isArray(r.json()),
  });
}
