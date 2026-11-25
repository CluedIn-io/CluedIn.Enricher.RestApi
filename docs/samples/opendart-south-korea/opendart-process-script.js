// REST API enricher (V2) - South Korean company lookup with OpenDART
//
// Looks up a golden record's DART corporation code in the Financial Supervisory Service's
// OpenDART company overview API (https://opendart.fss.or.kr/), and adds the result to the
// golden record as dart.* properties:
//
//   dart.corpCode                     8-digit DART corporation code that was looked up
//   dart.name                         registered name (Korean)
//   dart.nameEn                       English name
//   dart.stockName                    stock name (short name)
//   dart.stockCode                    6-digit stock code, for listed companies
//   dart.ceoName                      name of the CEO
//   dart.corporationClass             market: KOSPI, KOSDAQ, KONEX or Other
//   dart.corporateRegistrationNumber  13-digit corporate registration number (법인등록번호)
//   dart.businessRegistrationNumber   10-digit business registration number (사업자등록번호)
//   dart.address                      address
//   dart.website                      website
//   dart.irWebsite                    investor relations website
//   dart.phone                        phone number
//   dart.fax                          fax number
//   dart.industryCode                 industry code (KSIC)
//   dart.establishmentDate            date of establishment (YYYY-MM-DD)
//   dart.fiscalYearEndMonth           month the fiscal year ends, for example 12
//
// Needs an OpenDART API key (crtfc_key) in the enricher's API Key field. See README.md next
// to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid DART corporation code, when
// OpenDART has no data for it, or when the API can't answer. A missing or rejected API key
// fails the record with an error that says so.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const CORP_CODE_KEY = "organization.dartCorpCode";

const OPENDART_URL = "https://opendart.fss.or.kr/api/company.json";

// The enricher replaces the token below with the value of the API Key field.
const API_KEY = "{APIKey}";

// How long to reuse an answer for the same corporation code. 0 turns caching off.
const CACHE_MILLISECONDS = 24 * 60 * 60 * 1000;

// ---- Script ---------------------------------------------------------------------------

// OpenDART status codes. "000" is success.
const NO_DATA_STATUS = "013";
// Request limit exceeded. Throwing an error that mentions TooManyRequests makes the enricher
// retry the record instead of skipping it.
const RETRY_STATUSES = ["020"];
// Problems with the API key: not registered, not usable, IP address not allowed, expired.
const KEY_STATUSES = ["010", "011", "012", "901"];

const CORPORATION_CLASSES = { Y: "KOSPI", K: "KOSDAQ", N: "KONEX", E: "Other" };

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

// Accepts "00126380" and "0012 6380". Returns the 8 digits, or null when the value isn't a
// corporation code. A shorter number isn't padded with zeros: a 6-digit value is more likely
// to be a stock code, which would look up a different company.
function parseCorpCode(raw) {
  const digits = raw.replace(/[\s\-]/g, "");
  return /^\d{8}$/.test(digits) && !/^0+$/.test(digits) ? digits : null;
}

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

// OpenDART dates are YYYYMMDD.
function toIsoDate(value) {
  const text = clean(value);
  return text && /^\d{8}$/.test(text)
    ? `${text.substring(0, 4)}-${text.substring(4, 6)}-${text.substring(6, 8)}`
    : null;
}

// Calls the API. Returns the company overview, or null when there's no answer to store.
function lookUp(corpCode) {
  const apiResponse = http.send({
    url: `${OPENDART_URL}?crtfc_key=${encodeURIComponent(API_KEY)}&corp_code=${corpCode}`,
    method: "GET",
    headers: [
      { Key: "Accept", Value: "application/json" },
      // OpenDART redirects requests without a User-Agent to an HTML error page.
      { Key: "User-Agent", Value: "CluedIn REST API enricher" },
    ],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);
  const dartStatus = body && body.status !== undefined && body.status !== null ? String(body.status) : "";
  const message = body ? String(body.message || "") : "";

  if (status === "TooManyRequests" || RETRY_STATUSES.indexOf(dartStatus) >= 0) {
    throw new Error(`TooManyRequests - OpenDART request limit reached (${dartStatus || status} ${message}), the record will be retried`);
  }

  if (KEY_STATUSES.indexOf(dartStatus) >= 0 || status === "Unauthorized" || status === "Forbidden") {
    throw new Error(`OpenDART rejected the API key (status ${dartStatus || status}: ${message}). Check the API Key field of the enricher.`);
  }

  if (status === "OK" && dartStatus === "000" && body.corp_code) {
    return body;
  }

  if (dartStatus === NO_DATA_STATUS) {
    log(`OpenDART has no company for corporation code ${corpCode}.`);
  } else {
    // For example 100 (invalid parameter) or 800 (system maintenance).
    log(`OpenDART could not look up ${corpCode}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
  }
  return null;
}

function run() {
  const corpCode = parseCorpCode(getValue(CORP_CODE_KEY));
  if (!corpCode) {
    // No valid corporation code on this record (this is also what Test connection hits,
    // because it passes placeholder values instead of real ones).
    return [];
  }

  if (API_KEY.trim() === "") {
    log("OpenDART needs an API key. Enter it in the API Key field of the enricher.");
    return [];
  }

  const cacheKey = `dart:${corpCode}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let company = cached ? parseJson(String(cached)) : null;

  if (!company) {
    company = lookUp(corpCode);
    if (!company) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(company), CACHE_MILLISECONDS);
    }
  }

  const data = {
    "dart.corpCode": clean(company.corp_code),
    "dart.name": clean(company.corp_name),
    "dart.nameEn": clean(company.corp_name_eng),
    "dart.stockName": clean(company.stock_name),
    "dart.stockCode": clean(company.stock_code),
    "dart.ceoName": clean(company.ceo_nm),
    "dart.corporationClass": CORPORATION_CLASSES[clean(company.corp_cls)] || clean(company.corp_cls),
    "dart.corporateRegistrationNumber": clean(company.jurir_no),
    "dart.businessRegistrationNumber": clean(company.bizr_no),
    "dart.address": clean(company.adres),
    "dart.website": clean(company.hm_url),
    "dart.irWebsite": clean(company.ir_url),
    "dart.phone": clean(company.phn_no),
    "dart.fax": clean(company.fax_no),
    "dart.industryCode": clean(company.induty_code),
    "dart.establishmentDate": toIsoDate(company.est_dt),
    "dart.fiscalYearEndMonth": clean(company.acc_mt),
  };

  // Leave out values OpenDART didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null || data[key] === undefined) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
