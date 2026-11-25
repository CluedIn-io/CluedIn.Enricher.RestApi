// REST API enricher (V2) - Philippine company lookup with the SEC API Marketplace
//
// Looks up a golden record's SEC registration number with the Philippine Securities and
// Exchange Commission's SECNumber API on the SEC API Marketplace (https://portal.sec.gov.ph/),
// and adds the result to the golden record as secph.* properties:
//
//   secph.secNumber      SEC registration number that was looked up, for example A199600179
//   secph.name           registered company name
//   secph.dateApproved   date the registration was approved
//   secph.status         registration status
//   secph.licenses       secondary licenses
//
// Credentials go in the enricher's API Key field, in one of two forms:
//   consumerKey:consumerSecret  (recommended) the Production Keys of your Marketplace
//                               application. The script gets an access token from the token
//                               endpoint itself and reuses it until shortly before it expires.
//   accessToken                 an access token you generated yourself. It expires, after
//                               which every record fails until you replace it.
// See README.md next to this file for the enricher settings.
//
// A record gets no result (nothing is stored) when it has no valid SEC number, when the SEC has
// no company with that number, or when the API can't answer. Missing or rejected credentials
// fail the record with an error that says so.

// ---- Settings -------------------------------------------------------------------------

// Vocabulary key to read. Add the same key to the Vocabulary and Properties field.
const SEC_NUMBER_KEY = "organization.secRegistrationNumber";

const SEC_URL = "https://gwwso2.sec.gov.ph/secnumber/1.0.0/client_sec_no_status.php";
const TOKEN_URL = "https://portal.sec.gov.ph/oauth2/token";

// The {APIKey} placeholder is replaced with the API Key field value.
const API_KEY = "{APIKey}";

// How long to reuse an answer for the same SEC number. 0 turns caching off. The free SECNumber
// package allows only a few calls a day, so a long cache saves calls.
const CACHE_MILLISECONDS = 30 * 24 * 60 * 60 * 1000;

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

// Accepts "A199600179", "a1996-00179", "CS 2019 12345". Returns the number in capitals without
// spaces or dashes, or null when it doesn't look like an SEC registration number (up to 4
// letters followed by 5 to 12 digits).
function parseSecNumber(raw) {
  const text = raw.toUpperCase().replace(/[\s.\-/]/g, "");
  return /^[A-Z]{0,4}\d{5,12}$/.test(text) ? text : null;
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
  const us = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(text);
  if (us) {
    return `${us[3]}-${us[1].padStart(2, "0")}-${us[2].padStart(2, "0")}`;
  }
  return text;
}

function credentials() {
  const value = clean(API_KEY) || "";
  const separator = value.indexOf(":");
  if (separator > 0) {
    return { consumerKey: value.substring(0, separator), consumerSecret: value.substring(separator + 1) };
  }
  return value ? { accessToken: value } : null;
}

// Gets an access token with the OAuth2 client credentials grant.
function requestToken(creds) {
  const tokenResponse = http.send({
    url: TOKEN_URL,
    method: "POST",
    headers: [
      { Key: "Content-Type", Value: "application/x-www-form-urlencoded" },
      { Key: "Accept", Value: "application/json" },
    ],
    body:
      "grant_type=client_credentials" +
      `&client_id=${encodeURIComponent(creds.consumerKey)}` +
      `&client_secret=${encodeURIComponent(creds.consumerSecret)}` +
      "&scope=openid",
  });

  const status = String(tokenResponse.HttpStatus || "");
  const body = parseJson(tokenResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - the SEC token endpoint is busy, the record will be retried");
  }

  if (status !== "OK" || !body || !body.access_token) {
    const reason = body && (body.error_description || body.error) ? `${body.error || ""} ${body.error_description || ""}`.trim() : `HTTP ${status}`;
    // A 403 HTML page (rather than a JSON error) means the service is unavailable, not that
    // the credentials are wrong.
    if (status === "BadRequest" || status === "Unauthorized" || (status === "Forbidden" && body)) {
      throw new Error(`The SEC token endpoint rejected the consumer key and secret in the API Key field (${reason}).`);
    }
    log(`Could not get an SEC access token. ${reason}. ${String(tokenResponse.Content || "").substring(0, 200)}`);
    return null;
  }

  return { accessToken: String(body.access_token), expiresIn: Number(body.expires_in) || 3600 };
}

