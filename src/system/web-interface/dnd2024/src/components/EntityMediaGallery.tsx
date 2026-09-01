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
        <figure key={attachment.mediaId}>
          <MediaImage fallback={null} media={attachment} />
          {attachment.caption ? <figcaption>{attachment.caption}</figcaption> : null}
        </figure>
      ))}
    </section>
  );
}
