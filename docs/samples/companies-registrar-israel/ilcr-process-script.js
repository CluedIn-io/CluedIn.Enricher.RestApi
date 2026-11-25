// REST API enricher (V2) - Israeli company lookup with the Companies Registrar's open data
//
// Looks up a golden record's Israeli company number in the Israeli Corporations Authority's
// companies dataset (מאגר חברות - רשם החברות), which the Israel Government Data portal
// publishes at https://data.gov.il/dataset/ica_companies, using the portal's CKAN datastore
// search API, and adds the result to the golden record as ilcr.* properties:
//
//   ilcr.companyNumber        9-digit company number that was looked up
//   ilcr.name                 registered name (Hebrew)
//   ilcr.nameEn               registered English name, when there is one
//   ilcr.companyType          type of corporation, for example ישראלית חברה ציבורית
//   ilcr.status               company status, for example פעילה (active)
//   ilcr.subStatus            sub-status, when there is one
//   ilcr.description          company description, when there is one
//   ilcr.purpose              company purpose
//   ilcr.limitation           limitation, for example מוגבלת (limited)
//   ilcr.violator             violating company indication, when there is one
//   ilcr.governmentCompany    "True" / "False" - government company
//   ilcr.incorporationDate    date of incorporation (YYYY-MM-DD)
//   ilcr.lastAnnualReportYear last year an annual report was filed
//   ilcr.address              registered address on one line
//   ilcr.city                 city of the registered address
//   ilcr.postalCode           postal code of the registered address
//   ilcr.country              country of the registered address
//
// No API key is needed. See README.md next to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid company number, when the
// number isn't in the dataset, or when the API can't answer.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const COMPANY_NUMBER_KEY = "organization.companyNumber";

const DATASTORE_URL = "https://data.gov.il/api/3/action/datastore_search";
// "Companies" (חברות) resource of the Companies Registrar dataset on data.gov.il
const RESOURCE_ID = "f004176c-b85f-4542-8901-7b3176f9a054";

// How long to reuse an answer for the same company number. 0 turns caching off.
const CACHE_MILLISECONDS = 24 * 60 * 60 * 1000;

// Which dataset column (the column names are in Hebrew) each property comes from. If the
// dataset renames a column, change it here.
const NUMBER_FIELD = "מספר חברה";
const FIELDS = {
  "ilcr.name": "שם חברה",
  "ilcr.nameEn": "שם באנגלית",
  "ilcr.companyType": "סוג תאגיד",
  "ilcr.status": "סטטוס חברה",
  "ilcr.subStatus": "תת סטטוס",
  "ilcr.description": "תאור חברה",
  "ilcr.purpose": "מטרת החברה",
  "ilcr.limitation": "מגבלות",
  "ilcr.violator": "מפרה",
  "ilcr.lastAnnualReportYear": "שנה אחרונה של דוח שנתי (שהוגש)",
  "ilcr.city": "שם עיר",
  "ilcr.postalCode": "מיקוד",
  "ilcr.country": "מדינה",
};
const INCORPORATION_DATE_FIELD = "תאריך התאגדות";
const GOVERNMENT_COMPANY_FIELD = "חברה ממשלתית";
const STREET_FIELD = "שם רחוב";
const HOUSE_NUMBER_FIELD = "מספר בית";

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

// Accepts "52-000011-8", "520000118", "520 000 118", etc. Returns the 9 digits, or null when
// the number isn't a valid Israeli corporation number: 9 digits, starting with 5, and a valid
// check digit (the same algorithm as the Israeli ID number).
function parseCompanyNumber(raw) {
  const digits = raw.replace(/[^0-9]/g, "");
  if (!/^5\d{8}$/.test(digits) || raw.replace(/[\s.\-/]/g, "") !== digits) {
    return null;
  }
  let sum = 0;
  for (let i = 0; i < 9; i++) {
    let product = Number(digits.charAt(i)) * (i % 2 === 0 ? 1 : 2);
    if (product > 9) {
      product -= 9;
    }
    sum += product;
  }
  return sum % 10 === 0 ? digits : null;
}

// The dataset writes a quote mark inside a name as "~", for example בע~מ (בע"מ).
function clean(value) {
  if (value === null || value === undefined) {
    return null;
  }
  const text = String(value).replace(/~/g, '"').replace(/\s+/g, " ").trim();
  return text === "" ? null : text;
}

function parseJson(text) {
  try {
    return JSON.parse(text);
  } catch (e) {
    return null;
  }
}

// "15/05/1921" -> "1921-05-15"
function isoDate(value) {
  const match = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(clean(value) || "");
  if (!match) {
    return null;
  }
  const pad = (x) => (x.length === 1 ? "0" + x : x);
  return `${match[3]}-${pad(match[2])}-${pad(match[1])}`;
}

function yesNo(value) {
  const text = clean(value);
  if (text === "כן") {
    return true;
  }
  if (text === "לא") {
    return false;
  }
  return null;
}

// Calls the datastore API. Returns the company's row, or null when there's no answer to store.
function lookUp(companyNumber) {
  const filters = {};
  filters[NUMBER_FIELD] = Number(companyNumber);
  const apiResponse = http.send({
    url: `${DATASTORE_URL}?resource_id=${RESOURCE_ID}&limit=1&filters=${encodeURIComponent(JSON.stringify(filters))}`,
    method: "GET",
    headers: [{ Key: "Accept", Value: "application/json" }],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests" || status === "ServiceUnavailable") {
    throw new Error(`TooManyRequests - data.gov.il is busy (HTTP ${status}), the record will be retried`);
  }

  const records = body && body.success === true && body.result && Array.isArray(body.result.records)
    ? body.result.records
    : null;

  if (status !== "OK" || !records) {
    log(`data.gov.il could not look up company ${companyNumber}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
    return null;
  }

  for (let i = 0; i < records.length; i++) {
    if (String(records[i][NUMBER_FIELD]) === companyNumber) {
      return records[i];
    }
  }

  log(`The Companies Registrar dataset has no company ${companyNumber}.`);
  return null;
}

function formatAddress(row) {
  const street = [clean(row[STREET_FIELD]), clean(row[HOUSE_NUMBER_FIELD])].filter((x) => !!x).join(" ");
  const parts = [street, clean(row[FIELDS["ilcr.city"]]), clean(row[FIELDS["ilcr.postalCode"]])].filter((x) => !!x);
  return parts.length > 0 ? parts.join(", ") : null;
}

function run() {
  const companyNumber = parseCompanyNumber(getValue(COMPANY_NUMBER_KEY));
  if (!companyNumber) {
    // No valid company number on this record (this is also what Test connection hits, because
    // it passes placeholder values instead of real ones).
    return [];
  }

  const cacheKey = `ilcr:${companyNumber}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let row = cached ? parseJson(String(cached)) : null;

  if (!row) {
    row = lookUp(companyNumber);
    if (!row) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(row), CACHE_MILLISECONDS);
    }
  }

  const data = { "ilcr.companyNumber": companyNumber };
  Object.keys(FIELDS).forEach((property) => {
    data[property] = clean(row[FIELDS[property]]);
  });
  data["ilcr.incorporationDate"] = isoDate(row[INCORPORATION_DATE_FIELD]);
  data["ilcr.governmentCompany"] = yesNo(row[GOVERNMENT_COMPANY_FIELD]);
  data["ilcr.address"] = formatAddress(row);

  // Leave out values the dataset didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
