// REST API enricher (V2) - Slovenian company lookup with the AJPES Business Register
//
// Looks up a golden record's Slovenian registration number (matična številka), or its tax number
// (davčna številka), in the Slovenian Business Register (Poslovni register Slovenije, PRS) with
// AJPES' restPrsInfo REST API (https://www.ajpes.si/restPrsInfo/swagger/index.html), and adds the
// result to the golden record as ajpes.* properties:
//
//   ajpes.registrationNumber  10-digit registration number (matična številka)
//   ajpes.taxNumber           tax number (davčna številka)
//   ajpes.vatId               VAT identification number (ID za DDV)
//   ajpes.name                full registered name
//   ajpes.shortName           short name
//   ajpes.legalForm           legal form
//   ajpes.address             registered address on one line
//   ajpes.municipality        municipality (občina)
//   ajpes.mainActivityCode    main activity code (SKD)
//   ajpes.mainActivity        main activity description
//   ajpes.sector              institutional sector
//   ajpes.size                company size (Slovenian classification)
//   ajpes.capital             registered capital, with currency
//   ajpes.foundationDate      date of foundation (YYYY-MM-DD)
//   ajpes.registrationDate    date of entry in the register (YYYY-MM-DD)
//   ajpes.deleted             "True" when the subject has been deleted from the register
//   ajpes.deletionDate        date it was deleted (YYYY-MM-DD)
//
// Needs an AJPES portal user with the PRS web service enabled. Enter "username:password" in the
// enricher's API Key field, and set DATA_SCHEME below to the data scheme AJPES registered for
// you. See README.md next to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid registration or tax number,
// when the register has no subject with it, or when the API can't answer. A missing or rejected
// user, or an unknown data scheme, fails the record with an error that says so.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary keys to read. Add the same keys to the Vocabulary and Properties field.
const REGISTRATION_NUMBER_KEY = "organization.registrationNumber";
// Optional. Used only when the record has no registration number.
const TAX_NUMBER_KEY = "organization.taxNumber";

// Use https://wwwa.ajpes.si/restPrsInfo to try the setup in AJPES' test environment first.
const AJPES_URL = "https://www.ajpes.si/restPrsInfo";

// The data scheme (šifra sheme) AJPES registered for your user, for example PRS_ON_P.
const DATA_SCHEME = "PRS_ON_P";

// The {APIKey} placeholder is replaced with the API Key field value ("username:password").
const API_KEY = "{APIKey}";

// How long to reuse an answer for the same subject. 0 turns caching off. Every lookup uses up
// a unit of your PRS package, so a long cache saves units.
const CACHE_MILLISECONDS = 7 * 24 * 60 * 60 * 1000;

// ---- Script ---------------------------------------------------------------------------

// AJPES status codes for a rejected user and for invalid input (such as an unknown scheme).
const AUTHENTICATION_ERROR = 4099;
const VALIDATION_ERROR = 4010;

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

// Accepts "5860571000", "5860571" (the 7-digit form, completed with 000) and spaced or dotted
// forms. Returns the 10 digits, or null when it isn't a registration number.
function parseRegistrationNumber(raw) {
  if (/[A-Za-z]/.test(raw)) {
    return null;
  }
  const digits = raw.replace(/[^0-9]/g, "");
  if (digits.length === 7) {
    return digits + "000";
  }
  return digits.length === 10 ? digits : null;
}

// Accepts "92719236", "SI92719236" and "SI 9271 9236". Returns the 8 digits, or null.
function parseTaxNumber(raw) {
  const text = raw.toUpperCase().replace(/^SI/, "");
  if (/[A-Z]/.test(text)) {
    return null;
  }
  const digits = text.replace(/[^0-9]/g, "");
  return /^[1-9]\d{7}$/.test(digits) ? digits : null;
}

