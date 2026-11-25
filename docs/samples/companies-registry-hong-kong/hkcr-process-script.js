// REST API enricher (V2) - Hong Kong company lookup with the Companies Registry
//
// Looks up a golden record's Hong Kong Business Registration (BR) number in the Companies
// Registry's open-data API (https://data.gov.hk/en-data/dataset/hk-cr-crdata-list-addr), and
// adds the result to the golden record as hkcr.* properties. It searches live local companies
// first and, if the number isn't found there, registered non-Hong Kong companies:
//
//   hkcr.brNumber              BR number that was looked up
//   hkcr.name                  English company name (local companies), or corporate name
//                              (non-Hong Kong companies)
//   hkcr.chineseName           Chinese company name (local companies)
//   hkcr.otherName             other corporate name (non-Hong Kong companies)
//   hkcr.approvedName          name approved for carrying on business in Hong Kong
//                              (non-Hong Kong companies)
//   hkcr.companyType           company type, for example Private company limited by shares
//   hkcr.address               registered office address (local companies), or principal place
//                              of business in Hong Kong (non-Hong Kong companies)
//   hkcr.incorporationDate     date of incorporation (local companies, YYYY-MM-DD)
//   hkcr.redomiciliationDate   date of re-domiciliation to Hong Kong (YYYY-MM-DD)
//   hkcr.registrationDate      date of registration (non-Hong Kong companies, YYYY-MM-DD)
//   hkcr.placeOfIncorporation  place of incorporation (non-Hong Kong companies)
//
// No API key is needed. See README.md next to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid BR number, when the number
// isn't a live company in either list, or when the API can't answer.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const BR_NUMBER_KEY = "organization.brNumber";

const LOCAL_COMPANIES_URL = "https://data.cr.gov.hk/cr/api/api/v1/api_builder/json/local/search";
// Registered non-Hong Kong companies. Set to "" to search local companies only.
const NON_HONG_KONG_COMPANIES_URL = "https://data.cr.gov.hk/cr/api/api/v1/api_builder/json/foreign/search";

// The API refuses requests without a User-Agent header (HTTP 403), and the enricher's HTTP
// client doesn't send one by default.
const USER_AGENT = "CluedIn REST API enricher";

// How long to reuse an answer for the same BR number. 0 turns caching off.
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

// Accepts "80029004", "8002 9004", and the full number printed on a BR certificate, such as
// "80029004-000-03-26-A" (only the first 8 characters identify the business). Returns the
// 8-character BR number, or null when the value doesn't look like one.
function parseBrNumber(raw) {
  const first = raw.toUpperCase().split("-")[0].replace(/[^0-9A-Z]/g, "");
  return /^[0-9A-Z]\d{7}$/.test(first) ? first : null;
}

// The API returns the text "NULL" when a value is empty.
function clean(value) {
  if (value === null || value === undefined) {
    return null;
  }
  const text = String(value).trim();
  return text === "" || text.toUpperCase() === "NULL" ? null : text;
}

function parseJson(text) {
  try {
    return JSON.parse(text);
  } catch (e) {
    return null;
  }
}

// "23-03-2026" (DD-MM-YYYY) becomes "2026-03-23".
function toIsoDate(value) {
  const match = /^(\d{2})-(\d{2})-(\d{4})$/.exec(clean(value) || "");
  return match ? `${match[3]}-${match[2]}-${match[1]}` : null;
}

// Searches one list. Returns the matching company, or null when it isn't in the list or the
// API can't answer.
function search(url, brNumber) {
  const apiResponse = http.send({
    // query[0][key1]=Brn&query[0][key2]=equal&query[0][key3]=<BR number>, with the brackets encoded
    url: `${url}?query%5B0%5D%5Bkey1%5D=Brn&query%5B0%5D%5Bkey2%5D=equal&query%5B0%5D%5Bkey3%5D=${encodeURIComponent(brNumber)}`,
    method: "GET",
    headers: [
      { Key: "Accept", Value: "application/json" },
      { Key: "User-Agent", Value: USER_AGENT },
    ],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - the Companies Registry API is busy, the record will be retried");
  }

  if (status === "OK" && Array.isArray(body)) {
    for (let i = 0; i < body.length; i++) {
      if (body[i] && clean(body[i].Brn) === brNumber) {
        return body[i];
      }
    }
    log(`Companies Registry found no company for BR number ${brNumber} at ${url}.`);
    return null;
  }

  // A number that isn't in the list comes back as HTTP 400 with an HTML page, not JSON.
  if (status === "BadRequest") {
    log(`Companies Registry found no company for BR number ${brNumber} at ${url}.`);
    return null;
  }

  log(`Companies Registry could not look up BR number ${brNumber} at ${url}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function lookUp(brNumber) {
  const local = search(LOCAL_COMPANIES_URL, brNumber);
  if (local) {
    return { local: local };
  }
  const nonHongKong = NON_HONG_KONG_COMPANIES_URL ? search(NON_HONG_KONG_COMPANIES_URL, brNumber) : null;
  return nonHongKong ? { nonHongKong: nonHongKong } : null;
}

function run() {
  const brNumber = parseBrNumber(getValue(BR_NUMBER_KEY));
  if (!brNumber) {
    // No valid BR number on this record (this is also what Test connection hits, because it
    // passes placeholder values instead of real ones).
    return [];
  }

  const cacheKey = `hkcr:${brNumber}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let found = cached ? parseJson(String(cached)) : null;

  if (!found) {
    found = lookUp(brNumber);
    if (!found) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(found), CACHE_MILLISECONDS);
    }
  }

  let data;
  if (found.local) {
    const c = found.local;
    data = {
      "hkcr.brNumber": clean(c.Brn),
      "hkcr.name": clean(c.English_Company_Name) || clean(c.Chinese_Company_Name),
      "hkcr.chineseName": clean(c.Chinese_Company_Name),
      "hkcr.companyType": clean(c.Company_Type),
      "hkcr.address": clean(c.Address_of_Registered_Office),
      "hkcr.incorporationDate": toIsoDate(c.Date_of_Incorporation),
      "hkcr.redomiciliationDate": toIsoDate(c["Re-domiciliation_Date"]),
    };
  } else {
    const c = found.nonHongKong;
    data = {
      "hkcr.brNumber": clean(c.Brn),
      "hkcr.name": clean(c.Corporate_Name) || clean(c.Other_Corporate_Name_s),
      "hkcr.otherName": clean(c.Other_Corporate_Name_s),
      "hkcr.approvedName": clean(c["Approved_name_for_carrying_on_business_in_H.K._Corporate"])
        || clean(c["Approved_name_for_carrying_on_business_in_H.K._Other_Corporate"]),
      "hkcr.companyType": clean(c.Company_Type),
      "hkcr.address": clean(c["Principal_Place_of_Business_in_H.K."]),
      "hkcr.registrationDate": toIsoDate(c.Date_of_Registration),
      "hkcr.placeOfIncorporation": clean(c.Place_of_Incorporation),
    };
  }

  // Leave out values the register didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
