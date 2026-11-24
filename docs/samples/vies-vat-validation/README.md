# VIES VAT number validation (REST API enricher V2)

`vies-process-script.js` is a V2 Process Script that checks an organization's EU VAT number
against the European Commission's VIES service and adds `vies.*` properties to the golden record.

The settings, the properties it adds, how it handles errors, and its limitations are documented in
one place, the user documentation:
[REST API enricher samples › VIES (EU VAT validation)](https://documentation.cluedin.net/govern/rest-api-enricher-samples/vies).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/ViesProcessScriptTests.cs` runs it with the same
script engine, limits and result handling as the enricher, so a change to the enricher that breaks
the script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
