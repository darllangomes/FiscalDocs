// Carga de consulta. O setup grava SEED documentos; depois cada cenário roda sozinho
// (em sequência, não ao mesmo tempo) para que as latências não se misturem.
//
// Variáveis: BASE_URL, API_KEY (obrigatória), SEED (padrão 5000), RATE (padrão 30) e
// DURATION de cada cenário (padrão 30s).
//
// O cenário "list_deep" pede a última página: é o que mostra o custo do OFFSET (ADR 0010).
import http from 'k6/http';
import { check } from 'k6';
import { buildNfe, newRunCode, ISSUER_CNPJS } from './lib/documents.js';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const API_KEY = __ENV.API_KEY;
const SEED = Number(__ENV.SEED || 5000);
const RATE = Number(__ENV.RATE || 30);
const DURATION = __ENV.DURATION || '30s';
const PAGE_SIZE = 20;
const HEADERS = { 'X-Api-Key': API_KEY };

const NAMES = ['list_default', 'list_filtered', 'list_cnpj', 'list_deep', 'get_by_id'];
const SLOT = 35; // segundos entre o início de um cenário e o do próximo

function scenario(exec, index) {
  return {
    executor: 'constant-arrival-rate',
    exec,
    rate: RATE,
    timeUnit: '1s',
    duration: DURATION,
    startTime: `${index * SLOT}s`,
    preAllocatedVUs: Math.max(20, RATE),
    maxVUs: Math.max(100, RATE * 4),
    tags: { scenario_name: exec },
  };
}

const scenarios = {};
const thresholds = { http_req_failed: ['rate<0.01'], dropped_iterations: ['count==0'] };
NAMES.forEach((name, index) => {
  scenarios[name] = scenario(name, index);
  thresholds[`http_req_duration{name:${name}}`] = ['p(95)<500'];
});

export const options = {
  scenarios,
  thresholds,
  setupTimeout: '10m',
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export function setup() {
  if (!API_KEY) {
    throw new Error('Defina API_KEY.');
  }

  const runCode = newRunCode();
  const ids = [];
  const batchSize = 50;

  for (let start = 0; start < SEED; start += batchSize) {
    const requests = [];
    for (let i = start; i < Math.min(start + batchSize, SEED); i++) {
      requests.push([
        'POST',
        `${BASE_URL}/api/v1/documents`,
        buildNfe(i, runCode).xml,
        { headers: { ...HEADERS, 'Content-Type': 'application/xml' }, tags: { name: 'seed' } },
      ]);
    }
    for (const response of http.batch(requests)) {
      if (response.status !== 201) {
        throw new Error(`Seed falhou: HTTP ${response.status}`);
      }
      if (ids.length < 200) {
        ids.push(response.json('id'));
      }
    }
  }

  return { ids, lastPage: Math.ceil(SEED / PAGE_SIZE) };
}

function list(query, name) {
  const response = http.get(`${BASE_URL}/api/v1/documents?${query}`, { headers: HEADERS, tags: { name } });
  check(response, { 'status 200': (r) => r.status === 200 });
}

export function list_default() {
  list(`pageSize=${PAGE_SIZE}`, 'list_default');
}

export function list_filtered() {
  list(`type=NFe&issuerState=PE&issuedFrom=2026-08-05&issuedTo=2026-08-20&pageSize=${PAGE_SIZE}`, 'list_filtered');
}

export function list_cnpj() {
  const cnpj = ISSUER_CNPJS[Math.floor(Math.random() * ISSUER_CNPJS.length)];
  list(`issuerCnpj=${cnpj}&pageSize=${PAGE_SIZE}`, 'list_cnpj');
}

export function list_deep(data) {
  list(`page=${data.lastPage}&pageSize=${PAGE_SIZE}`, 'list_deep');
}

export function get_by_id(data) {
  const id = data.ids[Math.floor(Math.random() * data.ids.length)];
  const response = http.get(`${BASE_URL}/api/v1/documents/${id}`, { headers: HEADERS, tags: { name: 'get_by_id' } });
  check(response, { 'status 200': (r) => r.status === 200 });
}
