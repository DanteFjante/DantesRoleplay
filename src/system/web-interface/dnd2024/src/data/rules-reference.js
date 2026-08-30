/** @typedef {import("./hub-types").RuleReadModel} RuleReadModel */

/**
 * @param {RuleReadModel[]} rules
 * @returns {string[]}
 */
export function ruleCategoryOptions(rules) {
  return ["All", ...new Set(rules.map((rule) => rule.category).filter(Boolean))];
}

/**
 * @param {RuleReadModel[]} rules
 * @param {string} query
 * @param {string} category
 * @returns {RuleReadModel[]}
 */
export function filterRuleReferences(rules, query, category) {
  const normalizedQuery = query.trim().toLocaleLowerCase();
  return rules.filter((rule) => {
    if (category !== "All" && rule.category !== category) return false;
    if (!normalizedQuery) return true;
    return [
      rule.title,
      rule.category,
      rule.subcategory,
      rule.summary,
      rule.id,
      rule.source?.locator ?? "",
    ].some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
  });
}
