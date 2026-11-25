// REST API enricher (V2) - Saudi commercial registration lookup with Wathq
//
// Looks up a golden record's Saudi commercial registration (CR) number with the Ministry of
// Commerce's Wathq Commercial Registration API (https://developer.wathq.sa/), by using its
// full information operation, and adds the result to the golden record as wathq.* properties:
//
//   wathq.crNumber       CR number that was looked up
//   wathq.name           registered name
//   wathq.entityNumber   CR entity number
//   wathq.mainCrNumber   CR number of the main registration (for a branch)
//   wathq.businessType   business type, for example Limited Liability Company
//   wathq.status         CR status
//   wathq.issueDate      issue date, as returned by Wathq
//   wathq.expiryDate     expiry date, as returned by Wathq
//   wathq.city           location (city) of the registration
//   wathq.address        national address on one line
//   wathq.website        website
//   wathq.email          email address
//   wathq.phone          telephone number
//   wathq.activities     ISIC activities, separated by "; "
//   wathq.paidCapital    paid-up capital
//   wathq.ecommerce      "True" / "False" - registered for e-commerce
//
// Needs a Wathq API key (sent in the apiKey header), entered in the enricher's API Key field.
// Calls are charged per your Wathq plan. See README.md next to this file.
//
// A record gets no result (nothing is stored) when it has no valid CR number, when Wathq has no
// registration with that number, or when the API can't answer. A missing or rejected key fails
// the record with an error that says so.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const CR_NUMBER_KEY = "organization.crNumber";

const WATHQ_URL = "https://api.wathq.sa/commercial-registration/fullinfo/";

// Language of names and descriptions in the answer: en or ar.
const LANGUAGE = "en";

// The {APIKey} placeholder is replaced with the API Key field value.
const API_KEY = "{APIKey}";

// How long to reuse an answer for the same CR number. 0 turns caching off. Every call is
// charged, so a long cache saves money.
const CACHE_MILLISECONDS = 7 * 24 * 60 * 60 * 1000;

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

// Accepts "1010000000", "1010 000 000", "CR 1010000000" and Arabic-Indic digits. Returns the 10
// digits, or null when it isn't a CR number.
function parseCrNumber(raw) {
  const western = raw.replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660));
  const text = western.toUpperCase().replace(/^C\.?R\.?/, "");
  if (!/^[0-9\s.-]+$/.test(text)) {
    return null;
  }
  const digits = text.replace(/[^0-9]/g, "");
  return /^[1-9]\d{9}$/.test(digits) ? digits : null;
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

// Dates are kept as Wathq returns them (Hijri dates such as 1440/05/12 aren't converted). A
// Gregorian timestamp is cut to its date, so the enricher doesn't reformat it.
function dateText(value) {
  const text = clean(value);
  if (!text) {
    return null;
  }
  const iso = /^(\d{4}-\d{2}-\d{2})T/.exec(text);
  return iso ? iso[1] : text;
}

// The English name where Wathq has one, otherwise the Arabic one.
function nameOf(item) {
  if (!item) {
    return null;
  }
  if (typeof item !== "object") {
    return clean(item);
  }
  return clean(item.nameEn) || clean(item.name);
}

function formatAddress(national) {
  if (!national) {
    return null;
  }
  const street = [clean(national.buildingNumber), clean(national.streetName)].filter((x) => !!x).join(" ");
  const district = national.district ? nameOf(national.district) : null;
  const city = [clean(national.city), clean(national.zipcode)].filter((x) => !!x).join(" ");
  const parts = [street, district, city].filter((x) => !!x);
  return parts.length > 0 ? parts.join(", ") : null;
}

function errorText(body) {
  return body ? [clean(body.code), clean(body.message)].filter((x) => !!x).join(" ") : "";
}

// Calls Wathq. Returns the registration, or null when there's no answer to store.
function lookUp(crNumber) {
  const apiResponse = http.send({
    url: `${WATHQ_URL}${crNumber}?language=${encodeURIComponent(LANGUAGE)}`,
    method: "GET",
    headers: [
      { Key: "apiKey", Value: API_KEY },
      { Key: "Accept", Value: "application/json" },
    ],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - the Wathq API is busy or the plan's rate limit was reached, the record will be retried");
  }

  if (status === "Unauthorized" || status === "Forbidden") {
    throw new Error(`The Wathq API rejected the API key (HTTP ${status}: ${errorText(body) || "no details"}). Check the API Key field of the enricher and your Wathq subscription.`);
  }

  if (status === "OK" && body && clean(body.crNumber)) {
    return body;
  }

  // 404 with code 404.2.1 "No Results Found", 400 for invalid input, or a server error.
  log(`Wathq could not look up ${crNumber}. HTTP ${status}. ${errorText(body) || String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function run() {
  const crNumber = parseCrNumber(getValue(CR_NUMBER_KEY));
  if (!crNumber) {
    // No valid CR number on this record (this is also what Test connection hits, because it
    // passes placeholder values instead of real ones).
    return [];
  }

  if (!API_KEY) {
    log("No Wathq API key. Enter it in the API Key field of the enricher.");
    return [];
  }

  const cacheKey = `wathq:${crNumber}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let cr = cached ? parseJson(String(cached)) : null;

  if (!cr) {
    cr = lookUp(crNumber);
    if (!cr) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(cr), CACHE_MILLISECONDS);
    }
  }

  const address = cr.address || {};
  const general = address.general || {};
  const activities = cr.activities && Array.isArray(cr.activities.isic)
    ? cr.activities.isic.map((x) => nameOf(x)).filter((x) => !!x)
    : [];
  const capital = cr.capital || {};

  const data = {
    "wathq.crNumber": clean(cr.crNumber),
    "wathq.name": clean(cr.crName),
    "wathq.entityNumber": clean(cr.crEntityNumber),
    "wathq.mainCrNumber": clean(cr.crMainNumber),
    "wathq.businessType": nameOf(cr.businessType),
    "wathq.status": nameOf(cr.status),
    "wathq.issueDate": dateText(cr.issueDate),
    "wathq.expiryDate": dateText(cr.expiryDate),
    "wathq.city": nameOf(cr.location),
    "wathq.address": formatAddress(address.national),
    "wathq.website": clean(general.website),
    "wathq.email": clean(general.email),
    "wathq.phone": clean(general.telephone1),
    "wathq.activities": activities.length > 0 ? activities.join("; ") : null,
    "wathq.paidCapital": typeof capital.paidAmount === "number" || clean(capital.paidAmount) ? capital.paidAmount : null,
    "wathq.ecommerce": typeof cr.isEcommerce === "boolean" ? cr.isEcommerce : null,
  };

  // Leave out values Wathq didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
