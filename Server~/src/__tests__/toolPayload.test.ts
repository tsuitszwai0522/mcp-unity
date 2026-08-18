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
});
