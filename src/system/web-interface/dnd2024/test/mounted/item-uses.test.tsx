import assert from "node:assert/strict";
import test from "node:test";
import React, { act } from "react";
import { JSDOM } from "jsdom";
import { ItemViewClient } from "../../src/server/item-view-client";
import { readItemUses, type ItemUsesRequest } from "../../src/server/item-uses-client";
import { ConnectedItemView } from "../../src/components/items/ConnectedItemView";
import { itemEnvelope, itemRequest } from "../fixtures/item-details";
import { usesData, usesEnvelope, usesRequest } from "../fixtures/item-uses";
const response = (data: unknown) => new Response(JSON.stringify(data));
const tick = () => new Promise(resolve => setTimeout(resolve, 10));
async function mount(client: ItemViewClient) {
  const dom = new JSDOM("<html><body><div id='root'></div></body></html>", { url: "https://table.test", pretendToBeVisual: true });
  const keys = ["window", "document", "HTMLElement", "Element", "Node", "Event", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const prior = keys.map(k => Object.getOwnPropertyDescriptor(globalThis, k));
  for(const key of keys) Object.defineProperty(globalThis, key, { configurable: true, writable: true, value: key === "IS_REACT_ACT_ENVIRONMENT" ? true : dom.window[key as keyof Window] });
  const { createRoot } = await import("react-dom/client"); const container = document.getElementById("root")!; const root = createRoot(container);
  const render = async (tab: "uses" | "details" = "uses", request = itemRequest) => { await act(async () => { root.render(<ConnectedItemView tab={tab} request={request} client={client} onTab={() => {}} onBack={() => {}} />); await tick(); }); await act(tick); };
  const click = async (text: string) => { const button = [...container.querySelectorAll("button")].find(b => b.textContent?.includes(text)); assert.ok(button); await act(async () => { button.click(); await tick(); }); await act(tick); };
  return { container, render, click, async cleanup() { await act(async () => root.unmount()); dom.window.close(); keys.forEach((k, i) => { if(prior[i]) Object.defineProperty(globalThis, k, prior[i]!); else Reflect.deleteProperty(globalThis, k); }); } };
}

test("uses transport validates scope, bounds, schema, uniqueness and continuation", async () => {
  let url = "";
  const result = await readItemUses(usesRequest, new AbortController().signal, (async u => { url=String(u); return response(usesEnvelope()); }) as typeof fetch);
  assert.equal(result.status, "ready");assert.match(url,/entities\/actor.fixture\/read-models\/dnd2024.query.inventory-item-uses/);
  assert.deepEqual(JSON.parse(new URL(url,"https://table.test").searchParams.get("input")!),{itemId:itemRequest.itemId,offset:0,expectedSourceRevision:null});
  for(const mutate of [(e:any)=>{e.data.observerId="other";},(e:any)=>{e.data.uses.entries[0].secret="private";},(e:any)=>{e.data.uses.entries[0].observerKnowledge="known";},
    (e:any)=>{e.data.uses.nextOffset=1;},(e:any)=>{e.data.uses.entries.push(e.data.uses.entries[0]);},(e:any)=>{e.outputSchemaHash="0".repeat(64);},
    (e:any)=>{e.data.uses.entries[0].effects=["x".repeat(513)];},(e:any)=>{e.extra="secret";}]) {
    const e=usesEnvelope();mutate(e);await assert.rejects(readItemUses(usesRequest,new AbortController().signal,(async()=>response(e)) as typeof fetch));
  }
  const next={...usesRequest,offset:4,expectedSourceRevision:"A".repeat(64)}, mismatch=usesEnvelope(next);mismatch.sourceRevisionFingerprint="B".repeat(64);
  assert.equal((await readItemUses(next,new AbortController().signal,(async()=>response(mismatch)) as typeof fetch)).status,"stale");
  for(const status of [403,409,500])assert.equal((await readItemUses(usesRequest,new AbortController().signal,(async()=>new Response(null,{status})) as typeof fetch)).status,status===403?"forbidden":status===409?"stale":"unavailable");
  let reads=0;assert.equal((await readItemUses({...usesRequest,offset:1},new AbortController().signal,(async()=>{reads++;return response(usesEnvelope());}) as typeof fetch)).status,"unavailable");assert.equal(reads,0);
  await assert.rejects(readItemUses(usesRequest,new AbortController().signal,(async()=>new Response("x".repeat(70001))) as typeof fetch));
});
test("Known uses loads only on first visit, caches pages and describes uses without action controls",async()=>{
  let details=0;const calls:ItemUsesRequest[]=[];
  const client=new ItemViewClient((async(url)=>{if(String(url).includes("inventory-item-details")){details++;return response(itemEnvelope());}
    const r={...usesRequest,...JSON.parse(new URL(String(url),"https://table.test").searchParams.get("input")!)};calls.push(r);return response(usesEnvelope(r));}) as typeof fetch);
  const view=await mount(client);try {
    await view.render("details");assert.equal(calls.length,0);await view.render();assert.equal(calls.length,1);assert.equal(details,1);
    assert.equal(view.container.querySelectorAll("article").length,4);
    for(const text of ["Staff attack","Carve a replacement peg","Consume a restorative","Signal across the ravine","believed","DM adjudication required","Requirements not met","Activation"])assert.ok(view.container.textContent!.includes(text));
    assert.equal([...view.container.querySelectorAll("button")].some(b=>/^(use|consume|execute|activate)$/i.test(b.textContent??"")),false);
    await view.render("details");await view.render();assert.equal(calls.length,1);
    await view.click("Next page of uses");assert.equal(calls[1].offset,4);assert.ok(calls[1].expectedSourceRevision);assert.equal(view.container.querySelectorAll("article").length,1);
    await view.render("details");await view.render();assert.equal(calls.length,2);
    const axe=(await import("axe-core")).default;assert.deepEqual((await axe.run(view.container,{rules:{"color-contrast":{enabled:false}}})).violations.filter(v=>["serious","critical"].includes(v.impact!)).map(v=>v.id),[]);
  }finally{await view.cleanup();}
});
test("empty, incomplete and unavailable uses stay distinct; stale continuation removes old rows",async()=>{
  let mode="empty";const client=new ItemViewClient((async(url)=>{if(String(url).includes("inventory-item-details"))return response(itemEnvelope());
    if(mode==="stale")return new Response(null,{status:409});if(mode==="unavailable")return new Response(null,{status:503});
    const data=usesData();if(mode==="empty")data.uses={state:"empty",entries:[],nextOffset:null,reasons:[]};
    if(mode==="partial"){data.uses.reasons=["source-incomplete"];data.uses.entries[0].availability="definition-incomplete";}
    return response(usesEnvelope(usesRequest,data));}) as typeof fetch);
  const view=await mount(client);try{await view.render();assert.match(view.container.textContent!,/No known uses are recorded/);
    mode="partial";await act(async()=>{client.invalidate();await tick();});await act(tick);assert.match(view.container.textContent!,/Use list is partial/);assert.match(view.container.textContent!,/Activity definition incomplete/);
    mode="stale";await view.click("Next page of uses");assert.equal(view.container.querySelectorAll("article").length,0);assert.match(view.container.textContent!,/Uses need a refresh/);
    mode="unavailable";await view.click("Refresh uses");assert.match(view.container.textContent!,/Uses unavailable/);assert.doesNotMatch(view.container.textContent!,/No known uses/);
  }finally{await view.cleanup();}
});
test("slow DM reads, selection changes and context invalidation cannot restore retired use details",async()=>{
  const pending:{request:ItemUsesRequest;resolve:(r:Response)=>void}[]=[];
  const client=new ItemViewClient((async(url)=>{const u=new URL(String(url),"https://table.test"),r={...usesRequest,...JSON.parse(u.searchParams.get("input")!),perspective:u.searchParams.get("perspective") as "player"|"dm"};
    if(String(url).includes("inventory-item-details"))return response(itemEnvelope(r));return new Promise<Response>(resolve=>pending.push({request:r,resolve}));}) as typeof fetch);
  const view=await mount(client);try{
    await view.render("uses",{...itemRequest,perspective:"dm"});await view.render("uses",{...itemRequest,itemId:"item.other"});
    await act(async()=>{const old=usesData(pending[0].request);old.uses.entries[0].name="PRIVATE DM USE";pending[0].resolve(response(usesEnvelope(pending[0].request,old)));pending[1].resolve(response(usesEnvelope(pending[1].request)));await tick();});
    assert.doesNotMatch(view.container.textContent!,/PRIVATE DM/);assert.match(view.container.textContent!,/Staff attack/);
    await act(async()=>{client.invalidate();await tick();});assert.equal(view.container.querySelectorAll("article").length,0);
  }finally{await view.cleanup();}
});
test("expired use data is cleared and explicit refresh starts at the first page",async()=>{
  let reads=0;const client=new ItemViewClient((async(url)=>{if(String(url).includes("inventory-item-details"))return response(itemEnvelope());reads++;return response(usesEnvelope());}) as typeof fetch,40);
  const view=await mount(client);try{await view.render();await act(async()=>{await new Promise(r=>setTimeout(r,60));});assert.equal(view.container.querySelectorAll("article").length,0);assert.match(view.container.textContent!,/Uses need a refresh/);await view.click("Refresh uses");assert.equal(reads,2);}finally{await view.cleanup();}
});
