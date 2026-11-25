// REST API enricher (V2) - South African company lookup with the CIPC APIVerse API
//
// Looks up a golden record's South African enterprise (company) registration number with the
// Companies and Intellectual Property Commission's "CIPC Public Data - Commercial" API on the
// APIVerse Hub (https://developer.cipc.co.za/), by using its Basic company information
// operation, and adds the result to the golden record as cipc.* properties:
//
//   cipc.enterpriseNumber   enterprise number that was looked up, for example 2020/939681/07
//   cipc.name               registered enterprise name
//   cipc.enterpriseType     enterprise type, for example Private Company
//   cipc.status             enterprise status, for example In Business
//   cipc.industry           industry (SIC) description
//   cipc.registrationDate   registration date
//   cipc.businessStartDate  business start date
//   cipc.financialYearEnd   financial year end
//   cipc.physicalAddress    physical address on one line
//   cipc.postalAddress      postal address on one line
//   cipc.countryCode        country code
//   cipc.taxNumber          tax number
//
// Needs an APIVerse subscription key (Ocp-Apim-Subscription-Key), entered in the enricher's
// API Key field. See README.md next to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid enterprise number, when
// CIPC has no enterprise with that number, or when the API can't answer. A missing or rejected
// key fails the record with an error that says so.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const REGISTRATION_NUMBER_KEY = "organization.registrationNumber";

const CIPC_URL = "https://apim.cipc.co.za/companies-api/v1/information";

// The {APIKey} placeholder is replaced with the API Key field value (your subscription key).
const API_KEY = "{APIKey}";

// How long to reuse an answer for the same enterprise. 0 turns caching off. The free APIVerse
// tier allows 100 calls a year, so a long cache saves calls.
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

// Accepts "2020/939681/07", "2020-939681-07", "2020 939681 07", "202093968107" and a leading
// "CK" for close corporations. Returns "YYYY/NNNNNN/NN", or null when it isn't an enterprise
// number.
function parseEnterpriseNumber(raw) {
  const text = raw.toUpperCase().replace(/^(CK|K)\s*/, "");
  if (!/^[0-9\s/.-]+$/.test(text)) {
    return null;
  }
  const digits = text.replace(/[^0-9]/g, "");
  if (!/^(18|19|20)\d{10}$/.test(digits)) {
    return null;
  }
  return `${digits.substring(0, 4)}/${digits.substring(4, 10)}/${digits.substring(10, 12)}`;
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

// Stores dates as YYYY-MM-DD where the format is recognisable, otherwise as returned. A full
// timestamp would be turned into a date in the server's own format by the enricher.
function formatDate(value) {
  const text = clean(value);
  if (!text) {
    return null;
  }
  const iso = /^(\d{4})[-/](\d{2})[-/](\d{2})/.exec(text);
  if (iso) {
    return `${iso[1]}-${iso[2]}-${iso[3]}`;
  }
  const compact = /^(\d{4})(\d{2})(\d{2})$/.exec(text);
  return compact ? `${compact[1]}-${compact[2]}-${compact[3]}` : text;
}

function formatAddress(item, prefix, codeField) {
  const parts = [];
  for (let i = 1; i <= 4; i++) {
    const line = clean(item[`${prefix}${i}`]);
    if (line) {
      parts.push(line);
    }
  }
  const code = clean(item[codeField]);
  if (code) {
    parts.push(code);
  }
  return parts.length > 0 ? parts.join(", ") : null;
}

function getEnterprises(body) {
  if (!body) {
    return [];
  }
  const list = body.Enterprise || body.enterprise || body.Enterprises || body.enterprises;
  if (Array.isArray(list)) {
    return list;
  }
  return list && typeof list === "object" ? [list] : [];
}

function normalise(number) {
  return String(number || "").replace(/[^0-9]/g, "");
}

// Calls the CIPC API. Returns the enterprise, or null when there's no answer to store.
function lookUp(enterpriseNumber) {
  const apiResponse = http.send({
    url: CIPC_URL,
    method: "POST",
    headers: [
      { Key: "Content-Type", Value: "application/json" },
      { Key: "Accept", Value: "application/json" },
      { Key: "Ocp-Apim-Subscription-Key", Value: API_KEY },
    ],
    body: { enterprise_number: enterpriseNumber },
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - the CIPC API rate limit was reached, the record will be retried");
  }

  if (status === "Unauthorized" || status === "Forbidden") {
    const message = body && body.message ? body.message : String(apiResponse.Content || "").substring(0, 200);
    throw new Error(`The CIPC API rejected the subscription key in the API Key field (HTTP ${status}). ${message}`);
  }

  if (status === "OK") {
    const enterprises = getEnterprises(body);
    for (let i = 0; i < enterprises.length; i++) {
      if (enterprises[i] && normalise(enterprises[i].enterprise_number) === normalise(enterpriseNumber)) {
        return enterprises[i];
      }
    }
  }

  log(`CIPC could not look up ${enterpriseNumber}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function run() {
  const enterpriseNumber = parseEnterpriseNumber(getValue(REGISTRATION_NUMBER_KEY));
  if (!enterpriseNumber) {
    // No valid enterprise number on this record (this is also what Test connection hits,
    // because it passes placeholder values instead of real ones).
    return [];
  }

  if (!clean(API_KEY)) {
    log("The CIPC API needs a subscription key. Enter your APIVerse subscription key in the enricher's API Key field.");
    return [];
  }

  const cacheKey = `cipc:${enterpriseNumber}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let item = cached ? parseJson(String(cached)) : null;

  if (!item) {
    item = lookUp(enterpriseNumber);
    if (!item) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(item), CACHE_MILLISECONDS);
    }
  }

  const data = {
    "cipc.enterpriseNumber": clean(item.enterprise_number) || enterpriseNumber,
    "cipc.name": clean(item.enterprise_name),
    "cipc.enterpriseType": clean(item.enterprise_type_description),
    "cipc.status": clean(item.enterprise_status_description),
    "cipc.industry": clean(item.sic_description),
    "cipc.registrationDate": formatDate(item.registration_date),
    "cipc.businessStartDate": formatDate(item.business_start_date),
    "cipc.financialYearEnd": clean(item.financial_year_end),
    "cipc.physicalAddress": formatAddress(item, "physical_address_", "physical_code"),
    "cipc.postalAddress": formatAddress(item, "postal_address_", "postal_code"),
    "cipc.countryCode": clean(item.country_code),
    "cipc.taxNumber": clean(item.tax_number),
  };

  // Leave out values CIPC didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