function credentials() {
  const value = String(API_KEY || "").trim();
  const separator = value.indexOf(":");
  if (separator <= 0 || separator === value.length - 1) {
    return null;
  }
  return { uporabnik: value.substring(0, separator), geslo: value.substring(separator + 1), shema: DATA_SCHEME };
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

// AJPES dates are ISO date-times. Returns YYYY-MM-DD; .NET's default date (0001-01-01) means none.
function toDate(value, specified) {
  const text = clean(value);
  if (specified === false || !text || !/^\d{4}-\d{2}-\d{2}/.test(text) || text.indexOf("0001-01-01") === 0) {
    return null;
  }
  return text.substring(0, 10);
}

function formatAddress(address) {
  if (!address) {
    return null;
  }
  const street = [clean(address.ulica) || clean(address.naselje), clean(address.stHisna), clean(address.txtDodatek)]
    .filter((x) => !!x)
    .join(" ");
  const town = [clean(address.sifPosta), clean(address.posta)].filter((x) => !!x).join(" ");
  const parts = [street, town].filter((x) => !!x);
  return parts.length > 0 ? parts.join(", ") : null;
}

function formatCapital(capital) {
  if (!capital || capital.vreKapitalSpecified === false || typeof capital.vreKapital !== "number") {
    return null;
  }
  return [String(capital.vreKapital), clean(capital.valuta) || clean(capital.sifValute)].filter((x) => !!x).join(" ");
}

// Calls a restPrsInfo method. Returns the response's formatted payload, or null when there's no
// answer to store.
function callAjpes(method, request, description) {
  const apiResponse = http.send({
    url: `${AJPES_URL}/${method}`,
    method: "POST",
    headers: [
      { Key: "Content-Type", Value: "application/json" },
      { Key: "Accept", Value: "application/json" },
    ],
    body: request,
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);
  const code = body && typeof body.status === "number" ? body.status : null;
  const message = body ? clean(body.message) || "" : "";

  if (status === "TooManyRequests" || status === "ServiceUnavailable") {
    throw new Error(`TooManyRequests - AJPES is busy (HTTP ${status}), the record will be retried`);
  }

  if (code === AUTHENTICATION_ERROR || status === "Unauthorized" || status === "Forbidden") {
    throw new Error(`AJPES rejected the user in the API Key field (status ${code || status}: ${message}).`);
  }

  if (code === VALIDATION_ERROR && /shem|storitve/i.test(message)) {
    throw new Error(`AJPES rejected the data scheme "${DATA_SCHEME}" (status ${code}: ${message}). Set DATA_SCHEME to the scheme AJPES registered for your user.`);
  }

  const formatted = body && body.payload ? body.payload.formatted : null;
  if (status === "OK" && formatted) {
    return formatted;
  }

  log(`AJPES could not look up ${description}. HTTP ${status}. ${code !== null ? `Status ${code}: ${message}` : String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function getByRegistrationNumber(registrationNumber, ident) {
  const data = callAjpes("get", { ident: ident, maticna: registrationNumber, izbrisan: false }, `registration number ${registrationNumber}`);
  return data && data.info ? data.info : null;
}

// Finds the registration number for a tax number, then gets the subject.
function getByTaxNumber(taxNumber, ident) {
  const found = callAjpes("find", { ident: ident, davcna: taxNumber, maxRecords: 1 }, `tax number ${taxNumber}`);
  const list = found && Array.isArray(found.prsData) ? found.prsData : [];
  const registrationNumber = list.length > 0 ? parseRegistrationNumber(String(list[0].maticna || "")) : null;
  if (!registrationNumber) {
    if (found) {
      log(`AJPES has no subject with tax number ${taxNumber}.`);
    }
    return null;
  }
  return getByRegistrationNumber(registrationNumber, ident);
}

function run() {
  const registrationNumber = parseRegistrationNumber(getValue(REGISTRATION_NUMBER_KEY));
  const taxNumber = registrationNumber ? null : parseTaxNumber(getValue(TAX_NUMBER_KEY));
  if (!registrationNumber && !taxNumber) {
    // No valid registration or tax number on this record (this is also what Test connection
    // hits, because it passes placeholder values instead of real ones).
    return [];
  }

  const ident = credentials();
  if (!ident) {
    log('AJPES needs a portal user. Enter "username:password" in the enricher\'s API Key field.');
    return [];
  }

  const cacheKey = registrationNumber ? `ajpes:${registrationNumber}` : `ajpes:tax:${taxNumber}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let info = cached ? parseJson(String(cached)) : null;

  if (!info) {
    info = registrationNumber ? getByRegistrationNumber(registrationNumber, ident) : getByTaxNumber(taxNumber, ident);
    if (!info) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(info), CACHE_MILLISECONDS);
    }
  }

  const firm = info.firma || {};
  const names = Array.isArray(firm.imeFirme) ? firm.imeFirme : [];
  const name = names.length > 0 ? names[0] : {};
  const activity = info.glavnaDejavnost || null;
  const address = firm.naslovF || null;
  // bZbrisan is a text flag; treat D (da), 1 and true as deleted.
  const deleted = /^(d|da|1|true)$/i.test(String(info.bZbrisan || "").trim());

  const data = {
    "ajpes.registrationNumber": clean(info.stMaticna) || registrationNumber,
    "ajpes.taxNumber": clean(info.stDavcna),
    "ajpes.vatId": clean(info.idDDV),
    "ajpes.name": clean(name.popolnoIme),
    "ajpes.shortName": clean(name.kratkoIme),
    "ajpes.legalForm": clean(firm.oblika),
    "ajpes.address": formatAddress(address),
    "ajpes.municipality": address ? clean(address.obcina) : null,
    "ajpes.mainActivityCode": activity ? clean(activity.sifDejavnost) : null,
    "ajpes.mainActivity": activity ? clean(activity.dejavnostOpis) : null,
    "ajpes.sector": info.sektor ? clean(info.sektor.sektorizacija) : null,
    "ajpes.size": clean(firm.velikostRS),
    "ajpes.capital": formatCapital(info.kapital),
    "ajpes.foundationDate": toDate(info.dtUstanovitev, info.dtUstanovitevSpecified),
    "ajpes.registrationDate": toDate(info.dtVpis, info.dtVpisSpecified),
    "ajpes.deleted": deleted ? true : null,
    "ajpes.deletionDate": deleted ? toDate(info.dtZbrisan, info.dtZbrisanSpecified) : null,
  };

  // Leave out values AJPES didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
