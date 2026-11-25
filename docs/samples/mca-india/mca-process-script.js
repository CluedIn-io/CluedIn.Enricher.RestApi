// REST API enricher (V2) - Indian company lookup with MCA company master data
//
// Looks up a golden record's Corporate Identification Number (CIN) in the Ministry of Corporate
// Affairs (MCA) company master data, published as the "Registrars of Companies (RoC)-wise
// Company Master Data" resource on India's Open Government Data platform (https://data.gov.in/),
// and adds the result to the golden record as mca.* properties:
//
//   mca.cin                      21-character CIN that was looked up
//   mca.name                     registered company name
//   mca.status                   company status, for example ACTIVE or STRIKE OFF
//   mca.class                    company class, for example Private or Public
//   mca.category                 company category, for example Company limited by Shares
//   mca.subCategory              company subcategory, for example Non-govt company
//   mca.rocCode                  Registrar of Companies, for example RoC-Mumbai
//   mca.registrationDate         date of registration (YYYY-MM-DD)
//   mca.registeredOfficeAddress  registered office address
//   mca.stateCode                state of the registered office
//   mca.authorizedCapital        authorized capital in rupees
//   mca.paidUpCapital            paid-up capital in rupees
//   mca.listingStatus            listing status, for example Listed or Unlisted
//   mca.industrialClassification industrial activity (NIC description)
//   mca.nicCode                  NIC industry code
//
// Needs a data.gov.in API key in the enricher's API Key field. See README.md next to this
// file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid CIN, when the dataset has
// no company with that CIN, or when the API can't answer. A missing or rejected API key fails
// the record with an error that says so.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const CIN_KEY = "organization.cin";

// data.gov.in resource for the MCA company master data.
const MCA_URL = "https://api.data.gov.in/resource/4dbe5667-7b6b-41d7-82af-211562424d9a";

// The enricher replaces the token below with the value of the API Key field.
const API_KEY = "{APIKey}";

// How long to reuse an answer for the same CIN. 0 turns caching off.
const CACHE_MILLISECONDS = 24 * 60 * 60 * 1000;

// Which dataset field each property comes from. Field names are matched without regard to
// case. If data.gov.in renames a field, change it here.
const CIN_FIELD = "CIN";
const FIELDS = {
  "mca.cin": "CIN",
  "mca.name": "CompanyName",
  "mca.status": "CompanyStatus",
  "mca.class": "CompanyClass",
  "mca.category": "CompanyCategory",
  "mca.subCategory": "CompanySubCategory",
  "mca.rocCode": "CompanyROCcode",
  "mca.registrationDate": "CompanyRegistrationdate_date",
  "mca.registeredOfficeAddress": "Registered_Office_Address",
  "mca.stateCode": "CompanyStateCode",
  "mca.authorizedCapital": "AuthorizedCapital",
  "mca.paidUpCapital": "PaidupCapital",
  "mca.listingStatus": "Listingstatus",
  "mca.industrialClassification": "CompanyIndustrialClassification",
  "mca.nicCode": "nic_code",
};
const DATE_PROPERTIES = ["mca.registrationDate"];

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

// Accepts "L17110MH1973PLC019786", "l17110mh1973plc019786" and "L 17110 MH 1973 PLC 019786".
// Returns the CIN, or null when the value isn't a CIN: listing status (L or U), 5-digit
// industry code, 2-letter state, year, 3-letter ownership code and 6-digit registration number.
function parseCin(raw) {
  const cin = raw.toUpperCase().replace(/[\s\-.]/g, "");
  return /^[LU]\d{5}[A-Z]{2}\d{4}[A-Z]{3}\d{6}$/.test(cin) ? cin : null;
}

// The dataset uses "NA", "-" and similar for values it doesn't have.
function clean(value) {
  if (value === null || value === undefined) {
    return null;
  }
  const text = String(value).replace(/\s+/g, " ").trim();
  return text === "" || /^(-+|NA|N\/A|NULL|NIL)$/i.test(text) ? null : text;
}

function parseJson(text) {
  try {
    return JSON.parse(text);
  } catch (e) {
    return null;
  }
}

// Reads a field from a record without regard to the case of the field name.
function field(record, name) {
  if (Object.prototype.hasOwnProperty.call(record, name)) {
    return record[name];
  }
  const lower = name.toLowerCase();
  const keys = Object.keys(record);
  for (let i = 0; i < keys.length; i++) {
    if (keys[i].toLowerCase() === lower) {
      return record[keys[i]];
    }
  }
  return null;
}

// Accepts YYYY-MM-DD (with or without a time), DD-MM-YYYY and DD/MM/YYYY. Returns YYYY-MM-DD.
function toIsoDate(value) {
  const text = clean(value);
  if (!text) {
    return null;
  }
  if (/^\d{4}-\d{2}-\d{2}/.test(text)) {
    return text.substring(0, 10);
  }
  const match = /^(\d{1,2})[-\/](\d{1,2})[-\/](\d{4})$/.exec(text);
  if (match) {
    return `${match[3]}-${("0" + match[2]).slice(-2)}-${("0" + match[1]).slice(-2)}`;
  }
  return null;
}

// Calls the API. Returns the company record, or null when there's no answer to store.
function lookUp(cin) {
  const apiResponse = http.send({
    url: `${MCA_URL}?api-key=${encodeURIComponent(API_KEY)}&format=json&limit=1&filters%5B${CIN_FIELD}%5D=${cin}`,
    method: "GET",
    headers: [{ Key: "Accept", Value: "application/json" }],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);
  const failed = status !== "OK" || (body && (body.status === "error" || !!body.error));
  const error = body && failed ? String(body.error || body.message || "") : "";

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - data.gov.in rate limit reached, the record will be retried");
  }

  if (status === "Unauthorized" || status === "Forbidden" || /key/i.test(error)) {
    throw new Error(`data.gov.in rejected the API key (HTTP ${status}${error ? `: ${error}` : ""}). Check the API Key field of the enricher.`);
  }

  const records = body && Array.isArray(body.records) ? body.records : [];
  if (status === "OK" && records.length > 0 && records[0]) {
    // The filter matches the CIN exactly, but check it anyway.
    const record = records[0];
    if (String(field(record, CIN_FIELD) || "").toUpperCase().trim() === cin) {
      return record;
    }
  }

  log(`MCA company master data has no company for ${cin}, or data.gov.in could not answer. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function run() {
  const cin = parseCin(getValue(CIN_KEY));
  if (!cin) {
    // No valid CIN on this record (this is also what Test connection hits, because it
    // passes placeholder values instead of real ones).
    return [];
  }

  if (API_KEY.trim() === "") {
    log("data.gov.in needs an API key. Enter it in the API Key field of the enricher.");
    return [];
  }

  const cacheKey = `mca:${cin}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let record = cached ? parseJson(String(cached)) : null;

  if (!record) {
    record = lookUp(cin);
    if (!record) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(record), CACHE_MILLISECONDS);
    }
  }

  const data = {};
  Object.keys(FIELDS).forEach((property) => {
    const raw = field(record, FIELDS[property]);
    const value = DATE_PROPERTIES.indexOf(property) >= 0 ? toIsoDate(raw) : clean(raw);
    // Leave out values the dataset doesn't have, so they don't overwrite existing data with blanks.
    if (value !== null) {
      data[property] = value;
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
