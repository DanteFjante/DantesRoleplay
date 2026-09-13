/** Keeps only the content routes emitted by authorized entity/media projections. */
export function entityMediaContentUrl(value) {
  if (typeof value !== "string") return null;
  try {
    const parsed = new URL(value, "http://media.invalid");
    if (parsed.origin !== "http://media.invalid" || parsed.hash) return null;
    if (/^\/api\/read-model-media\/[a-f0-9]{64}\/content$/u.test(parsed.pathname))
      return parsed.search === "" ? value : null;
    if (!/^\/api\/applications\/[^/?#\\\s]+\/state-spaces\/[^/?#\\\s]+\/(?:entities\/[^/?#\\\s]+\/)?media\/[^/?#\\\s]+\/content$/u.test(parsed.pathname) ||
        !(parsed.search === "" || /^\?perspective=(?:player|dm)$/u.test(parsed.search))) return null;
    return value;
  } catch { return null; }
}