// Returns an access token, from the cache when there's one that hasn't expired.
function getAccessToken(creds, forceNew) {
  if (creds.accessToken) {
    return creds.accessToken;
  }

  const cacheKey = `secph:token:${creds.consumerKey}`;
  if (!forceNew) {
    const cached = cache.get(cacheKey);
    if (cached) {
      return String(cached);
    }
  }

  const token = requestToken(creds);
  if (!token) {
    return null;
  }
  // Reuse the token until 5 minutes before it expires (at least 1 minute).
  const lifetime = Math.max(60, token.expiresIn - 300) * 1000;
  if (!forceNew) {
    cache.set(cacheKey, token.accessToken, lifetime);
  }
  return token.accessToken;
}

function callApi(secNumber, accessToken) {
  return http.send({
    url: `${SEC_URL}?sec_no=${encodeURIComponent(secNumber)}`,
    method: "GET",
    headers: [
      { Key: "Authorization", Value: `Bearer ${accessToken}` },
      { Key: "Accept", Value: "application/json" },
    ],
  });
}

// Calls the SECNumber API. Returns the company, or null when there's no answer to store.
function lookUp(secNumber, creds) {
  const accessToken = getAccessToken(creds, false);
  if (!accessToken) {
    return null;
  }

  let apiResponse = callApi(secNumber, accessToken);
  let status = String(apiResponse.HttpStatus || "");

  // A cached token can stop working before it expires (for example when the keys are
  // regenerated). Try once more with a new token.
  if (status === "Unauthorized" && !creds.accessToken) {
    const newToken = getAccessToken(creds, true);
    if (!newToken) {
      return null;
    }
    apiResponse = callApi(secNumber, newToken);
    status = String(apiResponse.HttpStatus || "");
  }

  const body = parseJson(apiResponse.Content);

  if (status === "TooManyRequests") {
    throw new Error("TooManyRequests - the SEC API is busy or the daily call limit was reached, the record will be retried");
  }

  // 401 is a missing, expired or wrong token. A 403 with a JSON body is a token that isn't
  // subscribed to the SECNumber API. (A 403 HTML page means the service is unavailable.)
  if (status === "Unauthorized" || (status === "Forbidden" && body)) {
    const message = body && body.description ? body.description : String(apiResponse.Content || "").substring(0, 200);
    throw new Error(`The SEC API rejected the credentials in the API Key field (HTTP ${status}). ${message}`);
  }

  if (status === "OK" && body) {
    const items = Array.isArray(body.data) ? body.data : Array.isArray(body) ? body : [];
    for (let i = 0; i < items.length; i++) {
      if (items[i] && parseSecNumber(String(items[i].sec_no || "")) === secNumber) {
        return items[i];
      }
    }
    if (items.length === 1 && items[0] && !items[0].sec_no) {
      return items[0];
    }
  }

  log(`The SEC API could not look up ${secNumber}. HTTP ${status}. ${String(apiResponse.Content || "").substring(0, 200)}`);
  return null;
}

function run() {
  const secNumber = parseSecNumber(getValue(SEC_NUMBER_KEY));
  if (!secNumber) {
    // No valid SEC number on this record (this is also what Test connection hits, because it
    // passes placeholder values instead of real ones).
    return [];
  }

  const creds = credentials();
  if (!creds) {
    log("The SEC API needs credentials. Enter consumerKey:consumerSecret (or an access token) in the enricher's API Key field.");
    return [];
  }

  const cacheKey = `secph:${secNumber}`;
  const cached = CACHE_MILLISECONDS > 0 ? cache.get(cacheKey) : null;
  let item = cached ? parseJson(String(cached)) : null;

  if (!item) {
    item = lookUp(secNumber, creds);
    if (!item) {
      return [];
    }
    if (CACHE_MILLISECONDS > 0) {
      cache.set(cacheKey, JSON.stringify(item), CACHE_MILLISECONDS);
    }
  }

  const data = {
    "secph.secNumber": clean(item.sec_no) || secNumber,
    "secph.name": clean(item.company_name),
    "secph.dateApproved": formatDate(item.date_approved),
    "secph.status": clean(item.status),
    "secph.licenses": clean(item.licenses),
  };

  // Leave out values the SEC didn't return, so they don't overwrite existing data with blanks.
  Object.keys(data).forEach((key) => {
    if (data[key] === null) {
      delete data[key];
    }
  });

  return [{ Data: data, Score: 100 }];
}

response = JSON.stringify(run());
