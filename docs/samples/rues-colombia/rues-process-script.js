// REST API enricher (V2) - Colombian company lookup in the RUES
//
// Looks up a golden record's Colombian NIT in the Registro Único Empresarial y Social (RUES),
// using the extract of the commercial register that Confecámaras publishes on the national
// open data portal (https://www.datos.gov.co/, dataset c82u-588k, "Personas Naturales, Personas
// Jurídicas y Entidades Sin Animo de Lucro"), and adds the result to the golden record as
// rues.* properties:
//
//   rues.nit                   NIT that was looked up, without the check digit
//   rues.checkDigit            NIT check digit (dígito de verificación)
//   rues.name                  registered name (razón social)
//   rues.acronym               acronym (sigla)
//   rues.status                status of the registration, for example ACTIVA or CANCELADA
//   rues.category              registration category, for example SOCIEDAD ó PERSONA JURIDICA PRINCIPAL ó ESAL
//   rues.legalOrganization     legal form, for example SOCIEDAD ANONIMA
//   rues.companyType           company type, for example SOCIEDAD COMERCIAL
//   rues.mainActivityCode      main economic activity (CIIU) code
//   rues.secondaryActivityCode secondary economic activity (CIIU) code
//   rues.chamberOfCommerce     chamber of commerce that holds the registration
//   rues.chamberCode           code of that chamber of commerce
//   rues.registrationNumber    commercial registration number (matrícula)
//   rues.registrationDate      registration date (YYYY-MM-DD)
//   rues.renewalDate           date of the last renewal (YYYY-MM-DD)
//   rues.lastRenewedYear       last year the registration was renewed
//   rues.expiryDate            date the company's term ends (YYYY-MM-DD), when it has one
//   rues.cancellationDate      date the registration was cancelled (YYYY-MM-DD)
//   rues.legalRepresentative   name of the legal representative
//   rues.updatedDate           date the RUES last updated the record (YYYY-MM-DD)
//
// No API key is needed. An optional Socrata app token (for higher rate limits) can be entered
// in the API Key field. See README.md next to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid NIT, when the NIT has no
// registration, when its registrations can't be told apart, or when the API can't answer.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const NIT_KEY = "organization.nit";

// The enricher replaces {APIKey} with the API Key field value: an optional datos.gov.co
// (Socrata) app token. Leave the field empty to call the API without one.
const APP_TOKEN = "{APIKey}";

const RUES_URL = "https://www.datos.gov.co/resource/c82u-588k.json";

// How long to reuse an answer for the same NIT. 0 turns caching off.
const CACHE_MILLISECONDS = 24 * 60 * 60 * 1000;

// ---- Script ---------------------------------------------------------------------------

const NIT_IDENTIFICATION_CLASS = "02"; // codigo_clase_identificacion for a NIT
const ACTIVE_STATUS = "01"; // codigo_estado_matricula for ACTIVA
const PRINCIPAL_CATEGORY = "01"; // codigo_categoria_matricula for a company, legal person or non-profit

function getValue(key) {
  // A plain loop rather than vocabularies.find(...): vocabularies is a .NET list, and when
  // .find is called from inside a function, later calls return the first call's match.
  const items = vocabularies || [];
  for (let i = 0; i < items.length; i++) {
    if (items[i].Key === key) {
      const value = items[i].Value;
      return value === null || value === undefined ? "" : String(value).trim();
    }
  }
  return "";
}

// DIAN check digit for a NIT.
function checkDigit(nit) {
  const weights = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];
  let sum = 0;
  for (let i = 0; i < nit.length; i++) {
    sum += Number(nit.charAt(nit.length - 1 - i)) * weights[i];
  }
  const remainder = sum % 11;
  return remainder < 2 ? remainder : 11 - remainder;
}

// Accepts "860002964", "860.002.964", "860.002.964-4", "NIT 860002964-4", etc. A check digit is
// only read after a dash, and is checked when present. Returns { nit, checkDigit } or null.
function parseNit(raw) {
  const text = raw.toUpperCase().replace(/^NIT\.?/, "").replace(/[\s.,]/g, "");
  const match = /^(\d{5,10})(?:-(\d))?$/.exec(text);
  if (!match) {
    return null;
  }
  const nit = match[1].replace(/^0+/, "");
  if (nit.length < 5) {
    return null;
  }
  const expected = checkDigit(nit);
  if (match[2] !== undefined && Number(match[2]) !== expected) {
    return null;
  }
  return { nit: nit, checkDigit: String(expected) };
}

function clean(value) {
  if (value === null || value === undefined) {
    return null;
  }
  const text = String(value).replace(/\s+/g, " ").trim();
  return text === "" ? null : text;
}

function parseJson(text) {
  try {
    return JSON.parse(text);
  } catch (e) {
    return null;
  }
}

