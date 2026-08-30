/** @typedef {import("./hub-types").RuleReadModel} RuleReadModel */
/** @typedef {"All" | "Action" | "Reaction"} RuleCategoryFilter */

/** @type {readonly RuleCategoryFilter[]} */
export const RULE_CATEGORY_OPTIONS = ["All", "Action", "Reaction"];

/**
 * @param {RuleReadModel[]} rules
 * @param {string} query
 * @param {RuleCategoryFilter} category
 * @returns {RuleReadModel[]}
 */
export function filterRuleReferences(rules, query, category) {
  const normalizedQuery = query.trim().toLocaleLowerCase();
  return rules.filter((rule) => {
    if (category !== "All" && rule.category !== category) return false;
    if (!normalizedQuery) return true;
    return [rule.title, rule.category, rule.summary, rule.source.locator]
      .some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
  });
}
