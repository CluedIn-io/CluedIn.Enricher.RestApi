// REST API enricher (V2) - Thai company lookup with the Department of Business Development
//
// Looks up a golden record's 13-digit juristic person registration number in the register
// of Thailand's Department of Business Development (DBD), using the juristic person API
// that DBD lists on its Open-DBD data portal (https://opendata.dbd.go.th/), and adds the
// result to the golden record as dbd.* properties:
//
//   dbd.juristicId          13-digit juristic person registration number that was looked up
//   dbd.name                registered name in Thai
//   dbd.nameEn              registered name in English
//   dbd.juristicType        type of juristic person (in Thai), for example บริษัทมหาชนจำกัด
//   dbd.status              status (in Thai), for example ยังดำเนินกิจการอยู่ (still operating)
//   dbd.registrationDate    date the juristic person was registered (YYYY-MM-DD)
//   dbd.registeredCapital   registered capital in baht
//   dbd.paidUpCapital       paid-up capital in baht
//   dbd.objectiveCode       business objective code (TSIC), for example 71209
//   dbd.objective           business objective in English (Thai when there's no English text)
//   dbd.branchName          branch name of the address (in Thai), for example สำนักงานใหญ่ (head office)
//   dbd.address             registered address on one line (in Thai)
//   dbd.province            province (in Thai)
//   dbd.provinceCode        province code, for example TH-10
//
// No API key is needed. See README.md next to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid juristic person number,
// when the number isn't registered, or when the API can't answer. DBD's firewall blocks some
// cloud-hosted servers; when it does, the script logs that and stores nothing.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const JURISTIC_ID_KEY = "organization.juristicId";

const DBD_URL = "https://openapi.dbd.go.th/api/v1/juristic_person/";

// Sent with every request. Some services reject requests without a User-Agent.
const USER_AGENT = "CluedIn REST API enricher";

// How long to reuse an answer for the same number. 0 turns caching off.
const CACHE_MILLISECONDS = 24 * 60 * 60 * 1000;

// ---- Script ---------------------------------------------------------------------------

// DBD answer codes (status.code in the response).
const DBD_SUCCESS = "1000";
const DBD_NO_DATA = "1004";

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

