// Gera NFes fictícias no formato real (leiaute 4.00): itens com impostos, transporte, pagamento,
// assinatura digital e protocolo. Cada documento tem chave de acesso única e dígito verificador
// válido; sem isso o teste mediria só o caminho de duplicata (200), não o de inserção (201).
//
// ITEMS (padrão 20) controla o tamanho: cerca de 9 KB fixos (as duas assinaturas e o resto do
// envelope) mais 0,75 KB por item. Com 20 itens o XML tem cerca de 24 KB.

const ITEMS = Number(__ENV.ITEMS || 20);

// Emitentes dos samples (CNPJ válido, numérico e alfanumérico) com a UF e o município de cada um.
const ISSUERS = [
  { cnpj: '11222333000181', state: 'PE', cUF: '26', cMun: '2611606', city: 'RECIFE', name: 'EMPRESA FICTICIA NUMERICA LTDA' },
  { cnpj: '12ABC34501DE35', state: 'PE', cUF: '26', cMun: '2611606', city: 'RECIFE', name: 'EMPRESA FICTICIA ALFANUMERICA LTDA' },
  { cnpj: '44555666000181', state: 'SP', cUF: '35', cMun: '3550308', city: 'SAO PAULO', name: 'TRANSPORTADORA FICTICIA NUMERICA LTDA' },
  { cnpj: '7K9X2M4P000144', state: 'SP', cUF: '35', cMun: '3550308', city: 'SAO PAULO', name: 'TRANSPORTADORA FICTICIA ALFANUMERICA LTDA' },
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
const money = (cents) => (cents / 100).toFixed(2);

// Pseudo-aleatório determinístico (Park-Miller): todo VU monta os mesmos itens, e a carga é repetível.
let seed = 20260816;
function next(max) {
  seed = (seed * 48271) % 2147483647;
  return 1 + (seed % max);
}

const BASE64 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
function filler(length) {
  let text = '';
  for (let i = 0; i < length; i++) {
    text += BASE64[next(64) - 1];
  }
  return text;
}

// ---------- Partes que não mudam de um documento para o outro (montadas uma vez por VU) ----------

const PRODUCTS = [
  ['AÇÚCAR REFINADO 1KG', '17019900', 429],
  ['ARROZ TIPO 1 PACOTE 5KG', '10063021', 2790],
  ['FEIJÃO CARIOCA 1KG', '07133319', 849],
  ['ÓLEO DE SOJA 900ML', '15079011', 699],
  ['CAFÉ TORRADO E MOÍDO 500G', '09012100', 1850],
  ['LEITE INTEGRAL UHT 1L', '04012010', 529],
  ['DETERGENTE LÍQUIDO 500ML', '34022000', 299],
  ['PAPEL HIGIÊNICO FOLHA DUPLA 12UN', '48181000', 2190],
  ['FARINHA DE TRIGO 1KG', '11010010', 579],
  ['MACARRÃO ESPAGUETE 500G', '19021100', 449],
];

let totalProducts = 0;
let totalIcms = 0;
let totalPis = 0;
let totalCofins = 0;
let totalTaxes = 0;
const details = [];

for (let n = 1; n <= ITEMS; n++) {
  const [name, ncm, priceCents] = PRODUCTS[(n - 1) % PRODUCTS.length];
  const quantity = next(48);
  const value = quantity * priceCents;
  const icms = Math.round(value * 0.18);
  const pis = Math.round(value * 0.0165);
  const cofins = Math.round(value * 0.076);
  const taxes = Math.round(value * 0.31);
  const price = (priceCents / 100).toFixed(10);

  totalProducts += value;
  totalIcms += icms;
  totalPis += pis;
  totalCofins += cofins;
  totalTaxes += taxes;

  details.push(
    `<det nItem="${n}"><prod><cProd>${100000 + n}</cProd><cEAN>SEM GTIN</cEAN><xProd>${name}</xProd>` +
      `<NCM>${ncm}</NCM><CFOP>5102</CFOP><uCom>UN</uCom><qCom>${quantity}.0000</qCom><vUnCom>${price}</vUnCom>` +
      `<vProd>${money(value)}</vProd><cEANTrib>SEM GTIN</cEANTrib><uTrib>UN</uTrib><qTrib>${quantity}.0000</qTrib>` +
      `<vUnTrib>${price}</vUnTrib><indTot>1</indTot></prod><imposto><vTotTrib>${money(taxes)}</vTotTrib>` +
      `<ICMS><ICMS00><orig>0</orig><CST>00</CST><modBC>3</modBC><vBC>${money(value)}</vBC><pICMS>18.0000</pICMS>` +
      `<vICMS>${money(icms)}</vICMS></ICMS00></ICMS>` +
      `<PIS><PISAliq><CST>01</CST><vBC>${money(value)}</vBC><pPIS>1.6500</pPIS><vPIS>${money(pis)}</vPIS></PISAliq></PIS>` +
      `<COFINS><COFINSAliq><CST>01</CST><vBC>${money(value)}</vBC><pCOFINS>7.6000</pCOFINS><vCOFINS>${money(cofins)}</vCOFINS></COFINSAliq></COFINS>` +
      `</imposto></det>`,
  );
}

const ITEMS_XML = details.join('');

const TOTALS_XML =
  `<total><ICMSTot><vBC>${money(totalProducts)}</vBC><vICMS>${money(totalIcms)}</vICMS><vICMSDeson>0.00</vICMSDeson>` +
  `<vFCP>0.00</vFCP><vBCST>0.00</vBCST><vST>0.00</vST><vFCPST>0.00</vFCPST><vFCPSTRet>0.00</vFCPSTRet>` +
  `<vProd>${money(totalProducts)}</vProd><vFrete>0.00</vFrete><vSeg>0.00</vSeg><vDesc>0.00</vDesc><vII>0.00</vII>` +
  `<vIPI>0.00</vIPI><vIPIDevol>0.00</vIPIDevol><vPIS>${money(totalPis)}</vPIS><vCOFINS>${money(totalCofins)}</vCOFINS>` +
  `<vOutro>0.00</vOutro><vNF>${money(totalProducts)}</vNF><vTotTrib>${money(totalTaxes)}</vTotTrib></ICMSTot></total>` +
  `<transp><modFrete>0</modFrete><transporta><CNPJ>44555666000181</CNPJ><xNome>TRANSPORTADORA FICTICIA NUMERICA LTDA</xNome>` +
  `<IE>392012347</IE><xEnder>RUA DAS CARGAS 500</xEnder><xMun>SAO PAULO</xMun><UF>SP</UF></transporta>` +
  `<vol><qVol>${ITEMS}</qVol><esp>VOLUMES</esp><pesoL>${ITEMS * 2}.000</pesoL><pesoB>${ITEMS * 2 + 3}.000</pesoB></vol></transp>` +
  `<pag><detPag><indPag>1</indPag><tPag>15</tPag><vPag>${money(totalProducts)}</vPag></detPag></pag>`;

const RECIPIENT_XML =
  '<dest><CNPJ>12ABC34501DE35</CNPJ><xNome>EMPRESA FICTICIA ALFANUMERICA LTDA</xNome><enderDest>' +
  '<xLgr>AVENIDA DOS EXEMPLOS</xLgr><nro>200</nro><xBairro>JARDINS</xBairro><cMun>3550308</cMun><xMun>SAO PAULO</xMun>' +
  '<UF>SP</UF><CEP>01000000</CEP><cPais>1058</cPais><xPais>BRASIL</xPais></enderDest><indIEDest>1</indIEDest>' +
  '<IE>110042490114</IE></dest>';

const DIGEST = filler(28);
const SIGNATURE_VALUE = filler(344);
const CERTIFICATE = filler(1868);

function signature(reference) {
  return (
    '<Signature xmlns="http://www.w3.org/2000/09/xmldsig#"><SignedInfo>' +
    '<CanonicalizationMethod Algorithm="http://www.w3.org/TR/2001/REC-xml-c14n-20010315"/>' +
    '<SignatureMethod Algorithm="http://www.w3.org/2000/09/xmldsig#rsa-sha1"/>' +
    `<Reference URI="#${reference}"><Transforms>` +
    '<Transform Algorithm="http://www.w3.org/2000/09/xmldsig#enveloped-signature"/>' +
    '<Transform Algorithm="http://www.w3.org/TR/2001/REC-xml-c14n-20010315"/></Transforms>' +
    '<DigestMethod Algorithm="http://www.w3.org/2000/09/xmldsig#sha1"/>' +
    `<DigestValue>${DIGEST}</DigestValue></Reference></SignedInfo>` +
    `<SignatureValue>${SIGNATURE_VALUE}</SignatureValue>` +
    `<KeyInfo><X509Data><X509Certificate>${CERTIFICATE}</X509Certificate></X509Data></KeyInfo></Signature>`
  );
}

// ---------- Parte que muda a cada documento ----------

// `sequence` precisa ser único dentro da execução e `runCode` (8 dígitos) separa uma execução
// da outra, para a chave não repetir entre execuções.
export function buildNfe(sequence, runCode) {
  const issuer = ISSUERS[sequence % ISSUERS.length];
  const day = 1 + (sequence % 28); // sempre em 2026-08, coerente com o AAMM da chave
  const number = pad(sequence, 9);
  const base = `${issuer.cUF}2608${issuer.cnpj}55001${number}1${runCode}`;
  const accessKey = base + checkDigit(base);
  const protocol = 135260000000000 + sequence;

  const inf =
    `<infNFe Id="NFe${accessKey}" versao="4.00"><ide><cUF>${issuer.cUF}</cUF><cNF>${runCode}</cNF>` +
    `<natOp>VENDA DE MERCADORIA ADQUIRIDA OU RECEBIDA DE TERCEIROS</natOp><mod>55</mod><serie>1</serie>` +
    `<nNF>${Number(number)}</nNF><dhEmi>2026-08-${pad(day, 2)}T09:12:44-03:00</dhEmi>` +
    `<dhSaiEnt>2026-08-${pad(day, 2)}T09:30:00-03:00</dhSaiEnt><tpNF>1</tpNF><idDest>2</idDest>` +
    `<cMunFG>${issuer.cMun}</cMunFG><tpImp>1</tpImp><tpEmis>1</tpEmis><cDV>${accessKey.slice(-1)}</cDV><tpAmb>2</tpAmb>` +
    `<finNFe>1</finNFe><indFinal>0</indFinal><indPres>1</indPres><procEmi>0</procEmi><verProc>FISCALDOCS-K6 1.0</verProc></ide>` +
    `<emit><CNPJ>${issuer.cnpj}</CNPJ><xNome>${issuer.name}</xNome><xFant>EMPRESA FICTICIA</xFant><enderEmit>` +
    `<xLgr>RUA DOS TESTES</xLgr><nro>100</nro><xBairro>CENTRO</xBairro><cMun>${issuer.cMun}</cMun><xMun>${issuer.city}</xMun>` +
    `<UF>${issuer.state}</UF><CEP>50000000</CEP><cPais>1058</cPais><xPais>BRASIL</xPais><fone>8133334444</fone></enderEmit>` +
    `<IE>0321418</IE><CRT>3</CRT></emit>` +
    RECIPIENT_XML +
    ITEMS_XML +
    TOTALS_XML +
    `<infAdic><infCpl>DOCUMENTO FICTICIO PARA TESTES DE CARGA. PEDIDO ${Number(number)} - ENTREGA AGENDADA PARA O PERIODO DA MANHA.</infCpl></infAdic>` +
    `</infNFe>`;

  const protocolXml =
    `<protNFe versao="4.00"><infProt Id="ID${protocol}"><tpAmb>2</tpAmb><verAplic>SVRS202605</verAplic>` +
    `<chNFe>${accessKey}</chNFe><dhRecbto>2026-08-${pad(day, 2)}T09:12:51-03:00</dhRecbto><nProt>${protocol}</nProt>` +
    `<digVal>${DIGEST}</digVal><cStat>100</cStat><xMotivo>Autorizado o uso da NF-e</xMotivo></infProt>` +
    `${signature(`ID${protocol}`)}</protNFe>`;

  return {
    accessKey,
    issuer,
    xml:
      '<?xml version="1.0" encoding="UTF-8"?>' +
      '<nfeProc xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00"><NFe>' +
      `${inf}${signature(`NFe${accessKey}`)}</NFe>${protocolXml}</nfeProc>`,
  };
}

// Tamanho do documento em bytes UTF-8, para registrar na execução.
export function sizeInBytes(xml) {
  return unescape(encodeURIComponent(xml)).length;
}

// Oito dígitos aleatórios para separar as execuções.
export function newRunCode() {
  return pad(Math.floor(Math.random() * 1e8), 8);
}
