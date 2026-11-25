// REST API enricher (V2) - Dutch company lookup with the KVK Basisprofiel API
//
// Looks up a golden record's KVK number (Chamber of Commerce number) in the Dutch Business
// Register (Handelsregister) by using the KVK Basisprofiel API
// (https://developers.kvk.nl/documentation/basisprofiel-api), and adds the result to the golden
// record as kvk.* properties:
//
//   kvk.kvkNumber          8-digit KVK number that was looked up
//   kvk.name               name (naam)
//   kvk.statutoryName      statutory name (statutaireNaam)
//   kvk.tradeNames         all trade names, separated by "; "
//   kvk.legalForm          legal form, for example Besloten vennootschap
//   kvk.rsin               RSIN (legal entities and partnerships identification number)
//   kvk.registrationDate   date of formal registration (YYYY-MM-DD)
//   kvk.startDate          date the business started (YYYY-MM-DD)
//   kvk.employees          total number of people working for the business
//   kvk.mainActivityCode   SBI code of the main activity, for example 01241
//   kvk.mainActivity       description of the main activity (Dutch)
//   kvk.establishmentNumber establishment number (vestigingsnummer) of the main establishment
//   kvk.visitingAddress    visiting address of the main establishment on one line
//   kvk.postalAddress      postal address of the main establishment on one line
//   kvk.city               city of the visiting address
//   kvk.postalCode         postal code of the visiting address
//   kvk.website            first website of the main establishment
//
// Needs a KVK API key, entered in the enricher's API Key field. See README.md next to this
// file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid KVK number, when the
// number isn't registered, or when the API can't answer.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const KVK_NUMBER_KEY = "organization.kvkNumber";

// Use https://api.kvk.nl/test/api/v1/basisprofielen/ (with KVK's public test API key) to try
// the setup against KVK's test data first.
const KVK_URL = "https://api.kvk.nl/api/v1/basisprofielen/";

// The {APIKey} placeholder is replaced with the API Key field value.
const API_KEY = "{APIKey}";

// How long to reuse an answer for the same KVK number. 0 turns caching off.
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

// Accepts "6875 0110", "68.75.01.10", "KVK 68750110", and numbers that lost their leading zero
// (for example 1234567). Returns the 8 digits, or null when it isn't a KVK number.
function parseKvkNumber(raw) {
  const text = raw.toUpperCase().replace(/^KVK/, "");
  if (/[A-Z]/.test(text)) {
    return null;
  }
  const digits = text.replace(/[^0-9]/g, "");
  if (digits.length < 7 || digits.length > 8) {
    return null;
  }
  return digits.length === 7 ? "0" + digits : digits;
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

// KVK dates are YYYYMMDD. Returns YYYY-MM-DD, or null for a missing or partial date (KVK uses
// 00 for an unknown month or day).
function toDate(value) {
  const text = clean(value);
  if (!text || !/^\d{8}$/.test(text) || text.substring(4, 6) === "00" || text.substring(6, 8) === "00") {
    return null;
  }
  return `${text.substring(0, 4)}-${text.substring(4, 6)}-${text.substring(6, 8)}`;
}

// volledigAdres separates the street line from the postcode and city with a run of spaces.
// Returns it on one line, for example "Hizzaarderlaan 3 A, 8823SJ Lollum".
function formatAddress(value) {
  if (value === null || value === undefined) {
    return null;
  }
  return clean(String(value).trim().replace(/\s{2,}/g, ", "));
}

function findAddress(addresses, type) {
  const list = Array.isArray(addresses) ? addresses : [];
  for (let i = 0; i < list.length; i++) {
    if (list[i] && list[i].type === type) {
      return list[i];
    }
  }
  return null;
}

function getErrors(body) {
  const errors = body && Array.isArray(body.fout) ? body.fout : [];
  return errors.map((x) => x && `${x.code}: ${x.omschrijving}`).filter((x) => !!x);
}

// Calls the API. Returns the basic profile, or null when there's no answer to store.
function lookUp(kvkNumber) {
  const apiResponse = http.send({
    url: KVK_URL + kvkNumber,
    method: "GET",
    headers: [
      { Key: "apikey", Value: API_KEY },
      { Key: "Accept", Value: "application/json" },
    ],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - the KVK API is busy, the record will be retried");
  }

  if (status === "Unauthorized" || status === "Forbidden") {
    throw new Error(`The KVK API rejected the API key (HTTP ${status}). Check the API Key field of the enricher.`);
  }

  if (status === "OK" && body && body.kvkNummer) {
    return body;
  }

  log(`KVK could not look up ${kvkNumber}. HTTP ${status}. ${getErrors(body).join(", ") || String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function run() {
  const kvkNumber = parseKvkNumber(getValue(KVK_NUMBER_KEY));
  if (!kvkNumber) {
    // No valid KVK number on this record (this is also what Test connection hits, because it
    // passes placeholder values instead of real ones).
    return [];
  }

  if (!API_KEY) {
    log("No KVK API key. Enter it in the API Key field of the enricher.");
    return [];
  }

  const cacheKey = `kvk:${kvkNumber}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let profile = cached ? parseJson(String(cached)) : null;

  if (!profile) {
    profile = lookUp(kvkNumber);
    if (!profile) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(profile), CACHE_MILLISECONDS);
    }
  }

  const embedded = profile._embedded || {};
  const owner = embedded.eigenaar || {};
  const mainEstablishment = embedded.hoofdvestiging || {};

  const tradeNames = (Array.isArray(profile.handelsnamen) ? profile.handelsnamen : [])
    .map((x) => x && clean(x.naam))
    .filter((x) => !!x);

  const activities = Array.isArray(profile.sbiActiviteiten) ? profile.sbiActiviteiten : [];
  let mainActivity = null;
  for (let i = 0; i < activities.length; i++) {
    if (activities[i] && activities[i].indHoofdactiviteit === "Ja") {
      mainActivity = activities[i];
      break;
    }
  }
  mainActivity = mainActivity || {};

  const visitingAddress = findAddress(mainEstablishment.adressen, "bezoekadres") || {};
  const postalAddress = findAddress(mainEstablishment.adressen, "correspondentieadres") || {};
  const websites = Array.isArray(mainEstablishment.websites) ? mainEstablishment.websites : [];

  const data = {
    "kvk.kvkNumber": clean(profile.kvkNummer),
    "kvk.name": clean(profile.naam),
    "kvk.statutoryName": clean(profile.statutaireNaam),
    "kvk.tradeNames": tradeNames.length > 0 ? tradeNames.join("; ") : null,
    "kvk.legalForm": clean(owner.rechtsvorm),
    "kvk.rsin": clean(owner.rsin),
    "kvk.registrationDate": toDate(profile.formeleRegistratiedatum),
    "kvk.startDate": toDate(profile.materieleRegistratie && profile.materieleRegistratie.datumAanvang),
    "kvk.employees": typeof profile.totaalWerkzamePersonen === "number" ? profile.totaalWerkzamePersonen : null,
    "kvk.mainActivityCode": clean(mainActivity.sbiCode),
    "kvk.mainActivity": clean(mainActivity.sbiOmschrijving),
    "kvk.establishmentNumber": clean(mainEstablishment.vestigingsnummer),
    "kvk.visitingAddress": formatAddress(visitingAddress.volledigAdres),
    "kvk.postalAddress": formatAddress(postalAddress.volledigAdres),
    "kvk.city": clean(visitingAddress.plaats),
    "kvk.postalCode": clean(visitingAddress.postcode),
    "kvk.website": websites.length > 0 ? clean(websites[0]) : null,
  };

  // Leave out values KVK didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
