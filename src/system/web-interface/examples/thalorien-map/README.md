# Thalorien map page

One self-contained page: `index.html`. No assets, no build step, no dev server.

- **World and Region scopes.** The continent art is inlined once; drilling into a region swaps the
  SVG `viewBox` to that region's window, so region art and world art are the same drawing and
  cannot disagree.
- **Placement is an authored table keyed by world entity id.** Nothing is derived from list order,
  so a place is always at the same point. A place with no entry is left off the map.
- **The base carries no text.** Every label is HTML drawn over the map, so the base cannot leak a
  name that marker filtering is meant to hide.
- Selecting a marker reads `/api/data/entity/<id>` for the live summary and degrades to a plain
  message if the read fails. The page performs no world write.

## Audience (Slice 2)

A published page cannot enforce an audience: whatever is embedded is shipped, and hiding a record
with JavaScript is not secrecy. So this page carries **public world information only**, and that is
checked against the live world rather than assumed:

```
node verify-audience.mjs                 # warns about draft records
node verify-audience.mjs --drafts=fail   # treat drafts as a failure
node verify-audience.mjs --host http://localhost:6217
```

It fails if any embedded place is missing from the live world, is not `visibility: public`, is
archived, or if the page contains a secret record. Run it on the machine hosting the site, before
publishing.

**Known state:** 23 of the 24 embedded places are still `status: "draft"` (only Brackenford is
`active`). The check warns rather than fails, because whether draft world records may appear on a
published page is an authoring decision, not a safety one.

## Publish

```powershell
Invoke-RestMethod -Uri 'http://localhost:6217/api/pages/thalorien-map' -Method Put `
  -ContentType 'text/html; charset=utf-8' -InFile '.\index.html'
```

Then open `http://localhost:6217/ui/thalorien-map`.

## Not built

City and Location scopes have no live data to hang on — no districts or interiors exist beneath any
settlement. Campaign overlays have no records. Generated location art needs a media store with
provenance and an approval gate.
