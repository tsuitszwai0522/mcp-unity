import { describe, expect, it } from '@jest/globals';
import { PAYLOAD_MAX_CHARS, payloadContent } from '../utils/toolPayload.js';

describe('payloadContent', () => {
  it('returns the complete pretty-printed JSON within the character limit', () => {
    const payload = { success: true, items: [{ id: 1 }] };

    expect(payloadContent(payload)).toEqual({
      type: 'text',
      text: JSON.stringify(payload, null, 2),
    });
  });

  it('falls back to compact JSON when pretty JSON alone exceeds the limit', () => {
    const entries = Array.from({ length: 200 }, (_, index) => ({
      key: `cb_ext_item_${String(index).padStart(3, '0')}`,
      value: `Ordinary equipment description ${index}`.padEnd(40, '.'),
    }));
    const payload = { entries, totalEntries: entries.length, truncated: false };
    const pretty = JSON.stringify(payload, null, 2);
    const compact = JSON.stringify(payload);

    expect(pretty.length).toBeGreaterThan(PAYLOAD_MAX_CHARS);
    expect(compact.length).toBeLessThanOrEqual(PAYLOAD_MAX_CHARS);
    expect(payloadContent(payload).text).toBe(compact);
  });

  it('returns valid JSON truncation metadata with a compact payload preview over the limit', () => {
    const payload = {
      elementInfo: { children: 'x'.repeat(PAYLOAD_MAX_CHARS) },
      requestId: 42,
    };
    const compact = JSON.stringify(payload);

    const content = payloadContent(payload);
    const summary = JSON.parse(content.text);

    expect(content.type).toBe('text');
    expect(summary).toEqual({
      requestId: 42,
      _truncated: true,
      _totalChars: compact.length,
      _keys: ['elementInfo', 'requestId'],
      _keysTruncated: false,
      _keyCount: 2,
      _droppedKeys: ['elementInfo'],
      _droppedKeysTruncated: false,
      _preview: compact.slice(0, 2000),
      _hint: expect.stringContaining('exceeds the 20000-character content limit'),
    });
    expect(summary._preview).toContain('x'.repeat(100));
    expect(content.text.length).toBeLessThanOrEqual(PAYLOAD_MAX_CHARS);
  });

  it('preserves top-level scalar fields while truncation metadata wins reserved-name collisions', () => {
    const payload = {
      table: 'CB_Tooltip',
      locale: null,
      totalEntries: 200,
      valuesIncluded: true,
      truncated: false,
      entries: Array.from({ length: 200 }, () => ({ value: 'x'.repeat(100) })),
      _truncated: false,
      _totalChars: -1,
      _preview: 'spoofed',
      _droppedKeys: 'spoofed',
    };
    const compact = JSON.stringify(payload);

    const summary = JSON.parse(payloadContent(payload).text);

    expect(summary).toMatchObject({
      table: 'CB_Tooltip',
      locale: null,
      totalEntries: 200,
      valuesIncluded: true,
      truncated: false,
      _truncated: true,
      _totalChars: compact.length,
      _droppedKeys: ['entries'],
      _droppedKeysTruncated: false,
      _preview: compact.slice(0, 2000),
    });
    expect(summary).not.toHaveProperty('entries');
  });

  it('treats the exact limit as complete and one character over as truncated', () => {
    const atLimit = 'x'.repeat(PAYLOAD_MAX_CHARS - 2);
    const overLimit = 'x'.repeat(PAYLOAD_MAX_CHARS - 1);

    expect(JSON.stringify(atLimit)).toHaveLength(PAYLOAD_MAX_CHARS);
    expect(payloadContent(atLimit).text).toBe(JSON.stringify(atLimit));

    const overLimitContent = payloadContent(overLimit);
    expect(JSON.stringify(overLimit)).toHaveLength(PAYLOAD_MAX_CHARS + 1);
    expect(JSON.parse(overLimitContent.text)).toMatchObject({
      _truncated: true,
      _totalChars: PAYLOAD_MAX_CHARS + 1,
    });
  });

  it('caps key metadata and keeps the truncation response itself within the limit', () => {
    const payload = Object.fromEntries(
      Array.from({ length: 3000 }, (_, index) => [`external_payload_key_${index}`, index]),
    );

    const content = payloadContent(payload);
    const summary = JSON.parse(content.text);

    expect(content.text.length).toBeLessThanOrEqual(PAYLOAD_MAX_CHARS);
    expect(summary._keys).toHaveLength(50);
    expect(summary._keysTruncated).toBe(true);
    expect(summary._keyCount).toBe(3000);
    expect(summary._droppedKeysTruncated).toBe(true);
    expect(summary._preview).toBe(JSON.stringify(payload).slice(0, 2000));
  });

  it('drops oversized key metadata until the full configured preview fits', () => {
    const limits = { maxChars: 500, previewChars: 20, maxKeys: 5 };
    const payload = Object.fromEntries(
      Array.from({ length: 5 }, (_, index) => [
        `external_payload_key_${index}_${'k'.repeat(100)}`,
        { nested: 'x'.repeat(200) },
      ]),
    );
    const compact = JSON.stringify(payload);

    const content = payloadContent(payload, limits);
    const summary = JSON.parse(content.text);

    expect(content.text.length).toBeLessThanOrEqual(limits.maxChars);
    expect(summary._keys.length).toBeGreaterThan(0);
    expect(summary._keys.length).toBeLessThan(limits.maxKeys);
    expect(summary._keysTruncated).toBe(true);
    expect(summary._droppedKeysTruncated).toBe(true);
    expect(summary._preview).toBe(compact.slice(0, limits.previewChars));
  });

  it('binary-searches the preview down when key removal alone cannot meet a small limit', () => {
    const limits = { maxChars: 300, previewChars: 200, maxKeys: 5 };
    const payload = Array.from({ length: 100 }, () => '\u0001'.repeat(20));
    const compact = JSON.stringify(payload);

    const content = payloadContent(payload, limits);
    const summary = JSON.parse(content.text);

    expect(content.text.length).toBeLessThanOrEqual(limits.maxChars);
    expect(summary._keys).toEqual([]);
    expect(summary._preview.length).toBeGreaterThan(0);
    expect(summary._preview.length).toBeLessThan(limits.previewChars);
    expect(summary._preview).toBe(compact.slice(0, summary._preview.length));
  });

  it('handles null and previews an oversized array without throwing', () => {
    expect(payloadContent(null)).toEqual({ type: 'text', text: 'null' });

    const payload = Array.from({ length: 300 }, () => 'x'.repeat(100));
    const content = payloadContent(payload);
    const summary = JSON.parse(content.text);

    expect(content.text.length).toBeLessThanOrEqual(PAYLOAD_MAX_CHARS);
    expect(summary).toMatchObject({
      _truncated: true,
      _keys: [],
      _keysTruncated: false,
      _keyCount: 0,
      _droppedKeys: [],
      _droppedKeysTruncated: false,
    });
    expect(summary._preview).toBe(JSON.stringify(payload).slice(0, 2000));
  });

  it('keeps an oversized top-level array prefix with machine-readable counts', () => {
    const limits = { maxChars: 600 };
    const entries = Array.from({ length: 60 }, (_, index) => ({
      index,
      value: `entry-${index}-${'x'.repeat(40)}`,
    }));
    const payload = { entries, total: entries.length, truncated: false };
    const compact = JSON.stringify(payload);

    const content = payloadContent(payload, limits);
    const summary = JSON.parse(content.text);

    expect(compact.length).toBeGreaterThan(limits.maxChars);
    expect(summary.entries.length).toBeGreaterThan(0);
    expect(summary.entries.length).toBeLessThan(entries.length);
    expect(summary.entries).toEqual(entries.slice(0, summary.entries.length));
    expect(summary.entries).toHaveLength(summary._arraysTruncated.entries.kept);
    expect(summary._arraysTruncated.entries.total).toBe(entries.length);
    expect(summary._totalChars).toBe(compact.length);
    expect(summary).not.toHaveProperty('_preview');
    expect(summary).not.toHaveProperty('_keys');
    expect(summary).not.toHaveProperty('_droppedKeys');
  });

  it('keeps the emitted array-prefix payload within the configured character limit', () => {
    const limits = { maxChars: 333 };
    const payload = {
      items: Array.from({ length: 100 }, () => '\u0001'.repeat(20)),
    };

    const content = payloadContent(payload, limits);

    expect(JSON.stringify(payload).length).toBeGreaterThan(limits.maxChars);
    expect(content.text.length).toBeLessThanOrEqual(limits.maxChars);
  });

  it('keeps the legacy drop-path output unchanged when no top-level array exists', () => {
    const limits = { maxChars: 500, previewChars: 40, maxKeys: 10 };
    const payload = {
      nested: { value: 'x'.repeat(600) },
      count: 7,
    };
    const compact = JSON.stringify(payload);
    const expected = JSON.stringify({
      count: 7,
      _truncated: true,
      _totalChars: compact.length,
      _keys: ['nested', 'count'],
      _keysTruncated: false,
      _keyCount: 2,
      _droppedKeys: ['nested'],
      _droppedKeysTruncated: false,
      _preview: compact.slice(0, limits.previewChars),
      _hint: 'Payload exceeds the 500-character content limit; narrow the request with a limit or filter.',
    }, null, 2);

    expect(payloadContent(payload, limits).text).toBe(expected);
  });

  it('keeps an empty array and reports zero kept when no element fits', () => {
    const limits = { maxChars: 260 };
    const entries = [{ value: 'x'.repeat(1000) }];
    const payload = { entries, total: entries.length };

    const content = payloadContent(payload, limits);
    const summary = JSON.parse(content.text);

    expect(content.text.length).toBeLessThanOrEqual(limits.maxChars);
    expect(summary.entries).toEqual([]);
    expect(summary._arraysTruncated.entries).toEqual({ kept: 0, total: entries.length });
  });

  it('falls through to the legacy drop path when even the empty-array probe is oversized', () => {
    const limits = { maxChars: 500, previewChars: 30, maxKeys: 10 };
    const payload = {
      entries: [1, 2, 3],
      detail: { value: 'x'.repeat(1000) },
      total: 3,
    };
    const compact = JSON.stringify(payload);

    const content = payloadContent(payload, limits);
    const summary = JSON.parse(content.text);

    expect(content.text.length).toBeLessThanOrEqual(limits.maxChars);
    expect(summary).not.toHaveProperty('entries');
    expect(summary._droppedKeys).toEqual(['entries', 'detail']);
    expect(summary._preview).toBe(compact.slice(0, limits.previewChars));
  });

  it('omits a fully retained small array from multi-array truncation metadata', () => {
    const limits = { maxChars: 400 };
    const small = [1, 2];
    const big = Array.from({ length: 100 }, () => 'x'.repeat(20));
    const payload = { small, big };

    const summary = JSON.parse(payloadContent(payload, limits).text);

    expect(summary.small).toEqual(small);
    expect(summary._arraysTruncated).not.toHaveProperty('small');
    expect(summary._arraysTruncated.big.kept).toBe(summary.big.length);
    expect(summary._arraysTruncated.big.total).toBe(big.length);
  });

  it('uses the monotonic probe shape when a completed small array leaves emitted metadata', () => {
    const payload = { a: [null], b: Array.from({ length: 1000 }, () => null) };
    // 呢個 236 係由目前實作逐值量出；本測試釘住二分判準必須用單調 probe，唔可以用 emit。
    // metadata 形狀一旦改動就要重新量度門檻，唔可以將呢個值當成任意常數。
    const limits = { maxChars: 236 };

    const content = payloadContent(payload, limits);
    const summary = JSON.parse(content.text);

    expect(content.text.length).toBeLessThanOrEqual(limits.maxChars);
    expect(summary.a).toEqual([null]);
    expect(summary._arraysTruncated).not.toHaveProperty('a');
    expect(summary.b).toEqual([null]);
    expect(summary._arraysTruncated.b).toEqual({ kept: 1, total: 1000 });
  });

  it('always emits non-empty array truncation metadata after the prefix path succeeds', () => {
    const payload = {
      entries: Array.from({ length: 100 }, () => 'x'.repeat(20)),
    };

    for (const maxChars of [250, 400, 800]) {
      const summary = JSON.parse(payloadContent(payload, { maxChars }).text);

      expect(Object.keys(summary._arraysTruncated).length).toBeGreaterThan(0);
    }
  });

  it('does not mutate the caller payload while slicing array prefixes', () => {
    const entries = Object.freeze(
      Array.from({ length: 20 }, () => Object.freeze({ value: 'x'.repeat(100) })),
    );
    const payload = Object.freeze({ entries, total: entries.length });

    expect(() => payloadContent(payload, { maxChars: 300 })).not.toThrow();
    expect(payload.entries).toBe(entries);
    expect(payload.entries).toHaveLength(20);
  });

  it('greedily keeps a complete small sibling when another array first element does not fit', () => {
    const content = payloadContent(
      { small: [1, 2, 3, 4, 5], big: ['x'.repeat(1000)] },
      { maxChars: 400 },
    );
    const summary = JSON.parse(content.text);

    expect(content.text.length).toBeLessThanOrEqual(400);
    expect(summary.small).toEqual([1, 2, 3, 4, 5]);
    expect(summary._arraysTruncated).not.toHaveProperty('small');
    expect(summary.big).toEqual([]);
    expect(summary._arraysTruncated.big).toEqual({ kept: 0, total: 1 });
  });

  it('greedily completes multiple small arrays after an oversized sibling top-up fails', () => {
    const payload = {
      first: [1, 2, 3],
      second: [4, 5, 6, 7],
      oversized: ['x'.repeat(1000)],
    };

    const content = payloadContent(payload, { maxChars: 450 });
    const summary = JSON.parse(content.text);

    expect(content.text.length).toBeLessThanOrEqual(450);
    expect(summary.first).toEqual(payload.first);
    expect(summary.second).toEqual(payload.second);
    expect(summary._arraysTruncated).not.toHaveProperty('first');
    expect(summary._arraysTruncated).not.toHaveProperty('second');
    expect(summary._arraysTruncated.oversized).toEqual({ kept: 0, total: 1 });
  });

  it('does not let a tool-owned _hint disable the array-prefix path', () => {
    const payload = {
      _hint: 'Tool-specific follow-up guidance',
      entries: Array.from({ length: 100 }, () => 'x'.repeat(30)),
    };

    const summary = JSON.parse(payloadContent(payload, { maxChars: 400 }).text);

    expect(summary.entries.length).toBeGreaterThan(0);
    expect(summary.entries).toHaveLength(summary._arraysTruncated.entries.kept);
    expect(summary._arraysTruncated.entries.total).toBe(payload.entries.length);
  });

  it('records __proto__ array truncation as an own metadata property', () => {
    const payload = Object.fromEntries([
      ['__proto__', [{ value: 'x'.repeat(1000) }]],
    ]);

    const summary = JSON.parse(payloadContent(payload, { maxChars: 300 }).text);
    const arraysTruncated = summary._arraysTruncated;

    expect(Object.prototype.hasOwnProperty.call(arraysTruncated, '__proto__')).toBe(true);
    expect(arraysTruncated.__proto__).toEqual({ kept: 0, total: 1 });
  });
});
