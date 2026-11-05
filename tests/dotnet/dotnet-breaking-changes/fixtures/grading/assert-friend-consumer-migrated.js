// Usage: node assert-friend-consumer-migrated.js <BillingTests.cs>
// The fixture's original in-repo consumer must migrate to CalculateDiscount. Other tests may
// deliberately exercise the retained ApplyDiscount compatibility shim.
const fs = require('fs');
const { mask } = require('./assert-tests-kill-mutants.js');

const originalTest = 'PricingMath_AppliesDiscountForFriendAssembly';

function maskInterpolatedReferences(source) {
  const out = mask(source).split('');
  const restoreExpression = (start, delimiterLength) => {
    const tail = mask(source.slice(start));
    const close = '}'.repeat(delimiterLength);
    let depth = 0;
    for (let j = 0; j < tail.length; j++) {
      if (depth === 0 && tail.startsWith(close, j)) {
        const end = start + j;
        const expression = maskInterpolatedReferences(source.slice(start, end));
        for (let k = 0; k < expression.length; k++) out[start + k] = expression[k];
        return end + delimiterLength;
      }
      if (tail[j] === '{') depth++;
      else if (tail[j] === '}' && depth > 0) depth--;
    }
    return -1;
  };

  for (let i = 0; i < source.length;) {
    if (source.startsWith('//', i)) {
      const end = source.indexOf('\n', i + 2);
      i = end < 0 ? source.length : end;
      continue;
    }
    if (source.startsWith('/*', i)) {
      const end = source.indexOf('*/', i + 2);
      i = end < 0 ? source.length : end + 2;
      continue;
    }
    if (source[i] === "'") {
      i++;
      while (i < source.length && source[i] !== "'") i += source[i] === '\\' ? 2 : 1;
      i++;
      continue;
    }

    const rawPrefix = /^(\$+)("{3,})/.exec(source.slice(i));
    if (rawPrefix) {
      const braces = rawPrefix[1].length;
      const quotes = rawPrefix[2].length;
      const open = '{'.repeat(braces);
      const close = '"'.repeat(quotes);
      let cursor = i + rawPrefix[0].length;
      while (cursor < source.length) {
        if (source.startsWith(close, cursor)) {
          cursor += quotes;
          break;
        }
        if (source.startsWith(open, cursor)) {
          const end = restoreExpression(cursor + braces, braces);
          if (end < 0) {
            cursor = source.length;
            break;
          }
          cursor = end;
          continue;
        }
        cursor++;
      }
      i = cursor;
      continue;
    }

    const prefix = source.startsWith('$@"', i) || source.startsWith('@$"', i)
      ? source.slice(i, i + 3)
      : source.startsWith('$"', i) ? '$"' : null;
    if (prefix) {
      const verbatim = prefix.includes('@');
      let cursor = i + prefix.length;
      while (cursor < source.length) {
        if (source[cursor] === '"' && verbatim && source[cursor + 1] === '"') {
          cursor += 2;
          continue;
        }
        if (source[cursor] === '"' && (!verbatim || source[cursor + 1] !== '"')) {
          cursor++;
          break;
        }
        if (!verbatim && source[cursor] === '\\') {
          cursor += 2;
          continue;
        }
        if (source[cursor] !== '{' || source[cursor + 1] === '{') {
          cursor += source[cursor] === '{' ? 2 : 1;
          continue;
        }

        const end = restoreExpression(cursor + 1, 1);
        if (end < 0) {
          cursor = source.length;
          break;
        }
        cursor = end;
      }
      i = cursor;
      continue;
    }

    if (source.startsWith('@"', i)) {
      i += 2;
      while (i < source.length) {
        if (source[i] === '"' && source[i + 1] === '"') {
          i += 2;
          continue;
        }
        if (source[i++] === '"') break;
      }
      continue;
    }
    const raw = /^("{3,})/.exec(source.slice(i));
    if (raw) {
      const end = source.indexOf(raw[1], i + raw[1].length);
      i = end < 0 ? source.length : end + raw[1].length;
      continue;
    }
    if (source[i] === '"') {
      i++;
      while (i < source.length) {
        if (source[i] === '\\') {
          i += 2;
          continue;
        }
        if (source[i++] === '"') break;
      }
      continue;
    }
    i++;
  }
  return out.join('');
}

function methodBodies(source) {
  const masked = mask(source);
  const references = maskInterpolatedReferences(source);
  const declaration = /\bvoid\s+([A-Za-z_]\w*)\s*\([^)]*\)\s*(\{|=>)/g;
  const methods = [];
  for (let match; (match = declaration.exec(masked));) {
    const bodyStart = match.index + match[0].length - match[2].length;
    if (match[2] === '{') {
      let depth = 0;
      for (let i = bodyStart; i < masked.length; i++) {
        if (masked[i] === '{') depth++;
        else if (masked[i] === '}' && --depth === 0) {
          methods.push({ name: match[1], body: references.slice(bodyStart + 1, i) });
          declaration.lastIndex = i + 1;
          break;
        }
      }
      continue;
    }

    let depth = 0;
    for (let i = bodyStart + 2; i < masked.length; i++) {
      if ('([{'.includes(masked[i])) depth++;
      else if (')]}'.includes(masked[i])) depth--;
      else if (masked[i] === ';' && depth === 0) {
        methods.push({ name: match[1], body: references.slice(bodyStart + 2, i) });
        declaration.lastIndex = i + 1;
        break;
      }
    }
  }
  return methods;
}

function pricingMathReferences(source) {
  const masked = mask(source);
  const aliases = [...masked.matchAll(
    /\busing\s+([A-Za-z_]\w*)\s*=\s*(?:global\s*::\s*)?(?:Billing\s*\.\s*)?PricingMath\s*;/g)]
    .map(match => match[1]);
  const hasStaticImport =
    /\busing\s+static\s+(?:global\s*::\s*)?(?:Billing\s*\.\s*)?PricingMath\s*;/.test(masked);
  return { aliases, hasStaticImport };
}

function referencesMember(method, member, { aliases, hasStaticImport }) {
  const qualifiers = ['PricingMath', ...aliases]
    .map(name => name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
    .join('|');
  if (new RegExp(`\\b(?:${qualifiers})\\s*\\.\\s*${member}\\b`).test(method.body)) {
    return true;
  }
  return hasStaticImport && new RegExp(`\\b${member}\\b`).test(method.body);
}

function check(source) {
  const methods = methodBodies(source);
  const references = pricingMathReferences(source);
  const usesCalculate = method => referencesMember(method, 'CalculateDiscount', references);
  const usesApply = method => referencesMember(method, 'ApplyDiscount', references);
  const original = methods.find(method => method.name === originalTest);

  if (original) {
    if (!usesCalculate(original)) {
      return `${originalTest} still does not use PricingMath.CalculateDiscount`;
    }
    if (usesApply(original)) {
      return `${originalTest} still uses PricingMath.ApplyDiscount`;
    }
  } else if (!methods.some(usesCalculate)) {
    return 'could not find a migrated test consumer of PricingMath.CalculateDiscount';
  }
  return null;
}

function main() {
  const file = process.argv[2];
  if (!file) {
    console.error('usage: node assert-friend-consumer-migrated.js <BillingTests.cs>');
    process.exit(2);
  }

  const error = check(fs.readFileSync(file, 'utf8'));
  if (error) {
    console.error(error);
    process.exit(1);
  }
  console.log(`${originalTest} migrated to PricingMath.CalculateDiscount`);
}

if (require.main === module) main();
module.exports = { maskInterpolatedReferences, methodBodies, pricingMathReferences, referencesMember, check };
