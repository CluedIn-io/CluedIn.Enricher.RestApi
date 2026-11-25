# Open-DBD company lookup, Thailand (REST API enricher V2)

`dbd-process-script.js` is a V2 Process Script that looks up an organization's 13-digit juristic
person registration number in the register of Thailand's Department of Business Development (DBD),
using the juristic person API listed on the Open-DBD data portal, and adds `dbd.*` properties to the
golden record.

The settings, the properties it adds, a sample request and response, how it handles errors, and
its limitations are documented in one place, the user documentation:
[REST API enricher samples › Open-DBD (Thailand)](https://documentation.cluedin.net/govern/rest-api-enricher-samples/dbd-thailand).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/DbdProcessScriptTests.cs` runs it with the same
script engine, limits and result handling as the enricher, so a change to the enricher that breaks
the script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
