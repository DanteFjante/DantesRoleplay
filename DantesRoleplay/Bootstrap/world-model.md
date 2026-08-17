---
id: procedure.world.model
category: world
name: Model something new in the world
governs: representing a new game concept as data, define_component
status: active
---

## Description
How to represent a new kind of thing — a stat, a condition, an item, a place, a relationship —
without changing the database schema.

## Instructions
1. Decide which of the five structures it is. There are only five, and everything is one of them:
   an **entity** (a thing that exists), a **component** (data attached to a thing), a
   **containment** (a thing inside another thing), a **relationship** (a named link between two
   things), or a **component definition** (declaring that a kind of component may exist).
2. Call `describe_world()` and read the existing component definitions before inventing one. Most
   new ideas are a field on a component that already exists, and two definitions meaning the same
   thing is the failure this system is built to avoid.
3. If it really is new, `define_component(...)` first, then attach it with `apply_effects`.
   Attaching an undeclared component type fails on purpose — it is almost always a typo.
4. Keep entities thin. An entity has an id and a name; everything else is components.
5. Use `component.set` to replace a component's data and `component.merge` to change some keys.
   Know which you want before you call: merge is shallow, and set discards anything you did not
   send.
6. Name definitions for what they hold, not for who holds them. `stats` is reusable; `goblin_stats`
   is a second definition you will regret.

## Constraints
- Never ask for a schema change to add a game concept. If you think you need one, you have
  modelled it wrong — re-read step 1.
- A thing is inside at most one container. Model "carried by two people" as a relationship.
- Component data must be a JSON object, never an array or a bare value.
- Component definition ids are permanent. There is no rename and no delete.
