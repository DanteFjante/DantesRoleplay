export type ResourceFailureCategory = "authorization" | "incompatible-data" | "stale-data" | "transport";

export type ResourceFailure = {
  category: ResourceFailureCategory;
  message: string;
};

export type ResourceResult<T> = {
  requestId: number;
  fingerprint: string;
  value: T;
};

export type ResourceState<T> =
  | { status: "unloaded"; data: null }
  | { status: "loading"; data: T | null }
  | { status: "ready"; data: T; result: ResourceResult<T> }
  | { status: "stale"; data: T; result: ResourceResult<T>; failure: ResourceFailure }
  | { status: "forbidden" | "incompatible" | "error"; data: null; failure: ResourceFailure };

export type ResourceEdit = {
  draft: unknown;
  status: "editing" | "pending" | "failed";
  error?: string;
};

export type ResourceEditAction =
  | { type: "edit-staged"; resourceKey: string; draft: unknown }
  | { type: "edit-cancelled"; resourceKey: string }
  | { type: "write-submitted"; resourceKey: string }
  | { type: "write-failed"; resourceKey: string; error: string }
  | { type: "write-confirmed"; resourceKey: string };

/** Shared draft state only. A committed, revalidated server resource remains the data authority. */
export function reduceResourceEdits(
  edits: Readonly<Record<string, ResourceEdit>>,
  action: ResourceEditAction,
): Record<string, ResourceEdit> {
  switch (action.type) {
    case "edit-staged":
      return { ...edits, [action.resourceKey]: { draft: action.draft, status: "editing" } };
    case "edit-cancelled": {
      if (!edits[action.resourceKey] || edits[action.resourceKey].status === "pending") return edits;
      const next = { ...edits };
      delete next[action.resourceKey];
      return next;
    }
    case "write-submitted": {
      const edit = edits[action.resourceKey];
      return !edit ? edits : { ...edits,
        [action.resourceKey]: { draft: edit.draft, status: "pending" },
      };
    }
    case "write-failed": {
      const edit = edits[action.resourceKey];
      return !edit || edit.status !== "pending" ? edits : { ...edits,
        [action.resourceKey]: { draft: edit.draft, status: "failed", error: action.error },
      };
    }
    case "write-confirmed": {
      const edit = edits[action.resourceKey];
      if (!edit || edit.status !== "pending") return edits;
      const next = { ...edits };
      delete next[action.resourceKey];
      return next;
    }
  }
}
