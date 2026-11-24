// REST API enricher (V2) - EU VAT number validation with VIES
//
// Checks a golden record's EU VAT number against the European Commission's VIES
// service (https://ec.europa.eu/taxation_customs/vies/) using its official JSON
// REST API, and adds the result to the golden record as vies.* properties:
//
//   vies.valid        "True" / "False" - whether VIES reports the number as valid
//   vies.name         registered trader name (when the member state returns it)
//   vies.address      registered trader address (when the member state returns it)
//   vies.countryCode  country code that was checked (EL for Greece, XI for Northern Ireland)
//   vies.vatNumber    VAT number that was checked, without the country prefix
//   vies.requestDate  date VIES answered the check (YYYY-MM-DD)
//
// No API key is needed. See README.md next to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no usable EU VAT number, or
// when VIES can't give an answer (invalid input, member state unavailable, time-out,
// and so on). This keeps an outage from being stored as vies.valid = False.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary keys to read. Add the same keys to the Vocabulary and Properties field.
const VAT_NUMBER_KEY = "organization.vat";
// Optional. Only used when the VAT number has no country prefix. Must hold a 2-letter code.
const COUNTRY_CODE_KEY = "organization.address.countryCode";

// Use .../check-vat-test-service to try the setup against VIES's test data first.
const VIES_URL = "https://ec.europa.eu/taxation_customs/vies/rest-api/check-vat-number";

// How long to reuse a VIES answer for the same VAT number. 0 turns caching off.
const CACHE_MILLISECONDS = 24 * 60 * 60 * 1000;

// ---- Script ---------------------------------------------------------------------------

// Country codes VIES accepts. Greece is EL in VIES, not GR. XI is Northern Ireland.
const VIES_COUNTRY_CODES = [
  "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "EL", "ES", "FI", "FR", "HR", "HU",
  "IE", "IT", "LT", "LU", "LV", "MT", "NL", "PL", "PT", "RO", "SE", "SI", "SK", "XI",
];

// VIES errors that mean "too busy, try again later". Throwing an error that mentions
// TooManyRequests makes the enricher retry the record instead of skipping it.
const VIES_RETRY_ERRORS = [
  "GLOBAL_MAX_CONCURRENT_REQ",
  "GLOBAL_MAX_CONCURRENT_REQ_TIME",
  "MS_MAX_CONCURRENT_REQ",
  "MS_MAX_CONCURRENT_REQ_TIME",
];

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

function toViesCountryCode(code) {
  const upper = String(code || "").trim().toUpperCase();
  const mapped = upper === "GR" ? "EL" : upper;
  return VIES_COUNTRY_CODES.indexOf(mapped) >= 0 ? mapped : null;
}

// Works out the VIES country code and national number from the record's values.
// Accepts "DE 123 456 789", "de123456789", "123.456.789" (with a country code key), etc.
function parseVatNumber(rawVatNumber, rawCountryCode) {
  const cleaned = rawVatNumber.toUpperCase().replace(/[^A-Z0-9]/g, "");
  if (!cleaned) {
    return null;
  }

  const prefixCountry = /^[A-Z]{2}/.test(cleaned) ? toViesCountryCode(cleaned.substring(0, 2)) : null;
  if (prefixCountry) {
    const nationalNumber = cleaned.substring(2);
    return nationalNumber ? { countryCode: prefixCountry, vatNumber: nationalNumber } : null;
  }

  const country = toViesCountryCode(rawCountryCode);
  return country ? { countryCode: country, vatNumber: cleaned } : null;
}

// VIES returns "---" when a member state doesn't share a value.
function clean(value) {
  if (value === null || value === undefined) {
    return null;
  }
  const text = String(value).trim();
  return text === "" || /^-+$/.test(text) ? null : text;
}

function parseJson(text) {
  try {
    return JSON.parse(text);
  } catch (e) {
    return null;
  }
}

function getViesErrors(body) {
  const wrappers = body && Array.isArray(body.errorWrappers) ? body.errorWrappers : [];
  return wrappers.map((x) => x && x.error).filter((x) => !!x);
}

// Calls VIES. Returns the parsed answer, or null when VIES couldn't give one.
function checkVatNumber(input) {
  const viesResponse = http.send({
    url: VIES_URL,
    method: "POST",
    headers: [
      { Key: "Content-Type", Value: "application/json" },
      { Key: "Accept", Value: "application/json" },
    ],
    body: { countryCode: input.countryCode, vatNumber: input.vatNumber },
  });

  const status = String(viesResponse.HttpStatus || "");
  const body = parseJson(viesResponse.Content);
  const errors = getViesErrors(body);

  if (status === "TooManyRequests" || errors.some((x) => VIES_RETRY_ERRORS.indexOf(x) >= 0)) {
    throw new Error(`TooManyRequests - VIES is busy (${errors.join(", ") || status}), the record will be retried`);
  }

  if (status !== "OK" || !body || typeof body.valid !== "boolean" || errors.length > 0) {
    log(`VIES could not check ${input.countryCode}${input.vatNumber}. HTTP ${status}. ${errors.join(", ") || String(viesResponse.Content || "").substring(0, 200)}`);
    return null;
  }

  return body;
}

function run() {
  const input = parseVatNumber(getValue(VAT_NUMBER_KEY), getValue(COUNTRY_CODE_KEY));
  if (!input) {
    // No EU VAT number on this record (this is also what Test Connection hits, because
    // it passes placeholder values instead of real ones).
    return [];
  }

  const cacheKey = `vies:${input.countryCode}${input.vatNumber}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let result = cached ? parseJson(String(cached)) : null;

  if (!result) {
    result = checkVatNumber(input);
    if (!result) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(result), CACHE_MILLISECONDS);
    }
  }

  const data = {
    "vies.valid": result.valid,
    "vies.countryCode": clean(result.countryCode) || input.countryCode,
    "vies.vatNumber": clean(result.vatNumber) || input.vatNumber,
    // Date only (YYYY-MM-DD). A full timestamp would be turned into a date in the server's
    // own format (e.g. 11/24/2026 03:44:18) when the enricher reads the script's result.
    "vies.requestDate": clean(result.requestDate) ? String(result.requestDate).substring(0, 10) : null,
    "vies.name": clean(result.name),
    // VIES puts each address line on its own line; store it on one line.
    "vies.address": clean(String(result.address || "").trim().replace(/\s*\n\s*/g, ", ")),
  };

  // Leave out values VIES didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
