import { ViewReadError } from "../data/view-read-client";

export async function boundedJson(response: Response): Promise<unknown> {
  if (!response.body) throw new ViewReadError("incompatible-data", "Missing item response.");
  const reader = response.body.getReader();
  const chunks: Uint8Array[] = []; let size = 0;
  try {
    while (true) {
      const next = await reader.read(); if (next.done) break;
      size += next.value.byteLength;
      if (size > 70_000) { await reader.cancel(); throw new ViewReadError("incompatible-data", "Item response exceeds its limit."); }
      chunks.push(next.value);
    }
  } finally { reader.releaseLock(); }
  const bytes = new Uint8Array(size); let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  try { return JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes)); }
  catch { throw new ViewReadError("incompatible-data", "Invalid item response."); }
}
