// REST API enricher (V2) - Greek company lookup with the GEMI Open Data API
//
// Looks up a golden record's Greek General Commercial Registry (GEMI) number, or its tax number
// (AFM), with the GEMI Open Data API (https://opendata.businessportal.gr/), and adds the result
// to the golden record as gemi.* properties:
//
//   gemi.gemiNumber          GEMI number (Αριθμός ΓΕΜΗ)
//   gemi.afm                 tax number (ΑΦΜ)
//   gemi.name                registered name in Greek
//   gemi.nameEn              registered name in Latin characters
//   gemi.distinctiveTitle    distinctive title(s) in Greek
//   gemi.status              company status (in English when GEMI provides it)
//   gemi.active              "True" / "False" - whether GEMI counts the status as active
//   gemi.legalForm           legal form (in English when GEMI provides it)
//   gemi.gemiOffice          GEMI office that keeps the record
//   gemi.address             street, number, postcode and city on one line
//   gemi.municipality        municipality
//   gemi.prefecture          prefecture
//   gemi.website             website
//   gemi.email               email
//   gemi.incorporationDate   date of incorporation (YYYY-MM-DD)
//   gemi.lastStatusChange    date of the last status change (YYYY-MM-DD)
//   gemi.isBranch            "True" when the record is a branch
//   gemi.mainActivityCode    main activity code (ΚΑΔ)
//   gemi.mainActivity        main activity description
//
// Needs a GEMI Open Data api_key, entered in the enricher's API Key field. See README.md next to
// this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid GEMI number or AFM, when GEMI
// has no company with it, or when the API can't answer. A missing or rejected key fails the
// record with an error that says so.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary keys to read. Add the same keys to the Vocabulary and Properties field.
const GEMI_NUMBER_KEY = "organization.gemiNumber";
// Optional. Used only when the record has no GEMI number.
const AFM_KEY = "organization.afm";

const GEMI_URL = "https://opendata-api.businessportal.gr/api/opendata/v1/companies";

// The {APIKey} placeholder is replaced with the API Key field value (your GEMI api_key).
const API_KEY = "{APIKey}";

// How long to reuse an answer for the same company. 0 turns caching off.
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

// Accepts "000121201000", "121201000", "121 201 000". Returns the number without leading zeros
// (the API takes it as an integer), or null when it isn't a GEMI number.
function parseGemiNumber(raw) {
  if (/[A-Za-z]/.test(raw)) {
    return null;
  }
  const digits = raw.replace(/[^0-9]/g, "");
  if (digits.length < 6 || digits.length > 12) {
    return null;
  }
  const number = digits.replace(/^0+/, "");
  return number.length >= 6 ? number : null;
}

