# D&D 2024 map slice 22 receipt — non-sticky detail and click-away selection

Status: **accepted**
Ruleset alignment: **ruleset-neutral**

## Delivered boundary

- Changed the selected-place panel from viewport-sticky positioning to normal page flow.
- Added map-canvas click-away behavior that clears the existing feature selection.
- Kept marker selection stable by stopping marker click propagation before selecting the exact feature.
- Kept closer-map double-click behavior stable and stopped its propagation as well.
- Added a focused presentation regression test for the layout and interaction contract.
- Published the corrected page as active `dnd2024-play` revision 22 on top of the newer revision 21 work.

## Evidence

| Check | Result |
| --- | --- |
| Focused map presentation tests | 4 passed, 0 failed |
| Full D&D web suite | 141 passed, 0 failed |
| Server bundle build | passed |
| Live marker selection | Eredane marker reported `aria-pressed="true"` and its full detail rendered |
| Live scroll behavior | panel computed `position: static`; a 457.33 px page scroll moved its viewport top by -457.33 px |
| Live click-away behavior | map-image click changed the marker to `aria-pressed="false"` and restored the empty selection prompt |
| Published bundle | revision 22, 5 assets, SHA-256 `C3C254F0C6AB097D513769765A079C6C9E9601D29F2CC4261144DF11D3BB6B68` |
| Rollback export | revision 21, SHA-256 `E291D57532BCB5AF6E965B71902CC664499269E8F1C7A42CC3EA73961384A3DF` |

## Deliberate exclusions

No map data, coordinates, marker eligibility, map hierarchy, audience filtering, list-mode behavior, route, schema, persistent state, asset, or gameplay rule changed.
