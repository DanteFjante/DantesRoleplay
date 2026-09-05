/** Signature for the generated validator; avoids inferring its large control-flow graph. */
declare function validate(value: unknown): boolean;
export default validate;
export const contract: { id: string; qualifiedId: string; version: number; contentHash: string; outputSchemaHash: string };
