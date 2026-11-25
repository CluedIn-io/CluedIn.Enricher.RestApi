// REST API enricher (V2) - Slovak company lookup with the Register of Legal Entities (RPO)
//
// Looks up a golden record's Slovak identification number (IČO) in the Register of Legal
// Entities, Entrepreneurs and Public Authorities (Register právnických osôb, RPO) that the
// Statistical Office of the Slovak Republic runs, using its public RPO API
// (https://api.statistics.sk/rpo/v1/), and adds the result to the golden record as rpo.*
// properties:
//
//   rpo.ico                     8-digit IČO that was looked up
//   rpo.name                    current registered name (the last one, for a dissolved entity)
//   rpo.legalForm               legal form, for example Akciová spoločnosť
//   rpo.legalFormCode           legal form code, for example 121
//   rpo.legalStatus             legal status, when there is one, for example Zaniknutá spoločnosť
//   rpo.address                 registered address on one line
//   rpo.municipality            municipality of the registered address
//   rpo.postalCode              postal code of the registered address
//   rpo.country                 country of the registered address
//   rpo.establishmentDate       date the entity was established (YYYY-MM-DD)
//   rpo.terminationDate         date the entity ceased to exist (YYYY-MM-DD)
//   rpo.mainActivity            main economic activity (SK NACE)
//   rpo.mainActivityCode        main economic activity code
//   rpo.sourceRegister          register the entity is in, for example Obchodný register
//   rpo.registrationOffice      registration office, for example Mestský súd Bratislava III
//   rpo.registrationNumber      file number at the registration office, for example Sa/426/B
//   rpo.registeredCapital       registered capital
//   rpo.registeredCapitalCurrency currency of the registered capital, for example EUR
//
// No API key is needed. See README.md next to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid IČO, when the IČO isn't in
// the register, or when the API can't answer.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const ICO_KEY = "organization.ico";

const RPO_URL = "https://api.statistics.sk/rpo/v1";

// How long to reuse an answer for the same IČO. 0 turns caching off.
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

