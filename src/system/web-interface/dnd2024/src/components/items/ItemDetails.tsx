import type { ItemDetailsData, ItemKnowledge, ItemSource } from "../../server/item-view-client";
import { ItemMediaGallery } from "../EntityMediaGallery";

function Knowledge({ value }: { value: ItemKnowledge | null }) {
  return value === null ? null : <small className="item-details__knowledge">Character knowledge: {value}</small>;
}
function Sources({ values }: { values: ItemSource[] }) {
  return values.length ? <details className="item-details__sources"><summary>Sources</summary>
    <ul>{values.map((source, index) => <li key={index}>{source.label} <small>({source.knowledgeState})</small></li>)}</ul>
  </details> : null;
}
const reasons: Record<ItemDetailsData["reasons"][number], string> = {
  "inventory-bound": "Some inventory contents are outside this view.", "source-incomplete": "Some recorded information is incomplete.",
  "page-limit": "Some details could not fit in this view.", "byte-limit": "Some details could not fit in this view.",
  "dependency-unavailable": "Some supporting information is unavailable.",
};
export function ItemDetails({ data, scopeKey }: { data: ItemDetailsData; scopeKey: string }) {
  return <div className="item-details">
    {data.state === "partial" ? <div className="item-details__notice" role="status"><strong>Some details are unavailable</strong>
      <ul>{[...new Set(data.reasons.map((reason) => reasons[reason]))].map((reason) => <li key={reason}>{reason}</li>)}</ul></div> : null}
    <div className={`item-details__lead${data.media.length ? "" : " item-details__lead--text"}`}>
      <div className="item-details__media"><ItemMediaGallery scopeKey={scopeKey} view={{ scopeKey, media: data.media }} /></div>
      <div>
        <Knowledge value={data.observerKnowledge} />
        <p className="item-details__description">{data.description ?? "No description available."}</p>
        <dl className="item-details__facts">
          {data.quantity !== null ? <div><dt>Quantity</dt><dd>{data.quantity}</dd></div> : null}
          {data.container ? <div><dt>Carried in</dt><dd>{data.container.name}<Knowledge value={data.container.observerKnowledge} /></dd></div> : null}
          {data.equipmentSlots.length ? <div><dt>Equipment</dt><dd>{data.equipmentSlots.join(", ")}</dd></div> : null}
        </dl>
        <Sources values={data.sources} />
      </div>
    </div>
    {data.properties.length ? <section aria-labelledby="item-properties-heading"><h2 id="item-properties-heading">Known properties</h2>
      <dl className="item-details__properties">{data.properties.map((property, index) => <div key={index}>
        <dt>{property.label}</dt><dd>{typeof property.value === "boolean" ? property.value ? "Yes" : "No" : String(property.value)}{property.unit ? ` ${property.unit}` : ""}
          <Knowledge value={property.observerKnowledge} /><Sources values={property.sources} />
        </dd></div>)}</dl>
    </section> : null}
  </div>;
}
