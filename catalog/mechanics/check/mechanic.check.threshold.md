---
id: mechanic.check.threshold
category: check
name: Test a value against a threshold
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap rule file."
---

## Description
Rolls 1–20, adds one of the subject's numbers, and compares the total to a threshold. The generic
"can they manage it?" rule that most other rules end up leaning on.

## Matches
check
test
try to
attempt
can they
roll for

## Requirements
```json
{
  "roles": {
    "subject": {
      "components": ["stats"],
      "description": "Whoever is attempting the thing."
    }
  }
}
```
