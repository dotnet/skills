// Usage: node assert-friend-consumer-migrated.js <BillingTests.cs>
// The fixture's original in-repo consumer must migrate to CalculateDiscount. Other tests may
// deliberately exercise the retained ApplyDiscount compatibility shim.
const fs = require('fs');
const { mask } = require('./assert-tests-kill-mutants.js');

const originalTest = 'PricingMath_AppliesDiscountForFriendAssembly';

function maskInterpolatedReferences(source) {
  const out = mask(source).split('');
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

        const expressionStart = cursor + 1;
        const tail = mask(source.slice(expressionStart));
        let depth = 1;
        let expressionEnd = expressionStart;
        for (let j = 0; j < tail.length; j++) {
          if (tail[j] === '{') depth++;
          else if (tail[j] === '}' && --depth === 0) {
            expressionEnd += j;
            break;
          }
        }
        if (depth !== 0) {
          cursor = source.length;
          break;
        }

        const expression = maskInterpolatedReferences(source.slice(expressionStart, expressionEnd));
        for (let j = 0; j < expression.length; j++) out[expressionStart + j] = expression[j];
        cursor = expressionEnd + 1;
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
