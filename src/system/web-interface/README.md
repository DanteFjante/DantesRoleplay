# Web interface

The existing ASP.NET host serves trusted HTML pages stored in SQLite. Uploading a page creates and
activates an immutable revision; it does not require a rebuild or restart.

## Upload and open a page

With the host running, upload a complete self-contained HTML file:

```powershell
Invoke-RestMethod `
  -Uri 'http://localhost:6217/api/pages/character-sheet' `
  -Method Put `
  -ContentType 'text/html; charset=utf-8' `
  -InFile '.\character-sheet.html'
```

Open `http://localhost:6217/ui/character-sheet`. Uploading the same ID again appends and activates
the next revision.

## Read dynamic data from a page

Read a complete entity with every attached component:

```javascript
const character = await fetch("/api/data/entity/creature.orban").then(response => response.json());
```

Read one component by using its component-definition ID as the data type:

```javascript
const inventory = await fetch("/api/data/inventory/creature.orban").then(response => response.json());
```

The component endpoint returns the stored JSON object directly. The web project has no list of
game-specific types and does not translate unknown fields.

## Current boundary

This first slice accepts self-contained HTML only. Separate assets, ZIP uploads, SSE, authentication,
sandboxing, CSP hardening, and game-state write endpoints are later features.
