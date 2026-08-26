# D&D code-adoption Slice 7A2 receipt — character proficiency and skill checks

Status: **accepted 2026-08-25**
Implementation: [Slice 7A2](../../DND-CODE-ADOPTION-SLICE-7A2-IMPLEMENTATION.md)

Delivered canonical level and skill-proficiency component records, their activated-path recorders, and
the named-skill extension of the accepted ability check. A named check derives the character
Proficiency Bonus from level, adds it once only for an explicitly listed skill, and remains seeded
and effect-free. Raw checks remain available.

Verification: focused D&D tests **5 passed**; catalog validation passed with 21 existing warnings;
full suite **965 passed**. No live database state or generic C# rule code changed.

Excluded: Expertise, tools, conditions, Advantage/Disadvantage, saving throws, Initiative, class
grants, and every later Parent 7 cohort. Slice 7A3 owns the next D20 circumstance behavior.

