# VIES VAT number validation (REST API enricher V2)

A ready-made setup for the REST API enricher that checks an organization's EU VAT number
against the European Commission's [VIES](https://ec.europa.eu/taxation_customs/vies/) service.
For each golden record it stores whether the number is valid, plus the registered trader
name and address, as `vies.*` properties.

It uses VIES's official JSON REST API (`POST https://ec.europa.eu/taxation_customs/vies/rest-api/check-vat-number`),
not the older SOAP service. No API key or account is needed.

## Enricher settings

| Setting | Value |
| --- | --- |
| Accepted Business Domain | `/Organization` (or whichever business domain holds your companies) |
| Version | `V2 (recommended)` |
| API Key | leave empty |
| Filters | recommended: a rule that only accepts records where `organization.vat` has a value, so records with no VAT number aren't sent to the enricher (otherwise each one is logged as "returned no results" on the enricher's **Logs** tab) |
| Vocabulary and Properties | see below |
| Process Script | the contents of [`vies-process-script.js`](vies-process-script.js) (you can edit it once the enricher is created) |

**Vocabulary and Properties**

```
organization.vat
organization.address.countryCode
```

`organization.vat` holds the VAT number. `organization.address.countryCode` is optional: it is
only used when the VAT number has no country prefix, and must be a 2-letter code. If your
data uses other vocabulary keys, change both this field and the `VAT_NUMBER_KEY` /
`COUNTRY_CODE_KEY` constants at the top of the script.

## What gets stored

| Property | Example | Notes |
| --- | --- | --- |
| `vies.valid` | `True` | `True` or `False` |
| `vies.name` | `GOOGLE IRELAND LIMITED` | Left out when the member state doesn't share it (VIES returns `---`) |
| `vies.address` | `3RD FLOOR, GORDON HOUSE, BARROW STREET, DUBLIN 4` | Address lines joined with `, `. Left out when not shared |
| `vies.countryCode` | `IE` | Country code that was checked. Greece is `EL`, Northern Ireland is `XI` |
| `vies.vatNumber` | `6388047V` | Number that was checked, without the country prefix |
| `vies.requestDate` | `2026-11-24` | Date VIES answered |

With CluedIn's enricher landing zone, these arrive on the enricher's **Preview** tab first.
Map them (Auto Mapping into a new `VIES` vocabulary with key prefix `vies` gives exactly the
keys above) and process them to add them to the golden records.

## How it handles input

- Spaces, dots, dashes and other separators are removed and the number is upper-cased, so
  `ie 6388047-v` becomes country `IE`, number `6388047V`.
- A leading 2-letter VIES country code is used as the country. `GR` is changed to `EL`, the
  code VIES uses for Greece.
- With no prefix, the country comes from `organization.address.countryCode`.
- Records with no VAT number, or a non-EU one (for example `GB...` or `US...`), are skipped
  without calling VIES.

## How it handles errors

- If VIES can't answer (invalid input, member state unavailable, time-out, a non-JSON gateway
  error and so on), the script logs the reason and returns no result. The record is not stored
  as `vies.valid = False` just because VIES was down.
- If VIES says it's too busy (`GLOBAL_MAX_CONCURRENT_REQ`, `MS_MAX_CONCURRENT_REQ`, their `_TIME`
  variants, or HTTP 429), the script throws an error mentioning `TooManyRequests`, which makes the
  enricher retry the record.
- Successful answers are cached for 24 hours per VAT number (`CACHE_MILLISECONDS`) so the same
  number isn't checked repeatedly. Failed checks aren't cached.

## Testing

- **Test connection** only checks that the script runs. It passes placeholder values, which the
  script treats as "no VAT number", so it doesn't call VIES (the test connection run has only
  2 seconds, and a VIES call usually takes longer).
- To try the setup against VIES's test data, temporarily set `VIES_URL` to
  `https://ec.europa.eu/taxation_customs/vies/rest-api/check-vat-test-service`. For example,
  `DE100` returns a valid result. See the VIES technical information page for the other test
  numbers, which return invalid results and each of the VIES errors.
- `test/integration/Integration.Tests/Samples/ViesProcessScriptTests.cs` runs this script
  with the same script engine and limits as the enricher, against a fake HTTP client.

## Limits

- VIES only confirms that a number is registered for intra-EU trade in an EU member state
  (plus Northern Ireland). It doesn't validate other tax IDs or non-EU VAT numbers.
- VIES depends on each member state's own system, which can be slow or unavailable at times.
  A check usually takes several seconds.
- Some member states (for example Germany) don't return the trader name or address.
- This setup doesn't do trader name/address matching or request a consultation number
  (requester identifier).
