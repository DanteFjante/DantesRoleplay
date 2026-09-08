import type { CharacterWalletV2 } from "../../data/hub-types";
import { Icon } from "../Icon";

export function WalletSummary({ complete = true, wallet }: { complete?: boolean; wallet: CharacterWalletV2 }) {
  return (
    <section aria-labelledby="character-wallet-heading" className="character-wallet">
      <header>
        <span><Icon name="Sparkles" size={19} /></span>
        <div><small>Carried wealth</small><h3 id="character-wallet-heading">Wallet</h3></div>
      </header>
      <dl className="character-wallet__totals">
        <div><dt>{complete ? "Gold pieces" : "Visible gold pieces"}</dt><dd>{wallet.gpCount.toLocaleString()}</dd></div>
        <div><dt>{complete ? "All coins" : "Visible coins"}</dt><dd>{wallet.coinCount.toLocaleString()}</dd></div>
        <div><dt>{complete ? "Copper value" : "Visible copper value"}</dt><dd>{wallet.copperValue.toLocaleString()}</dd></div>
      </dl>
      {!complete ? <p role="status">These totals cover only the visible bounded inventory.</p> : null}
      {wallet.denominations.length ? (
        <ul className="character-wallet__denominations">
          {wallet.denominations.map((row) => (
            <li key={row.code}>
              <span>{row.code.toUpperCase()}</span>
              <strong>{row.count.toLocaleString()}</strong>
              <small>{row.denomination.label}</small>
            </li>
          ))}
        </ul>
      ) : <p>No coins are recorded in this wallet.</p>}
    </section>
  );
}
