// REST API enricher (V2) - Taiwanese company lookup with the Ministry of Economic Affairs
//
// Looks up a golden record's Unified Business Number (統一編號) in the company registration
// data that the Ministry of Economic Affairs (MOEA) publishes on its GCIS open-data platform
// (https://data.gcis.nat.gov.tw/od/rule), and adds the result to the golden record as
// moea.* properties. Text values are in Traditional Chinese, as the register holds them:
//
//   moea.unifiedBusinessNumber   8-digit Unified Business Number that was looked up
//   moea.name                    registered company name
//   moea.status                  company status, for example 核准設立 (approved and established)
//   moea.responsiblePerson       name of the representative (代表人)
//   moea.address                 company location
//   moea.registrationAuthority   authority the company is registered with
//   moea.capital                 authorized capital in TWD
//   moea.paidInCapital           paid-in capital in TWD
//   moea.setupDate               date the company was established (YYYY-MM-DD)
//   moea.lastChangeDate          date of the last approved change (YYYY-MM-DD)
//   moea.revocationDate          date the company was dissolved or revoked (YYYY-MM-DD)
//   moea.suspensionStartDate     start of a suspension of business (YYYY-MM-DD)
//   moea.suspensionEndDate       end of a suspension of business (YYYY-MM-DD)
//
// No API key is needed. See README.md next to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid Unified Business Number,
// when the number isn't a registered company, or when the API can't answer.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const UNIFIED_BUSINESS_NUMBER_KEY = "organization.unifiedBusinessNumber";

// The GCIS "company registration basic information" (公司登記基本資料) dataset.
const MOEA_URL = "https://data.gcis.nat.gov.tw/od/data/api/5F64D864-61CB-4D0D-8AD9-492047CC1EA6";

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

// Accepts "22099131", "2209 9131", etc. Returns the 8 digits, or null when the value isn't a
// valid Unified Business Number. The check uses the weights 1,2,1,2,1,2,4,1 and modulus 5
// (numbers that passed the older modulus 10 check also pass it).
function parseUnifiedBusinessNumber(raw) {
  const digits = raw.replace(/[\s.\-]/g, "");
  if (!/^\d{8}$/.test(digits)) {
    return null;
  }
  const weights = [1, 2, 1, 2, 1, 2, 4, 1];
  let sum = 0;
  for (let i = 0; i < 8; i++) {
    const product = Number(digits.charAt(i)) * weights[i];
    sum += Math.floor(product / 10) + (product % 10);
  }
  // When the 7th digit is 7, its product (28) may count as 10 or as 1.
  const valid = sum % 5 === 0 || (digits.charAt(6) === "7" && (sum + 1) % 5 === 0);
  return valid ? digits : null;
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

// Dates are in the Republic of China (Minguo) calendar as YYYMMDD: "0760221" is 21 February
// of year 76, which is 1987 (76 + 1911).
function rocToIsoDate(value) {
  const match = /^(\d{2,3})(\d{2})(\d{2})$/.exec(clean(value) || "");
  if (!match) {
    return null;
  }
  return `${Number(match[1]) + 1911}-${match[2]}-${match[3]}`;
}

// Calls the API. Returns the company, or null when there's no answer to store.
function lookUp(unifiedBusinessNumber) {
  const filter = `Business_Accounting_NO eq ${unifiedBusinessNumber}`;
  const apiResponse = http.send({
    url: `${MOEA_URL}?$format=json&$filter=${encodeURIComponent(filter)}&$skip=0&$top=1`,
    method: "GET",
    headers: [{ Key: "Accept", Value: "application/json" }],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - the MOEA open-data API is busy, the record will be retried");
  }

  if (status === "OK" && Array.isArray(body)) {
    for (let i = 0; i < body.length; i++) {
      if (body[i] && clean(body[i].Business_Accounting_NO) === unifiedBusinessNumber) {
        return body[i];
      }
    }
  }

  // A number that isn't a registered company comes back as OK with an empty body.
  log(`MOEA found no company with Unified Business Number ${unifiedBusinessNumber}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function run() {
  const unifiedBusinessNumber = parseUnifiedBusinessNumber(getValue(UNIFIED_BUSINESS_NUMBER_KEY));
  if (!unifiedBusinessNumber) {
    // No valid number on this record (this is also what Test connection hits, because it
    // passes placeholder values instead of real ones).
    return [];
  }

  const cacheKey = `moea:${unifiedBusinessNumber}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let company = cached ? parseJson(String(cached)) : null;

  if (!company) {
    company = lookUp(unifiedBusinessNumber);
    if (!company) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(company), CACHE_MILLISECONDS);
    }
  }

  const data = {
    "moea.unifiedBusinessNumber": clean(company.Business_Accounting_NO),
    "moea.name": clean(company.Company_Name),
    "moea.status": clean(company.Company_Status_Desc),
    "moea.responsiblePerson": clean(company.Responsible_Name),
    "moea.address": clean(company.Company_Location),
    "moea.registrationAuthority": clean(company.Register_Organization_Desc),
    "moea.capital": typeof company.Capital_Stock_Amount === "number" ? company.Capital_Stock_Amount : null,
    "moea.paidInCapital": typeof company.Paid_In_Capital_Amount === "number" ? company.Paid_In_Capital_Amount : null,
    "moea.setupDate": rocToIsoDate(company.Company_Setup_Date),
    "moea.lastChangeDate": rocToIsoDate(company.Change_Of_Approval_Data),
    "moea.revocationDate": rocToIsoDate(company.Revoke_App_Date),
    "moea.suspensionStartDate": rocToIsoDate(company.Sus_Beg_Date),
    "moea.suspensionEndDate": rocToIsoDate(company.Sus_End_Date),
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