// Accepts "094014201", "EL094014201", "GR 094 014 201" and numbers that lost their leading zero.
// Returns the 9 digits, or null when it isn't a valid AFM (check digit).
function parseAfm(raw) {
  const text = raw.toUpperCase().replace(/^(EL|GR)/, "");
  if (/[A-Z]/.test(text)) {
    return null;
  }
  let digits = text.replace(/[^0-9]/g, "");
  if (digits.length === 8) {
    digits = "0" + digits;
  }
  if (!/^\d{9}$/.test(digits) || /^0+$/.test(digits)) {
    return null;
  }
  let sum = 0;
  for (let i = 0; i < 8; i++) {
    sum += Number(digits.charAt(i)) * Math.pow(2, 8 - i);
  }
  return (sum % 11) % 10 === Number(digits.charAt(8)) ? digits : null;
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

// GEMI dates come as YYYY-MM-DD (optionally with a time) or DD/MM/YYYY. Returns YYYY-MM-DD.
function toDate(value) {
  const text = clean(value);
  if (!text) {
    return null;
  }
  if (/^\d{4}-\d{2}-\d{2}/.test(text)) {
    return text.substring(0, 10);
  }
  const match = /^(\d{1,2})\/(\d{1,2})\/(\d{4})/.exec(text);
  if (match) {
    return `${match[3]}-${("0" + match[2]).slice(-2)}-${("0" + match[1]).slice(-2)}`;
  }
  return null;
}

function joinList(list) {
  const values = (Array.isArray(list) ? list : []).map((x) => clean(x)).filter((x) => !!x);
  return values.length > 0 ? values.join("; ") : null;
}

// English description when there is one, otherwise the Greek one.
function describe(item) {
  return item ? clean(item.descrEn) || clean(item.descr) : null;
}

function mainActivity(activities) {
  const list = Array.isArray(activities) ? activities.filter((x) => x && x.activity && !clean(x.dtTo)) : [];
  for (let i = 0; i < list.length; i++) {
    if (/κύρια|κυρια|main|primary/i.test(String(list[i].type || ""))) {
      return list[i].activity;
    }
  }
  return list.length > 0 ? list[0].activity : null;
}

function formatAddress(company) {
  const street = [clean(company.street), clean(company.streetNumber)].filter((x) => !!x).join(" ");
  const town = [clean(company.zipCode), clean(company.city)].filter((x) => !!x).join(" ");
  const parts = [street, town].filter((x) => !!x);
  return parts.length > 0 ? parts.join(", ") : null;
}

// Calls the API. Returns the parsed body, or null when there's no answer to store.
function callGemi(url, description) {
  const apiResponse = http.send({
    url: url,
    method: "GET",
    headers: [
      { Key: "api_key", Value: API_KEY },
      { Key: "Accept", Value: "application/json" },
    ],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error(`TooManyRequests - the GEMI Open Data API rate limit was reached for ${description}, the record will be retried`);
  }

  if (status === "Unauthorized" || status === "Forbidden") {
    const message = body && body.message ? body.message : String(apiResponse.Content || "").substring(0, 200);
    throw new Error(`The GEMI Open Data API rejected the api_key in the API Key field (HTTP ${status}: ${message}).`);
  }

  if (status === "OK" && body) {
    return body;
  }

  if (status === "NotFound") {
    log(`GEMI has no company for ${description}.`);
  } else {
    log(`GEMI could not look up ${description}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
  }
  return null;
}

function lookUpByGemiNumber(gemiNumber) {
  const company = callGemi(`${GEMI_URL}/${gemiNumber}`, `GEMI number ${gemiNumber}`);
  return company && company.arGemi !== undefined && company.arGemi !== null ? company : null;
}

// The search can return the company and its branches; prefer the company itself.
function lookUpByAfm(afm) {
  const body = callGemi(`${GEMI_URL}?afm=${afm}&resultsSize=10`, `AFM ${afm}`);
  const results = body && Array.isArray(body.searchResults) ? body.searchResults.filter((x) => x && x.afm === afm) : [];
  if (results.length === 0) {
    if (body) {
      log(`GEMI has no company for AFM ${afm}.`);
    }
    return null;
  }
  const head = results.filter((x) => x.isBranch !== true);
  return head.length > 0 ? head[0] : results[0];
}

function run() {
  const gemiNumber = parseGemiNumber(getValue(GEMI_NUMBER_KEY));
  const afm = gemiNumber ? null : parseAfm(getValue(AFM_KEY));
  if (!gemiNumber && !afm) {
    // No valid GEMI number or AFM on this record (this is also what Test connection hits,
    // because it passes placeholder values instead of real ones).
    return [];
  }

  if (!clean(API_KEY)) {
    log("The GEMI Open Data API needs an api_key. Enter it in the enricher's API Key field.");
    return [];
  }

  const cacheKey = gemiNumber ? `gemi:${gemiNumber}` : `gemi:afm:${afm}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let company = cached ? parseJson(String(cached)) : null;

  if (!company) {
    company = gemiNumber ? lookUpByGemiNumber(gemiNumber) : lookUpByAfm(afm);
    if (!company) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(company), CACHE_MILLISECONDS);
    }
  }

  const activity = mainActivity(company.activities);
  const companyStatus = company.status || null;

  const data = {
    "gemi.gemiNumber": company.arGemi !== undefined && company.arGemi !== null ? String(company.arGemi) : null,
    "gemi.afm": clean(company.afm),
    "gemi.name": clean(company.coNameEl),
    "gemi.nameEn": Array.isArray(company.coNamesEn) && company.coNamesEn.length > 0 ? clean(company.coNamesEn[0]) : null,
    "gemi.distinctiveTitle": joinList(company.coTitlesEl),
    "gemi.status": describe(companyStatus),
    "gemi.active": companyStatus && typeof companyStatus.isActive === "boolean" ? companyStatus.isActive : null,
    "gemi.legalForm": describe(company.legalType),
    "gemi.gemiOffice": describe(company.gemiOffice),
    "gemi.address": formatAddress(company),
    "gemi.municipality": describe(company.municipality),
    "gemi.prefecture": describe(company.prefecture),
    "gemi.website": clean(company.url),
    "gemi.email": clean(company.email),
    "gemi.incorporationDate": toDate(company.incorporationDate),
    "gemi.lastStatusChange": toDate(company.lastStatusChange),
    "gemi.isBranch": typeof company.isBranch === "boolean" ? company.isBranch : null,
    "gemi.mainActivityCode": activity ? clean(activity.id) : null,
    "gemi.mainActivity": activity ? clean(activity.descr) : null,
  };

  // Leave out values GEMI didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
