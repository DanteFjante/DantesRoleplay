import { Icon } from "./Icon";

export function HubUnavailable({ message }: { message: string }) {
  return (
    <main className="hub-unavailable">
      <section aria-labelledby="hub-unavailable-heading">
        <span className="hub-unavailable__icon"><Icon name="Shield" size={26} /></span>
        <span className="eyebrow">Private table</span>
        <h1 id="hub-unavailable-heading">The table view is unavailable</h1>
        <p>{message}</p>
        <p className="hub-unavailable__hint">Sign in through the private table link, then try again.</p>
      </section>
    </main>
  );
}
