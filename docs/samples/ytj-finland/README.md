# Finnish Business Information System company lookup, Finland (REST API enricher V2)

`ytj-process-script.js` is a V2 Process Script that looks up an organization's Finnish Business
ID (Y-tunnus) in the Finnish Business Information System (YTJ) by using the Finnish Patent and
Registration Office's open-data YTJ API and adds `ytj.*` properties to the golden record.

The settings, the properties it adds, a sample request and response, how it handles errors, and
its limitations are documented in one place, the user documentation:
[REST API enricher samples › Finnish Business Information System (Finland)](https://documentation.cluedin.net/govern/rest-api-enricher-samples/ytj-finland).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/YtjProcessScriptTests.cs` runs it with the same
script engine, limits and result handling as the enricher, so a change to the enricher that breaks
the script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
