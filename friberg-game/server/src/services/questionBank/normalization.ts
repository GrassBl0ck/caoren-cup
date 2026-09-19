export function normalizePersonAlias(value: string): string {
  return value.normalize('NFKC').trim().toLocaleLowerCase('en-US').replace(/[\s_-]+/g, '');
}
