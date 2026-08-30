export function resolveHubSurface(envelope) {
  return envelope?.status === "ready" ? "table" : "rules";
}
