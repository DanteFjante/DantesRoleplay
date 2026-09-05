import { useState } from "react";
import type { VisualMediaAttachment } from "../data/hub-types";
import { MediaImage } from "./MediaImage";

export function EntityMediaGallery({
  attachments,
  label,
}: {
  attachments: VisualMediaAttachment[];
  label: string;
}) {
  if (attachments.length === 0) return null;
  return (
    <section aria-label={label} className="entity-media-gallery" data-count={attachments.length}>
      {attachments.map((attachment) => (
        <figure key={`${attachment.mediaId}:${attachment.imageUrl}`}>
          <MediaImage fallback={null} media={attachment} />
          {attachment.caption ? <figcaption>{attachment.caption}</figcaption> : null}
        </figure>
      ))}
    </section>
  );
}

export type ItemMediaEntry = { contentUrl: string; alt: string; caption: string | null };
export type ItemMediaView = { scopeKey: string; media: ItemMediaEntry[] };

// The caller stamps a completed response with the scope used for that request.
// A pending or stale response never carries a previous perspective's gallery forward.
export function ItemMediaGallery({ scopeKey, view }: { scopeKey: string; view: ItemMediaView | null }) {
  const images = view?.scopeKey === scopeKey ? view.media.filter((image) =>
    /^\/api\/read-model-media\/[a-f0-9]{64}\/content$/.test(image.contentUrl) &&
    image.alt.trim().length > 0 && image.alt.length <= 240 &&
    (image.caption === null || image.caption.length <= 240)).slice(0, 8) : [];
  if (!images.length) return <p className="media-fallback">No image available.</p>;
  return <section aria-label="Item images" className="entity-media-gallery" data-count={images.length}>
    {images.map((image) => <ItemMediaFigure key={`${scopeKey}:${image.contentUrl}`} image={image} />)}
  </section>;
}

function ItemMediaFigure({ image }: { image: ItemMediaEntry }) {
  const [failed, setFailed] = useState(false);
  if (failed) return <p className="media-fallback">No image available.</p>;
  return <figure>
    <img src={image.contentUrl} alt={image.alt} loading="lazy" decoding="async" onError={() => setFailed(true)} />
    {image.caption ? <figcaption>{image.caption}</figcaption> : null}
  </figure>;
}
