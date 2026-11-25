# Enterprise Register company lookup, Latvia (REST API enricher V2)

`lvur-process-script.js` is a V2 Process Script that looks up a Latvian registration number in the Enterprise Register's open data on data.gov.lv and adds `lvur.*` properties to the golden record.

The settings, the properties it adds, a sample request and response, how it handles errors, and
its limitations are documented in one place, the user documentation:
[REST API enricher samples › Enterprise Register (Latvia)](https://documentation.cluedin.net/govern/rest-api-enricher-samples/enterprise-register-latvia).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/LvurProcessScriptTests.cs` runs it with the same
script engine, limits and result handling as the enricher, so a change to the enricher that breaks
the script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
