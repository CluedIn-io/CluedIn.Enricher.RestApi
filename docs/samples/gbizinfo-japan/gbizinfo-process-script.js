// REST API enricher (V2) - Japanese company lookup with gBizINFO
//
// Looks up a golden record's 13-digit Japanese corporate number (法人番号) in gBizINFO, the
// Ministry of Economy, Trade and Industry's corporate information service, using its REST API
// (https://info.gbiz.go.jp/), and adds the result to the golden record as gbiz.* properties:
//
//   gbiz.corporateNumber      13-digit corporate number that was looked up
//   gbiz.name                 registered name (Japanese)
//   gbiz.nameEn               English name, when registered
//   gbiz.kana                 name in katakana
//   gbiz.location             head office address
//   gbiz.postalCode           head office postal code
//   gbiz.status               status, when gBizINFO has one (for example 閉鎖 for closed)
//   gbiz.closeDate            date the registration was closed (YYYY-MM-DD)
//   gbiz.closeCause           reason the registration was closed
//   gbiz.representativeName   name of the representative
//   gbiz.capitalStock         capital stock in yen
//   gbiz.employeeNumber       number of employees
//   gbiz.dateOfEstablishment  date of establishment (YYYY-MM-DD)
//   gbiz.foundingYear         year the business was founded
//   gbiz.companyUrl           website
//   gbiz.businessSummary      business summary
//   gbiz.industry             industries, separated by "; "
//   gbiz.updateDate           date gBizINFO last updated the record (YYYY-MM-DD)
//
// Needs a gBizINFO API token in the enricher's API Key field. See README.md next to this file
// for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid corporate number, when
// gBizINFO has no record for it, or when the API can't answer. A missing or rejected API token
// fails the record with an error that says so.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const CORPORATE_NUMBER_KEY = "organization.corporateNumber";

const GBIZINFO_URL = "https://api.info.gbiz.go.jp/hojin/v2/hojin/";

// The enricher replaces the token below with the value of the API Key field.
const API_KEY = "{APIKey}";

// How long to reuse an answer for the same corporate number. 0 turns caching off.
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

// Accepts "7000012050002", "7-0000-1205-0002", "7000 0120 50002", and full-width digits.
// Returns the 13 digits, or null when the number isn't a valid corporate number (the first
// digit is a check digit over the other 12).
function parseCorporateNumber(raw) {
  const halfWidth = raw.replace(/[０-９]/g, (c) => String.fromCharCode(c.charCodeAt(0) - 0xfee0));
  const digits = halfWidth.replace(/[\s\-.]/g, "");
  if (!/^\d{13}$/.test(digits)) {
    return null;
  }
  // Check digit = 9 - (sum of the 12 base digits, weighted 1, 2, 1, 2, ... from the right) mod 9
  let sum = 0;
  for (let n = 1; n <= 12; n++) {
    sum += Number(digits.charAt(13 - n)) * (n % 2 === 1 ? 1 : 2);
  }
  return 9 - (sum % 9) === Number(digits.charAt(0)) ? digits : null;
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

// gBizINFO dates can carry a time and offset (1949-05-24T00:00:00+09:00); keep the date only.
function dateOnly(value) {
  const text = clean(value);
  return text && /^\d{4}-\d{2}-\d{2}/.test(text) ? text.substring(0, 10) : null;
}

function numberOrNull(value) {
  return typeof value === "number" ? value : null;
}

// Calls the API. Returns the corporation, or null when there's no answer to store.
function lookUp(corporateNumber) {
  const apiResponse = http.send({
    url: GBIZINFO_URL + corporateNumber,
    method: "GET",
    headers: [
      { Key: "X-hojinInfo-api-token", Value: API_KEY },
      { Key: "Accept", Value: "application/json" },
    ],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - gBizINFO is busy, the record will be retried");
  }

  if (status === "Unauthorized" || status === "Forbidden") {
    throw new Error(`gBizINFO rejected the API token (HTTP ${status}). Check the API Key field of the enricher.`);
  }

  const infos = body && Array.isArray(body["hojin-infos"]) ? body["hojin-infos"] : [];
  if (status === "OK" && infos.length > 0 && infos[0] && infos[0].corporate_number) {
    return infos[0];
  }

  log(`gBizINFO could not look up ${corporateNumber}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function run() {
  const corporateNumber = parseCorporateNumber(getValue(CORPORATE_NUMBER_KEY));
  if (!corporateNumber) {
    // No valid corporate number on this record (this is also what Test connection hits,
    // because it passes placeholder values instead of real ones).
    return [];
  }

  if (API_KEY.trim() === "") {
    log("gBizINFO needs an API token. Enter it in the API Key field of the enricher.");
    return [];
  }

  const cacheKey = `gbiz:${corporateNumber}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let hojin = cached ? parseJson(String(cached)) : null;

  if (!hojin) {
    hojin = lookUp(corporateNumber);
    if (!hojin) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(hojin), CACHE_MILLISECONDS);
    }
  }

  const industry = Array.isArray(hojin.industry) ? hojin.industry.filter((x) => !!clean(x)) : [];

  const data = {
    "gbiz.corporateNumber": clean(hojin.corporate_number),
    "gbiz.name": clean(hojin.name),
    "gbiz.nameEn": clean(hojin.name_en),
    "gbiz.kana": clean(hojin.kana),
    "gbiz.location": clean(hojin.location),
    "gbiz.postalCode": clean(hojin.postal_code),
    "gbiz.status": clean(hojin.status),
    "gbiz.closeDate": dateOnly(hojin.close_date),
    "gbiz.closeCause": clean(hojin.close_cause),
    "gbiz.representativeName": clean(hojin.representative_name),
    "gbiz.capitalStock": numberOrNull(hojin.capital_stock),
    "gbiz.employeeNumber": numberOrNull(hojin.employee_number),
    "gbiz.dateOfEstablishment": dateOnly(hojin.date_of_establishment),
    "gbiz.foundingYear": numberOrNull(hojin.founding_year),
    "gbiz.companyUrl": clean(hojin.company_url),
    "gbiz.businessSummary": clean(hojin.business_summary),
    "gbiz.industry": industry.length > 0 ? industry.join("; ") : null,
    "gbiz.updateDate": dateOnly(hojin.update_date),
  };

  // Leave out values gBizINFO didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
