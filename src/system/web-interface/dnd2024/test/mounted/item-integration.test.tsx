import assert from "node:assert/strict";
import test from "node:test";
import React, { act } from "react";
import { JSDOM } from "jsdom";
import { DndInformationHub } from "../../src/components/DndInformationHub";
import { integrationEnvelope, integrationInventory, integrationRead, integrationResponse, type ItemRead } from "../fixtures/item-integration";
import { itemRouteHash, navigateItemRoute, parseItemRoute } from "../../src/data/item-view-route";
import type { InventoryContainerResult } from "../../src/data/hub-types";
const tick = () => new Promise(resolve => setTimeout(resolve, 25));
async function perform(action: () => void) { await act(async () => { action(); await tick(); }); await act(tick); }
async function mount(hash = itemRouteHash(integrationInventory)) {
  const dom = new JSDOM("<!doctype html><html lang='en'><head><title>Integration</title></head><body><div id='root'></div></body></html>", { url: "https://table.test/published/revision?keep=yes" + hash, pretendToBeVisual: true });
  const keys = ["window", "document", "HTMLElement", "Element", "Node", "Event", "MouseEvent", "fetch", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const prior = keys.map(k => Object.getOwnPropertyDescriptor(globalThis,k));
  const calls: ItemRead[] = [], hubCalls: string[] = [], inventoryCalls: string[] = [], pending: { read: ItemRead; resolve: (r: Response) => void }[] = [];
  const control = { delayDm: false, mode: "ready", quantity: 1 };
  const fetchImpl = (async (url, init) => { assert.ok(!init?.method || init.method === "GET");const read = integrationRead(String(url)); calls.push(read);
    if(control.delayDm && read.request.perspective === "dm") return new Promise<Response>(resolve => pending.push({ read, resolve }));
    const response = integrationResponse(read, control.mode);
    if (read.tab === "details" && control.mode === "ready") {
      const payload = await response.json();
      payload.data.quantity = control.quantity;
      return new Response(JSON.stringify(payload), { status: response.status, headers: response.headers });
    }
    return response;
  }) as typeof fetch;
  for(const k of keys) Object.defineProperty(globalThis,k,{ configurable:true,writable:true,value:k === "IS_REACT_ACT_ENVIRONMENT" ? true : k === "fetch" ? fetchImpl : dom.window[k as keyof Window] });
  dom.window.requestAnimationFrame = cb => dom.window.setTimeout(() => cb(0),0);
  dom.window.scrollTo = (_x,y) => Object.defineProperty(dom.window,"scrollY",{ configurable:true,value:y });
  const { createRoot } = await import("react-dom/client");const container = document.getElementById("root")!;const root = createRoot(container);
  const initial = integrationEnvelope();
  const loadCharacterInventory = async (_envelope: unknown, actorId: string): Promise<InventoryContainerResult> => {
    inventoryCalls.push(actorId);
    const sheet = initial.party.find(member => member.id === actorId)?.characterSheet;
    if (!sheet) throw new Error("Missing inventory fixture");
    return { status: "ready", failureCategory: null, diagnosticId: `inventory-${actorId}`, data: {
      version: 2, container: sheet.subject, state: "ready", reasons: [],
      items: sheet.inventory.items.map(item => ({ ...item, classification: "item" as const })), wallet: sheet.wallet,
      walletState: { status: "complete", reason: null },
      limits: { contentsDepth: 1, itemCount: 200, directComplete: true, recursiveComplete: false }, projection: {
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
      },
    } };
  };
  await act(async()=>{root.render(<DndInformationHub initialEnvelope={initial} loadContent={async()=>({}) as never} loadEnvelope={async perspective=>{hubCalls.push(perspective);return integrationEnvelope(perspective);}} loadCharacterInventory={loadCharacterInventory}/>);await tick();});
  await act(tick);await act(tick);
  const click = (label:string) => perform(()=>{const b=[...container.querySelectorAll<HTMLButtonElement>("button")].find(b=>b.textContent?.trim()===label);assert.ok(b,"Missing button "+label);b.focus();b.click();});
  return { container, calls, hubCalls, inventoryCalls, pending, control, click, async cleanup(){await act(async()=>root.unmount());dom.window.close();keys.forEach((k,i)=>{if(prior[i])Object.defineProperty(globalThis,k,prior[i]!);else Reflect.deleteProperty(globalThis,k);});} };
}
async function openStaff(view: Awaited<ReturnType<typeof mount>>) {
  const disclosure = view.container.querySelector<HTMLButtonElement>('[aria-controls="inventory-contents-item-pack"]')!;assert.ok(disclosure);
  await perform(()=>disclosure.click());
  window.scrollTo(0,487);
  await perform(()=>{const b=view.container.querySelector<HTMLButtonElement>('[data-item-open="item.staff"]')!;b.focus();b.click();});
}
test("full hub inventory journey respects tab request budgets and caches fresh returns across Back/Forward",async()=>{
  const v=await mount();try{
    assert.equal(v.calls.length,0);assert.deepEqual(v.inventoryCalls,["actor.fixture"]);await openStaff(v);assert.deepEqual(v.calls.map(c=>c.tab),["details"]);assert.equal(document.activeElement?.id,"item-view-heading");
    await v.click("Known recipes");await v.click("Known uses");await v.click("Details");assert.deepEqual(v.calls.map(c=>c.tab),["details","recipes","uses"]);assert.deepEqual(v.hubCalls,[]);
    await perform(()=>v.container.querySelector<HTMLButtonElement>(".item-page__breadcrumbs li:nth-last-child(2) button")!.click());assert.equal(parseItemRoute(window.location.hash).kind,"inventory");assert.equal(v.container.querySelector('[aria-controls="inventory-contents-item-pack"]')?.getAttribute("aria-expanded"),"true");assert.equal(window.scrollY,487);assert.equal((document.activeElement as HTMLElement).dataset.itemOpen,"item.staff");
    await perform(()=>window.history.forward());await v.click("Known recipes");await v.click("Known uses");assert.equal(v.calls.length,3);assert.deepEqual(v.hubCalls,[]);
    assert.equal(window.location.pathname,"/published/revision");assert.equal(window.location.search,"?keep=yes");
    await act(async()=>{await new Promise(r=>setTimeout(r,100));});assert.equal(v.calls.length,3);
    await perform(()=>{window.dispatchEvent(new Event("focus"));document.dispatchEvent(new Event("visibilitychange"));});
    assert.equal(v.calls.length,3,"focus and visibility changes keep fresh item resources");
    const before=window.scrollY;await perform(()=>v.container.dispatchEvent(new window.WheelEvent("wheel",{deltaY:80,bubbles:true})));assert.equal(window.scrollY,before);assert.equal(v.calls.length,3);
  }finally{await v.cleanup();}
});
test("three-tab keyboard navigation and explicit continuation/refresh preserve focus",async()=>{
  const v=await mount();try{
    await openStaff(v);
    const tab=v.container.querySelector<HTMLButtonElement>('#item-tab-details')!;await perform(()=>{tab.focus();tab.dispatchEvent(new window.KeyboardEvent("keydown",{key:"End",bubbles:true}));});
    assert.equal(document.activeElement?.id,"item-tab-uses");assert.equal(v.container.querySelector('[role="tabpanel"]')?.getAttribute("aria-labelledby"),"item-tab-uses");
    await v.click("Next page of uses");assert.equal(document.activeElement?.id,"item-panel");assert.equal(v.calls.at(-1)?.input.offset,4);
    await v.click("Back to first uses");assert.equal(document.activeElement?.id,"item-panel");
    await v.click("Known recipes");await v.click("Next page: makes this item");assert.equal(document.activeElement?.id,"item-panel");assert.equal(v.calls.at(-1)?.input.makesOffset,1);assert.equal(v.calls.at(-1)?.input.usesOffset,0);
    await perform(()=>v.container.querySelector('#item-tab-recipes')!.dispatchEvent(new window.KeyboardEvent("keydown",{key:"Home",bubbles:true})));assert.equal(document.activeElement?.id,"item-tab-details");
    const axe=(await import("axe-core")).default;for(const label of ["Details","Known recipes","Known uses"]){await v.click(label);const result=await axe.run(v.container,{rules:{"color-contrast":{enabled:false}}});assert.deepEqual(result.violations.filter(r=>["serious","critical"].includes(r.impact!)).map(r=>({id:r.id,nodes:r.nodes.map(n=>({html:n.html,summary:n.failureSummary}))})),[]);}
  }finally{await v.cleanup();}
});
test("perspective reversal clears all tabs and ignores late DM responses without repeated hub discovery",async()=>{
  const v=await mount();try{
    await openStaff(v);await v.click("Known recipes");await v.click("Known uses");v.control.delayDm=true;
    await v.click("DM");assert.doesNotMatch(v.container.textContent!,/Staff attack|Restoring the travel staff/);assert.deepEqual(v.hubCalls,["dm"]);
    await v.click("Known recipes");await v.click("Player");assert.deepEqual(v.hubCalls,["dm","player"]);
    await perform(()=>{for(const p of v.pending)p.resolve(integrationResponse(p.read));});
    assert.doesNotMatch(v.container.textContent!,/DM PRIVATE/);await v.click("Known uses");assert.doesNotMatch(v.container.textContent!,/DM PRIVATE/);
    assert.deepEqual(v.hubCalls,["dm","player"]);
  }finally{await v.cleanup();}
});
test("observer changes, unidentified selection and invalidation never reuse another selection's contents",async()=>{
  const v=await mount();try{
    await openStaff(v);await v.click("Known recipes");await v.click("Known uses");
    await perform(()=>navigateItemRoute({...integrationInventory,kind:"item",characterId:"actor.second",itemId:"item.staff",tab:"uses"}));assert.match(v.container.textContent!,/No known uses are recorded/);assert.doesNotMatch(v.container.textContent!,/Traveller’s notebook/);
    await perform(()=>navigateItemRoute({...integrationInventory,kind:"item",itemId:"item.unknown",tab:"recipes"}));assert.doesNotMatch(v.container.textContent!,/Staff attack|Restoring the travel staff|DM PRIVATE/);assert.equal(v.container.querySelector("img"),null);assert.match(v.container.textContent!,/supporting details are unavailable/);
    await perform(()=>navigateItemRoute({...integrationInventory,kind:"item",itemId:"item.staff",tab:"uses"}));
    v.control.mode="stale";await perform(()=>window.dispatchEvent(new Event("dnd2024-view-invalidated")));assert.doesNotMatch(v.container.textContent!,/Staff attack|Restoring the travel staff/);assert.match(v.container.textContent!,/Uses need a refresh/);
    assert.deepEqual(v.hubCalls,[]);
  }finally{await v.cleanup();}
});

test("transport failures remain distinct from empty knowledge and explicit retries preserve panel focus",async()=>{
  const v=await mount();try{
    await openStaff(v);await v.click("Known recipes");await v.click("Known uses");
    v.control.mode="unavailable";await perform(()=>window.dispatchEvent(new Event("dnd2024-view-invalidated")));
    for(const [tab,retry] of [["Details","Refresh details"],["Known recipes","Refresh recipes"],["Known uses","Refresh uses"]]){
      await v.click(tab);assert.doesNotMatch(v.container.querySelector('[role="tabpanel"]')!.textContent!,/No known (uses|recipes)/);
      await v.click(retry);assert.equal(document.activeElement?.id,"item-panel");
      assert.match(v.container.querySelector('[role="tabpanel"]')!.textContent!,/unavailable/i);
    }
    v.control.mode="ready";await v.click("Refresh uses");assert.equal(document.activeElement?.id,"item-panel");assert.match(v.container.textContent!,/Staff attack/);
    assert.deepEqual(v.hubCalls,[]);
  }finally{await v.cleanup();}
});

test("a committed Item notice refetches the visible item without focus or hub rediscovery", async () => {
  const view = await mount();
  try {
    await openStaff(view);
    const before = view.calls.length;
    view.control.quantity = 7;
    await perform(() => window.dispatchEvent(new window.CustomEvent("dnd2024-object-changed", { detail: {
      contractVersion: 1, cursor: 1, applicationId: "dnd2024", stateSpaceId: "fixture",
      object: { qualifiedId: "dnd2024.object.inventory-item-instance-records", version: 2 },
    } })));
    assert.equal(view.calls.length, before + 1);
    assert.equal(view.calls.at(-1)?.tab, "details");
    assert.match(view.container.querySelector('[role="tabpanel"]')?.textContent ?? "", /7/);
    assert.deepEqual(view.hubCalls, []);
    await perform(() => window.dispatchEvent(new window.CustomEvent("dnd2024-object-changed", { detail: {
      object: { qualifiedId: "dnd2024.object.faction-directory-page", version: 2 },
    } })));
    assert.equal(view.calls.length, before + 1);
  } finally { await view.cleanup(); }
});
