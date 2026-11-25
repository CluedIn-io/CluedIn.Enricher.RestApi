// REST API enricher (V2) - Maltese company lookup with the Malta Business Registry API
//
// TEMPLATE: the Malta Business Registry (MBR) gives the endpoint URL, the authentication header
// and the response format to subscribers of its API packages; they aren't published. Fill in
// MBR_URL and API_KEY_HEADER below from the documentation MBR sends you, and check FIELDS against
// it, before you use this script.
//
// Looks up a golden record's Maltese company registration number with the MBR "Basic Company
// Details" API package (https://mbr.mt/), and adds the result to the golden record as mbr.*
// properties:
//
//   mbr.registrationNumber  registration number, for example C 12345
//   mbr.name                registered company name
//   mbr.registrationDate    date the company was registered
//   mbr.registeredAddress   registered office address
//   mbr.state               company state, for example Active
//
// Needs an MBR API subscription key, entered in the enricher's API Key field. See README.md next
// to this file.
//
// A record gets no result (nothing is stored) when it has no valid registration number, when MBR
// has no company with that number, when the script isn't configured yet, or when the API can't
// answer. A rejected key fails the record with an error that says so.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const REGISTRATION_NUMBER_KEY = "organization.registrationNumber";

// The Basic Company Details endpoint from MBR's API documentation. {registrationNumber} is
// replaced with the URL-encoded number (for example C%2012345); without it, the number is added
// to the end of the URL.
const MBR_URL = "";

// The request header MBR's documentation says carries your key. The key itself goes in the
// enricher's API Key field.
const API_KEY_HEADER = "";

// Prefix used when a registration number has digits only. C is for companies.
const DEFAULT_PREFIX = "C";

// The enricher replaces {APIKey} with the API Key field value.
const API_KEY = "{APIKey}";

// How long to reuse an answer for the same company. 0 turns caching off.
const CACHE_MILLISECONDS = 7 * 24 * 60 * 60 * 1000;

// Response fields to read for each property. The first name found (ignoring case) is used.
// Replace them with the names in MBR's documentation if they differ.
const FIELDS = {
  "mbr.registrationNumber": ["registrationNumber", "companyRegistrationNumber", "regNo", "companyNumber"],
  "mbr.name": ["companyName", "name"],
  "mbr.registrationDate": ["registrationDate", "dateOfRegistration", "incorporationDate"],
  "mbr.registeredAddress": ["registeredAddress", "registeredOffice", "address"],
  "mbr.state": ["state", "status", "companyState", "companyStatus"],
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

// Accepts "C 12345", "c12345", "C-12345", "OC 1234", "P 123" and "12345" (DEFAULT_PREFIX is added).
// Returns "PREFIX DIGITS", or null when it isn't an MBR registration number.
function parseRegistrationNumber(raw) {
  const text = raw.toUpperCase().replace(/[\s.-]+/g, " ").trim();
  const match = /^([A-Z]{1,3}) ?(\d{1,6})$/.exec(text) || /^()(\d{1,6})$/.exec(text);
  if (!match) {
    return null;
  }
  const prefix = match[1] || DEFAULT_PREFIX;
  return `${prefix} ${match[2]}`;
}

function clean(value) {
  if (value === null || value === undefined || typeof value === "object") {
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

function dateText(value) {
  const text = clean(value);
  if (!text) {
    return null;
  }
  const iso = /^(\d{4}-\d{2}-\d{2})T/.exec(text);
  return iso ? iso[1] : text;
}

// The company object in the answer: the body itself, or its data/result/company property, or
// the first item of an array.
function companyOf(body) {
  let item = body;
  for (let i = 0; i < 3 && item; i++) {
    if (Array.isArray(item)) {
      item = item.length > 0 ? item[0] : null;
    } else if (typeof item === "object" && (item.data || item.result || item.company)) {
      item = item.data || item.result || item.company;
    } else {
      break;
    }
  }
  return item && typeof item === "object" && !Array.isArray(item) ? item : null;
}

function readField(company, names) {
  const keys = Object.keys(company);
  for (let i = 0; i < names.length; i++) {
    for (let j = 0; j < keys.length; j++) {
      if (keys[j].toLowerCase() === names[i].toLowerCase()) {
        const value = company[keys[j]];
        if (value && typeof value === "object" && !Array.isArray(value)) {
          // An address object: join its text values.
          const parts = Object.keys(value).map((k) => clean(value[k])).filter((x) => !!x);
          return parts.length > 0 ? parts.join(", ") : null;
        }
        return clean(value);
      }
    }
  }
  return null;
}

// Calls the MBR API. Returns the company, or null when there's no answer to store.
function lookUp(registrationNumber) {
  const encoded = encodeURIComponent(registrationNumber);
  const url = MBR_URL.indexOf("{registrationNumber}") >= 0
    ? MBR_URL.split("{registrationNumber}").join(encoded)
    : MBR_URL + encoded;

  const apiResponse = http.send({
    url: url,
    method: "GET",
    headers: [
      { Key: API_KEY_HEADER, Value: API_KEY },
      { Key: "Accept", Value: "application/json" },
      { Key: "User-Agent", Value: "CluedIn REST API enricher" },
    ],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - the MBR API is busy, the record will be retried");
  }

  if (status === "Unauthorized" || status === "Forbidden") {
    throw new Error(`The MBR API rejected the API key (HTTP ${status}). Check the API Key field of the enricher, and API_KEY_HEADER in the script.`);
  }

  const company = status === "OK" ? companyOf(body) : null;
  if (company && readField(company, FIELDS["mbr.name"])) {
    return company;
  }

  log(`MBR could not look up ${registrationNumber}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function run() {
  const registrationNumber = parseRegistrationNumber(getValue(REGISTRATION_NUMBER_KEY));
  if (!registrationNumber) {
    // No valid registration number on this record (this is also what Test connection hits,
    // because it passes placeholder values instead of real ones).
    return [];
  }

  if (!MBR_URL || !API_KEY_HEADER) {
    log("The MBR script isn't configured. Set MBR_URL and API_KEY_HEADER from MBR's API documentation.");
    return [];
  }

  if (!API_KEY) {
    log("No MBR API key. Enter it in the API Key field of the enricher.");
    return [];
  }

  const cacheKey = `mbr:${registrationNumber}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let company = cached ? parseJson(String(cached)) : null;

  if (!company) {
    company = lookUp(registrationNumber);
    if (!company) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(company), CACHE_MILLISECONDS);
    }
  }

  const data = {};
  Object.keys(FIELDS).forEach((property) => {
    const value = readField(company, FIELDS[property]);
    if (value !== null) {
      data[property] = property === "mbr.registrationDate" ? dateText(value) : value;
    }
  });
  if (!data["mbr.registrationNumber"]) {
    data["mbr.registrationNumber"] = registrationNumber;
  }

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
