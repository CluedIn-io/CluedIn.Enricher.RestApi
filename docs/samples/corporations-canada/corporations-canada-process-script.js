// REST API enricher (V2) - Canadian federal corporation lookup with Corporations Canada
//
// Looks up a golden record's federal corporation number (or, if it has none, its 9-digit
// business number) in the Corporations Canada Federal Corporation API
// (https://api.ised-isde.canada.ca/en/docs?api=corporations), and adds the result to the
// golden record as corporationscanada.* properties:
//
//   corporationscanada.corporationNumber  corporation number, as returned (for example 1007)
//   corporationscanada.name               current corporation name
//   corporationscanada.status             status, for example Active or Dissolved
//   corporationscanada.act                act the corporation is governed by
//   corporationscanada.businessNumber     9-digit business number (BN9) from the CRA
//   corporationscanada.address            registered office address on one line
//   corporationscanada.city               city of the registered office
//   corporationscanada.province           province code, for example BC
//   corporationscanada.postalCode         postal code
//   corporationscanada.incorporationDate  date of incorporation (YYYY-MM-DD)
//   corporationscanada.lastAnnualReturn   most recent year an annual return was filed
//
// Needs an ISED API key, entered in the enricher's API Key field. Sign in to the ISED API
// store (https://api.ised-isde.canada.ca/) and subscribe to the Corporations API to get one.
// See README.md next to this file for the enricher settings.
//
// Only federal corporations are covered (not corporations registered only with a province or
// territory). A record gets no result (nothing is stored) when it has no valid number, when
// the corporation isn't found, or when the API can't answer. An API key that the API rejects
// fails the record with an error, so the problem is visible on the enricher's Logs tab.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary keys to read. Add the same keys to the Vocabulary and Properties field.
const CORPORATION_NUMBER_KEY = "organization.canadaCorporationNumber";
// Optional. Only used when the record has no corporation number. A 9-digit business number,
// or a 15-character program account number such as 106679285RC0001.
const BUSINESS_NUMBER_KEY = "organization.canadaBusinessNumber";

// The enricher replaces {APIKey} with the API Key field value (your ISED API key).
const API_KEY = "{APIKey}";

const CORPORATIONS_URL = "https://apigateway-passerelledapi.ised-isde.canada.ca/corporations/api/v1/corporations/";
// eng for English, fra for French
const LANGUAGE = "eng";

// How long to reuse an answer for the same number. 0 turns caching off.
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

// Accepts "000100-7", "1007", "1234567", etc. Returns the number without leading zeros, or null.
function parseCorporationNumber(raw) {
  const text = raw.replace(/[\s-]/g, "");
  if (!/^\d{1,7}$/.test(text)) {
    return null;
  }
  const number = text.replace(/^0+/, "");
  return number === "" ? null : number;
}

