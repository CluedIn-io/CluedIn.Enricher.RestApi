// REST API enricher (V2) - Estonian company lookup with the e-Business Register
//
// Looks up a golden record's Estonian registry code in the e-Business Register (Äriregister),
// using the register's free public company search service
// (https://ariregister.rik.ee/eng/api/autocomplete), and adds the result to the golden record
// as ariregister.* properties:
//
//   ariregister.registryCode    8-digit registry code that was looked up
//   ariregister.name            registered name
//   ariregister.statusCode      status code, for example R (registered) or L (in liquidation)
//   ariregister.status          status in English, for the codes listed in STATUS_NAMES
//   ariregister.legalAddress    legal address on one line
//   ariregister.postalCode      postal code of the legal address
//   ariregister.legalFormCode   the register's legal form code, for example 1 or 5
//   ariregister.previousNames   previous names, separated by "; "
//   ariregister.url             link to the company's page in the e-Business Register
//
// No API key is needed. See README.md next to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid registry code, when the
// code isn't found (deleted companies aren't returned), or when the service can't answer.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const REGISTRY_CODE_KEY = "organization.registryCode";

// "eng" returns links to the English pages of the register. Use "est" for Estonian.
const ARIREGISTER_URL = "https://ariregister.rik.ee/eng/api/autocomplete";

// How long to reuse an answer for the same registry code. 0 turns caching off.
const CACHE_MILLISECONDS = 24 * 60 * 60 * 1000;

// Status codes with an English name. Other codes are stored in ariregister.statusCode only.
const STATUS_NAMES = {
  R: "Registered",
  L: "In liquidation",
};

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

// Accepts "10137319", "10 137 319", "EE10137319" (with an Estonian prefix), etc. Returns the 8
// digits, or null when the value isn't an 8-digit code.
function parseRegistryCode(raw) {
  const digits = raw.toUpperCase().replace(/^EE/, "").replace(/[\s.\-]/g, "");
  return /^\d{8}$/.test(digits) ? digits : null;
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

// Calls the service. Returns the company whose registry code matches, or null when there's
// no answer to store.
function lookUp(registryCode) {
  const apiResponse = http.send({
    url: `${ARIREGISTER_URL}?q=${encodeURIComponent(registryCode)}`,
    method: "GET",
    headers: [{ Key: "Accept", Value: "application/json" }],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - the e-Business Register is busy, the record will be retried");
  }

  if (status === "OK" && body && Array.isArray(body.data)) {
    // The search also matches names, so pick the company whose registry code is the one we sent.
    for (let i = 0; i < body.data.length; i++) {
      if (body.data[i] && String(body.data[i].reg_code) === registryCode) {
        return body.data[i];
      }
    }
    log(`e-Business Register found no company with registry code ${registryCode}.`);
    return null;
  }

  log(`e-Business Register could not look up ${registryCode}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function run() {
  const registryCode = parseRegistryCode(getValue(REGISTRY_CODE_KEY));
  if (!registryCode) {
    // No valid registry code on this record (this is also what Test connection hits, because
    // it passes placeholder values instead of real ones).
    return [];
  }

  const cacheKey = `ariregister:${registryCode}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let company = cached ? parseJson(String(cached)) : null;

  if (!company) {
    company = lookUp(registryCode);
    if (!company) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(company), CACHE_MILLISECONDS);
    }
  }

  const statusCode = clean(company.status);
  const previousNames = Array.isArray(company.historical_names)
    ? company.historical_names.map((x) => clean(x)).filter((x) => !!x)
    : [];

  const data = {
    "ariregister.registryCode": clean(company.reg_code),
    "ariregister.name": clean(company.name),
    "ariregister.statusCode": statusCode,
    "ariregister.status": statusCode && STATUS_NAMES[statusCode] ? STATUS_NAMES[statusCode] : null,
    "ariregister.legalAddress": clean(company.legal_address),
    "ariregister.postalCode": clean(company.zip_code),
    "ariregister.legalFormCode": clean(company.legal_form),
    "ariregister.previousNames": previousNames.length > 0 ? previousNames.join("; ") : null,
    "ariregister.url": clean(company.url),
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
