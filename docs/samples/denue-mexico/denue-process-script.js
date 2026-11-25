// REST API enricher (V2) - Mexican business establishment lookup with INEGI's DENUE API
//
// Looks up a golden record's DENUE establishment ID in INEGI's Directorio Estadístico Nacional
// de Unidades Económicas (DENUE), the national directory of business establishments, by using
// the DENUE API's Ficha method (https://www.inegi.org.mx/servicios/api_denue.html), and adds
// the result to the golden record as denue.* properties:
//
//   denue.id               DENUE establishment ID that was looked up
//   denue.clee             CLEE (Clave Estadística Empresarial) of the establishment
//   denue.name             name of the establishment
//   denue.legalName        registered business name (razón social)
//   denue.activity         economic activity class (Spanish)
//   denue.employeeRange    number of people employed, as a range, for example 0 a 5 personas
//   denue.address          street address on one line
//   denue.postalCode       postal code
//   denue.location         locality, municipality and state
//   denue.phone            telephone number
//   denue.email            email address
//   denue.website          website
//   denue.establishmentType establishment type, for example Fijo
//   denue.latitude         latitude
//   denue.longitude        longitude
//
// DENUE describes establishments (each shop, office or plant), not legal entities. It has no
// lookup by RFC (tax ID), so the input must be the DENUE establishment ID.
//
// Needs an INEGI DENUE API token, entered in the enricher's API Key field. See README.md next
// to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid DENUE ID, when DENUE has no
// establishment with that ID, or when the API can't answer. A missing or rejected token fails
// the record with an error that says so.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const DENUE_ID_KEY = "organization.denueId";

const DENUE_URL = "https://www.inegi.org.mx/app/api/denue/v1/consulta/Ficha/";

// The {APIKey} placeholder is replaced with the API Key field value (your DENUE API token).
const API_KEY = "{APIKey}";

// How long to reuse an answer for the same establishment. 0 turns caching off.
const CACHE_MILLISECONDS = 24 * 60 * 60 * 1000;

// ---- Script ---------------------------------------------------------------------------

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

// Accepts "34185", "34 185", "ID 34185". Returns the digits, or null when it isn't a DENUE ID.
function parseDenueId(raw) {
  const text = raw.toUpperCase().replace(/^ID/, "").replace(/[\s.,-]/g, "");
  if (!/^\d{1,12}$/.test(text)) {
    return null;
  }
  const id = text.replace(/^0+/, "");
  return id ? id : null;
}

function clean(value) {
  if (value === null || value === undefined) {
    return null;
  }
  const text = String(value).trim();
  return text === "" ? null : text;
}

function parseJson(text) {
  try {
    return JSON.parse(text);
  } catch (e) {
    return null;
  }
}

function formatAddress(item) {
  let street = [clean(item.Tipo_vialidad), clean(item.Calle), clean(item.Num_Exterior)].filter((x) => !!x).join(" ");
  if (clean(item.Num_Interior)) {
    street = `${street} Int. ${clean(item.Num_Interior)}`.trim();
  }
  const parts = [street, clean(item.Colonia), clean(item.CP)].filter((x) => !!x);
  return parts.length > 0 ? parts.join(", ") : null;
}

// Calls the DENUE API. Returns the establishment, or null when there's no answer to store.
function lookUp(id) {
  const apiResponse = http.send({
    url: `${DENUE_URL}${id}/${encodeURIComponent(API_KEY)}`,
    method: "GET",
    headers: [{ Key: "Accept", Value: "application/json" }],
  });

  const status = String(apiResponse.HttpStatus || "");
  const content = String(apiResponse.Content || "");

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - the DENUE API is busy, the record will be retried");
  }

  // A wrong token is answered with HTTP 200 and the text "No autorizado. Utilice una clave válida."
  if (status === "Unauthorized" || status === "Forbidden" || /no autorizado/i.test(content)) {
    throw new Error(`The DENUE API rejected the token in the API Key field (HTTP ${status}: ${content.substring(0, 100)}).`);
  }

  const body = parseJson(content);
  const items = Array.isArray(body) ? body : [];
  for (let i = 0; i < items.length; i++) {
    if (items[i] && String(items[i].Id) === id) {
      return items[i];
    }
  }

  // The token isn't included in the message, so it doesn't end up in the server log.
  log(`DENUE could not look up establishment ${id}. HTTP ${status}. ${content.substring(0, 200)}`);
  return null;
}

function run() {
  const id = parseDenueId(getValue(DENUE_ID_KEY));
  if (!id) {
    // No valid DENUE ID on this record (this is also what Test connection hits, because it
    // passes placeholder values instead of real ones).
    return [];
  }

  if (!clean(API_KEY)) {
    log("The DENUE API needs a token. Enter your INEGI DENUE API token in the enricher's API Key field.");
    return [];
  }

  const cacheKey = `denue:${id}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let item = cached ? parseJson(String(cached)) : null;

  if (!item) {
    item = lookUp(id);
    if (!item) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(item), CACHE_MILLISECONDS);
    }
  }

  const data = {
    "denue.id": clean(item.Id),
    "denue.clee": clean(item.CLEE),
    "denue.name": clean(item.Nombre),
    "denue.legalName": clean(item.Razon_social),
    "denue.activity": clean(item.Clase_actividad),
    "denue.employeeRange": clean(item.Estrato),
    "denue.address": formatAddress(item),
    "denue.postalCode": clean(item.CP),
    "denue.location": clean(item.Ubicacion),
    "denue.phone": clean(item.Telefono),
    "denue.email": clean(item.Correo_e),
    "denue.website": clean(item.Sitio_internet),
    "denue.establishmentType": clean(item.Tipo),
    "denue.latitude": clean(item.Latitud),
    "denue.longitude": clean(item.Longitud),
  };

  // Leave out values DENUE didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