// Accepts "0107544000108", "0 1075 44000 10 8", "0107-544-000-108", etc. Returns the 13 digits,
// or null when the number isn't valid (the same MOD 11 check digit as Thai ID numbers).
function parseJuristicId(raw) {
  const digits = raw.replace(/[\s.\-]/g, "");
  if (!/^\d{13}$/.test(digits)) {
    return null;
  }
  let sum = 0;
  for (let i = 0; i < 12; i++) {
    sum += Number(digits.charAt(i)) * (13 - i);
  }
  return (11 - (sum % 11)) % 10 === Number(digits.charAt(12)) ? digits : null;
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

// DBD dates are YYYYMMDD. Years of 2400 or later are Buddhist Era years (543 years ahead).
function formatDate(value) {
  const text = clean(value);
  if (!text || !/^\d{8}$/.test(text)) {
    return null;
  }
  let year = Number(text.substring(0, 4));
  if (year >= 2400) {
    year -= 543;
  }
  const month = text.substring(4, 6);
  const day = text.substring(6, 8);
  return month === "00" || day === "00" ? null : `${year}-${month}-${day}`;
}

// DBD sends amounts as text, for example "28562996250.0".
function toAmount(value) {
  const text = clean(value);
  if (!text) {
    return null;
  }
  const amount = Number(text.replace(/,/g, ""));
  return isFinite(amount) ? amount : null;
}

function formatAddress(addressType) {
  if (!addressType) {
    return null;
  }
  const subDistrict = addressType["cd:CitySubDivision"] || {};
  const district = addressType["cd:City"] || {};
  const province = addressType["cd:CountrySubDivision"] || {};
  const parts = [
    clean(addressType["cd:Address"]),
    clean(subDistrict["cr:CitySubDivisionTextTH"]),
    clean(district["cr:CityTextTH"]),
    clean(province["cr:CountrySubDivisionTextTH"]),
  ].filter((x) => !!x);
  return parts.length > 0 ? parts.join(", ") : null;
}

// Calls the API. Returns the juristic person, or null when there's no answer to store.
function lookUp(juristicId) {
  const apiResponse = http.send({
    url: DBD_URL + juristicId,
    method: "GET",
    headers: [
      { Key: "Accept", Value: "application/json" },
      { Key: "User-Agent", Value: USER_AGENT },
    ],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - the DBD API is busy, the record will be retried");
  }

  if (!body) {
    // For example DBD's firewall (Incapsula), which answers 403 with an HTML page when it
    // blocks the server the enricher runs on.
    log(`DBD could not look up ${juristicId}. HTTP ${status}, and the answer wasn't JSON. DBD's firewall may be blocking this server. ${String(apiResponse.Content || "").substring(0, 120)}`);
    return null;
  }

  const code = body.status ? String(body.status.code || "") : "";
  const description = body.status ? String(body.status.description || "") : "";
  const first = Array.isArray(body.data) && body.data.length > 0 ? body.data[0] : null;
  const person = first ? first["cd:OrganizationJuristicPerson"] : null;

  if (status === "OK" && code === DBD_SUCCESS && person && person["cd:OrganizationJuristicID"]) {
    return person;
  }

  if (code === DBD_NO_DATA || status === "NotFound" || (code === DBD_SUCCESS && !person)) {
    log(`DBD has no juristic person with number ${juristicId}.`);
  } else {
    log(`DBD could not look up ${juristicId}. HTTP ${status}. ${code} ${description}`);
  }
  return null;
}

function run() {
  const juristicId = parseJuristicId(getValue(JURISTIC_ID_KEY));
  if (!juristicId) {
    // No valid juristic person number on this record (this is also what Test connection
    // hits, because it passes placeholder values instead of real ones).
    return [];
  }

  const cacheKey = `dbd:${juristicId}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let person = cached ? parseJson(String(cached)) : null;

  if (!person) {
    person = lookUp(juristicId);
    if (!person) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(person), CACHE_MILLISECONDS);
    }
  }

  const objectiveContainer = person["cd:OrganizationJuristicObjective"] || {};
  const objective = Array.isArray(objectiveContainer["td:JuristicObjective"])
    ? objectiveContainer["td:JuristicObjective"][0] || {}
    : objectiveContainer["td:JuristicObjective"] || {};
  const addressContainer = person["cd:OrganizationJuristicAddress"] || {};
  const addressType = addressContainer["cr:AddressType"] || null;
  const province = addressType ? addressType["cd:CountrySubDivision"] || {} : {};

  const data = {
    "dbd.juristicId": clean(person["cd:OrganizationJuristicID"]),
    "dbd.name": clean(person["cd:OrganizationJuristicNameTH"]),
    "dbd.nameEn": clean(person["cd:OrganizationJuristicNameEN"]),
    "dbd.juristicType": clean(person["cd:OrganizationJuristicType"]),
    "dbd.status": clean(person["cd:OrganizationJuristicStatus"]),
    "dbd.registrationDate": formatDate(person["cd:OrganizationJuristicRegisterDate"]),
    "dbd.registeredCapital": toAmount(person["cd:OrganizationJuristicRegisterCapital"]),
    "dbd.paidUpCapital": toAmount(person["cd:OrganizationJuristicPaidUpCapital"]),
    "dbd.objectiveCode": clean(objective["td:JuristicObjectiveCode"]),
    "dbd.objective": clean(objective["td:JuristicObjectiveTextEN"]) || clean(objective["td:JuristicObjectiveTextTH"]),
    "dbd.branchName": clean(person["cd:OrganizationJuristicBranchName"]),
    "dbd.address": formatAddress(addressType),
    "dbd.province": clean(province["cr:CountrySubDivisionTextTH"]),
    "dbd.provinceCode": clean(province["cr:CountrySubDivisionCode"]),
  };

  // Leave out values DBD didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
