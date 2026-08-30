"use client";

import { useEffect, useState, type ReactNode } from "react";

import type { VisualMedia } from "../data/hub-types";

export function MediaImage({
  className,
  fallback,
  loading = "lazy",
  media,
}: {
  className?: string;
  fallback: ReactNode;
  loading?: "eager" | "lazy";
  media?: VisualMedia | null;
}) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [media?.imageUrl]);

  if (!media || failed) return <>{fallback}</>;
  return (
    <img
      alt={media.alt}
      className={className}
      decoding="async"
      height={media.height}
      loading={loading}
      onError={() => setFailed(true)}
      src={media.imageUrl}
      width={media.width}
    />
  );
}
