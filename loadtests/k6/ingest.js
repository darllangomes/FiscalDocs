// Carga de ingestão: POST /api/v1/documents com uma NFe nova a cada iteração.
//
// Variáveis: BASE_URL, API_KEY (obrigatória), RATES (taxas em iterações por segundo, separadas por
// vírgula, padrão 50), DURATION (segundos de cada taxa, padrão 60) e ITEMS (itens por NFe, padrão 20).
//
// Cada taxa é um cenário separado, em sequência e com uma pausa entre eles, para que a latência de uma
// não contamine a da próxima. A taxa é fixa (arrival rate), não o número de VUs: assim o servidor
// lento não "alivia" a própria carga.
import http from 'k6/http';
import { check } from 'k6';
import exec from 'k6/execution';
import { Counter } from 'k6/metrics';
import { buildNfe, newRunCode, sizeInBytes } from './lib/documents.js';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const API_KEY = __ENV.API_KEY;
const RATES = (__ENV.RATES || __ENV.RATE || '50').split(',').map(Number);
const DURATION_S = parseInt(__ENV.DURATION || '60', 10);
const GAP_S = 15;

const unexpected = new Counter('ingest_unexpected_status');

const scenarios = {};
const thresholds = {
  // Toda requisição deve devolver 201: 200 seria chave repetida e 429 seria rate limit.
  ingest_unexpected_status: ['count==0'],
  http_req_failed: ['rate<0.01'],
};
const indexByScenario = {};

RATES.forEach((rate, index) => {
  const name = `rate_${rate}`;
  indexByScenario[name] = index;
  scenarios[name] = {
    executor: 'constant-arrival-rate',
    rate,
    timeUnit: '1s',
    duration: `${DURATION_S}s`,
    startTime: `${index * (DURATION_S + GAP_S)}s`,
    preAllocatedVUs: Math.max(20, rate),
    maxVUs: Math.max(100, rate * 4),
  };
  thresholds[`http_req_duration{scenario:${name}}`] = ['p(95)<500'];
  // Se o k6 não conseguiu iniciar iterações na taxa pedida, o resultado dessa taxa não vale.
  thresholds[`dropped_iterations{scenario:${name}}`] = ['count==0'];
});

export const options = {
  scenarios,
  thresholds,
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export function setup() {
  if (!API_KEY) {
    throw new Error('Defina API_KEY.');
  }
  const runCode = newRunCode();
  console.log(`Documento de exemplo: ${sizeInBytes(buildNfe(0, runCode).xml)} bytes`);
  return { runCode };
}

export default function (data) {
  // O número da iteração recomeça em cada cenário, então cada taxa ganha uma faixa própria de números.
  const sequence = indexByScenario[exec.scenario.name] * 1000000 + exec.scenario.iterationInTest;
  const { xml } = buildNfe(sequence, data.runCode);

  const response = http.post(`${BASE_URL}/api/v1/documents`, xml, {
    headers: { 'X-Api-Key': API_KEY, 'Content-Type': 'application/xml' },
    tags: { name: 'ingest' },
  });

  const created = check(response, { 'status 201': (r) => r.status === 201 });
  if (!created) {
    unexpected.add(1);
  }
}
