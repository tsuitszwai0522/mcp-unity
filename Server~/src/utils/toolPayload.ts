export const PAYLOAD_MAX_CHARS = 20000;

export const PAYLOAD_STRUCTURED = Symbol.for('mcpUnity.structuredPayload');

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

const withStructured = (text: string): PayloadTextContent => {
  const item: PayloadTextContent = { type: 'text', text };
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return item;
  }
  if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return item;
  }
  Object.defineProperty(item, PAYLOAD_STRUCTURED, { value: parsed, enumerable: false });
  return item;
};

export function attachStructuredContent(result: unknown): unknown {
  if (result === null || typeof result !== 'object' || Array.isArray(result)) {
    return result;
  }

  const toolResult = result as Record<string, unknown>;
  if (toolResult.structuredContent !== undefined || !Array.isArray(toolResult.content)) {
    return result;
  }

  for (const item of toolResult.content) {
    if (item === null || typeof item !== 'object') {
      continue;
    }
    if (Object.prototype.hasOwnProperty.call(item, PAYLOAD_STRUCTURED)) {
      const structuredContent = (item as Record<PropertyKey, unknown>)[PAYLOAD_STRUCTURED];
      return { ...toolResult, structuredContent };
    }
  }

  return result;
}

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
  '_arraysTruncated',
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
    return withStructured(pretty);
  }

  const compact = JSON.stringify(payload) ?? 'null';
  if (compact.length <= maxChars) {
    return withStructured(compact);
  }

  if (payload !== null && typeof payload === 'object' && !Array.isArray(payload)) {
    const payloadRecord = payload as Record<string, unknown>;
    const arrayEntries = Object.entries(payloadRecord).filter(
      (entry): entry is [string, unknown[]] => Array.isArray(entry[1]),
    );
    const hasTruncationMetadata = ['_truncated', '_arraysTruncated']
      .some((key) => Object.prototype.hasOwnProperty.call(payloadRecord, key));

    // 已帶裁剪 metadata 的 payload 保持走既有路徑，避免重新解讀保留欄位。
    if (arrayEntries.length > 0 && !hasTruncationMetadata) {
      const uniformArrayLimits = (maxItems: number) => new Map(
        arrayEntries.map(([key, value]) => [key, Math.min(maxItems, value.length)]),
      );
      const serializeArrayPrefix = (
        arrayLimits: ReadonlyMap<string, number>,
        includeCompleteArrays: boolean,
      ) => {
        const prefixPayload: Record<string, unknown> = { ...payloadRecord };
        const arraysTruncated = Object.create(null) as Record<string, { kept: number; total: number }>;

        for (const [key, value] of arrayEntries) {
          const kept = Math.min(arrayLimits.get(key) ?? 0, value.length);
          prefixPayload[key] = value.slice(0, kept);
          if (includeCompleteArrays || kept < value.length) {
            arraysTruncated[key] = { kept, total: value.length };
          }
        }

        return JSON.stringify({
          ...prefixPayload,
          _truncated: true,
          _totalChars: compact.length,
          _arraysTruncated: arraysTruncated,
          _hint: `Payload exceeds the ${maxChars}-character content limit; narrow the request with a limit or filter.`,
        });
      };
      const emptyProbe = serializeArrayPrefix(uniformArrayLimits(0), true);

      if (emptyProbe.length <= maxChars) {
        let low = 1;
        let high = Math.max(...arrayEntries.map(([, value]) => value.length));
        let fittedMaxItems = 0;

        // 搜尋固定列齊所有陣列 metadata 的單調形態，輸出時才移除完整陣列條目。
        while (low <= high) {
          const middle = Math.floor((low + high) / 2);
          const probe = serializeArrayPrefix(uniformArrayLimits(middle), true);
          if (probe.length <= maxChars) {
            fittedMaxItems = middle;
            low = middle + 1;
          } else {
            high = middle - 1;
          }
        }

        const fittedArrayLimits = uniformArrayLimits(fittedMaxItems);
        const shortestArraysFirst = [...arrayEntries]
          .sort((left, right) => left[1].length - right[1].length);

        // 每個提升都實際序列化 probe 驗證，失敗只還原該陣列嘅上限。
        for (const [key, value] of shortestArraysFirst) {
          const previousLimit = fittedArrayLimits.get(key) ?? 0;
          if (previousLimit >= value.length) {
            continue;
          }

          fittedArrayLimits.set(key, value.length);
          const topUpProbe = serializeArrayPrefix(fittedArrayLimits, true);
          if (topUpProbe.length > maxChars) {
            fittedArrayLimits.set(key, previousLimit);
          }
        }

        return withStructured(serializeArrayPrefix(fittedArrayLimits, false));
      }
    }
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

  return withStructured(metadata);
}