// Accepts "31322832", "31 322 832", "IČO: 31 322 832" and shorter numbers written without
// their leading zeros ("683 025" -> "00683025"). Returns the 8 digits, or null when the value
// isn't a valid IČO (modulus 11 check digit).
function parseIco(raw) {
  const text = raw.toUpperCase().replace(/^(IČO|ICO)\s*:?\s*/, "");
  if (!/^[0-9\s]+$/.test(text)) {
    return null;
  }
  let digits = text.replace(/\s/g, "");
  if (digits.length < 6 || digits.length > 8) {
    return null;
  }
  while (digits.length < 8) {
    digits = "0" + digits;
  }
  let sum = 0;
  for (let i = 0; i < 7; i++) {
    sum += Number(digits.charAt(i)) * (8 - i);
  }
  const checkDigit = (11 - (sum % 11)) % 10;
  return checkDigit === Number(digits.charAt(7)) ? digits : null;
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

function dateOnly(value) {
  const text = clean(value);
  return text && /^\d{4}-\d{2}-\d{2}/.test(text) ? text.substring(0, 10) : null;
}

// The current entry of a history list: one without validTo, otherwise the one that started
// last (for example the last name of a dissolved entity).
function current(list) {
  const items = Array.isArray(list) ? list : [];
  let best = null;
  for (let i = 0; i < items.length; i++) {
    const item = items[i];
    if (!item) {
      continue;
    }
    if (!item.validTo) {
      return item;
    }
    if (!best || String(item.validFrom || "") > String(best.validFrom || "")) {
      best = item;
    }
  }
  return best;
}

function codelistValue(entry) {
  return entry && entry.value ? clean(entry.value.value) : null;
}

function codelistCode(entry) {
  return entry && entry.value ? clean(entry.value.code) : null;
}

function formatAddress(address) {
  if (!address) {
    return null;
  }
  const regNumber = address.regNumber ? String(address.regNumber) : "";
  const buildingNumber = clean(address.buildingNumber) || "";
  const number = regNumber && buildingNumber ? `${regNumber}/${buildingNumber}` : regNumber || buildingNumber;
  const street = [clean(address.street), number].filter((x) => !!x).join(" ");
  const postalCode = Array.isArray(address.postalCodes) ? clean(address.postalCodes[0]) : null;
  const town = [postalCode, address.municipality ? clean(address.municipality.value) : null].filter((x) => !!x).join(" ");
  const parts = [street, town].filter((x) => !!x);
  return parts.length > 0 ? parts.join(", ") : null;
}

function get(url, what) {
  const apiResponse = http.send({
    url: url,
    method: "GET",
    headers: [{ Key: "Accept", Value: "application/json" }],
  });
  const status = String(apiResponse.HttpStatus || "");
  if (status === "TooManyRequests" || status === "ServiceUnavailable") {
    throw new Error(`TooManyRequests - the RPO API is busy (HTTP ${status}), the record will be retried`);
  }
  const body = parseJson(apiResponse.Content);
  if (status !== "OK" || !body) {
    log(`The RPO API could not ${what}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
    return null;
  }
  return body;
}

function hasIdentifier(entity, ico) {
  const identifiers = entity && Array.isArray(entity.identifiers) ? entity.identifiers : [];
  for (let i = 0; i < identifiers.length; i++) {
    if (identifiers[i] && String(identifiers[i].value) === ico) {
      return true;
    }
  }
  return false;
}

// Finds the entity by IČO, then gets its current details. Returns the properties to store, or
// null when there's no answer to store.
function lookUp(ico) {
  const search = get(`${RPO_URL}/search?identifier=${ico}`, `search for IČO ${ico}`);
  if (!search) {
    return null;
  }
  const results = Array.isArray(search.results) ? search.results : [];
  let found = null;
  for (let i = 0; i < results.length; i++) {
    if (hasIdentifier(results[i], ico)) {
      found = results[i];
      break;
    }
  }
  if (!found) {
    log(`The Register of Legal Entities has no entity with IČO ${ico}.`);
    return null;
  }

  const entity = get(`${RPO_URL}/entity/${found.id}?showHistoricalData=false&showOrganizationUnits=false`, `get entity ${found.id} (IČO ${ico})`);
  if (!entity) {
    return null;
  }

  // Without history, a dissolved entity has no current name or address: use the last ones
  // from the search result, which includes history.
  const name = current(entity.fullNames) || current(found.fullNames);
  const address = current(entity.addresses) || current(found.addresses);
  const legalForm = current(entity.legalForms);
  const legalStatus = current(entity.legalStatuses);
  const sourceRegister = entity.sourceRegister || found.sourceRegister || {};
  const office = current(sourceRegister.registrationOffices);
  const fileNumber = current(sourceRegister.registrationNumbers);
  const mainActivity = entity.statisticalCodes ? entity.statisticalCodes.mainActivity : null;
  let capital = null;
  const equities = Array.isArray(entity.equities) ? entity.equities : [];
  for (let i = 0; i < equities.length; i++) {
    if (equities[i] && typeof equities[i].value === "number" && !equities[i].validTo) {
      capital = equities[i];
      break;
    }
  }

  return {
    "rpo.ico": ico,
    "rpo.name": name ? clean(name.value) : null,
    "rpo.legalForm": codelistValue(legalForm),
    "rpo.legalFormCode": codelistCode(legalForm),
    "rpo.legalStatus": codelistValue(legalStatus),
    "rpo.address": formatAddress(address),
    "rpo.municipality": address && address.municipality ? clean(address.municipality.value) : null,
    "rpo.postalCode": address && Array.isArray(address.postalCodes) ? clean(address.postalCodes[0]) : null,
    "rpo.country": address && address.country ? clean(address.country.value) : null,
    "rpo.establishmentDate": dateOnly(entity.establishment || found.establishment),
    "rpo.terminationDate": dateOnly(entity.termination || found.termination),
    "rpo.mainActivity": mainActivity ? clean(mainActivity.value) : null,
    "rpo.mainActivityCode": mainActivity ? clean(mainActivity.code) : null,
    "rpo.sourceRegister": sourceRegister.value ? clean(sourceRegister.value.value) : null,
    "rpo.registrationOffice": office ? clean(office.value) : null,
    "rpo.registrationNumber": fileNumber ? clean(fileNumber.value) : null,
    "rpo.registeredCapital": capital ? capital.value : null,
    "rpo.registeredCapitalCurrency": capital && capital.currency ? clean(capital.currency.code) : null,
  };
}

function run() {
  const ico = parseIco(getValue(ICO_KEY));
  if (!ico) {
    // No valid IČO on this record (this is also what Test connection hits, because it passes
    // placeholder values instead of real ones).
    return [];
  }

  // Only the properties are cached, not the full API responses, which can be large.
  const cacheKey = `rpo:${ico}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let data = cached ? parseJson(String(cached)) : null;

  if (!data) {
    data = lookUp(ico);
    if (!data) {
      return [];
    }
    // Leave out values the register didn't return, so they don't overwrite existing data with blanks.
    Object.keys(data).forEach((key) => {
      if (data[key] === null) {
        delete data[key];
      }
    });
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(data), CACHE_MILLISECONDS);
    }
  }

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
