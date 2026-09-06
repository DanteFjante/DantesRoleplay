const DEFAULT_PAGE_SIZE = 100;
const DEFAULT_MAXIMUM_PAGES = 100;
const DEFAULT_MAXIMUM_ITEMS = 10_000;
const MAXIMUM_CURSOR_LENGTH = 4_096;

const incomplete = (reason, pagesRead) => ({ status: "incomplete", reason, pagesRead, items: [] });

/**
 * Consumes an opaque-cursor collection without returning a credible partial result.
 * The caller owns transport and item semantics; this owner enforces page shape,
 * advancing cursors and hard traversal bounds shared by current and future loaders.
 */
export async function readCompletePages({
  fetchPage,
  pageSize = DEFAULT_PAGE_SIZE,
  maximumPages = DEFAULT_MAXIMUM_PAGES,
  maximumItems = DEFAULT_MAXIMUM_ITEMS,
}) {
  if (typeof fetchPage !== "function" || !Number.isInteger(pageSize) || pageSize < 1 ||
      !Number.isInteger(maximumPages) || maximumPages < 1 ||
      !Number.isInteger(maximumItems) || maximumItems < 1) {
    throw new TypeError("Complete pagination requires positive integer bounds and a page reader.");
  }

  const items = [];
  const seenCursors = new Set();
  let cursor = null;

  for (let pageNumber = 1; pageNumber <= maximumPages; pageNumber += 1) {
    let page;
    try {
      page = await fetchPage(cursor);
    } catch {
      return incomplete("page-unavailable", pageNumber - 1);
    }
    if (!page || !Array.isArray(page.items)) return incomplete("malformed-page", pageNumber - 1);
    if (page.items.length > pageSize) return incomplete("oversized-page", pageNumber - 1);
    if (items.length + page.items.length > maximumItems) return incomplete("item-limit", pageNumber - 1);
    items.push(...page.items);

    if (page.nextCursor === null || page.nextCursor === undefined) {
      return { status: "complete", items };
    }
    if (typeof page.nextCursor !== "string" || page.nextCursor.length < 1 ||
        page.nextCursor.length > MAXIMUM_CURSOR_LENGTH) {
      return incomplete("malformed-cursor", pageNumber);
    }
    if (seenCursors.has(page.nextCursor)) return incomplete("repeated-cursor", pageNumber);
    if (pageNumber === maximumPages || items.length === maximumItems) {
      return incomplete(pageNumber === maximumPages ? "page-limit" : "item-limit", pageNumber);
    }
    seenCursors.add(page.nextCursor);
    cursor = page.nextCursor;
  }

  return incomplete("page-limit", maximumPages);
}
