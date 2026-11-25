// REST API enricher (V2) - Latvian company lookup with the Enterprise Register's open data
//
// Looks up a golden record's Latvian registration number in the Register of Enterprises of the
// Republic of Latvia (Uzņēmumu reģistrs) open data, which the Latvian Open Data Portal
// publishes at https://data.gov.lv/dati/lv/dataset/uz, using the portal's CKAN datastore search
// API, and adds the result to the golden record as lvur.* properties:
//
//   lvur.registrationNumber  11-digit registration number that was looked up
//   lvur.name                registered name
//   lvur.legalForm           legal form, for example Akciju sabiedrība
//   lvur.legalFormCode       legal form code, for example AS
//   lvur.register            register the entity is in, for example Komercreģistrs
//   lvur.status              Registered, Liquidated or Reorganised
//   lvur.registrationDate    date of registration (YYYY-MM-DD)
//   lvur.terminationDate     date the entity was removed from the register (YYYY-MM-DD)
//   lvur.address             legal address
//   lvur.postalCode          postal code of the legal address, for example LV-1010
//   lvur.sepaCreditorId      SEPA creditor identifier
//
// No API key is needed. See README.md next to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid registration number, when
// the number isn't in the register, or when the API can't answer.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const REGISTRATION_NUMBER_KEY = "organization.registrationNumber";

const DATASTORE_URL = "https://data.gov.lv/dati/api/3/action/datastore_search";
// "Register" resource of the Enterprise Register's open data on data.gov.lv
const RESOURCE_ID = "25e80bf3-f107-4ab4-89ef-251b5b9374e9";

// How long to reuse an answer for the same registration number. 0 turns caching off.
const CACHE_MILLISECONDS = 24 * 60 * 60 * 1000;

// ---- Script ---------------------------------------------------------------------------

// What the register's "closed" code means. A blank code means the entity is still registered.
const CLOSED_STATUSES = { L: "Liquidated", R: "Reorganised" };

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

// Accepts "40003032949", "4000 303 2949" and the VAT number form "LV40003032949". Returns the
// 11 digits, or null when the value isn't an 11-digit Latvian registration number.
function parseRegistrationNumber(raw) {
  const text = raw.toUpperCase().replace(/^LV/, "").replace(/[\s.\-]/g, "");
  return /^\d{11}$/.test(text) ? text : null;
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

// "1991-10-08T00:00:00" -> "1991-10-08"
function dateOnly(value) {
  const text = clean(value);
  return text && /^\d{4}-\d{2}-\d{2}/.test(text) ? text.substring(0, 10) : null;
}

// Calls the datastore API. Returns the entity's row, or null when there's no answer to store.
function lookUp(registrationNumber) {
  const filters = { regcode: registrationNumber };
  const apiResponse = http.send({
    url: `${DATASTORE_URL}?resource_id=${RESOURCE_ID}&limit=1&filters=${encodeURIComponent(JSON.stringify(filters))}`,
    method: "GET",
    headers: [{ Key: "Accept", Value: "application/json" }],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests" || status === "ServiceUnavailable") {
    throw new Error(`TooManyRequests - data.gov.lv is busy (HTTP ${status}), the record will be retried`);
  }

  const records = body && body.success === true && body.result && Array.isArray(body.result.records)
    ? body.result.records
    : null;

  if (status !== "OK" || !records) {
    log(`data.gov.lv could not look up registration number ${registrationNumber}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
    return null;
  }

  for (let i = 0; i < records.length; i++) {
    if (String(records[i].regcode) === registrationNumber) {
      return records[i];
    }
  }

  log(`The Enterprise Register has no entity with registration number ${registrationNumber}.`);
  return null;
}

function run() {
  const registrationNumber = parseRegistrationNumber(getValue(REGISTRATION_NUMBER_KEY));
  if (!registrationNumber) {
    // No valid registration number on this record (this is also what Test connection hits,
    // because it passes placeholder values instead of real ones).
    return [];
  }

  const cacheKey = `lvur:${registrationNumber}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let row = cached ? parseJson(String(cached)) : null;

  if (!row) {
    row = lookUp(registrationNumber);
    if (!row) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(row), CACHE_MILLISECONDS);
    }
  }

  const closed = clean(row.closed);
  const postalCode = clean(row.index);

  const data = {
    "lvur.registrationNumber": registrationNumber,
    "lvur.name": clean(row.name),
    "lvur.legalForm": clean(row.type_text),
    "lvur.legalFormCode": clean(row.type),
    "lvur.register": clean(row.regtype_text),
    "lvur.status": closed ? CLOSED_STATUSES[closed] || closed : "Registered",
    "lvur.registrationDate": dateOnly(row.registered),
    "lvur.terminationDate": dateOnly(row.terminated),
    "lvur.address": clean(row.address),
    // Latvian postal codes are written LV-NNNN; the register stores only the digits.
    "lvur.postalCode": postalCode && /^\d{4}$/.test(postalCode) ? `LV-${postalCode}` : postalCode,
    "lvur.sepaCreditorId": clean(row.sepa),
  };

  // Leave out values the register didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
