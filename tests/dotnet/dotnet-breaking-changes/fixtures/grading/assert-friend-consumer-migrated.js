// Usage: node assert-friend-consumer-migrated.js <BillingTests.cs>
// The fixture's original in-repo consumer must migrate to CalculateDiscount. Other tests may
// deliberately exercise the retained ApplyDiscount compatibility shim.
const fs = require('fs');
const { mask } = require('./assert-tests-kill-mutants.js');

const originalTest = 'PricingMath_AppliesDiscountForFriendAssembly';

function methodBodies(source) {
  const masked = mask(source);
  const declaration = /\bvoid\s+([A-Za-z_]\w*)\s*\([^)]*\)\s*(\{|=>)/g;
  const methods = [];
  for (let match; (match = declaration.exec(masked));) {
    const bodyStart = match.index + match[0].length - match[2].length;
    if (match[2] === '{') {
      let depth = 0;
      for (let i = bodyStart; i < masked.length; i++) {
        if (masked[i] === '{') depth++;
        else if (masked[i] === '}' && --depth === 0) {
          methods.push({ name: match[1], body: masked.slice(bodyStart + 1, i) });
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
        methods.push({ name: match[1], body: masked.slice(bodyStart + 2, i) });
        declaration.lastIndex = i + 1;
        break;
      }
    }
  }
  return methods;
}

function check(source) {
  const methods = methodBodies(source);
  const callsCalculate = method =>
    /\bPricingMath\s*\.\s*CalculateDiscount\s*\(/.test(method.body);
  const callsApply = method =>
    /\bPricingMath\s*\.\s*ApplyDiscount\s*\(/.test(method.body);
  const original = methods.find(method => method.name === originalTest);

  if (original) {
    if (!callsCalculate(original)) {
      return `${originalTest} still does not call PricingMath.CalculateDiscount`;
    }
    if (callsApply(original)) {
      return `${originalTest} still calls PricingMath.ApplyDiscount`;
    }
  } else if (!methods.some(callsCalculate)) {
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
module.exports = { methodBodies, check };