// RUES dates are YYYYMMDD. 99991231 (no end date) and 00000000 are left out.
function formatDate(value) {
  const text = clean(value);
  if (!text || !/^\d{8}$/.test(text) || text.substring(0, 4) === "9999" || text.substring(0, 4) === "0000") {
    return null;
  }
  return `${text.substring(0, 4)}-${text.substring(4, 6)}-${text.substring(6, 8)}`;
}

// fecha_actualizacion looks like "2026/10/23 08:10:16.610000000".
function formatUpdatedDate(value) {
  const text = clean(value);
  return text && /^\d{4}\/\d{2}\/\d{2}/.test(text) ? text.substring(0, 10).replace(/\//g, "-") : null;
}

// Ranks a registration: active first, then the principal registration, then the most recently
// renewed. Higher is better.
function rank(row) {
  return (row.codigo_estado_matricula === ACTIVE_STATUS ? 2 : 0) +
    (row.codigo_categoria_matricula === PRINCIPAL_CATEGORY ? 1 : 0);
}

function renewal(row) {
  return clean(row.fecha_renovacion) || clean(row.fecha_matricula) || "";
}

// Chooses one registration for the NIT, or null when there's none or they can't be told apart.
function chooseRegistration(rows, nit) {
  // Only registrations identified by this NIT. The extract also lists some natural persons
  // under another NIT (their own identification number is different), which are ignored.
  const candidates = rows.filter((row) => row && clean(row.nit) === nit &&
    (row.codigo_clase_identificacion === NIT_IDENTIFICATION_CLASS || clean(row.numero_identificacion) === nit));
  if (candidates.length === 0) {
    log(`RUES has no registration identified by NIT ${nit}.`);
    return null;
  }

  candidates.sort((a, b) => rank(b) - rank(a) || (renewal(b) > renewal(a) ? 1 : renewal(b) < renewal(a) ? -1 : 0));
  const best = candidates[0];
  const tied = candidates.filter((row) => rank(row) === rank(best) && clean(row.razon_social) !== clean(best.razon_social));
  if (tied.length > 0) {
    log(`RUES has registrations under different names for NIT ${nit} (${clean(best.razon_social)}, ${clean(tied[0].razon_social)}). Nothing was added.`);
    return null;
  }
  return best;
}

// Calls the API. Returns the registrations for the NIT (possibly none), or null when the API
// can't answer.
function lookUp(nit) {
  const headers = [{ Key: "Accept", Value: "application/json" }];
  if (clean(APP_TOKEN)) {
    headers.push({ Key: "X-App-Token", Value: APP_TOKEN.trim() });
  }

  const apiResponse = http.send({
    url: `${RUES_URL}?nit=${nit}`,
    method: "GET",
    headers: headers,
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - datos.gov.co is throttling requests, the record will be retried");
  }

  if ((status === "Forbidden" || status === "Unauthorized") && clean(APP_TOKEN)) {
    throw new Error(`datos.gov.co rejected the app token in the API Key field (HTTP ${status}). ${body && body.message ? body.message : ""}`);
  }

  if (status === "OK" && Array.isArray(body)) {
    return body;
  }

  log(`RUES could not look up NIT ${nit}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function run() {
  const input = parseNit(getValue(NIT_KEY));
  if (!input) {
    // No valid NIT on this record (this is also what Test connection hits, because it passes
    // placeholder values instead of real ones).
    return [];
  }

  const cacheKey = `rues:${input.nit}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let row = cached ? parseJson(String(cached)) : null;

  if (!row) {
    const rows = lookUp(input.nit);
    if (!rows) {
      return [];
    }
    row = chooseRegistration(rows, input.nit);
    if (!row) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(row), CACHE_MILLISECONDS);
    }
  }

  const data = {
    "rues.nit": input.nit,
    "rues.checkDigit": clean(row.digito_verificacion) || input.checkDigit,
    "rues.name": clean(row.razon_social),
    "rues.acronym": clean(row.sigla),
    "rues.status": clean(row.estado_matricula),
    "rues.category": clean(row.categoria_matricula),
    "rues.legalOrganization": clean(row.organizacion_juridica),
    "rues.companyType": clean(row.tipo_sociedad),
    "rues.mainActivityCode": clean(row.cod_ciiu_act_econ_pri),
    "rues.secondaryActivityCode": clean(row.cod_ciiu_act_econ_sec),
    "rues.chamberOfCommerce": clean(row.camara_comercio),
    "rues.chamberCode": clean(row.codigo_camara),
    "rues.registrationNumber": clean(row.matricula),
    "rues.registrationDate": formatDate(row.fecha_matricula),
    "rues.renewalDate": formatDate(row.fecha_renovacion),
    "rues.lastRenewedYear": clean(row.ultimo_ano_renovado) === "0" ? null : clean(row.ultimo_ano_renovado),
    "rues.expiryDate": formatDate(row.fecha_vigencia),
    "rues.cancellationDate": formatDate(row.fecha_cancelacion),
    "rues.legalRepresentative": clean(row.representante_legal),
    "rues.updatedDate": formatUpdatedDate(row.fecha_actualizacion),
  };

  // Leave out values RUES didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
