const MEDIA_EXTENSIONS = Object.freeze({
  "image/png": "png",
  "image/jpeg": "jpg",
  "image/webp": "webp",
});

const CONTENT_KEY = /^sha256\.([a-f0-9]{64})$/u;

/**
 * Resolves one already-authorized, content-addressed media variant to immutable host media.
 * The host publishes only reviewed hashes. This function contains no entity IDs, audience policy,
 * hidden-media registry, or fallback between variants.
 */
export function resolveMediaAssetUrl(assetKey, sha256, mimeType) {
  const match = typeof assetKey === "string" ? CONTENT_KEY.exec(assetKey) : null;
  const extension = typeof mimeType === "string" ? MEDIA_EXTENSIONS[mimeType] : null;
  if (!match || match[1] !== sha256 || !extension) return null;
  return `/components/media/${assetKey}.${extension}`;
}
