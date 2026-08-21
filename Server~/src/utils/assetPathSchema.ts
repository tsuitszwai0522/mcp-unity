import * as z from 'zod';

/**
 * Schema-side early validation for explicit Unity asset paths. Unity remains the
 * authority and repeats validation against the real project root, including for
 * batch_execute calls that bypass individual tool schemas.
 */
export function explicitAssetPathSchema(description: string, allowAssetsRoot = false) {
  return z.string().refine(
    (value) => isExplicitAssetPathInsideAssets(value, allowAssetsRoot),
    'Path must explicitly remain inside Assets/ after resolving dot segments'
  ).describe(description);
}

export function isExplicitAssetPathInsideAssets(value: string, allowAssetsRoot = false): boolean {
  if (value === 'Assets') return allowAssetsRoot;
  if (!value.startsWith('Assets/')) return false;

  let depth = 0;
  for (const segment of value.slice('Assets/'.length).split('/')) {
    if (segment === '' || segment === '.') continue;
    if (segment === '..') {
      if (depth === 0) return false;
      depth -= 1;
      continue;
    }
    depth += 1;
  }

  return depth > 0;
}

