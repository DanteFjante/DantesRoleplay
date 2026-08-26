---
id: mechanic.dnd2024.item-activity.use
category: ruleset.dnd2024.core.data.item-activity
name: Use fixed physical item activity
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Consumes descriptor-stated quantity and creates its descriptor-stated item in the same container.

## Matches

use item activity

## Requirements

```json
{"roles":{"item":{"components":["dnd2024.item-instance","dnd2024.item-quantity"],"includeContents":true,"contentsDepth":1},"definition":{"components":["dnd2024.item-definition","dnd2024.item-activity"]},"grantDefinition":{"components":["dnd2024.item-definition","dnd2024.item-instance"]}}}
```
