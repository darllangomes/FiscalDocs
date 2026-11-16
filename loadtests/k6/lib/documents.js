// Gera NFe fictícias com chave de acesso única e dígito verificador válido.
// Sem isso o teste mediria só o caminho de duplicata (200), não o de inserção (201).

// Emitentes reais dos samples (CNPJ válido, numérico e alfanumérico) e a UF de cada um.
const ISSUERS = [
  { cnpj: '11222333000181', state: 'PE', name: 'EMPRESA FICTICIA NUMERICA LTDA' },
  { cnpj: '12ABC34501DE35', state: 'PE', name: 'EMPRESA FICTICIA ALFANUMERICA LTDA' },
  { cnpj: '44555666000181', state: 'SP', name: 'TRANSPORTADORA FICTICIA NUMERICA LTDA' },
  { cnpj: '7K9X2M4P000144', state: 'SP', name: 'TRANSPORTADORA FICTICIA ALFANUMERICA LTDA' },
];

export const ISSUER_CNPJS = ISSUERS.map((i) => i.cnpj);

// Módulo 11 igual ao do domínio: pesos 2 a 9 da direita para a esquerda, valor = ASCII - 48.
function checkDigit(text) {
  let sum = 0;
  let weight = 2;
  for (let i = text.length - 1; i >= 0; i--) {
    sum += (text.charCodeAt(i) - 48) * weight;
    weight = weight === 9 ? 2 : weight + 1;
  }
  const remainder = sum % 11;
  return remainder < 2 ? 0 : 11 - remainder;
}

const pad = (value, size) => String(value).padStart(size, '0');

// `sequence` precisa ser único dentro da execução (use o número da iteração) e `runCode`
// (8 dígitos) separa uma execução da outra, para a chave não repetir entre execuções.
export function buildNfe(sequence, runCode) {
  const issuer = ISSUERS[sequence % ISSUERS.length];
  const day = 1 + (sequence % 28); // sempre em 2026-08, coerente com o AAMM da chave
  const number = pad(sequence, 9);
  const base = `262608${issuer.cnpj}55001${number}1${runCode}`;
  const accessKey = base + checkDigit(base);
  const total = (100 + (sequence % 9000) / 100).toFixed(2);

  return {
    accessKey,
    issuer,
    xml: `<?xml version="1.0" encoding="UTF-8"?>
<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
  <NFe>
    <infNFe Id="NFe${accessKey}" versao="4.00">
      <ide>
        <cUF>26</cUF>
        <cNF>${runCode}</cNF>
        <natOp>VENDA DE MERCADORIA</natOp>
        <mod>55</mod>
        <serie>1</serie>
        <nNF>${Number(number)}</nNF>
        <dhEmi>2026-08-${pad(day, 2)}T10:30:00-03:00</dhEmi>
        <tpNF>1</tpNF>
        <tpAmb>2</tpAmb>
      </ide>
      <emit>
        <CNPJ>${issuer.cnpj}</CNPJ>
        <xNome>${issuer.name}</xNome>
        <enderEmit>
          <xLgr>RUA DOS TESTES</xLgr>
          <nro>100</nro>
          <xBairro>CENTRO</xBairro>
          <cMun>2611606</cMun>
          <xMun>RECIFE</xMun>
          <UF>${issuer.state}</UF>
          <CEP>50000000</CEP>
        </enderEmit>
      </emit>
      <dest>
        <CPF>12345678909</CPF>
        <xNome>CLIENTE FICTICIO DA SILVA</xNome>
        <enderDest>
          <xLgr>AVENIDA DOS EXEMPLOS</xLgr>
          <nro>200</nro>
          <xBairro>JARDINS</xBairro>
          <cMun>3550308</cMun>
          <xMun>SAO PAULO</xMun>
          <UF>SP</UF>
          <CEP>01000000</CEP>
        </enderDest>
      </dest>
      <total>
        <ICMSTot>
          <vProd>${total}</vProd>
          <vNF>${total}</vNF>
        </ICMSTot>
      </total>
    </infNFe>
  </NFe>
</nfeProc>
`,
  };
}

// Oito dígitos aleatórios para separar as execuções.
export function newRunCode() {
  return pad(Math.floor(Math.random() * 1e8), 8);
}
