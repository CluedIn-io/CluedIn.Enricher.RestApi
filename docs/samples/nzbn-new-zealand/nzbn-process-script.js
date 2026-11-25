// REST API enricher (V2) - New Zealand company lookup with the NZBN API
//
// Looks up a golden record's New Zealand Business Number (NZBN) in the NZBN Register, using
// the Companies Office / MBIE NZBN API v5 (https://portal.api.business.govt.nz/api/nzbn), and
// adds the result to the golden record as nzbn.* properties:
//
//   nzbn.nzbn              13-digit NZBN that was looked up
//   nzbn.name              entity name
//   nzbn.status            entity status, for example Registered or Removed
//   nzbn.entityType        entity type, for example NZ Limited Company
//   nzbn.entityTypeCode    entity type code, for example LTD
//   nzbn.registrationDate  date the entity was registered (YYYY-MM-DD)
//   nzbn.sourceRegister    register that holds the entity, for example COMPANY
//   nzbn.sourceRegisterId  the entity's number in that register (the company number for companies)
//   nzbn.registeredAddress current registered address on one line
//   nzbn.postalAddress     current postal address on one line
//   nzbn.website           first website
//   nzbn.industryCode      first industry classification code (BIC), for example I490008
//   nzbn.industry          first industry classification description
//   nzbn.tradingNames      current trading names, separated by "; "
//
// Needs an NZBN API subscription key, entered in the enricher's API Key field. Subscribe to
// the NZBN API on https://portal.api.business.govt.nz/ to get one.
// See README.md next to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid NZBN, when the NZBN isn't
// found, or when the API can't answer. A subscription key that the API rejects fails the
// record with an error, so the problem is visible on the enricher's Logs tab.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const NZBN_KEY = "organization.nzbn";

// The enricher replaces {APIKey} with the API Key field value (your NZBN API subscription key).
const API_KEY = "{APIKey}";

// Use https://api.business.govt.nz/sandbox/nzbn/v5/entities/ with a sandbox subscription
// key to try the setup against sandbox data first.
const NZBN_URL = "https://api.business.govt.nz/gateway/nzbn/v5/entities/";

// How long to reuse an answer for the same NZBN. 0 turns caching off.
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

// Accepts "9429040402543", "94 2904 0402 543", etc. Returns the 13 digits, or null when the
// number isn't a valid NZBN (starts with 94, GS1 check digit).
function parseNzbn(raw) {
  const digits = raw.replace(/[^0-9]/g, "");
  if (!/^94\d{11}$/.test(digits)) {
    return null;
  }
  let sum = 0;
  for (let i = 0; i < 12; i++) {
    sum += Number(digits.charAt(i)) * (i % 2 === 0 ? 1 : 3);
  }
  return (10 - (sum % 10)) % 10 === Number(digits.charAt(12)) ? digits : null;
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

// "1940-04-26T00:00:00.000+1200" -> "1940-04-26" (the date in New Zealand time)
function dateOnly(value) {
  const text = clean(value);
  return text ? text.substring(0, 10) : null;
}

function currentItems(list) {
  return Array.isArray(list) ? list.filter((x) => x && !clean(x.endDate)) : [];
}

function formatAddress(entity, addressType) {
  const list = entity.addresses && Array.isArray(entity.addresses.addressList) ? entity.addresses.addressList : [];
  const addresses = currentItems(list).filter((x) => x.addressType === addressType);
  if (addresses.length === 0) {
    return null;
  }
  const address = addresses[0];
  const parts = [address.address1, address.address2, address.address3, address.address4, address.postCode]
    .map((x) => clean(x))
    .filter((x) => !!x);
  if (clean(address.countryCode) && address.countryCode !== "NZ") {
    parts.push(address.countryCode);
  }
  return parts.length > 0 ? parts.join(", ") : null;
}

// Calls the NZBN API. Returns the entity, or null when there's no answer to store.
function lookUp(nzbn) {
  const apiResponse = http.send({
    url: NZBN_URL + nzbn,
    method: "GET",
    headers: [
      { Key: "Accept", Value: "application/json" },
      { Key: "Ocp-Apim-Subscription-Key", Value: API_KEY },
    ],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - NZBN API rate limit reached, the record will be retried");
  }

  if (status === "Unauthorized" || status === "Forbidden") {
    throw new Error(`The NZBN API rejected the subscription key in the API Key field (HTTP ${status}). ${String(apiResponse.Content || "").substring(0, 200)}`);
  }

  if (status === "OK" && body && body.nzbn) {
    return body;
  }

  log(`The NZBN API could not look up ${nzbn}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function run() {
  const nzbn = parseNzbn(getValue(NZBN_KEY));
  if (!nzbn) {
    // No valid NZBN on this record (this is also what Test connection hits, because it passes
    // placeholder values instead of real ones).
    return [];
  }

  if (!clean(API_KEY)) {
    log("The NZBN API needs a subscription key. Enter your key in the enricher's API Key field.");
    return [];
  }

  const cacheKey = `nzbn:${nzbn}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let entity = cached ? parseJson(String(cached)) : null;

  if (!entity) {
    entity = lookUp(nzbn);
    if (!entity) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(entity), CACHE_MILLISECONDS);
    }
  }

  const websites = Array.isArray(entity.websites) ? entity.websites.filter((x) => x && clean(x.url)) : [];
  const industries = Array.isArray(entity.industryClassifications)
    ? entity.industryClassifications.filter((x) => x && (clean(x.classificationCode) || clean(x.classificationDescription)))
    : [];
  const tradingNames = currentItems(entity.tradingNames).map((x) => clean(x.name)).filter((x) => !!x);

  const data = {
    "nzbn.nzbn": clean(entity.nzbn),
    "nzbn.name": clean(entity.entityName),
    "nzbn.status": clean(entity.entityStatusDescription),
    "nzbn.entityType": clean(entity.entityTypeDescription),
    "nzbn.entityTypeCode": clean(entity.entityTypeCode),
    "nzbn.registrationDate": dateOnly(entity.registrationDate),
    "nzbn.sourceRegister": clean(entity.sourceRegister),
    "nzbn.sourceRegisterId": clean(entity.sourceRegisterUniqueIdentifier),
    "nzbn.registeredAddress": formatAddress(entity, "REGISTERED"),
    "nzbn.postalAddress": formatAddress(entity, "POSTAL"),
    "nzbn.website": websites.length > 0 ? clean(websites[0].url) : null,
    "nzbn.industryCode": industries.length > 0 ? clean(industries[0].classificationCode) : null,
    "nzbn.industry": industries.length > 0 ? clean(industries[0].classificationDescription) : null,
    "nzbn.tradingNames": tradingNames.length > 0 ? tradingNames.join("; ") : null,
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
