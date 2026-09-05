import { useEffect, useRef, useState } from "react";
import type { VisualMedia } from "../data/hub-types";
import { acceptBoard, backgroundPrompt, generateBoardDraft, prepareBoard, uploadDraftImage,
  type BoardDraftInput, type BoardDraftScope, type DraftImage, type DraftProjection, type PreparedBoard } from "../server/board-draft";
import { TacticalBoard } from "./TacticalBoard";

export function BoardDraftWorkshop({ scope, onAccepted }: { scope: BoardDraftScope; onAccepted: () => void }) {
  const [input, setInput] = useState<BoardDraftInput>({ columns: 20, rows: 20, obstacleCount: 8, seed: 1, setting: "ruin", prompt: "" });
  const [draft, setDraft] = useState<DraftProjection | null>(null);
  const [image, setImage] = useState<DraftImage | null>(null);
  const [preview, setPreview] = useState<VisualMedia | undefined>();
  const [prepared, setPrepared] = useState<PreparedBoard | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [message, setMessage] = useState("");
  const [created, setCreated] = useState(0);
  const controller = useRef<AbortController | null>(null);
  const operation = useRef(0);
  useEffect(() => () => { operation.current++; controller.current?.abort(); }, []);
  useEffect(() => () => { if (preview) URL.revokeObjectURL(preview.imageUrl); }, [preview]);
  useEffect(() => {
    if (!created) return;
    const timer = window.setTimeout(() => {
      operation.current++; controller.current?.abort(); setBusy(false);
      setDraft(null); setImage(null); setPreview(undefined); setPrepared(null); setConfirmed(false);
      setMessage("This private preview expired. Reload the current board before generating another draft. Accepted state and stored blobs were not deleted.");
    }, 15 * 60 * 1000);
    return () => window.clearTimeout(timer);
  }, [created]);

  async function run(work: (signal: AbortSignal, current: () => boolean) => Promise<void>) {
    controller.current?.abort();
    const request = ++operation.current;
    const next = new AbortController(); controller.current = next;
    const current = () => request === operation.current && !next.signal.aborted;
    setBusy(true); setMessage("");
    try { await work(next.signal, current); }
    catch (error) { if (current()) setMessage(error instanceof Error ? error.message : "The draft could not be prepared. Keep using the current grid."); }
    finally { if (current()) setBusy(false); }
  }

  const generate = () => run(async (signal, current) => {
    setPrepared(null); setConfirmed(false); setImage(null); setPreview(undefined); setSubmitted(false); setDraft(null);
    const result = await generateBoardDraft(scope, input, signal);
    if (current()) { setDraft(result); setCreated(Date.now()); setMessage("Private draft only. Review the geometry before requesting or supplying a background."); }
  });
  const discard = () => {
    operation.current++; controller.current?.abort(); setBusy(false); setDraft(null); setImage(null); setPreview(undefined);
    setPrepared(null); setConfirmed(false); setCreated(0);
    setMessage(submitted ? "Preview closed. Refresh to verify the submitted acceptance; closing does not roll back game state."
      : "Private preview discarded. The current combat board was not changed.");
    setSubmitted(false);
  };
  const edit = (index: number, patch: Partial<NonNullable<typeof draft>["data"]["board"]["obstacles"][number]>) => {
    if (!draft) return;
    setDraft({ ...draft, data: { ...draft.data, board: { ...draft.data.board,
      obstacles: draft.data.board.obstacles.map((item, row) => row === index ? { ...item, ...patch } : item) } } });
    setPrepared(null); setConfirmed(false);
    setImage(null); setPreview(undefined);
  };

  return <section className="current-scene-panel board-draft-workshop" aria-labelledby="board-draft-title">
    <h2 id="board-draft-title">GM combat-map workshop</h2>
    <p>Generate a deterministic layout first. No image provider is configured here: use the aligned background request with your image tool, then upload its PNG, or accept the structured grid without artwork.</p>
    <fieldset disabled={busy || submitted}><legend>Layout request</legend><div className="board-draft-fields">
      {([['columns','Columns',4,64],['rows','Rows',4,64],['obstacleCount','Obstacles',0,32],['seed','Seed',0,2147483647]] as const).map(([key,label,min,max]) =>
        <label key={key}>{label}<input type="number" min={min} max={max} value={input[key]} onChange={(event) => setInput({ ...input, [key]: Number(event.target.value) })} /></label>)}
      <label>Setting<select value={input.setting} onChange={(event) => setInput({ ...input, setting: event.target.value as BoardDraftInput['setting'] })}>
        <option value="ruin">Ruins</option><option value="woodland">Woodland</option><option value="chamber">Chamber</option></select></label>
    </div><label>Private background directions<textarea maxLength={600} value={input.prompt} onChange={(event) => setInput({ ...input, prompt: event.target.value })} /></label>
    <button type="button" onClick={() => void generate()}>{draft ? "Regenerate draft" : "Generate combat map"}</button></fieldset>
    {message ? <p role="status">{message}</p> : null}
    {busy ? <p role="status">Working on the private preview…</p> : null}
    {draft ? <>
      <h3>Private preview — not accepted game state</h3>
      <p>Seed {draft.data.seed}. Review expires after 15 minutes. Artwork never determines movement or obstacle rules.</p>
      <TacticalBoard board={{ ...draft.data.board, participants: [] }} background={preview} placeholder title="Draft layout" />
      <fieldset disabled={busy || submitted}><legend>Edit reviewed obstacles</legend>
        {draft.data.board.obstacles.map((item, index) => <div className="board-draft-obstacle" key={item.id}>
          <label>Label {index+1}<input maxLength={200} value={item.label} onChange={(event) => edit(index, { label: event.target.value })} /></label>
          {(['x','y','width','height'] as const).map((key) => <label key={key}>{key}<input type="number" min={key==='x'||key==='y'?0:1} max={64} value={item.area[key]}
            onChange={(event) => edit(index, { area: { ...item.area, [key]: Number(event.target.value) } })} /></label>)}
          <button type="button" onClick={() => { setDraft({ ...draft, data: { ...draft.data, board: { ...draft.data.board, obstacles: draft.data.board.obstacles.filter((_, row) => row !== index) } } }); setPrepared(null); setConfirmed(false); }}>Remove {item.label}</button>
        </div>)}
        <h3>Optional aligned background</h3>
        <p>{draft.data.backgroundRequest.width} × {draft.data.backgroundRequest.height} pixels; PNG, at most 10 MiB. Use this request after reviewing the layout:</p>
        <textarea readOnly aria-label="Background image request" value={backgroundPrompt(draft.data)} />
        <label>Upload reviewed PNG<input type="file" accept="image/png" onChange={(event) => {
          const file = event.target.files?.[0]; event.target.value = "";
          if (file) void run(async (signal, current) => {
            setPrepared(null); setConfirmed(false);
            const result = await uploadDraftImage(scope, file, draft.data, signal);
            if (current()) { setImage(result); setPreview({ imageUrl: URL.createObjectURL(file), alt: "Private draft background", width: result.width, height: result.height }); }
          });
        }} /></label>
        {image ? <button type="button" onClick={() => { setImage(null); setPreview(undefined); setPrepared(null); setConfirmed(false); }}>Use grid without artwork</button> : null}
        <button type="button" onClick={() => void run(async (signal, current) => {
          const result = await prepareBoard(scope, draft, image, signal);
          if (current()) { setPrepared(result); setConfirmed(false); setMessage("The exact proposal is prepared. Explicit acceptance is still required."); }
        })}>Review acceptance</button>
      </fieldset>
      {prepared ? <div>
        <label><input type="checkbox" disabled={busy || submitted} checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)} /> I reviewed this layout and any background for player visibility.</label>
        <button type="button" disabled={!confirmed || busy || submitted} onClick={() => void run(async (signal, current) => {
          setSubmitted(true);
          await acceptBoard(scope, prepared, signal);
          if (current()) { setMessage("Acceptance confirmed. Refreshing the canonical board."); onAccepted(); }
        })}>Accept reviewed board</button>
      </div> : null}
      <button type="button" disabled={busy} onClick={discard}>{submitted ? "Close preview" : "Discard draft"}</button>
      {submitted ? <button type="button" disabled={busy} onClick={onAccepted}>Refresh accepted state</button> : null}
    </> : null}
  </section>;
}
