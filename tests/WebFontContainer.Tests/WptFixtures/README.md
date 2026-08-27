# WPT css/WOFF2 conformance fixtures

These `.woff2` files are unmodified copies of W3C web-platform-tests fixtures:

- **Origin:** `web-platform-tests` repository, path `css/WOFF2/support/*.woff2`
  (local checkout `C:\PROJECTS\3P_WPT`)
- **WPT commit:** `01af755cb072a07302e07cd12662b49b7f78ba79` (2026-08-17)
- **License:** W3C test-suite artifacts, 3-Clause BSD (see WPT `LICENSE.md`)
- **Consumed by:** `Woff2WptConformanceTests.cs` — decoder-side accept/reject
  verdicts per `AN_SilkyNvg/plans/WOFF_decoder_strictness.md` §3/§4.

Each fixture is the malformed (must-REJECT) or valid-but-unusual (must-ACCEPT)
WOFF2 container used by the WPT reftest of the same name
(`css/WOFF2/{name}.xht`). Only the 40 fixtures named by the plan are copied;
the WPT reftest pages themselves are NOT copied (no WPT runner in this repo).