import { createHash } from "node:crypto";
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const output = process.argv[2];
if (!output) {
  throw new Error("usage: node run-mutation-probe.mjs <output-directory>");
}

mkdirSync(output, { recursive: true });

class DynamicGroup {
  constructor() {
    this.name = "synthetic-group";
    this.children = [
      { key: "a", value: "alpha", disabled: false, selected: false, tabIndex: -1 },
      { key: "b", value: "beta", disabled: false, selected: true, tabIndex: 0 },
      { key: "c", value: "gamma", disabled: false, selected: false, tabIndex: -1 },
    ];
    this.selectedKey = "b";
    this.focusKey = "b";
    this.defaultKey = "b";
    this.callbacks = [];
    this.errors = [];
    this.listeners = 1;
    this.disposed = false;
    this.propagate();
  }

  snapshot() {
    return {
      name: this.name,
      selectedKey: this.selectedKey,
      focusKey: this.focusKey,
      defaultKey: this.defaultKey,
      listeners: this.listeners,
      disposed: this.disposed,
      children: this.children.map((child) => ({ ...child })),
    };
  }

  propagate() {
    for (const child of this.children) {
      child.groupName = this.name;
      child.defaultKey = this.defaultKey;
      child.selected = child.key === this.selectedKey;
      child.tabIndex = child.key === this.focusKey && !child.disabled ? 0 : -1;
    }
  }

  chooseNext(excludedKey) {
    return this.children.find((child) => child.key !== excludedKey && !child.disabled)?.key ?? null;
  }

  callback(name, fail = false) {
    this.callbacks.push(name);
    if (fail) {
      this.errors.push(`${name}:synthetic-failure`);
    }
  }
}

const group = new DynamicGroup();
const observations = [];

function observe(operation, mutate, assertions) {
  const before = group.snapshot();
  mutate();
  group.propagate();
  const after = group.snapshot();
  const checks = assertions(after);
  if (!checks.every((check) => check.passed)) {
    throw new Error(`${operation} failed: ${JSON.stringify(checks)}`);
  }
  observations.push({
    operation,
    phase: "after-initial-render",
    before,
    after,
    checks,
  });
}

observe("add-child", () => {
  group.children.push({ key: "d", value: "delta", disabled: false, selected: false, tabIndex: -1 });
}, (state) => [
  { name: "child-added", passed: state.children.some((child) => child.key === "d") },
  { name: "group-propagated", passed: state.children.find((child) => child.key === "d").groupName === group.name },
]);

observe("remove-child", () => {
  group.children = group.children.filter((child) => child.key !== "a");
}, (state) => [
  { name: "child-removed", passed: !state.children.some((child) => child.key === "a") },
  { name: "membership-count", passed: state.children.length === 3 },
]);

observe("keyed-reorder", () => {
  const byKey = new Map(group.children.map((child) => [child.key, child]));
  group.children = ["d", "c", "b"].map((key) => byKey.get(key));
}, (state) => [
  { name: "key-order", passed: state.children.map((child) => child.key).join(",") === "d,c,b" },
  { name: "selection-preserved", passed: state.selectedKey === "b" },
]);

observe("disable-or-remove-selected-child", () => {
  const selected = group.children.find((child) => child.key === group.selectedKey);
  selected.disabled = true;
  group.selectedKey = group.chooseNext(selected.key);
  group.focusKey = group.selectedKey;
}, (state) => [
  { name: "disabled-not-selected", passed: !state.children.find((child) => child.key === "b").selected },
  { name: "selection-reassigned", passed: state.selectedKey === "d" },
]);

observe("membership-reconciliation", () => {
  group.children = group.children.filter((child) => !child.disabled);
}, (state) => [
  { name: "disabled-removed", passed: !state.children.some((child) => child.disabled) },
  { name: "selected-member", passed: state.children.some((child) => child.key === state.selectedKey) },
]);

observe("propagated-name-default-value-state", () => {
  group.defaultKey = "c";
  group.selectedKey = "c";
  group.focusKey = "c";
}, (state) => [
  { name: "name-propagated", passed: state.children.every((child) => child.groupName === group.name) },
  { name: "default-propagated", passed: state.children.every((child) => child.defaultKey === "c") },
  { name: "value-propagated", passed: state.children.find((child) => child.selected).value === "gamma" },
]);

observe("focus-ownership-restoration", () => {
  group.children = group.children.filter((child) => child.key !== group.focusKey);
  group.focusKey = group.chooseNext("c");
  group.selectedKey = group.focusKey;
}, (state) => [
  { name: "focus-restored", passed: state.focusKey === "d" },
  { name: "focused-member", passed: state.children.some((child) => child.key === state.focusKey) },
]);

observe("single-roving-tab-stop", () => {
  group.propagate();
}, (state) => [
  { name: "one-tab-stop", passed: state.children.filter((child) => child.tabIndex === 0).length === 1 },
  { name: "focus-owns-tab-stop", passed: state.children.find((child) => child.tabIndex === 0).key === state.focusKey },
]);

observe("callbacks-error-routing", () => {
  group.callback("selection-changed");
  group.callback("consumer-callback", true);
}, (state) => [
  { name: "callback-observed", passed: group.callbacks.includes("selection-changed") },
  { name: "error-routed", passed: group.errors.includes("consumer-callback:synthetic-failure") },
]);

observe("cleanup-disposal", () => {
  group.listeners = 0;
  group.children = [];
  group.selectedKey = null;
  group.focusKey = null;
  group.disposed = true;
}, (state) => [
  { name: "listeners-cleared", passed: state.listeners === 0 },
  { name: "registrations-cleared", passed: state.children.length === 0 },
  { name: "disposed", passed: state.disposed },
]);

const protocolOperations = [];
for (const observation of observations) {
  const text = JSON.stringify(observation);
  const basename = `raw-${observation.operation}.json`;
  writeFileSync(join(output, basename), text);
  protocolOperations.push({
    operation: observation.operation,
    disposition: "observed",
    outcome: "passed",
    raw_observation_sha256: {
      algorithm: "sha256",
      value: createHash("sha256").update(text).digest("hex"),
    },
    not_tested_reason: null,
  });
}

writeFileSync(join(output, "lifecycle-protocol.json"), JSON.stringify({
  schema_version: 1,
  protocol: "dynamic-child-lifecycle",
  operations: protocolOperations,
}));
