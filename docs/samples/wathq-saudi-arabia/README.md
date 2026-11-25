# Wathq Commercial Registration (Saudi Arabia) (REST API enricher V2)

`wathq-process-script.js` is a V2 Process Script that looks up an organization's Saudi commercial
registration (CR) number with the Ministry of Commerce's Wathq Commercial Registration API and adds
`wathq.*` properties to the golden record. It needs a Wathq API key.

The settings, the properties it adds, a sample request and response, how it handles errors, and
its limitations are documented in one place, the user documentation:
[REST API enricher samples › Wathq Commercial Registration (Saudi Arabia)](https://documentation.cluedin.net/govern/rest-api-enricher-samples/wathq-saudi-arabia).

This folder is the tested copy of the script.
`test/integration/Integration.Tests/Samples/WathqProcessScriptTests.cs` runs it with the same
script engine, limits and result handling as the enricher, so a change to the enricher that breaks
the script fails the build. The documentation page shows the same script; when you change it here,
update the page in CluedIn-io/CluedIn.Documentation too.
