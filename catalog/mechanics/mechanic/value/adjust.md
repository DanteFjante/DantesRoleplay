---
id: mechanic.value.adjust
category: change
name: Adjust a number
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap rule file."
---

## Description
Adds to or subtracts from one of the subject's numbers, optionally clamped. The other half of most
rules: something was decided, now the world changes.

## Matches
spend
lose
gain
recover
restore
reduce
increase

## Requirements
```json
{
  "roles": {
    "subject": {
      "components": ["fixture.legacy.stats"],
      "description": "Whose number is changing."
    }
  }
}
```
