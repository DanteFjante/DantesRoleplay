import type { ResourceEdit } from "../data/resource-state";

function draftPremise(edit: ResourceEdit | undefined, fallback: string) {
  const draft = edit?.draft;
  return draft && typeof draft === "object" && "premise" in draft &&
    typeof draft.premise === "string" ? draft.premise : fallback;
}

/** Reusable scalar editor; draft state is local until the mapped server write is confirmed. */
export function CampaignPremiseEditor({
  currentPremise,
  edit,
  onBegin,
  onChange,
  onCancel,
  onSave,
}: {
  currentPremise: string;
  edit?: ResourceEdit;
  onBegin: () => void;
  onChange: (premise: string) => void;
  onCancel: () => void;
  onSave: (premise: string) => void;
}) {
  if (!edit) {
    return <button className="campaign-premise-editor__open" onClick={onBegin} type="button">
      Edit campaign premise
    </button>;
  }

  const draft = draftPremise(edit, currentPremise);
  const normalized = draft.trim();
  const pending = edit.status === "pending";
  const invalid = normalized.length === 0 || normalized.length > 1_000;
  const unchanged = normalized === currentPremise;
  return (
    <form className="campaign-premise-editor" onSubmit={(event) => {
      event.preventDefault();
      if (!pending && !invalid && !unchanged) onSave(normalized);
    }}>
      <label htmlFor="campaign-premise-draft">Campaign premise</label>
      <textarea
        disabled={pending}
        id="campaign-premise-draft"
        maxLength={1_000}
        onChange={(event) => onChange(event.currentTarget.value.slice(0, 1_000))}
        rows={4}
        value={draft}
      />
      <div className="campaign-premise-editor__meta">
        <span>{draft.length.toLocaleString()} / 1,000</span>
        {unchanged ? <span>No unsaved changes</span> : null}
      </div>
      {edit.status === "failed" ? <p className="campaign-premise-editor__error" role="alert">
        {edit.error ?? "The campaign premise was not changed."}
      </p> : null}
      {pending ? <p aria-live="polite" role="status">Saving the campaign premise…</p> : null}
      <div className="campaign-premise-editor__actions">
        <button disabled={pending || invalid || unchanged} type="submit">
          {edit.status === "failed" ? "Retry save" : "Save premise"}
        </button>
        <button disabled={pending} onClick={onCancel} type="button">Cancel</button>
      </div>
    </form>
  );
}
