---
id: mechanic.game.core.world.location.media-attach
category: game.core.media
name: Attach finalized visual-media references to a location
scope: ""
status: active
createdBy: "seed"
changeNote: "Seeded from the embedded catalog mechanic."
---

## Description
Adds caller-authored, already-finalized blob references to one location's ordered visual-media
component. The location may be a shell created by the same atomic composite. It validates the
complete closed attachment shape and preserves prior active entries; it cannot upload bytes,
create a blob, infer alt text, or choose player/GM visibility.

## Matches
attach finalized visual media to a location
add reviewed images to a location

## Requirements
```json
{"roles":{"location":{"components":["game.core.world.location"],"optional":true,"optionalComponents":["game.core.media.visual"],"description":"The exact existing location; omitted only for a same-composite shell."}},"inputSchema":{"type":"object","additionalProperties":false,"required":["attachments"],"properties":{"locationId":{"type":"string","pattern":"^location\\.[a-z0-9][a-z0-9.-]*$","maxLength":200},"locationName":{"type":"string","minLength":1,"maxLength":160},"locationStatus":{"enum":["draft","active"]},"attachments":{"type":"array","minItems":1,"maxItems":64,"items":{"type":"object","additionalProperties":false,"required":["role","visibility","sha256","mimeType","width","height","alt","caption","order","provenance"],"properties":{"role":{"enum":["portrait","setting","map","illustration","icon","scene","handout"]},"visibility":{"type":"array","minItems":1,"maxItems":2,"uniqueItems":true,"items":{"enum":["player","dm"]}},"sha256":{"type":"string","pattern":"^[a-f0-9]{64}$"},"mimeType":{"enum":["image/png","image/jpeg","image/webp"]},"width":{"type":"integer","minimum":1,"maximum":10000},"height":{"type":"integer","minimum":1,"maximum":10000},"alt":{"type":"string","minLength":1,"maxLength":500},"caption":{"type":"string","maxLength":1000},"order":{"type":"integer","minimum":0,"maximum":10000},"provenance":{"type":"object","additionalProperties":false,"required":["kind","credit","source","reviewedOn","version"],"properties":{"kind":{"enum":["generated","original","commissioned","licensed"]},"credit":{"type":"string","minLength":1,"maxLength":500},"source":{"type":"string","minLength":1,"maxLength":500},"reviewedOn":{"type":"string","pattern":"^[0-9]{4}-[0-9]{2}-[0-9]{2}$"},"version":{"type":"integer","minimum":1,"maximum":1000000}}}}}}}}}
```
