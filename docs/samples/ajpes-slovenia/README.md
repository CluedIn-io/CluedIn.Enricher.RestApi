# AJPES Business Register company lookup, Slovenia (REST API enricher V2)

`ajpes-process-script.js` is a V2 Process Script that looks up an organization's Slovenian
registration number or tax number in AJPES' restPrsInfo REST API (Slovenian Business Register) and
adds `ajpes.*` properties to the golden record.

The settings, the properties it adds, a sample request and response, how it handles errors, and its
limitations are documented in one place, the user documentation: [REST API enricher samples › AJPES
Business Register
(Slovenia)](https://documentation.cluedin.net/govern/rest-api-enricher-samples/ajpes-slovenia).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/AjpesProcessScriptTests.cs` runs it with the same script
engine, limits and result handling as the enricher, so a change to the enricher that breaks the
script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
