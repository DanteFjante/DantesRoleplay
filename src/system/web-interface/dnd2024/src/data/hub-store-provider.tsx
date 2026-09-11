import { useMemo, type ReactNode } from "react";
import { Provider } from "react-redux";

import { createHubStore, type HubStore } from "./hub-store";

/** Supplies a stable production store, or an explicitly injected isolated store for a fixture. */
export function HubStoreProvider({ children, store }: { children: ReactNode; store?: HubStore }) {
  const activeStore = useMemo(() => store ?? createHubStore(), [store]);
  return <Provider store={activeStore}>{children}</Provider>;
}
