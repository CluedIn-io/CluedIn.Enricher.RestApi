// REST API enricher (V2) - Nigerian company lookup with the CAC VAS API
//
// Looks up a golden record's RC, BN or IT number in the Corporate Affairs Commission's (CAC)
// register by using the "Get Company by RC Number" operation of the CAC-verified VAS API
// (https://vas.cac.gov.ng/), and adds the result to the golden record as cac.* properties:
//
//   cac.rcNumber          registration number that was looked up
//   cac.name              registered entity name
//   cac.entityType        entity type, for example BUSINESS_NAME_FIRM
//   cac.status            entity status, for example ACTIVE
//   cac.address           registered address
//   cac.registrationDate  date the entity was registered (YYYY-MM-DD)
//   cac.lineOfBusiness    lines of business, separated by "; "
//
// Needs a VAS API key (sent in the X_API_KEY header), entered in the enricher's API Key field.
// Each call is charged to your VAS account balance. See README.md next to this file.
//
// A record gets no result (nothing is stored) when it has no valid registration number, when CAC
// has no entity with that number, or when the API can't answer. A missing or rejected key, or an
// account without enough balance, fails the record with an error that says so.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const RC_NUMBER_KEY = "organization.rcNumber";

// Use https://vasapp.oasisproducts.ng/api/vas/validation/company/rc (with a staging API key) to
// try the setup against CAC's staging environment first.
const CAC_URL = "https://vasapp.cac.gov.ng/api/vas/validation/company/rc";

// The {APIKey} placeholder is replaced with the API Key field value.
const API_KEY = "{APIKey}";

// How long to reuse an answer for the same registration number. 0 turns caching off. Every
// call is charged, so a long cache saves money.
const CACHE_MILLISECONDS = 7 * 24 * 60 * 60 * 1000;

// ---- Script ---------------------------------------------------------------------------

const MONTHS = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

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

// Accepts "RC 123456", "RC-123456", "rc123456", "BN 2345678", "IT 12345" and plain digits.
// Returns the digits, or null when it isn't a CAC registration number.
function parseRcNumber(raw) {
  const text = raw.toUpperCase().replace(/^(RC|BN|IT)[\s.:-]*/, "");
  if (!/^[0-9\s.-]+$/.test(text)) {
    return null;
  }
  const digits = text.replace(/[^0-9]/g, "").replace(/^0+/, "");
  return /^\d{1,9}$/.test(digits) ? digits : null;
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

// The API returns dates such as "Wed Jul 26 12:32:04 GMT 2023". Returns "2023-07-26", or null.
function toDate(value) {
  const text = clean(value);
  if (!text) {
    return null;
  }
  const iso = /^(\d{4}-\d{2}-\d{2})/.exec(text);
  if (iso) {
    return iso[1];
  }
  const match = /^[A-Za-z]{3} ([A-Za-z]{3}) (\d{1,2}) [\d:]+ [A-Z]+ (\d{4})$/.exec(text);
  if (!match) {
    return null;
  }
  const month = MONTHS.indexOf(match[1].toUpperCase()) + 1;
  if (month === 0) {
    return null;
  }
  return `${match[3]}-${month < 10 ? "0" : ""}${month}-${match[2].length === 1 ? "0" : ""}${match[2]}`;
}

function errorText(body) {
  if (!body) {
    return "";
  }
  return [body.error, body.message].filter((x) => !!clean(x)).join(" - ");
}

// Calls the VAS API. Returns the entity, or null when there's no answer to store.
function lookUp(rcNumber) {
  const apiResponse = http.send({
    url: CAC_URL,
    method: "POST",
    headers: [
      { Key: "X_API_KEY", Value: API_KEY },
      { Key: "Content-Type", Value: "application/json" },
      { Key: "Accept", Value: "application/json" },
    ],
    body: { rc_number: rcNumber },
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - the CAC VAS API is busy, the record will be retried");
  }

  if (status === "Unauthorized") {
    throw new Error("The CAC VAS API rejected the API key (HTTP Unauthorized). Check the API Key field of the enricher.");
  }

  // 403: not enough balance on the VAS account, or no permission for this service.
  if (status === "Forbidden") {
    throw new Error(`The CAC VAS API refused the call (HTTP Forbidden): ${errorText(body) || "forbidden"}. Check your VAS account balance and services.`);
  }

  const data = body && body.data;
  if (status === "OK" && data && !Array.isArray(data) && clean(data.rc_number)) {
    return data;
  }

  // 400 with "company not found" or "invalid rc_number passed", or a server error.
  log(`CAC could not look up ${rcNumber}. HTTP ${status}. ${errorText(body) || String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function run() {
  const rcNumber = parseRcNumber(getValue(RC_NUMBER_KEY));
  if (!rcNumber) {
    // No valid registration number on this record (this is also what Test connection hits,
    // because it passes placeholder values instead of real ones).
    return [];
  }

  if (!API_KEY) {
    log("No CAC VAS API key. Enter it in the API Key field of the enricher.");
    return [];
  }

  const cacheKey = `cac:${rcNumber}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let entity = cached ? parseJson(String(cached)) : null;

  if (!entity) {
    entity = lookUp(rcNumber);
    if (!entity) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(entity), CACHE_MILLISECONDS);
    }
  }

  const lines = Array.isArray(entity.line_of_business)
    ? entity.line_of_business.map((x) => clean(x)).filter((x) => !!x)
    : [];

  const data = {
    "cac.rcNumber": clean(entity.rc_number),
    "cac.name": clean(entity.entity_name),
    "cac.entityType": clean(entity.entity_type),
    "cac.status": clean(entity.entity_status),
    "cac.address": clean(entity.address),
    "cac.registrationDate": toDate(entity.registration_date),
    "cac.lineOfBusiness": lines.length > 0 ? lines.join("; ") : null,
  };

  // Leave out values CAC didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
