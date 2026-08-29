# D&D 2024 web UI migration Slice 4 implementation — local information-hub publication

Status: **completed**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), accepted C5 player viewport composition
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**. This slice publishes reviewed browser presentation only.

## Outcome

Publish the reviewed information-hub page and component bundle to the local D&D 2024 play page so the local player view loads the green-and-gold World/Campaign/Party/Current/Rules interface.

## Authority and safety

- The local web-page database remains authoritative for the page record.
- Export a timestamped database/WAL/SHM backup before the page update.
- Publish only the reviewed `dnd2024-play` HTML source after restarting the local host with the reviewed component bundle.
- Verify the served page, main navigation, live workspace load, and computed presentation colors in a browser.

## Exclusions

No campaign data, data projection, audience policy, visibility policy, catalog record, map, or rule record changes are authorized. This is a presentation deployment only.

## Exit gate

Record the backup location, published revision, and browser smoke result in `DND2024-WEB-UI-MIGRATION-SLICE-4-RECEIPT.md`.
