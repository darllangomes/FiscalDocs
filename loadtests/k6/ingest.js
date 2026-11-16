// Carga de ingestão: POST /api/v1/documents com uma NFe nova a cada iteração.
//
// Variáveis: BASE_URL, API_KEY (obrigatória), RATE (iterações por segundo, padrão 50) e
// DURATION (padrão 60s). A taxa é fixa (arrival rate), não o número de VUs: assim o
// servidor lento não "alivia" a própria carga.
import http from 'k6/http';
import { check } from 'k6';
import exec from 'k6/execution';
import { Counter } from 'k6/metrics';
import { buildNfe, newRunCode } from './lib/documents.js';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const API_KEY = __ENV.API_KEY;
const RATE = Number(__ENV.RATE || 50);

const unexpected = new Counter('ingest_unexpected_status');

export const options = {
  scenarios: {
    ingest: {
      executor: 'constant-arrival-rate',
      rate: RATE,
      timeUnit: '1s',
      duration: __ENV.DURATION || '60s',
      preAllocatedVUs: Math.max(20, RATE),
      maxVUs: Math.max(100, RATE * 4),
    },
  },
  thresholds: {
    // Toda requisição deve devolver 201: 200 seria chave repetida e 429 seria rate limit.
    ingest_unexpected_status: ['count==0'],
    http_req_failed: ['rate<0.01'],
    'http_req_duration{name:ingest}': ['p(95)<500'],
    // Se o k6 não conseguiu iniciar iterações na taxa pedida, o resultado não vale.
    dropped_iterations: ['count==0'],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export function setup() {
  if (!API_KEY) {
    throw new Error('Defina API_KEY.');
  }
  return { runCode: newRunCode() };
}

export default function (data) {
  const { xml } = buildNfe(exec.scenario.iterationInTest, data.runCode);

  const response = http.post(`${BASE_URL}/api/v1/documents`, xml, {
    headers: { 'X-Api-Key': API_KEY, 'Content-Type': 'application/xml' },
    tags: { name: 'ingest' },
  });

  const created = check(response, { 'status 201': (r) => r.status === 201 });
  if (!created) {
    unexpected.add(1);
  }
}
