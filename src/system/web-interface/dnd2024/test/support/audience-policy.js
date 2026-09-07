const VALID_PERSPECTIVES = new Set(["player", "dm"]);
const TOKEN_LIMIT = 200;
const EMAIL_LIMIT = 254;

function validToken(value) {
  return (
    typeof value === "string" &&
    value.length > 0 &&
    value.length <= TOKEN_LIMIT &&
    value === value.trim() &&
    !/\s/u.test(value)
  );
}

export function parseDmPrincipalIds(value) {
  if (typeof value !== "string" || value.length === 0) return [];
  const tokens = value.split(",");
  if (tokens.some((token) => !validToken(token)) || new Set(tokens).size !== tokens.length) return [];
  return tokens;
}

function normalizeEmail(value) {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > EMAIL_LIMIT ||
    value !== value.trim() ||
    /\s/u.test(value)
  ) {
    return null;
  }

  const parts = value.split("@");
  if (parts.length !== 2 || parts.some((part) => part.length === 0)) return null;
  return value.toLocaleLowerCase("en-US");
}

export function parseDmEmails(value) {
  if (typeof value !== "string" || value.length === 0) return [];
  const tokens = value.split(",").map(normalizeEmail);
  if (tokens.some((token) => token === null) || new Set(tokens).size !== tokens.length) return [];
  return tokens;
}

export function normalizeRequestedPerspective(value) {
  return VALID_PERSPECTIVES.has(value) ? value : "player";
}

export function resolveAudience({
  authenticatedUserId,
  authenticatedUserEmail,
  requestedPerspective,
  dmPrincipalIds,
  dmEmails = [],
  nodeEnvironment = "production",
  localSeat,
}) {
  const developmentSeat =
    nodeEnvironment !== "production" && VALID_PERSPECTIVES.has(localSeat) ? localSeat : null;
  const principalValid = validToken(authenticatedUserId);
  const authenticatedEmail = normalizeEmail(authenticatedUserEmail);

  if (!principalValid && !authenticatedEmail && !developmentSeat) {
    return { status: "denied" };
  }

  const idIsDm = principalValid && dmPrincipalIds.includes(authenticatedUserId);
  const emailIsDm = authenticatedEmail !== null && dmEmails.includes(authenticatedEmail);
  const seat = developmentSeat ?? (idIsDm || emailIsDm ? "dm" : "player");
  const requested = normalizeRequestedPerspective(requestedPerspective);
  const perspective = seat === "dm" ? requested : "player";

  return {
    status: "ready",
    seat,
    perspective,
    allowedPerspectives: seat === "dm" ? ["dm", "player"] : ["player"],
  };
}
