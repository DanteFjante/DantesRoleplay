/** @typedef {import("./hub-types").RuleReadModel} RuleReadModel */

/**
 * @param {RuleReadModel[]} rules
 * @returns {RuleReadModel["section"][]}
 */
export function ruleSectionOptions(rules) {
  const sections = new Map();
  for (const rule of rules) sections.set(rule.section.id, rule.section);
  return [...sections.values()].sort((left, right) => left.order - right.order
    || left.label.localeCompare(right.label)
    || left.id.localeCompare(right.id));
}

/**
 * @param {RuleReadModel[]} rules
 * @param {string} query
 * @param {string} sectionId
 * @returns {RuleReadModel[]}
 */
export function filterRuleReferences(rules, query, sectionId) {
  const normalizedQuery = query.trim().toLocaleLowerCase();
  return rules.filter((rule) => {
    if (sectionId && rule.section.id !== sectionId) return false;
    if (!normalizedQuery) return true;
    return [
      rule.title,
      rule.section.label,
      rule.summary,
      rule.id,
      rule.resolutionKey,
      rule.source.label,
      rule.source.classification,
      ...rule.blocks.flatMap((block) => [block.heading ?? "", block.body ?? "", ...block.items]),
      ...rule.examples.flatMap((example) => [example.title, example.body]),
      ...rule.citations.flatMap((citation) => [citation.sourceId, citation.locator]),
      ...rule.authority.mechanicIds,
      ...rule.authority.procedureIds,
    ].some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
  });
}
