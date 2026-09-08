export function resolveHubSurface(envelope) {
  if (envelope?.status === "ready") return "table";
  if (envelope?.status === "denied") return "denied";
  if (envelope?.reason === "connection") return "connection";
  return "unavailable";
}