// Accepts "106679285", "106 679 285", "106679285RC0001", etc. Returns the 9-digit business
// number, or null when it isn't valid (Luhn check digit).
function parseBusinessNumber(raw) {
  const text = raw.toUpperCase().replace(/[\s-]/g, "");
  const match = /^(\d{9})([A-Z]{2}\d{4})?$/.exec(text);
  if (!match) {
    return null;
  }
  const digits = match[1];
  let sum = 0;
  for (let i = 0; i < 9; i++) {
    let digit = Number(digits.charAt(8 - i));
    if (i % 2 === 1) {
      digit *= 2;
      if (digit > 9) {
        digit -= 9;
      }
    }
    sum += digit;
  }
  return sum % 10 === 0 ? digits : null;
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

function dateOnly(value) {
  const text = clean(value);
  return text ? text.substring(0, 10) : null;
}

function currentName(corporation) {
  const names = Array.isArray(corporation.corporationNames) ? corporation.corporationNames : [];
  let fallback = null;
  for (let i = 0; i < names.length; i++) {
    const name = names[i] && names[i].CorporationName;
    if (name && name.current === true && clean(name.name)) {
      if (String(name.nameType || "").toLowerCase() === "primary") {
        return clean(name.name);
      }
      fallback = fallback || clean(name.name);
    }
  }
  return fallback;
}

function firstAddress(corporation) {
  const addresses = Array.isArray(corporation.adresses) ? corporation.adresses : [];
  for (let i = 0; i < addresses.length; i++) {
    if (addresses[i] && addresses[i].address) {
      return addresses[i].address;
    }
  }
  return null;
}

function formatAddress(address) {
  if (!address) {
    return null;
  }
  const lines = Array.isArray(address.addressLine) ? address.addressLine.map((x) => clean(x)).filter((x) => !!x) : [];
  const parts = lines.concat([clean(address.city), clean(address.provinceCode), clean(address.postalCode)].filter((x) => !!x));
  if (clean(address.countryCode) && address.countryCode !== "CA") {
    parts.push(address.countryCode);
  }
  return parts.length > 0 ? parts.join(", ") : null;
}

function incorporationDate(corporation) {
  const activities = Array.isArray(corporation.activities) ? corporation.activities : [];
  for (let i = 0; i < activities.length; i++) {
    const activity = activities[i] && activities[i].activity;
    if (activity && String(activity.activity || "").trim().toLowerCase() === "incorporation") {
      return dateOnly(activity.date);
    }
  }
  return null;
}

function lastAnnualReturn(corporation) {
  const returns = Array.isArray(corporation.annualReturns) ? corporation.annualReturns : [];
  let latest = null;
  for (let i = 0; i < returns.length; i++) {
    const year = returns[i] && returns[i].annualReturn ? clean(returns[i].annualReturn.yearOfFiling) : null;
    if (year && (!latest || year > latest)) {
      latest = year;
    }
  }
  return latest;
}

// Calls the Corporations API. Returns the corporation, or null when there's no answer to store.
function lookUp(number) {
  const apiResponse = http.send({
    url: `${CORPORATIONS_URL}${number}.json?lang=${LANGUAGE}`,
    method: "GET",
    headers: [
      { Key: "Accept", Value: "application/json" },
      { Key: "user-key", Value: API_KEY },
    ],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - Corporations Canada API rate limit reached, the record will be retried");
  }

  if (status === "Unauthorized" || status === "Forbidden") {
    throw new Error(`The Corporations Canada API rejected the API key in the API Key field (HTTP ${status}). ${String(apiResponse.Content || "").substring(0, 200)}`);
  }

  // A corporation that isn't found still returns OK, with messages instead of a corporation:
  // ["could not find corporation 123", "Corporation 123 est inconnu."]
  if (status === "OK" && Array.isArray(body)) {
    for (let i = 0; i < body.length; i++) {
      if (body[i] && typeof body[i] === "object" && clean(body[i].corporationId)) {
        return body[i];
      }
    }
  }

  log(`Corporations Canada could not look up ${number}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function run() {
  const number = parseCorporationNumber(getValue(CORPORATION_NUMBER_KEY))
    || parseBusinessNumber(getValue(BUSINESS_NUMBER_KEY));
  if (!number) {
    // No valid number on this record (this is also what Test connection hits, because it
    // passes placeholder values instead of real ones).
    return [];
  }

  if (!clean(API_KEY)) {
    log("The Corporations Canada API needs an API key. Enter your ISED API key in the enricher's API Key field.");
    return [];
  }

  const cacheKey = `corporationscanada:${number}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let corporation = cached ? parseJson(String(cached)) : null;

  if (!corporation) {
    corporation = lookUp(number);
    if (!corporation) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(corporation), CACHE_MILLISECONDS);
    }
  }

  const address = firstAddress(corporation);
  const businessNumbers = corporation.businessNumbers || {};

  const data = {
    "corporationscanada.corporationNumber": clean(corporation.corporationId),
    "corporationscanada.name": currentName(corporation),
    "corporationscanada.status": clean(corporation.status),
    "corporationscanada.act": clean(corporation.act),
    "corporationscanada.businessNumber": clean(businessNumbers.businessNumber),
    "corporationscanada.address": formatAddress(address),
    "corporationscanada.city": address ? clean(address.city) : null,
    "corporationscanada.province": address ? clean(address.provinceCode) : null,
    "corporationscanada.postalCode": address ? clean(address.postalCode) : null,
    "corporationscanada.incorporationDate": incorporationDate(corporation),
    "corporationscanada.lastAnnualReturn": lastAnnualReturn(corporation),
  };

  // Leave out values the API didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
