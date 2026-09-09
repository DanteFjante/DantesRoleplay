import type { CharacterWalletV2 } from "../../data/hub-types";
import { Icon } from "../Icon";

export function WalletSummary({ status = "complete", wallet }: {
  status?: "complete" | "partial" | "unavailable";
  wallet: CharacterWalletV2 | null;
}) {
  return (
    <section aria-labelledby="character-wallet-heading" className="character-wallet">
      <header>
        <span><Icon name="Sparkles" size={19} /></span>
        <div><small>Carried wealth</small><h3 id="character-wallet-heading">Wallet</h3></div>
      </header>
      {wallet ? <dl className="character-wallet__totals">
        <div><dt>{status === "complete" ? "Gold pieces" : "Bounded gold pieces"}</dt><dd>{wallet.gpCount.toLocaleString()}</dd></div>
        <div><dt>{status === "complete" ? "All coins" : "Bounded coins"}</dt><dd>{wallet.coinCount.toLocaleString()}</dd></div>
        <div><dt>{status === "complete" ? "Copper value" : "Bounded copper value"}</dt><dd>{wallet.copperValue.toLocaleString()}</dd></div>
      </dl> : null}
      {status === "partial" ? <p role="status">This calculation covers four container levels and may omit deeper currency.</p> : null}
      {status === "unavailable" ? <p role="status">Wallet totals could not be calculated. Inventory browsing remains available.</p> : null}
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
      ) : status === "unavailable" ? null : <p>No coins are recorded in this wallet.</p>}
    </section>
  );
}
