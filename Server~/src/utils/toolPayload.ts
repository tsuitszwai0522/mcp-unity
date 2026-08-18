export const PAYLOAD_MAX_CHARS = 20000;

const PAYLOAD_PREVIEW_CHARS = 2000;
const PAYLOAD_MAX_KEYS = 50;

export type PayloadLimits = {
  maxChars?: number;
  previewChars?: number;
  maxKeys?: number;
};

export type PayloadTextContent = {
  type: 'text';
  text: string;
};

const payloadKeys = (payload: unknown): string[] => {
  if (payload === null || typeof payload !== 'object' || Array.isArray(payload)) {
    return [];
  }

  return Object.keys(payload);
};

type PayloadScalar = string | number | boolean | null;

const truncationMetadataKeys = new Set([
  '_truncated',
  '_totalChars',
  '_keys',
  '_keysTruncated',
  '_keyCount',
  '_droppedKeys',
  '_droppedKeysTruncated',
  '_preview',
  '_hint',
]);

const payloadScalarEntries = (payload: unknown): Array<[string, PayloadScalar]> => {
  if (payload === null || typeof payload !== 'object' || Array.isArray(payload)) {
    return [];
  }

  return Object.entries(payload).filter((entry): entry is [string, PayloadScalar] => {
    const value = entry[1];
    return value === null || typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean';
  });
};

// Truncation metadata has a fixed serialized floor, so maxChars is not a hard guarantee below that floor.
export function payloadContent(payload: unknown, limits?: PayloadLimits): PayloadTextContent {
  const maxChars = limits?.maxChars ?? PAYLOAD_MAX_CHARS;
  const previewChars = limits?.previewChars ?? PAYLOAD_PREVIEW_CHARS;
  const maxKeys = limits?.maxKeys ?? PAYLOAD_MAX_KEYS;
  const pretty = JSON.stringify(payload, null, 2) ?? 'null';

  if (pretty.length <= maxChars) {
    return { type: 'text', text: pretty };
  }

  const compact = JSON.stringify(payload) ?? 'null';
  if (compact.length <= maxChars) {
    return { type: 'text', text: compact };
  }

  const allKeys = payloadKeys(payload);
  let includedKeys = allKeys.slice(0, maxKeys);
  let droppedKeyLimit = maxKeys;
  const scalarEntries = payloadScalarEntries(payload).filter(([key]) => !truncationMetadataKeys.has(key));
  let includedScalarEntries: Array<[string, PayloadScalar]> = [];
  const preview = compact.slice(0, previewChars);
  const serializeMetadata = (
    keys: string[],
    previewText: string,
    scalars: Array<[string, PayloadScalar]> = includedScalarEntries,
  ) => {
    const outputKeys = new Set([
      ...truncationMetadataKeys,
      ...scalars.map(([key]) => key),
    ]);
    const allDroppedKeys = allKeys.filter((key) => !outputKeys.has(key));
    const droppedKeys = allDroppedKeys.slice(0, droppedKeyLimit);

    return JSON.stringify({
      ...Object.fromEntries(scalars),
      _truncated: true,
      _totalChars: compact.length,
      _keys: keys,
      _keysTruncated: keys.length < allKeys.length,
      _keyCount: allKeys.length,
      _droppedKeys: droppedKeys,
      _droppedKeysTruncated: droppedKeys.length < allDroppedKeys.length,
      _preview: previewText,
      _hint: `Payload exceeds the ${maxChars}-character content limit; narrow the request with a limit or filter.`,
    }, null, 2);
  };
  const fitScalarEntries = (keys: string[], previewText: string) => {
    const fitted: Array<[string, PayloadScalar]> = [];

    for (const entry of scalarEntries) {
      fitted.push(entry);
      if (serializeMetadata(keys, previewText, fitted).length > maxChars) {
        fitted.pop();
      }
    }

    return fitted;
  };

  let metadata = serializeMetadata(includedKeys, preview, []);

  // A pathological number of long dropped keys must not make the metadata exceed the cap.
  while (metadata.length > maxChars && droppedKeyLimit > 0) {
    droppedKeyLimit -= 1;
    metadata = serializeMetadata(includedKeys, preview, []);
  }

  // Unusually long property names must not make the truncation metadata exceed the same cap.
  while (metadata.length > maxChars && includedKeys.length > 0) {
    includedKeys = includedKeys.slice(0, -1);
    metadata = serializeMetadata(includedKeys, preview, []);
  }

  if (serializeMetadata(includedKeys, '', scalarEntries).length <= maxChars) {
    includedScalarEntries = scalarEntries;
    metadata = serializeMetadata(includedKeys, preview);
  } else if (metadata.length <= maxChars) {
    includedScalarEntries = fitScalarEntries(includedKeys, preview);
    metadata = serializeMetadata(includedKeys, preview);
  } else {
    // Preserve scalar contract fields ahead of preview text when both cannot fit in full.
    includedScalarEntries = fitScalarEntries(includedKeys, '');
    metadata = serializeMetadata(includedKeys, preview);
  }

  if (metadata.length > maxChars) {
    let low = 0;
    let high = preview.length;
    let fitted = serializeMetadata(includedKeys, '');

    while (low <= high) {
      const middle = Math.floor((low + high) / 2);
      const candidate = serializeMetadata(includedKeys, preview.slice(0, middle));
      if (candidate.length <= maxChars) {
        fitted = candidate;
        low = middle + 1;
      } else {
        high = middle - 1;
      }
    }

    metadata = fitted;
  }

  return { type: 'text', text: metadata };
}
