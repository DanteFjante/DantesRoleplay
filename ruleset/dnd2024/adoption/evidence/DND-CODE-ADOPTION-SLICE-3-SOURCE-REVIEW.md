# D&D code-adoption Slice 3 source review — raw fixed-DC ability check

Status: **verified for test-only Slice 3 planning, 2026-08-25**
Source ID: `source.dnd2024.srd-5.2.1`
Official document: [System Reference Document 5.2 PDF](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf)
Reviewed PDF SHA-256: `CF18E1F88A360646940B6FADA63FD1BD04C1C02581CA668A9115C2F4577BF8AA`
Foundry pin: `275bed0be4ccfa15e6b3347acccb8da8784726d9`
Donor pin: `ead852b19b9e45f54f43e193caf4f10aad91a91b`

## Exact SRD 5.2.1 locators

| Locator | Printed PDF page | Meaning used by Slice 3 |
| --- | ---: | --- |
| `Playing the Game > The Six Abilities > Ability Scores` | 5 | Six scores describe a creature's abilities; the stated range reaches 1–30. |
| `Playing the Game > The Six Abilities > Ability Modifiers` and `Ability Modifiers` table | 5–6 | Each score has a derived modifier; the table corresponds to rounding down `(score - 10) / 2`. |
| `Playing the Game > D20 Tests` | 6 | Roll 1d20, add the relevant ability modifier and other applicable modifiers, then compare the total with the target number. |
| `Playing the Game > D20 Tests > Ability Checks > Ability Modifier` | 6 | A raw ability check is named for and uses the selected ability modifier. |
| `Playing the Game > D20 Tests > Ability Checks > Difficulty Class` | 6 | The GM/rule supplies the DC; a D20 Test succeeds when its total equals or exceeds the target. |
| `Playing the Game > D20 Tests > Attack Rolls > Rolling 20 or 1` | 7 | Automatic natural-20/natural-1 outcomes are stated for attack rolls. Slice 3 infers no such special branch for an ability check and follows the general total-versus-DC rule. |

The PDF pages were visually inspected as laid out, including the ability-modifier table and the
separate attack-roll natural-20/natural-1 section. No 2014 rule, optional rule, house rule,
proficiency, Advantage/Disadvantage, or consequence rule is used by the selected probe.

## Foundry dnd5e engineering review

Exact file: [`module/dice/d20-roll.mjs`](https://github.com/foundryvtt/dnd5e/blob/275bed0be4ccfa15e6b3347acccb8da8784726d9/module/dice/d20-roll.mjs),
Git blob `33d1551d5ed8fcc1aaac6a28d1238101d71b2035`.

Useful engineering evidence at the pinned commit:

- the d20 formula is assembled from a die plus modifier parts, while the target is carried
  separately;
- normal, Advantage, and Disadvantage are explicit modes, and neither flag resolves to normal;
- target and roll-mode configuration are applied to the die before evaluation; and
- critical/fumble presentation is separately gated, so Slice 3 does not copy it into a raw ability
  check.

Foundry remains `reference-only`. No code, assets, runtime dependencies, or rule meaning are copied.

## Standalone donor review

- [`src/derive/ability.ts`](https://github.com/greghcarr/dnd-srd-engine/blob/ead852b19b9e45f54f43e193caf4f10aad91a91b/src/derive/ability.ts),
  Git blob `b18fed9555c670e41045b3c8f3f9c791d62f821f`, contains a bounded integer 1–30
  `abilityModifier` pure function using `Math.floor((score - 10) / 2)`. It is suitable as a parity
  comparator, but first-party archive recovery has precedence for the wrapper.
- [`src/derive/ability-check.ts`](https://github.com/greghcarr/dnd-srd-engine/blob/ead852b19b9e45f54f43e193caf4f10aad91a91b/src/derive/ability-check.ts),
  Git blob `bce546d51233258a8cf991c9fb3b33b255e3d3f5`, depends on the donor Character,
  content pack, item instances, effect stack, levels, proficiency, conditions, and consumer facts.
  It is outside the selected closed seam and must not be imported or executed by Slice 3.
- [`src/rng/dice.ts`](https://github.com/greghcarr/dnd-srd-engine/blob/ead852b19b9e45f54f43e193caf4f10aad91a91b/src/rng/dice.ts),
  Git blob `db5dd5e8bd83a512c2d45430d6bc6afa73f6a834`, confirms an injected RNG design but
  is unnecessary because the kernel already owns seeded `ctx.randomInt`.

## Planning conclusion

The exact rule and engineering evidence needed by Slice 3 is available. The safe shortcut is to
reuse the current structural-projection and Jint RNG owners, recover only the narrowed first-party
JavaScript behavior, and treat donor/Foundry implementations as comparison/reference evidence.
This review authorizes no runtime source record, rule, schema, projection, or activation.
