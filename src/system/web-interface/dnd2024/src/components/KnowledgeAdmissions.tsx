import type { KnowledgeAdmission } from "../data/hub-types";

/** Shows whose knowledge admits a record without turning differing views into a party belief. */
export function KnowledgeAdmissions({ admissions }: { admissions?: KnowledgeAdmission[] }) {
  if (!admissions?.length) return null;
  return <details className="knowledge-admissions">
    <summary>Party knowledge sources</summary>
    <ul>{admissions.map((admission) => <li key={admission.actorId}>
      <strong>{admission.actorName}</strong>: {admission.stance}
      {admission.source === "baseline" ? " (shared background)" : " (recorded knowledge)"}
    </li>)}</ul>
  </details>;
}
