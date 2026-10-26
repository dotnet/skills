// Usage: node assert-friend-consumer-migrated.js <BillingTests.cs>
// The fixture's original in-repo consumer must migrate to CalculateDiscount. Other tests may
// deliberately exercise the retained ApplyDiscount compatibility shim.
const fs = require('fs');
const { mask } = require('./assert-tests-kill-mutants.js');

const originalTest = 'PricingMath_AppliesDiscountForFriendAssembly';

function methodBody(source, methodName) {
  const masked = mask(source);
  const escaped = methodName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const declaration = new RegExp(`\\bvoid\\s+${escaped}\\s*\\([^)]*\\)\\s*\\{`, 'g');
  const match = declaration.exec(masked);
  if (!match) return null;

  const open = masked.indexOf('{', match.index);
  let depth = 0;
  for (let i = open; i < masked.length; i++) {
    if (masked[i] === '{') depth++;
    else if (masked[i] === '}' && --depth === 0) return masked.slice(open + 1, i);
  }
  return null;
}

function check(source) {
  const body = methodBody(source, originalTest);
  if (body === null) {
    return `could not find the original ${originalTest} test method`;
  }
  if (!/\bPricingMath\s*\.\s*CalculateDiscount\s*\(/.test(body)) {
    return `${originalTest} still does not call PricingMath.CalculateDiscount`;
  }
  if (/\bPricingMath\s*\.\s*ApplyDiscount\s*\(/.test(body)) {
    return `${originalTest} still calls PricingMath.ApplyDiscount`;
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
module.exports = { methodBody, check };
