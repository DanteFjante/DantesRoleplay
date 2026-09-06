import assert from "node:assert/strict";
import test from "node:test";
import React, { act } from "react";
import { JSDOM } from "jsdom";
import { DndInformationHub } from "../../src/components/DndInformationHub";
import { integrationEnvelope, integrationInventory, integrationRead, integrationResponse, type ItemRead } from "../fixtures/item-integration";
import { itemRouteHash, navigateItemRoute, parseItemRoute } from "../../src/data/item-view-route";
const tick = () => new Promise(resolve => setTimeout(resolve, 25));
async function perform(action: () => void) { await act(async () => { action(); await tick(); }); await act(tick); }
async function mount(hash = itemRouteHash(integrationInventory)) {
  const dom = new JSDOM("<!doctype html><html lang='en'><head><title>Integration</title></head><body><div id='root'></div></body></html>", { url: "https://table.test/published/revision?keep=yes" + hash, pretendToBeVisual: true });
  const keys = ["window", "document", "HTMLElement", "Element", "Node", "Event", "MouseEvent", "fetch", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const prior = keys.map(k => Object.getOwnPropertyDescriptor(globalThis,k));
  const calls: ItemRead[] = [], hubCalls: string[] = [], pending: { read: ItemRead; resolve: (r: Response) => void }[] = [];
  const control = { delayDm: false, mode: "ready" };
  const fetchImpl = (async (url, init) => { assert.ok(!init?.method || init.method === "GET");const read = integrationRead(String(url)); calls.push(read);
    if(control.delayDm && read.request.perspective === "dm") return new Promise<Response>(resolve => pending.push({ read, resolve }));
    return integrationResponse(read, control.mode);
  }) as typeof fetch;
  for(const k of keys) Object.defineProperty(globalThis,k,{ configurable:true,writable:true,value:k === "IS_REACT_ACT_ENVIRONMENT" ? true : k === "fetch" ? fetchImpl : dom.window[k as keyof Window] });
  dom.window.requestAnimationFrame = cb => dom.window.setTimeout(() => cb(0),0);
  dom.window.scrollTo = (_x,y) => Object.defineProperty(dom.window,"scrollY",{ configurable:true,value:y });
  const { createRoot } = await import("react-dom/client");const container = document.getElementById("root")!;const root = createRoot(container);
  const initial = integrationEnvelope();
  await act(async()=>{root.render(<DndInformationHub initialEnvelope={initial} loadContent={async()=>({}) as never} loadEnvelope={async perspective=>{hubCalls.push(perspective);return integrationEnvelope(perspective);}}/>);await tick();});
  await act(tick);await act(tick);
  const click = (label:string) => perform(()=>{const b=[...container.querySelectorAll<HTMLButtonElement>("button")].find(b=>b.textContent?.trim()===label);assert.ok(b,"Missing button "+label);b.focus();b.click();});
  return { container, calls, hubCalls, pending, control, click, async cleanup(){await act(async()=>root.unmount());dom.window.close();keys.forEach((k,i)=>{if(prior[i])Object.defineProperty(globalThis,k,prior[i]!);else Reflect.deleteProperty(globalThis,k);});} };
}
async function openStaff(view: Awaited<ReturnType<typeof mount>>) {
  const disclosure = view.container.querySelector<HTMLDetailsElement>(".character-inventory__branch")!;assert.ok(disclosure);
  await perform(()=>{disclosure.open=true;disclosure.dispatchEvent(new Event("toggle"));});
  window.scrollTo(0,487);
  await perform(()=>{const b=view.container.querySelector<HTMLButtonElement>('[data-item-open="item.staff"]')!;b.focus();b.click();});
}
test("full hub inventory journey respects tab request budgets and caches fresh returns across Back/Forward",async()=>{
  const v=await mount();try{
    assert.equal(v.calls.length,0);await openStaff(v);assert.deepEqual(v.calls.map(c=>c.tab),["details"]);assert.equal(document.activeElement?.id,"main-view-heading");
    await v.click("Known recipes");await v.click("Known uses");await v.click("Details");assert.deepEqual(v.calls.map(c=>c.tab),["details","recipes","uses"]);assert.deepEqual(v.hubCalls,[]);
    await v.click("Back to inventory");assert.equal(parseItemRoute(window.location.hash).kind,"inventory");assert.equal(v.container.querySelector<HTMLDetailsElement>(".character-inventory__branch")?.open,true);assert.equal(window.scrollY,487);assert.equal((document.activeElement as HTMLElement).dataset.itemOpen,"item.staff");
    await perform(()=>window.history.forward());await v.click("Known recipes");await v.click("Known uses");assert.equal(v.calls.length,3);assert.deepEqual(v.hubCalls,[]);
    assert.equal(window.location.pathname,"/published/revision");assert.equal(window.location.search,"?keep=yes");
    await act(async()=>{await new Promise(r=>setTimeout(r,100));});assert.equal(v.calls.length,3);
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
