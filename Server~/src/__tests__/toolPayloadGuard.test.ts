import { describe, expect, it } from '@jest/globals';
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

const toolsDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'tools');

function propertyName(node: ts.PropertyName): string | undefined {
  if (ts.isIdentifier(node) || ts.isStringLiteral(node) || ts.isNumericLiteral(node)) {
    return node.text;
  }

  if (ts.isComputedPropertyName(node)) {
    const expression = unwrapExpression(node.expression);
    return ts.isStringLiteral(expression) || ts.isNoSubstitutionTemplateLiteral(expression)
      ? expression.text
      : undefined;
  }

  return undefined;
}

function unwrapExpression(node: ts.Expression): ts.Expression {
  let current = node;
  while (ts.isAsExpression(current) || ts.isTypeAssertionExpression(current) || ts.isParenthesizedExpression(current)) {
    current = current.expression;
  }
  return current;
}

function nonImageDataProperties(fileName: string, source: string): string[] {
  // Known static-analysis boundary: non-literal computed keys and values introduced through
  // object spreads cannot be resolved here without interprocedural data-flow analysis.
  const sourceFile = ts.createSourceFile(fileName, source, ts.ScriptTarget.Latest, true);
  const violations: string[] = [];

  const visit = (node: ts.Node) => {
    const isDataProperty =
      (ts.isPropertyAssignment(node) || ts.isShorthandPropertyAssignment(node)) &&
      propertyName(node.name) === 'data' &&
      ts.isObjectLiteralExpression(node.parent);

    if (isDataProperty) {
      const typeProperty = node.parent.properties.find(
        (property): property is ts.PropertyAssignment =>
          ts.isPropertyAssignment(property) && propertyName(property.name) === 'type',
      );
      const typeValue = typeProperty ? unwrapExpression(typeProperty.initializer) : undefined;
      const isImageContent = typeValue !== undefined && ts.isStringLiteral(typeValue) && typeValue.text === 'image';

      if (!isImageContent) {
        const { line } = sourceFile.getLineAndCharacterOfPosition(node.name.getStart(sourceFile));
        violations.push(`${fileName}:${line + 1}`);
      }
    }

    ts.forEachChild(node, visit);
  };

  visit(sourceFile);
  return violations;
}

function recursivelyListedTypeScriptFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      return recursivelyListedTypeScriptFiles(path);
    }
    return entry.isFile() && entry.name.endsWith('.ts') ? [path] : [];
  });
}

describe('tool payload contract source guard', () => {
  it('recursively allows MCP image data only in tool sources', () => {
    const violations = recursivelyListedTypeScriptFiles(toolsDir)
      .flatMap((filePath) =>
        nonImageDataProperties(filePath, readFileSync(filePath, 'utf8')),
      );

    expect(violations).toEqual([]);
  });

  it('catches the former dynamic spread-conditional sibling', () => {
    const formerDynamicReturn = `return {
  content: [{ type: 'text' as const, text }],
  ...(result.success !== undefined ? { data: result } : {})
};`;
    expect(nonImageDataProperties('dynamicTools.ts', formerDynamicReturn)).toEqual([
      'dynamicTools.ts:3',
    ]);
  });

  it('catches a computed data property', () => {
    const computedDataReturn = `return {
  content: [{ type: 'text' as const, text }],
  ['data']: result
};`;

    expect(nonImageDataProperties('computedDataTool.ts', computedDataReturn)).toEqual([
      'computedDataTool.ts:3',
    ]);
  });

  it('catches a shorthand data property', () => {
    const shorthandDataReturn = `return {
  content,
  data
};`;

    expect(nonImageDataProperties('shorthandDataTool.ts', shorthandDataReturn)).toEqual([
      'shorthandDataTool.ts:3',
    ]);
  });
});
