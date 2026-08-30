# DND2024 Campaign entity-link navigation — Slice 17 receipt

Status: **verified**

Campaign recap, outcome, and clue references now navigate to exact records in the currently
projected World. Location references retain their existing detail navigation. Person/creature
references open and focus the exact People card; faction references open, select, and focus the
exact Faction card. Unknown or omitted IDs fail closed before navigation changes.

Evidence:

- focused Campaign/navigation/state tests: **27/27 passed**;
- D&D 2024 server production bundle: **passed**;
- full D&D 2024 web suite: **107/107 passed**.

No campaign record association was invented or written. Live records whose authoritative source
contains no World references still render no links. Places Visited remains empty, and navigation
does not create or imply a visit.
