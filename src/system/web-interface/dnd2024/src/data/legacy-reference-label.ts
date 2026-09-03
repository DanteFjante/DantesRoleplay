export function legacyReferenceLabel(value: string): string {
  const parts = value.split(".").filter(Boolean);
  const last = parts.at(-1) ?? value;
  const slug = /^v\d+$/iu.test(last) && parts.length > 1
    ? parts.at(-2) ?? last
    : last;
  const words = slug
    .split(/[-_]/u)
    .map((word) => word.trim())
    .filter(Boolean);

  return words.length > 0
    ? words.map((word) => `${word[0]?.toLocaleUpperCase() ?? ""}${word.slice(1)}`).join(" ")
    : value;
}
