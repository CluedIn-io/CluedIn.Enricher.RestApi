# Instructions for AI assistants

This repository is CluedIn's **REST API enricher**: one configurable enricher that calls an
external HTTP API from a user-written JavaScript script (run by [Jint](https://github.com/sebastienros/jint)).
Most requests to "support service X" do **not** need a code change here. They need a script, the
enricher settings, a test, and a documentation page. This file is the procedure for that, and the
behaviour you have to design around.

## Adding a sample for a new external service

1. **Check the service first.** Prefer its official JSON REST API. Call it yourself to see real
   responses, including errors, empty results and how slow it is. Note whether it needs an API
   key and what its rate limits are.
2. **Write a V2 Process Script** in `docs/samples/<service>/<service>-process-script.js`. V2 (one
   script that makes its own HTTP calls) is the default for new samples; V1 (separate request and
   response scripts) exists for older setups. Put the vocabulary keys, URL and cache duration in
   constants at the top so users can change them.
3. **Add tests** in `test/integration/Integration.Tests/Samples/<Service>ProcessScriptTests.cs`.
   Copy the pattern in `ViesProcessScriptTests.cs`:
   - Embed the script as a resource in the test project (`<EmbeddedResource ... LogicalName="Samples.<file>.js" />`),
     so the tests always run the published sample.
   - Create the engine with **`new Jint.Engine(...)`**, fully qualified, using the same options as
     `InternalExecuteSearchV2` (`LimitRecursion(64)`, `MaxStatements(10_000)`, `TimeoutInterval(120s)`).
     A bare `Engine` resolves to the `CluedIn.Engine` namespace inside `CluedIn.*` and won't compile.
   - Use a fake `http` object that reads the request the way `ScriptHttpClient` does (serialize,
     then deserialize), and parse the script's `response` with
     `JsonConvert.DeserializeObject<ResultsDto[]>`, as the provider does.
   - Cover: normal result, input normalisation, missing input (no HTTP call), each kind of API
     error, retryable errors, caching, and Test connection's placeholder input.
4. **Add a short `README.md`** next to the script saying it is the tested copy, and linking to the
   documentation page. Don't repeat the settings there.
5. **Document it** in CluedIn-io/CluedIn.Documentation as a page under
   `docs/05-govern-and-improve/rest-api-enricher-samples/` (one page per external service; see the
   VIES page for the layout). Follow that repo's `AGENTS.md`/`CONTRIBUTING.md` and run its
   `_migration/validate.py`. The script on the page must be identical to the tested copy here.
6. **Verify it live** on a running CluedIn (see [Verifying in a real environment](#verifying-in-a-real-environment)).

## The V2 script contract

Globals the script gets:

| Global | What it is |
| --- | --- |
| `vocabularies` | A .NET `List<PropertyDto>`: `{ Key, Value }` for each key in the enricher's **Vocabulary and Properties** field. A key appears only if the golden record has a value for it. |
| `http.send(request)` | Synchronous HTTP call. `request` is `{ url, method, headers: [{ Key, Value }], body }`. `method` defaults to GET. An object `body` is sent as JSON unless another `Content-Type` header is given. Returns `{ HttpStatus, Content, ContentType, Headers }`. `HttpStatus` is the .NET `HttpStatusCode` name (`"OK"`, `"BadRequest"`, `"GatewayTimeout"`, `"TooManyRequests"`), not a number. Times out after 60 seconds. |
| `cache` | `get(key)`, `set(key, value, ms)`, `getOrSetValue(...)`, `getOrSetFactory(key, fn, ms)`. Scoped to the organization. `set` does nothing if the key exists. Store strings (for example `JSON.stringify(...)`). |
| `stringEncoder` | `toBase64`, `fromBase64`, `urlEncode`, `urlDecode`. |
| `log(value)` | Writes `User Script log: ...` at Information level in the server log. |

Members can be called in camelCase or PascalCase (`http.send` and `http.Send` both work). Response
fields are PascalCase (`HttpStatus`, `Content`).

Output: set the global `response` to a JSON **string** of `[{ "Data": { "<vocab.key>": value, ... }, "Score": 100 }]`.
An empty string or `"[]"` means no result.

## Behaviour to design around

These all happened while building the VIES sample. Each one is invisible if you only test the
script in Node.

- **`vocabularies.find(...)` inside a function is unreliable.** Because `vocabularies` is a .NET
  list, calling `vocabularies.find(x => x.Key === key)` from a function returns the *first* call's
  match on every later call, whatever `key` is. Look values up with a plain `for` loop.
- **ISO timestamps become dates in the server's format.** A string such as `"2026-11-24T03:44:18.999Z"`
  in `Data` is deserialized to a `DateTime` and stored as `11/24/2026 03:44:18`. Store a date-only
  string (`YYYY-MM-DD`) or another format that isn't parsed.
- **Every value is stored with `.ToString()`.** Booleans become `True`/`False`. Leave out keys with
  no value rather than returning `null` or a placeholder.
- **Keys are rewritten.** Characters other than letters, digits, dots and spaces are removed, and the
  last segment is camel-cased: `vies.vat number` → `vies.vatNumber`, `vies.VAT` → `vies.vat`.
- **Test connection allows 2 seconds and passes `DummyTestValue` for every key.** A script that calls
  a slow API fails Test connection. Treat the placeholder as "no input" and return `"[]"` without
  calling the API; then Test connection checks only that the script runs. (Real enrichment allows
  120 seconds.)
- **10,000 statements maximum.** Avoid looping over large responses character by character.
- **Don't store an outage as a negative result.** If the API can't answer, log the reason and return
  `"[]"`. To make the enricher retry the record instead, throw an error whose message contains
  `TooManyRequests`; the enricher retries every second, up to 1,000 times. Use that only for
  rate-limit or busy responses. Any other exception fails the record.
- **"No result" shows as an error in the UI.** Each record that returns `"[]"` is logged as
  `enricher-exception` ("returned no results") on the enricher's **Logs** tab. Tell users to add an
  enricher filter so records without the input key aren't sent.
- **`Score`** is added to the results as a confidence score property when the enricher sends
  results to its landing zone (results appear on its **Preview** tab) or **Include Confidence Score** is on.

## Building and testing

- `dotnet build` / `dotnet test` need credentials for CluedIn's private NuGet feeds (CluedIn.Core
  and the testing packages come from there). A scratch project that only uses nuget.org won't catch
  problems such as the `Engine` namespace clash above. Run the real `Integration.Tests` project.
- In CI, the **Multi-version build+test** jobs do not compile `test/integration`. Only the
  **Integration tests** jobs do, for CluedIn 4.7.0, 4.8.0 and 5.0.0, so a test that doesn't compile
  shows up there. The repo uses `LangVersion` 13, and the 4.x legs target net6.0.

## Verifying in a real environment

1. In the UI, add a **REST API** enricher: business domain, Version **V2**, and the vocabulary keys in
   **Vocabulary and Properties**. The Process Script can only be edited after the enricher is
   created, on its **Configuration › Scripts** page. Then run **Test connection**.
2. Make sure golden records exist that have the input keys (the normal case: enrichment adds to
   existing golden records). Records arrive through any data source. For a quick test you can post
   clues to the public API (`POST /api/v1/clue?save=true`, JSON). Property keys inside
   `entityData.properties` must be prefixed with `property-` (for example `"property-organization.vat"`),
   or the values are ignored and the script gets an empty `vocabularies`.
3. With the enricher active, processing those records runs it (or use the **Enrich** button). The
   results appear on the enricher's **Preview** tab first. They reach golden records only after you
   **Map** them (Auto Mapping into a vocabulary whose key prefix matches your `Data` keys) and
   **Process** them. Auto Mapping may also use a `name` column as the golden record name.
4. Check that the result merged into the **existing** golden record: one record, with both its
   original source and the REST API source on its **Properties** page.
