import type { CharacterWalletV2, InventoryWalletSummary } from "../../data/hub-types";
import { Icon } from "../Icon";

export function WalletSummary({ status = "complete", reason = null, wallet }: {
  status?: "complete" | "partial" | "unavailable" | "forbidden";
  reason?: "depth-limit" | "field-unavailable" | "read-failed" | "authorization" | null;
  wallet: CharacterWalletV2 | InventoryWalletSummary | null;
}) {
  const total = (value: number | null) => value === null ? "Unavailable" : value.toLocaleString();
  return (
    <section aria-labelledby="character-wallet-heading" className="character-wallet">
      <header>
        <span><Icon name="Sparkles" size={19} /></span>
        <div><small>Carried wealth</small><h3 id="character-wallet-heading">Wallet</h3></div>
      </header>
      {wallet ? <dl className="character-wallet__totals">
        <div><dt>{status === "complete" ? "Gold pieces" : "Bounded gold pieces"}</dt><dd>{total(wallet.gpCount)}</dd></div>
        <div><dt>{status === "complete" ? "All coins" : "Bounded coins"}</dt><dd>{total(wallet.coinCount)}</dd></div>
        <div><dt>{status === "complete" ? "Copper value" : "Bounded copper value"}</dt><dd>{total(wallet.copperValue)}</dd></div>
      </dl> : null}
      {status === "partial" ? <p role="status">{reason === "depth-limit"
        ? "This calculation covers four container levels and may omit deeper currency."
        : "Some wallet fields are unavailable; no totals were inferred."}</p> : null}
      {status === "unavailable" ? <p role="status">Wallet totals could not be calculated. Inventory browsing remains available.</p> : null}
      {status === "forbidden" ? <p role="status">This seat is not authorized to read wallet totals.</p> : null}
      {wallet?.denominations.length ? (
        <ul className="character-wallet__denominations">
          {wallet.denominations.map((row) => (
            <li key={row.code}>
              <span>{row.code.toUpperCase()}</span>
              <strong>{row.count.toLocaleString()}</strong>
              <small>{row.denomination.label}</small>
            </li>
          ))}
        </ul>
      ) : status === "unavailable" || status === "forbidden" || (status === "partial" && reason === "field-unavailable")
        ? null : <p>{status === "partial"
          ? "No coins were found within the inspected container levels."
          : "No coins are recorded in this wallet."}</p>}
    </section>
  );
}
