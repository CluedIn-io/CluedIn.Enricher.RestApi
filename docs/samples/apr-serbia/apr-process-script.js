// REST API enricher (V2) - Serbian company lookup with the APR Companies Open Data API
//
// Looks up a golden record's Serbian registration number (matični broj) in the Business
// Registers Agency's (APR) Register of Companies, by using APR's open data API
// (https://openapi.apr.gov.rs/api/opendata/companies), and adds the result to the golden
// record as apr.* properties:
//
//   apr.registrationNumber  8-digit registration number (matični broj)
//   apr.name                registered business name (poslovno ime)
//   apr.status              status, for example Активан (active) or У ликвидацији (in liquidation)
//   apr.legalForm           legal form
//   apr.foundationDate      date of foundation (YYYY-MM-DD)
//   apr.municipality        municipality of the registered seat
//   apr.municipalityCode    municipality code
//   apr.activityCode        registered activity code
//   apr.dataDate            date of the APR data the answer comes from (YYYY-MM-DD)
//
// No API key is needed. See README.md next to this file for the enricher settings.
//
// The API has no per-company lookup: it returns the whole register (about 130,000 companies,
// roughly 60 MB) in one response, updated monthly. The script downloads it once, keeps it in the
// enricher's cache for CACHE_MILLISECONDS, and finds each company in the cached copy. The first
// record after the cache expires waits for the download (usually 20 to 40 seconds); records that
// arrive meanwhile are retried until the download has finished.
//
// A record gets no result (nothing is stored) when it has no valid registration number or when
// the company isn't in the open data. The open data only has companies that are active, in
// liquidation or in bankruptcy.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const REGISTRATION_NUMBER_KEY = "organization.registrationNumber";

const APR_URL = "https://openapi.apr.gov.rs/api/opendata/companies";

// How long to keep the downloaded register. APR updates it monthly. Must be more than 0.
const CACHE_MILLISECONDS = 24 * 60 * 60 * 1000;

// ---- Script ---------------------------------------------------------------------------

const CACHE_KEY = "apr:companies";

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

// Accepts "07042701", "0704 2701", "MB 07042701" and numbers that lost their leading zero.
// Returns the 8 digits, or null when it isn't a registration number.
function parseRegistrationNumber(raw) {
  const text = raw.toUpperCase().replace(/^MB/, "");
  if (/[A-Z]/.test(text)) {
    return null;
  }
  const digits = text.replace(/[^0-9]/g, "");
  if (digits.length === 7) {
    return "0" + digits;
  }
  return digits.length === 8 && !/^0+$/.test(digits) ? digits : null;
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

function dateOnly(value) {
  const text = clean(value);
  return text && /^\d{4}-\d{2}-\d{2}/.test(text) ? text.substring(0, 10) : null;
}

// Downloads the whole register. Returns its text; throws when APR can't provide it.
function download() {
  const apiResponse = http.send({
    url: APR_URL,
    method: "GET",
    headers: [
      { Key: "Accept", Value: "application/json" },
      { Key: "User-Agent", Value: "CluedIn REST API enricher" },
    ],
  });

  const status = String(apiResponse.HttpStatus || "");
  const content = String(apiResponse.Content || "");
  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - APR is busy, the record will be retried");
  }
  if (status !== "OK" || content.indexOf('"Podaci"') < 0) {
    throw new Error(`APR could not provide the register of companies. HTTP ${status}. ${content.substring(0, 200)}`);
  }
  return content;
}

// Finds one company in the register's text without parsing all of it: each company is a flat
// object keyed by its registration number, for example "07042701": { "PoslovnoIme": ... }.
function findCompany(text, registrationNumber) {
  const marker = `"${registrationNumber}"`;
  let at = text.indexOf(marker);
  while (at >= 0) {
    const colon = text.indexOf(":", at + marker.length);
    const start = text.indexOf("{", at + marker.length);
    if (colon >= 0 && start > colon && text.substring(at + marker.length, colon).trim() === "" && text.substring(colon + 1, start).trim() === "") {
      // A name could contain "}", so try the next few closing braces.
      let end = text.indexOf("}", start);
      for (let attempt = 0; attempt < 5 && end >= 0; attempt++) {
        const company = parseJson(text.substring(start, end + 1));
        if (company) {
          return company;
        }
        end = text.indexOf("}", end + 1);
      }
      return null;
    }
    at = text.indexOf(marker, at + marker.length);
  }
  return null;
}

function run() {
  const registrationNumber = parseRegistrationNumber(getValue(REGISTRATION_NUMBER_KEY));
  if (!registrationNumber) {
    // No valid registration number on this record (this is also what Test connection hits,
    // because it passes placeholder values instead of real ones).
    return [];
  }

  // Only one download at a time: other records wait for it (cluster-wide lock). If the wait
  // runs out, the record is retried, by when the register is usually in the cache.
  const cached = cache.getOrSetFactory(CACHE_KEY, () => download(), CACHE_MILLISECONDS);
  if (cached === null || cached === undefined) {
    throw new Error("TooManyRequests - the APR register of companies is still downloading, the record will be retried");
  }

  const text = String(cached);
  const company = findCompany(text, registrationNumber);
  if (!company) {
    log(`APR's open data has no company with registration number ${registrationNumber}. It only has companies that are active, in liquidation or in bankruptcy.`);
    return [];
  }

  const dataDate = /"DatumPreseka"\s*:\s*"([^"]+)"/.exec(text.substring(0, 300));

  const data = {
    "apr.registrationNumber": registrationNumber,
    "apr.name": clean(company.PoslovnoIme),
    "apr.status": clean(company.NazivStatus),
    "apr.legalForm": clean(company.NazivPravneForme),
    "apr.foundationDate": dateOnly(company.DatumOsnivanja),
    "apr.municipality": clean(company.NazivOpstine),
    "apr.municipalityCode": clean(company.SifraOpstine),
    "apr.activityCode": clean(company.SifraDelatnosti),
    "apr.dataDate": dataDate ? dateOnly(dataDate[1]) : null,
  };

  // Leave out values APR didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
