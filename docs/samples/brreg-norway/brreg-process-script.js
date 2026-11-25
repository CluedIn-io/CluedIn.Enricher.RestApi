// REST API enricher (V2) - Norwegian company lookup with the Brønnøysund Register Centre
//
// Looks up a golden record's Norwegian organization number in the Central Coordinating
// Register for Legal Entities (Enhetsregisteret), using the Brønnøysund Register Centre's
// open-data API (https://data.brreg.no/enhetsregisteret/api/docs/index.html), and adds the
// result to the golden record as brreg.* properties:
//
//   brreg.organizationNumber  9-digit organization number that was looked up
//   brreg.name                registered name
//   brreg.organizationForm    organization form, for example Allmennaksjeselskap
//   brreg.organizationFormCode organization form code, for example ASA
//   brreg.businessAddress     business address on one line
//   brreg.postalAddress       postal address on one line
//   brreg.municipality        municipality of the business address
//   brreg.countryCode         country code of the business address
//   brreg.industryCode        main industry code (NACE, SN2007), for example 06.100
//   brreg.industry            main industry description (Norwegian)
//   brreg.employees           number of employees
//   brreg.website             website
//   brreg.foundationDate      date the entity was founded (YYYY-MM-DD)
//   brreg.registrationDate    date it was registered in Enhetsregisteret (YYYY-MM-DD)
//   brreg.vatRegistered       "True" / "False" - registered in the VAT register
//   brreg.bankrupt            "True" / "False"
//   brreg.underLiquidation    "True" / "False"
//   brreg.deleted             "True" when the entity has been deleted from the register
//   brreg.deletionDate        date it was deleted (YYYY-MM-DD)
//
// No API key is needed. See README.md next to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid organization number, when
// the number isn't registered (or has been removed from the public register), or when the API
// can't answer.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const ORGANIZATION_NUMBER_KEY = "organization.organizationNumber";

const BRREG_URL = "https://data.brreg.no/enhetsregisteret/api/enheter/";

// How long to reuse an answer for the same organization number. 0 turns caching off.
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

// Accepts "923 609 016", "923609016", "NO 923 609 016 MVA", etc. Returns the 9 digits, or null
// when the number isn't a valid Norwegian organization number (MOD11 check digit).
function parseOrganizationNumber(raw) {
  const digits = raw.toUpperCase().replace(/^NO/, "").replace(/MVA$/, "").replace(/[^0-9]/g, "");
  if (!/^[89]\d{8}$/.test(digits)) {
    return null;
  }
  const weights = [3, 2, 7, 6, 5, 4, 3, 2];
  let sum = 0;
  for (let i = 0; i < 8; i++) {
    sum += Number(digits.charAt(i)) * weights[i];
  }
  const remainder = sum % 11;
  const checkDigit = remainder === 0 ? 0 : 11 - remainder;
  return checkDigit !== 10 && checkDigit === Number(digits.charAt(8)) ? digits : null;
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

function dateOnly(value) {
  const text = clean(value);
  return text ? text.substring(0, 10) : null;
}

function formatAddress(address) {
  if (!address) {
    return null;
  }
  const lines = Array.isArray(address.adresse) ? address.adresse.filter((x) => !!clean(x)) : [];
  const postalTown = [clean(address.postnummer), clean(address.poststed)].filter((x) => !!x).join(" ");
  if (postalTown) {
    lines.push(postalTown);
  }
  if (clean(address.landkode) && address.landkode !== "NO" && clean(address.land)) {
    lines.push(address.land);
  }
  return lines.length > 0 ? lines.join(", ") : null;
}

// Calls the API. Returns the entity, or null when there's no answer to store.
function lookUp(organizationNumber) {
  const apiResponse = http.send({
    url: BRREG_URL + organizationNumber,
    method: "GET",
    headers: [{ Key: "Accept", Value: "application/json" }],
  });

  const status = String(apiResponse.HttpStatus || "");
  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - the Brønnøysund Register Centre API is busy, the record will be retried");
  }

  // A deleted entity is still returned (with OK), with its name and deletion date (slettedato).
  if (status === "OK" && body && body.organisasjonsnummer) {
    return body;
  }

  // 410 Gone: the entity has been removed from the public register for legal reasons. The
  // register asks that copies aren't kept, so nothing is stored or cached.

  log(`Brønnøysund Register Centre could not look up ${organizationNumber}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function run() {
  const organizationNumber = parseOrganizationNumber(getValue(ORGANIZATION_NUMBER_KEY));
  if (!organizationNumber) {
    // No valid organization number on this record (this is also what Test connection hits,
    // because it passes placeholder values instead of real ones).
    return [];
  }

  const cacheKey = `brreg:${organizationNumber}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let entity = cached ? parseJson(String(cached)) : null;

  if (!entity) {
    entity = lookUp(organizationNumber);
    if (!entity) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(entity), CACHE_MILLISECONDS);
    }
  }

  const form = entity.organisasjonsform || {};
  const industry = entity.naeringskode1 || {};
  const businessAddress = entity.forretningsadresse || entity.beliggenhetsadresse || null;
  const deletionDate = dateOnly(entity.slettedato);

  const data = {
    "brreg.organizationNumber": clean(entity.organisasjonsnummer),
    "brreg.name": clean(entity.navn),
    "brreg.organizationForm": clean(form.beskrivelse),
    "brreg.organizationFormCode": clean(form.kode),
    "brreg.businessAddress": formatAddress(businessAddress),
    "brreg.postalAddress": formatAddress(entity.postadresse),
    "brreg.municipality": businessAddress ? clean(businessAddress.kommune) : null,
    "brreg.countryCode": businessAddress ? clean(businessAddress.landkode) : null,
    "brreg.industryCode": clean(industry.kode),
    "brreg.industry": clean(industry.beskrivelse),
    "brreg.employees": typeof entity.antallAnsatte === "number" ? entity.antallAnsatte : null,
    "brreg.website": clean(entity.hjemmeside),
    "brreg.foundationDate": dateOnly(entity.stiftelsesdato),
    "brreg.registrationDate": dateOnly(entity.registreringsdatoEnhetsregisteret),
    "brreg.vatRegistered": typeof entity.registrertIMvaregisteret === "boolean" ? entity.registrertIMvaregisteret : null,
    "brreg.bankrupt": typeof entity.konkurs === "boolean" ? entity.konkurs : null,
    "brreg.underLiquidation": typeof entity.underAvvikling === "boolean" ? entity.underAvvikling : null,
    "brreg.deleted": deletionDate ? true : null,
    "brreg.deletionDate": deletionDate,
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
