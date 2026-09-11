import assert from "node:assert/strict";
import test from "node:test";
import React, { act } from "react";
import { JSDOM } from "jsdom";
import { CharacterOverview } from "../../src/components/character/CharacterOverview";
import { FeatureGroups } from "../../src/components/character/CharacterSheet";
import type { CanonicalCharacterData, CharacterSheetData, PartyMemberReadModel } from "../../src/data/hub-types";

const reference = { id: "actor.synthetic", label: "Synthetic Hero" };

const sheet: CanonicalCharacterData = {
  version: 2,
  subject: reference,
  identity: { biography: "A surviving biography remains visible." },
  features: [{ feature: { id: "feature.synthetic", label: "Synthetic Feature" },
    grantedBy: { id: "class.synthetic", label: "Synthetic Class" },
    grantKind: { id: "feat", label: "Feat" }, classLevel: 1 }],
  dossier: {
    coverage: "partial",
    unavailableSections: ["origin", "levelOneRules"],
    features: [{
      definition: { id: "feature.synthetic", label: "Synthetic Feature", canonicalName: "Synthetic Feature",
        kind: "feature", status: "active", summary: "A useful feature description.", source: null },
      grantedBy: { id: "class.synthetic", label: "Synthetic Class", canonicalName: "Synthetic Class",
        kind: "class", status: "active", summary: null, source: null },
      grantKind: "feat", classLevel: 1, configurationKey: null,
      implementation: { status: "executable", reason: null, entitlementKey: "feature.synthetic", nextCapabilityId: null },
    }],
  },
};

const member: PartyMemberReadModel = {
  id: reference.id, initials: "SH", name: reference.label, detail: "Synthetic hero", status: "active",
  isCurrent: true, recordStatus: "Canonical character state", sheetStatus: "canonical", inventoryStatus: "empty",
  sheetState: { status: "ready", data: [], source: "canonical" },
  inventoryState: { status: "empty", data: [], source: "canonical" },
  sheet: [], knowledge: [], backstory: [], origin: [], inventory: [], characterSheet: sheet,
};

async function mount(node: React.ReactElement) {
  const dom = new JSDOM("<html><body><div id='root'></div></body></html>", { pretendToBeVisual: true });
  const keys = ["window", "document", "HTMLElement", "Element", "Node", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const previous = keys.map((key) => Object.getOwnPropertyDescriptor(globalThis, key));
  for (const key of keys) Object.defineProperty(globalThis, key, { configurable: true, writable: true,
    value: key === "IS_REACT_ACT_ENVIRONMENT" ? true : dom.window[key as keyof Window] });
  const { createRoot } = await import("react-dom/client");
  const container = dom.window.document.getElementById("root")!;
  const root = createRoot(container);
  await act(async () => { root.render(node); });
  return { container, async cleanup() { await act(async () => root.unmount()); dom.window.close();
    keys.forEach((key, index) => { if (previous[index]) Object.defineProperty(globalThis, key, previous[index]!);
      else Reflect.deleteProperty(globalThis, key); }); } };
}

test("partial dossier keeps useful feature text and identity presentation mounted", async () => {
  const view = await mount(<><CharacterOverview member={member} onOpenSection={() => {}} />
    <FeatureGroups sheet={sheet as CharacterSheetData} /></>);
  try {
    assert.match(view.container.textContent ?? "", /A surviving biography remains visible/);
    assert.match(view.container.textContent ?? "", /some sections unavailable/);
    assert.match(view.container.textContent ?? "", /A useful feature description/);
    assert.match(view.container.textContent ?? "", /Synthetic Feature/);
  } finally {
    await view.cleanup();
  }

  const missingExecutionEvidence: CanonicalCharacterData = {
    ...sheet,
    dossier: { coverage: "partial", unavailableSections: ["features"], features: [] },
  };
  const unavailable = await mount(<FeatureGroups sheet={missingExecutionEvidence} />);
  try {
    assert.match(unavailable.container.textContent ?? "", /Execution status unavailable/);
    assert.doesNotMatch(unavailable.container.textContent ?? "", /Executable from current canonical state/);
  } finally {
    await unavailable.cleanup();
  }
});

test("feature execution evidence matches both feature and granting source", async () => {
  const secondGrant = { id: "background.synthetic", label: "Synthetic Background" };
  const twoGrantSheet: CanonicalCharacterData = {
    ...sheet,
    features: [...(sheet.features ?? []), {
      feature: { id: "feature.synthetic", label: "Synthetic Feature" },
      grantedBy: secondGrant,
      grantKind: { id: "feat", label: "Feat" },
      classLevel: 1,
    }],
    dossier: {
      ...sheet.dossier,
      features: [...(sheet.dossier?.features ?? []), {
        definition: sheet.dossier!.features![0].definition,
        grantedBy: { id: secondGrant.id, label: secondGrant.label, canonicalName: secondGrant.label,
          kind: "background", status: "active", summary: null, source: null },
        grantKind: "feat", classLevel: 1, configurationKey: null,
        implementation: { status: "pending", reason: "choose-source", entitlementKey: null, nextCapabilityId: null },
      }],
    },
  };
  const view = await mount(<FeatureGroups sheet={twoGrantSheet as CharacterSheetData} />);
  try {
    const text = view.container.textContent ?? "";
    assert.equal((text.match(/Executable from current canonical state/g) ?? []).length, 1);
    assert.match(text, /Pending: choose source/);
  } finally {
    await view.cleanup();
  }
});

test("dossier-only feature evidence remains visible when sheet feature rows are unavailable", async () => {
  const dossierOnly: CanonicalCharacterData = {
    ...sheet,
    features: undefined,
    dossier: {
      ...sheet.dossier,
      features: [{ ...sheet.dossier!.features![0], implementation: undefined }],
      origin: { traits: [{ key: "trait.synthetic", label: "Synthetic Trait", status: "active",
        reason: null, mechanicId: null, source: null }] },
    },
  };
  const view = await mount(<FeatureGroups sheet={dossierOnly as CharacterSheetData} />);
  try {
    const text = view.container.textContent ?? "";
    assert.match(text, /Synthetic Feature/);
    assert.match(text, /A useful feature description/);
    assert.match(text, /Synthetic Trait/);
    assert.match(text, /Execution status unavailable/);
    assert.doesNotMatch(text, /Executable from current canonical state/);
  } finally {
    await view.cleanup();
  }
});
