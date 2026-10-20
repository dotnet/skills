#!/usr/bin/env node

import fs from "node:fs";
import path from "node:path";

const contracts = new Set(["razor-pages", "todo-api", "mvc", "parent-api", "blazor"]);
const [contract, rootArg] = process.argv.slice(2);

if (!contracts.has(contract) || !rootArg) {
  console.error("Usage: node validators/validate-generated.mjs <contract> <root>");
  console.error(`Contracts: ${[...contracts].join(", ")}`);
  process.exit(2);
}

const root = path.resolve(rootArg);
if (!fs.existsSync(root) || !fs.statSync(root).isDirectory()) {
  console.error(`Root is not a directory: ${root}`);
  process.exit(2);
}

const includedExtensions = new Set([".cs", ".cshtml", ".razor", ".http", ".json", ".csproj"]);
const skippedDirectories = new Set([".git", "bin", "obj", "node_modules"]);

function readFiles(directory, base = directory) {
  const result = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    if (entry.isDirectory() && skippedDirectories.has(entry.name)) continue;
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      result.push(...readFiles(full, base));
    } else if (entry.isFile() && includedExtensions.has(path.extname(entry.name).toLowerCase())) {
      result.push({
        full,
        rel: path.relative(base, full).replaceAll("\\", "/"),
        text: fs.readFileSync(full, "utf8").replaceAll("\r\n", "\n"),
      });
    }
  }
  return result;
}

const files = readFiles(root);
const issues = [];
const fail = (condition, message) => {
  if (!condition) issues.push(message);
};
const matching = (pattern) => files.filter((file) => pattern.test(file.rel));
const joined = (pattern = /.*/) => matching(pattern).map((file) => `\n/* ${file.rel} */\n${file.text}`).join("\n");
const cs = joined(/\.cs$/i);
const razor = joined(/\.(?:cshtml|razor)$/i);
const projectText = joined(/\.(?:csproj|json)$/i);

function test(text, ...patterns) {
  return patterns.every((pattern) => pattern.test(text));
}

function any(text, ...patterns) {
  return patterns.some((pattern) => pattern.test(text));
}

function count(text, pattern) {
  return [...text.matchAll(pattern)].length;
}

function findFile(pattern) {
  return files.find((file) => pattern.test(file.rel));
}

function requireFile(pattern, label) {
  const file = findFile(pattern);
  fail(Boolean(file), `Missing ${label}.`);
  return file?.text ?? "";
}

function activeSource(text) {
  const mask = (value) => value.replace(/[^\n]/g, " ");
  let result = "";
  let index = 0;

  while (index < text.length) {
    if (text.startsWith("@*", index)) {
      const end = text.indexOf("*@", index + 2);
      const next = end < 0 ? text.length : end + 2;
      result += mask(text.slice(index, next));
      index = next;
      continue;
    }
    if (text.startsWith("//", index)) {
      const end = text.indexOf("\n", index + 2);
      const next = end < 0 ? text.length : end;
      result += mask(text.slice(index, next));
      index = next;
      continue;
    }
    if (text.startsWith("/*", index)) {
      const end = text.indexOf("*/", index + 2);
      const next = end < 0 ? text.length : end + 2;
      result += mask(text.slice(index, next));
      index = next;
      continue;
    }

    const character = text[index];
    if (character === '"') {
      let quoteCount = 1;
      while (text[index + quoteCount] === '"') quoteCount += 1;
      if (quoteCount >= 3) {
        const delimiter = '"'.repeat(quoteCount);
        const end = text.indexOf(delimiter, index + quoteCount);
        const next = end < 0 ? text.length : end + quoteCount;
        result += mask(text.slice(index, next));
        index = next;
        continue;
      }

      const verbatim =
        text[index - 1] === "@" ||
        (text[index - 1] === "$" && text[index - 2] === "@");
      let next = index + 1;
      while (next < text.length) {
        if (verbatim && text[next] === '"' && text[next + 1] === '"') {
          next += 2;
          continue;
        }
        if (!verbatim && text[next] === "\\") {
          next += 2;
          continue;
        }
        if (text[next] === '"') {
          next += 1;
          break;
        }
        next += 1;
      }
      result += mask(text.slice(index, next));
      index = next;
      continue;
    }
    if (character === "'") {
      let next = index + 1;
      while (next < text.length) {
        if (text[next] === "\\") {
          next += 2;
          continue;
        }
        if (text[next] === "'") {
          next += 1;
          break;
        }
        next += 1;
      }
      result += mask(text.slice(index, next));
      index = next;
      continue;
    }

    result += character;
    index += 1;
  }

  return result;
}

function sourceWithoutComments(text) {
  const mask = (value) => value.replace(/[^\n]/g, " ");
  let result = "";
  let index = 0;

  while (index < text.length) {
    if (text.startsWith("//", index)) {
      const end = text.indexOf("\n", index + 2);
      const next = end < 0 ? text.length : end;
      result += mask(text.slice(index, next));
      index = next;
      continue;
    }
    if (text.startsWith("/*", index)) {
      const end = text.indexOf("*/", index + 2);
      const next = end < 0 ? text.length : end + 2;
      result += mask(text.slice(index, next));
      index = next;
      continue;
    }

    const character = text[index];
    if (character === '"') {
      let quoteCount = 1;
      while (text[index + quoteCount] === '"') quoteCount += 1;
      if (quoteCount >= 3) {
        const delimiter = '"'.repeat(quoteCount);
        const end = text.indexOf(delimiter, index + quoteCount);
        const next = end < 0 ? text.length : end + quoteCount;
        result += text.slice(index, next);
        index = next;
        continue;
      }

      const verbatim =
        text[index - 1] === "@" ||
        (text[index - 1] === "$" && text[index - 2] === "@");
      let next = index + 1;
      while (next < text.length) {
        if (verbatim && text[next] === '"' && text[next + 1] === '"') {
          next += 2;
          continue;
        }
        if (!verbatim && text[next] === "\\") {
          next += 2;
          continue;
        }
        if (text[next] === '"') {
          next += 1;
          break;
        }
        next += 1;
      }
      result += text.slice(index, next);
      index = next;
      continue;
    }
    if (character === "'") {
      let next = index + 1;
      while (next < text.length) {
        if (text[next] === "\\") {
          next += 2;
          continue;
        }
        if (text[next] === "'") {
          next += 1;
          break;
        }
        next += 1;
      }
      result += text.slice(index, next);
      index = next;
      continue;
    }

    result += character;
    index += 1;
  }

  return result;
}

function hasActiveHostExtensionCall(name) {
  const escapedName = name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const invocation = new RegExp(
    `\\b(?:app|endpoints)\\b\\s*\\.\\s*${escapedName}\\s*\\(`,
    "i",
  );
  return matching(/\.cs$/i).some((file) => invocation.test(activeSource(file.text)));
}

function validateNoEnsureCreated() {
  const activeGeneratedSource = matching(/\.(?:cs|cshtml|razor)$/i)
    .map((file) => activeSource(file.text))
    .join("\n");
  fail(
    !/\.\s*EnsureCreated(?:Async)?\s*\(/.test(activeGeneratedSource),
    "Generated sources must not call EnsureCreated or EnsureCreatedAsync.",
  );
}

function validateNoStartupSeeding() {
  const startupFiles = matching(/(?:^|\/)(?:Program|Startup)\.cs$/i);
  const startupExecution = startupFiles.map((file) => {
    const active = activeSource(file.text);
    const run = active.search(/\bapp\s*\.\s*Run(?:Async)?\s*\(/);
    let execution = run < 0 ? active : active.slice(0, statementAt(active, run).length + run);
    const endpointMappings = [...execution.matchAll(
      /\.\s*Map(?:Get|Post|Put|Patch|Delete)\s*\(/g,
    )].reverse();
    for (const mapping of endpointMappings) {
      const start = statementStart(execution, mapping.index);
      const end = start + statementAt(execution, start).length;
      execution =
        execution.slice(0, start) +
        execution.slice(start, end).replace(/[^\n]/g, " ") +
        execution.slice(end);
    }
    return execution;
  }).join("\n");
  const obviousSeedCall =
    /\b(?:[A-Za-z_]\w*\s*\.\s*Seed\w*|(?:Seed\w*|DbInitializer|DataInitializer|DatabaseInitializer)\s*\.\s*Initialize(?:Async)?)\s*\(/i;
  const entityAdd =
    /\b(?!services\b)[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*\s*\.\s*(?:Add|AddAsync|AddRange|AddRangeAsync)\s*\(/i;
  const savesChanges = /\.\s*SaveChanges(?:Async)?\s*\(/i;
  fail(
    !obviousSeedCall.test(startupExecution) &&
      !(entityAdd.test(startupExecution) && savesChanges.test(startupExecution)),
    "Generated host startup must not seed or insert database entities.",
  );
}

function balancedEnd(text, start, open, close) {
  let depth = 0;
  let quote = "";
  let escaped = false;
  for (let index = start; index < text.length; index += 1) {
    const character = text[index];
    if (quote) {
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === quote) quote = "";
      continue;
    }
    if (character === '"' || character === "'") {
      quote = character;
      continue;
    }
    if (character === open) depth += 1;
    if (character === close && --depth === 0) return index;
  }
  return -1;
}

function statementAt(text, start) {
  let parens = 0;
  let braces = 0;
  let brackets = 0;
  let quote = "";
  let escaped = false;
  for (let index = start; index < text.length; index += 1) {
    const character = text[index];
    if (quote) {
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === quote) quote = "";
      continue;
    }
    if (character === '"' || character === "'") quote = character;
    else if (character === "(") parens += 1;
    else if (character === ")") parens -= 1;
    else if (character === "{") braces += 1;
    else if (character === "}") braces -= 1;
    else if (character === "[") brackets += 1;
    else if (character === "]") brackets -= 1;
    else if (character === ";" && parens === 0 && braces === 0 && brackets === 0) {
      return text.slice(start, index + 1);
    }
  }
  return text.slice(start);
}

function methodsNamed(text, name) {
  const escapedName = name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const pattern = new RegExp(
    `(?:public|private|protected|internal)\\s+(?:static\\s+)?(?:async\\s+)?[^;{}]{0,220}\\b${escapedName}\\s*\\(`,
    "g",
  );
  const methods = [];
  for (const match of text.matchAll(pattern)) {
    const nameIndex = match.index + match[0].lastIndexOf(name);
    const openParen = text.indexOf("(", nameIndex + name.length);
    const closeParen = balancedEnd(text, openParen, "(", ")");
    if (closeParen < 0) continue;
    let cursor = closeParen + 1;
    while (/\s/.test(text[cursor] ?? "")) cursor += 1;
    let body = "";
    if (text.startsWith("=>", cursor)) {
      const end = text.indexOf(";", cursor);
      body = text.slice(cursor, end < 0 ? text.length : end + 1);
    } else {
      const openBrace = text.indexOf("{", cursor);
      if (openBrace < 0 || openBrace - cursor > 80) continue;
      const closeBrace = balancedEnd(text, openBrace, "{", "}");
      body = text.slice(openBrace, closeBrace < 0 ? text.length : closeBrace + 1);
    }
    const prefix = text.slice(Math.max(0, match.index - 240), match.index);
    methods.push({ prefix, signature: text.slice(match.index, closeParen + 1), body });
  }
  return methods;
}

function expandReferencedMethods(text, fragment) {
  let expanded = fragment;
  const pending = [...fragment.matchAll(/\b([A-Za-z_]\w*)\s*\(/g)].map((match) => match[1]);
  const seen = new Set();
  while (pending.length > 0) {
    const name = pending.shift();
    if (seen.has(name) || /^(if|for|foreach|while|switch|return|new|nameof)$/.test(name)) continue;
    seen.add(name);
    for (const method of methodsNamed(text, name)) {
      const addition = `\n${method.signature}\n${method.body}`;
      expanded += addition;
      for (const match of addition.matchAll(/\b([A-Za-z_]\w*)\s*\(/g)) pending.push(match[1]);
    }
  }
  return expanded;
}

function staticStringValue(text, expression, beforeIndex, seen = new Set()) {
  const candidate = expression.trim();
  const regularLiteral = candidate.match(/^"((?:\\.|[^"\\])*)"$/s);
  if (regularLiteral) {
    try {
      return JSON.parse(candidate);
    } catch {
      return null;
    }
  }
  const verbatimLiteral = candidate.match(/^@"((?:""|[^"])*)"$/s);
  if (verbatimLiteral) return verbatimLiteral[1].replaceAll('""', '"');
  if (!/^@?[A-Za-z_]\w*$/.test(candidate) || seen.has(candidate)) return null;

  let assignment = null;
  const assignmentPattern = /(?<![.\w])(@?[A-Za-z_]\w*)\s*=(?!=|>)\s*([^;]+);/g;
  for (const match of text.matchAll(assignmentPattern)) {
    if (match.index >= beforeIndex) break;
    if (match[1] === candidate) assignment = match;
  }
  if (!assignment) return null;

  const nextSeen = new Set(seen);
  nextSeen.add(candidate);
  return staticStringValue(text, assignment[2], assignment.index, nextSeen);
}

function endpointRecords(text, supportingText = text) {
  const groups = new Map();
  for (const match of text.matchAll(/\b(?:var\s+)?(\w+)\s*=\s*[^;]*?\bMapGroup\s*\(/g)) {
    const openParen = text.indexOf("(", match.index + match[0].lastIndexOf("MapGroup"));
    const closeParen = balancedEnd(text, openParen, "(", ")");
    if (closeParen < 0) continue;
    const argument = splitTopLevelArguments(text.slice(openParen + 1, closeParen))[0] ?? "";
    const base = staticStringValue(text, argument, match.index);
    if (base === null) continue;
    groups.set(match[1], { base, statement: statementAt(text, match.index) });
  }

  const records = [];
  const mapping = /\b(\w+)\.Map(Get|Post|Put|Patch|Delete)\s*\(/g;
  for (const match of text.matchAll(mapping)) {
    const statement = statementAt(text, match.index);
    const openParen = text.indexOf("(", match.index);
    const closeParen = balancedEnd(text, openParen, "(", ")");
    const call = closeParen < 0 ? statement : text.slice(openParen + 1, closeParen);
    const routeArgument = splitTopLevelArguments(call)[0] ?? "";
    const routeTemplate = staticStringValue(text, routeArgument, match.index);
    const handler = call.match(/,\s*([A-Za-z_]\w*)\s*$/)?.[1];
    let behavior = statement;
    let handlerSignature = call;
    if (handler) {
      const methods = methodsNamed(supportingText, handler);
      handlerSignature = methods.map((method) => method.signature).join("\n");
      for (const method of methods) behavior += `\n${method.signature}\n${method.body}`;
    }
    behavior = expandReferencedMethods(supportingText, behavior);
    const group = groups.get(match[1]);
    records.push({
      verb: match[2].toUpperCase(),
      receiver: match[1],
      route: routeTemplate === null
        ? null
        : `${group?.base ?? ""}${routeTemplate}`.replace(/\/+/g, "/"),
      statement,
      behavior,
      handlerSignature,
      tags: group?.statement ?? "",
    });
  }
  return records;
}

function migrationTableNames() {
  const tableNames = new Set();
  for (const file of matching(/(?:^|\/)Migrations\/.*\.cs$/i)
    .filter((candidate) => !/Designer|ModelSnapshot/i.test(candidate.rel))) {
    const migration = sourceWithoutComments(file.text);
    if (!/:\s*Migration\b/.test(migration)) continue;
    for (const up of methodsNamed(migration, "Up")) {
      for (const match of up.body.matchAll(/\.\s*CreateTable\s*\(/g)) {
        const openParen = up.body.indexOf("(", match.index);
        const closeParen = balancedEnd(up.body, openParen, "(", ")");
        if (closeParen < 0) continue;
        const argumentsList = splitTopLevelArguments(up.body.slice(openParen + 1, closeParen));
        const nameArgument =
          argumentsList.find((argument) => /^\s*name\s*:/i.test(argument)) ??
          argumentsList[0] ??
          "";
        const value = argumentValue(nameArgument).match(/^"((?:\\.|[^"\\])*)"$/s);
        if (!value) continue;
        try {
          tableNames.add(JSON.parse(`"${value[1]}"`));
        } catch {
        }
      }
    }
  }
  return tableNames;
}

function hasMigration(...entities) {
  const tables = migrationTableNames();
  const names = {
    Product: ["Product", "Products"],
    Category: ["Category", "Categories"],
    TodoItem: ["TodoItem", "TodoItems", "Todo", "Todos"],
    Book: ["Book", "Books"],
    Author: ["Author", "Authors"],
    Employee: ["Employee", "Employees"],
    Department: ["Department", "Departments"],
  };
  return entities.every((entity) =>
    (names[entity] ?? [entity, `${entity}s`]).some((name) => tables.has(name)));
}

function argumentValue(argument) {
  return argument.replace(/^\s*[A-Za-z_]\w*\s*:\s*/, "").trim();
}

function parameterNames(parameters) {
  return splitTopLevelArguments(parameters).map((parameter) =>
    parameter.match(/(@?[A-Za-z_]\w*)\s*(?:=[\s\S]*)?$/)?.[1] ?? ""
  ).filter(Boolean);
}

function invokesUseSqliteOn(body, parameter) {
  const escapedParameter = parameter.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return new RegExp(`\\b${escapedParameter}\\s*\\.\\s*UseSqlite\\s*\\(`).test(body);
}

function lambdaUsesSqlite(argument) {
  const value = argumentValue(argument);
  const arrow = value.indexOf("=>");
  if (arrow < 0) return false;

  let parameters = value.slice(0, arrow).trim()
    .replace(/^(?:(?:static|async)\s+)+/, "");
  if (parameters.startsWith("(")) {
    const closeParen = balancedEnd(parameters, 0, "(", ")");
    if (closeParen < 0 || parameters.slice(closeParen + 1).trim() !== "") return false;
    parameters = parameters.slice(1, closeParen);
  }

  const body = value.slice(arrow + 2);
  return parameterNames(parameters).some((parameter) => invokesUseSqliteOn(body, parameter));
}

function configurationMethodsNamed(text, name) {
  const escapedName = name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const pattern = new RegExp(`\\b${escapedName}\\s*\\(`, "g");
  const methods = [];
  for (const match of text.matchAll(pattern)) {
    const prefixStart = Math.max(
      text.lastIndexOf("\n", match.index - 1),
      text.lastIndexOf("{", match.index - 1),
      text.lastIndexOf("}", match.index - 1),
      text.lastIndexOf(";", match.index - 1),
    ) + 1;
    const declarationPrefix = text.slice(prefixStart, match.index).trim();
    if (
      !/^(?:(?:public|private|protected|internal|static|async|unsafe|partial|new|virtual|sealed|override|extern)\s+)*[A-Za-z_@][\w@.:?<>\[\],]*$/.test(
        declarationPrefix,
      )
    ) {
      continue;
    }

    const openParen = text.indexOf("(", match.index);
    const closeParen = balancedEnd(text, openParen, "(", ")");
    if (closeParen < 0) continue;
    let cursor = closeParen + 1;
    while (/\s/.test(text[cursor] ?? "")) cursor += 1;
    if (text.startsWith("=>", cursor)) {
      const end = text.indexOf(";", cursor);
      methods.push({
        parameters: text.slice(openParen + 1, closeParen),
        body: text.slice(cursor, end < 0 ? text.length : end + 1),
      });
    } else if (text[cursor] === "{") {
      const closeBrace = balancedEnd(text, cursor, "{", "}");
      if (closeBrace < 0) continue;
      methods.push({
        parameters: text.slice(openParen + 1, closeParen),
        body: text.slice(cursor, closeBrace + 1),
      });
    }
  }
  return methods;
}

function namedMethodUsesSqlite(text, argument) {
  const methodName = argumentValue(argument).match(/(?:^|\.)([A-Za-z_]\w*)$/)?.[1];
  if (!methodName) return false;
  return configurationMethodsNamed(text, methodName).some((method) =>
    parameterNames(method.parameters).some((parameter) =>
      invokesUseSqliteOn(method.body, parameter)
    )
  );
}

function contextRegistrationStatus(name, allowFactory) {
  const escapedName = name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const registration = allowFactory ? "AddDbContext(?:Factory)?" : "AddDbContext";
  const pattern = new RegExp(
    `\\b${registration}\\s*<\\s*${escapedName}\\s*>\\s*\\(`,
    "g",
  );
  let registered = false;
  let sqliteConfigured = false;
  const activeFiles = matching(/\.cs$/i).map((file) => ({
    ...file,
    activeText: activeSource(file.text),
  }));
  const activeCs = activeFiles.map((file) => file.activeText).join("\n");

  for (const file of activeFiles) {
    const text = file.activeText;
    for (const match of text.matchAll(pattern)) {
      registered = true;
      const openParen = text.indexOf("(", match.index);
      const closeParen = balancedEnd(text, openParen, "(", ")");
      if (closeParen < 0) continue;
      const argumentsList = splitTopLevelArguments(text.slice(openParen + 1, closeParen));
      if (
        argumentsList.some((argument) =>
          lambdaUsesSqlite(argument) || namedMethodUsesSqlite(activeCs, argument)
        )
      ) {
        sqliteConfigured = true;
      }
    }
  }

  return { registered, sqliteConfigured };
}

function checkContext(name, entities, allowFactory = true) {
  const registration = contextRegistrationStatus(name, allowFactory);
  fail(registration.registered, `${name} is not registered.`);
  fail(
    registration.sqliteConfigured,
    `SQLite is not configured by the ${name} registration.`,
  );
  for (const entity of entities) {
    fail(
      new RegExp(`DbSet\\s*<\\s*${entity}\\s*>`, "i").test(cs),
      `${name} does not expose a ${entity} DbSet.`,
    );
  }
  fail(hasMigration(...entities), `A real migration for ${entities.join(" and ")} is missing.`);
}

function responseMetadataStatuses(statement) {
  const statuses = new Set();
  const statusFromExpression = (text) => {
    const expression = stripOuterParentheses(text);
    const constant = expression.match(
      /^(?:global::)?(?:Microsoft\.AspNetCore\.Http\.)?StatusCodes\.Status([1-5]\d{2})[A-Za-z0-9_]*$/,
    );
    if (constant) return Number(constant[1]);
    const literal = expression.match(/^([1-5]\d{2})$/);
    return literal ? Number(literal[1]) : null;
  };
  const statusFromArguments = (argumentsText) => {
    const args = splitTopLevelArguments(argumentsText);
    const named = args.find((argument) => /^\s*statusCode\s*[:=]/i.test(argument));
    if (named) {
      return statusFromExpression(named.replace(/^\s*statusCode\s*[:=]\s*/i, ""));
    }
    for (const argument of args) {
      if (/^\s*[A-Za-z_]\w*\s*[:=]/.test(argument)) continue;
      const status = statusFromExpression(argument);
      if (status !== null) return status;
    }
    return null;
  };

  for (const match of statement.matchAll(
    /\.(ProducesValidationProblem|ProducesProblem|Produces)(?=\s*(?:<|\())/g,
  )) {
    const openParen = statement.indexOf("(", match.index + match[0].length);
    if (openParen < 0) continue;
    const closeParen = balancedEnd(statement, openParen, "(", ")");
    if (closeParen < 0) continue;
    const argumentsText = statement.slice(openParen + 1, closeParen);
    const beforeArguments = statement.slice(match.index, openParen);
    const status = statusFromArguments(argumentsText);
    if (status !== null) {
      statuses.add(status);
    } else if (match[1] === "ProducesValidationProblem") {
      statuses.add(400);
    } else if (match[1] === "ProducesProblem" && argumentsText.trim() === "") {
      statuses.add(500);
    } else if (
      match[1] === "Produces" &&
      /<[\s\S]+>/.test(beforeArguments) &&
      splitTopLevelArguments(argumentsText).every((argument) =>
        /^\s*[A-Za-z_]\w*\s*[:=]/.test(argument))
    ) {
      statuses.add(200);
    }
  }

  for (const match of statement.matchAll(/ProducesResponseType(?:Attribute)?\s*\(/g)) {
    const openParen = statement.indexOf("(", match.index);
    const closeParen = balancedEnd(statement, openParen, "(", ")");
    if (closeParen < 0) continue;
    const status = statusFromArguments(statement.slice(openParen + 1, closeParen));
    if (status !== null) statuses.add(status);
  }
  for (const match of statement.matchAll(/Responses\s*\[\s*["']([1-5]\d{2})["']\s*\]/g)) {
    statuses.add(Number(match[1]));
  }
  return statuses;
}

function requireResponseStatuses(record, label, requirements) {
  const statuses = responseMetadataStatuses(record.statement);
  for (const requirement of requirements) {
    const accepted = Array.isArray(requirement) ? requirement : [requirement];
    fail(
      accepted.some((status) => statuses.has(status)),
      `${label} ${record.verb} ${record.route || "(group root)"} lacks response metadata for ${accepted.join(" or ")}.`,
    );
  }
}

function checkMetadata(records, label) {
  for (const record of records) {
    const routeLabel = `${record.verb} ${record.route || "(group root)"}`;
    fail(/\.WithName\s*\(/.test(record.statement), `${label} ${routeLabel} lacks an OpenAPI name.`);
    fail(
      /\.WithDescription\s*\(|\bDescription\s*=/.test(record.statement),
      `${label} ${routeLabel} lacks an OpenAPI description.`,
    );
    fail(
      responseMetadataStatuses(record.statement).size > 0,
      `${label} ${routeLabel} lacks response metadata.`,
    );
    fail(
      /\.WithTags\s*\(/.test(record.statement + record.tags),
      `${label} ${routeLabel} lacks OpenAPI tags.`,
    );
  }
  const names = records
    .map((record) => record.statement.match(/\.WithName\s*\(\s*"([^"]+)"/)?.[1])
    .filter(Boolean);
  fail(new Set(names).size === names.length, `${label} OpenAPI names are not unique.`);
}

function recordsForBase(records, base) {
  const normalizedBase = base.toLowerCase().replace(/\/+$/, "");
  return records.filter((record) => {
    if (typeof record.route !== "string") return false;
    const normalizedRoute = record.route.toLowerCase().replace(/\/+$/, "");
    return normalizedRoute === normalizedBase ||
      normalizedRoute.startsWith(`${normalizedBase}/`);
  });
}

function checkApiCrud(records, base, entity, options = {}) {
  const scoped = recordsForBase(records, base);
  const list = scoped.find((record) => record.verb === "GET" && !/\{[^}]*id/i.test(record.route));
  const details = scoped.find((record) => record.verb === "GET" && /\{[^}]*id/i.test(record.route));
  const create = scoped.find((record) => record.verb === "POST");
  const update = scoped.find((record) => /^(PUT|PATCH)$/.test(record.verb));
  const remove = scoped.find((record) => record.verb === "DELETE");

  fail(Boolean(list), `${entity} list route is missing.`);
  fail(Boolean(details), `${entity} by-id route is missing.`);
  fail(Boolean(create), `${entity} create route is missing.`);
  fail(Boolean(update), `${entity} update route is missing.`);
  fail(Boolean(remove), `${entity} delete route is missing.`);

  if (list) fail(/(?:ToList|ToArray)Async\s*\(/.test(list.behavior), `${entity} list does not read asynchronously.`);
  if (list) requireResponseStatuses(list, entity, [200]);
  if (details) {
    fail(
      /(?:Find|FirstOrDefault|SingleOrDefault)Async\s*\(/.test(details.behavior),
      `${entity} by-id does not perform an async lookup.`,
    );
    fail(/NotFound\s*\(/.test(details.behavior), `${entity} by-id does not handle not-found.`);
    requireResponseStatuses(details, entity, [200, 404]);
  }
  if (create) {
    fail(/\.(?:Add|AddAsync)\s*\(/.test(create.behavior), `${entity} create does not add an entity.`);
    fail(/SaveChangesAsync\s*\(/.test(create.behavior), `${entity} create does not save asynchronously.`);
    fail(/Created(?:AtRoute|AtAction)?\s*\(/.test(create.behavior), `${entity} create does not return Created.`);
    requireResponseStatuses(create, entity, [201, [400, 422]]);
  }
  if (update) {
    const submittedEntity =
      parameterNameForType(update.handlerSignature, options.validationType ?? "") ||
      parameterNameForType(update.handlerSignature, entity);
    fail(
      /(?:Find|FirstOrDefault|SingleOrDefault)Async\s*\(/.test(update.behavior),
      `${entity} update does not load the target asynchronously.`,
    );
    fail(
      hasTargetEntityMutation(
        update.behavior,
        entity,
        submittedEntity,
      ),
      `${entity} update does not change the entity.`,
    );
    fail(/SaveChangesAsync\s*\(/.test(update.behavior), `${entity} update does not save asynchronously.`);
    fail(/NotFound\s*\(/.test(update.behavior), `${entity} update does not handle not-found.`);
    requireResponseStatuses(update, entity, [[200, 204], 404, [400, 422]]);
  }
  if (remove) {
    fail(
      /(?:Find|FirstOrDefault|SingleOrDefault)Async\s*\(/.test(remove.behavior),
      `${entity} delete does not load the target asynchronously.`,
    );
    fail(/\.Remove\s*\(/.test(remove.behavior), `${entity} delete does not remove the entity.`);
    fail(/SaveChangesAsync\s*\(/.test(remove.behavior), `${entity} delete does not save asynchronously.`);
    fail(/NotFound\s*\(/.test(remove.behavior), `${entity} delete does not handle not-found.`);
    fail(/NoContent\s*\(/.test(remove.behavior), `${entity} delete does not return NoContent.`);
    requireResponseStatuses(remove, entity, [204, 404]);
  }

  if (options.validation) {
    const field = options.validationField ?? "(?:Title|Name)";
    for (const [operation, record] of [["create", create], ["update", update]]) {
      if (!record) continue;
      const requestParameter =
        parameterNameForType(record.handlerSignature, options.validationType ?? "") ||
        parameterNameForType(record.handlerSignature, entity);
      fail(
        /(?:ValidationProblem|BadRequest)\s*\(/.test(record.behavior) &&
        any(
          record.behavior,
          new RegExp(`IsNullOrWhiteSpace\\s*\\([^)]*${field}`, "i"),
          /TryValidateObject\s*\(/,
        ) &&
        nonEmptyValidationErrorsReachResponse(record.behavior, field, requestParameter),
        `${entity} ${operation} does not enforce required text validation.`,
      );
    }
  }

  checkMetadata(scoped, entity);
  return { scoped, list, details, create, update, remove };
}

function httpRequestBlocks(http) {
  return http.split(/^###.*$/m).map((source, index) => {
    const request = source.match(/^\s*(GET|POST|PUT|PATCH|DELETE)\s+(\S+)/im);
    return {
      source,
      index,
      method: request?.[1].toUpperCase() ?? "",
      url: request?.[2] ?? "",
      name: source.match(/^\s*#\s*@name\s+([A-Za-z_][\w.-]*)\s*$/im)?.[1] ?? "",
      contentType: source.match(/^\s*Content-Type\s*:\s*([^\r\n;]+)/im)?.[1].trim() ?? "",
    };
  }).filter((block) => block.method);
}

function routeIdExpression(block, base) {
  const escaped = base.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return block.url.match(new RegExp(`${escaped}/([^/?#\\s]+)`, "i"))?.[1] ?? "";
}

function responseIdExpression(expression, requestName) {
  const escapedName = requestName.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return new RegExp(
    `^${escapedName}\\.response\\.body(?:\\.\\$)?(?:\\.id|\\[['"]id['"]\\])$`,
    "i",
  ).test(expression.trim());
}

function responseIdVariables(http, createBlock) {
  const variables = new Set();
  const assignment = /^\s*@([A-Za-z_][\w.-]*)\s*=\s*\{\{([^}]+)\}\}\s*$/gim;
  for (const match of http.matchAll(assignment)) {
    if (responseIdExpression(match[2], createBlock.name)) variables.add(match[1].toLowerCase());
  }

  const handlerSet =
    /(?:client\.(?:global|environment|variables)|request\.variables)\.set\s*\(\s*["']([A-Za-z_][\w.-]*)["']\s*,\s*response\.body(?:\.\$)?(?:\.id|\[['"]id['"]\])\s*\)/gim;
  for (const match of createBlock.source.matchAll(handlerSet)) {
    variables.add(match[1].toLowerCase());
  }
  return variables;
}

function usesCreatedResponseId(expression, createBlock, variables) {
  const references = [...expression.matchAll(/\{\{([^}]+)\}\}/g)].map((match) => match[1].trim());
  return references.some((reference) =>
    responseIdExpression(reference, createBlock.name) || variables.has(reference.toLowerCase()));
}

function requestJsonObject(block) {
  const startMatch = block.source.match(/^\s*\{(?!\{)/m);
  if (!startMatch) return "";
  const start = startMatch.index + startMatch[0].lastIndexOf("{");
  const end = balancedEnd(block.source, start, "{", "}");
  return end < 0 ? "" : block.source.slice(start, end + 1);
}

function topLevelJsonFields(body) {
  const fields = new Map();
  let index = 1;
  while (index < body.length - 1) {
    while (/[\s,]/.test(body[index] ?? "")) index += 1;
    if (body[index] !== '"') {
      index += 1;
      continue;
    }

    let key = "";
    index += 1;
    while (index < body.length) {
      if (body[index] === "\\") {
        key += body[index + 1] ?? "";
        index += 2;
      } else if (body[index] === '"') {
        index += 1;
        break;
      } else {
        key += body[index];
        index += 1;
      }
    }
    while (/\s/.test(body[index] ?? "")) index += 1;
    if (body[index] !== ":") continue;
    index += 1;
    while (/\s/.test(body[index] ?? "")) index += 1;

    const valueStart = index;
    let nesting = 0;
    let quote = "";
    let escaped = false;
    for (; index < body.length; index += 1) {
      const character = body[index];
      if (quote) {
        if (escaped) escaped = false;
        else if (character === "\\") escaped = true;
        else if (character === quote) quote = "";
        continue;
      }
      if (character === '"' || character === "'") {
        quote = character;
        continue;
      }
      if (character === "{" || character === "[") {
        nesting += 1;
        continue;
      }
      if (character === "}" || character === "]") {
        if (nesting > 0) {
          nesting -= 1;
          continue;
        }
        break;
      }
      if (character === "," && nesting === 0) break;
    }
    fields.set(key.toLowerCase(), body.slice(valueStart, index).trim());
  }
  return fields;
}

function hasJsonObjectWithFields(block, fields) {
  const json = requestJsonObject(block);
  if (!json) return false;
  const values = topLevelJsonFields(json);
  return fields.every((field) => values.has(field.toLowerCase()));
}

function jsonFieldValue(block, field) {
  const json = requestJsonObject(block);
  return json ? topLevelJsonFields(json).get(field.toLowerCase()) ?? "" : "";
}

function checkHttpCrud(base, entity, options = {}) {
  const http = joined(/\.http$/i);
  fail(Boolean(http.trim()), `${entity} executable .http workflow is missing.`);
  if (!http.trim()) return;
  const escaped = base.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const blocks = httpRequestBlocks(http);
  const flows = {
    list: blocks.find((block) =>
      block.method === "GET" && new RegExp(`${escaped}(?:\\?|$)`, "i").test(block.url)),
    details: blocks.find((block) => block.method === "GET" && routeIdExpression(block, base)),
    create: blocks.find((block) =>
      block.method === "POST" && new RegExp(`${escaped}(?:\\?|$)`, "i").test(block.url)),
    update: blocks.find((block) =>
      ["PUT", "PATCH"].includes(block.method) && routeIdExpression(block, base)),
    delete: blocks.find((block) => block.method === "DELETE" && routeIdExpression(block, base)),
  };
  const dependentBlocks = blocks.filter((block) =>
    ["GET", "PUT", "PATCH", "DELETE"].includes(block.method) &&
    Boolean(routeIdExpression(block, base)));
  for (const [flow, block] of Object.entries(flows)) {
    fail(Boolean(block), `${entity} .http workflow lacks ${flow}.`);
  }

  const requestFields = options.requestFields ?? [];
  for (const [flow, block] of [["create", flows.create], ["update", flows.update]]) {
    if (!block || requestFields.length === 0) continue;
    fail(
      block.contentType.toLowerCase() === "application/json",
      `${entity} .http ${flow} must send Content-Type: application/json.`,
    );
    fail(
      hasJsonObjectWithFields(block, requestFields),
      `${entity} .http ${flow} must send a JSON object with ${requestFields.join(" and ")}.`,
    );
  }

  if (options.requireCreateDependency && flows.create) {
    fail(
      dependentBlocks.every((block) => block.index > flows.create.index),
      `${entity} .http dependent requests must follow create.`,
    );
  }

  if (options.requireCreatedIdReuse && flows.create) {
    fail(Boolean(flows.create.name), `${entity} .http POST must have a request name for response reuse.`);
    const variables = responseIdVariables(http, flows.create);
    for (const block of dependentBlocks) {
      const id = routeIdExpression(block, base);
      fail(
        Boolean(flows.create.name) && usesCreatedResponseId(id, flows.create, variables),
        `${entity} .http ${block.method} by-id request must reuse the id returned by POST.`,
      );
    }
  }
  return { blocks, flows };
}

function validateRazorPages() {
  checkContext("AppDbContext", ["Product", "Category"], false);

  const surfaces = ["Index", "Details", "Create", "Edit", "Delete"];
  for (const entity of ["Products", "Categories"]) {
    const singular = entity.endsWith("ies")
      ? `${entity.slice(0, -3)}y`
      : entity.slice(0, -1);
    const surface = {};
    const model = {};
    for (const action of surfaces) {
      surface[action] = requireFile(
        new RegExp(`(?:^|/)Pages/${entity}/${action}\\.cshtml$`, "i"),
        `${entity} ${action} Razor Page`,
      );
      model[action] = requireFile(
        new RegExp(`(?:^|/)Pages/${entity}/${action}\\.cshtml\\.cs$`, "i"),
        `${entity} ${action} PageModel`,
      );
      fail(/@page\b/.test(surface[action]), `${entity} ${action} is not routable.`);
      fail(/:\s*PageModel\b/.test(model[action]), `${entity} ${action} has no PageModel.`);
    }

    fail(test(surface.Index, /(?:foreach|@for)\b/i, /asp-page\s*=\s*["'](?:\.\/)?Create/i), `${entity} Index has no real list/create UI.`);
    for (const action of ["Edit", "Details", "Delete"]) {
      fail(new RegExp(`asp-page\\s*=\\s*["'](?:\\./)?${action}`, "i").test(surface.Index), `${entity} Index does not link to ${action}.`);
    }
    fail(test(model.Index, /OnGetAsync\s*\(/, new RegExp(`\\.${entity}\\b`), /(?:ToList|ToArray)Async\s*\(/), `${entity} Index does not load an async list.`);

    fail(any(surface.Details, /DisplayFor\s*\(/, /@Model\.\w+/), `${entity} Details has no displayed data.`);
    fail(test(surface.Details, /asp-page\s*=\s*["'](?:\.\/)?Edit/i, /asp-page\s*=\s*["'](?:\.\/)?Index/i), `${entity} Details lacks action links.`);
    fail(test(model.Details, /OnGetAsync\s*\(/, /(?:Find|FirstOrDefault|SingleOrDefault)Async\s*\(/, /NotFound\s*\(/), `${entity} Details lacks async lookup/not-found behavior.`);

    for (const action of ["Create", "Edit"]) {
      fail(test(surface[action], /<form\b[^>]*method\s*=\s*["']post/i, /asp-validation-(?:summary|for)/i, /<(?:input|select)\b/i), `${entity} ${action} lacks a validated form.`);
      fail(test(model[action], /\[BindProperty\]/, /OnPostAsync\s*\(/, /ModelState\.IsValid/), `${entity} ${action} lacks bound validation-aware POST behavior.`);
      const post = methodsNamed(model[action], "OnPostAsync")[0] ??
        methodsNamed(model[action], "OnPost")[0];
      const submitted = boundPropertyName(model[action], singular);
      const submittedNames = requestDerivedNames(post?.body ?? "", submitted);
      const persistence = action === "Create"
        ? [/\.(?:Add|AddAsync)\s*\(/, /SaveChangesAsync\s*\(/]
        : [
            (fragment) => hasSubmittedEntityMutation(fragment, submittedNames),
            /SaveChangesAsync\s*\(/,
          ];
      fail(
        Boolean(post) &&
          modelStateControlsPersistence(post.body, persistence, /\breturn\s+Page\s*\(/),
        `${entity} ${action} POST can persist when ModelState is invalid.`,
      );
    }
    fail(test(model.Create, /\.(?:Add|AddAsync)\s*\(/, /SaveChangesAsync\s*\(/), `${entity} Create does not add and save.`);
    fail(test(model.Edit, /OnGetAsync\s*\(/, /(?:Find|FirstOrDefault|SingleOrDefault)Async\s*\(/), `${entity} Edit lacks async read behavior.`);
    fail(validRazorPostEdit(model.Edit, singular), `${entity} Edit POST does not persist submitted values.`);

    fail(test(surface.Delete, /<form\b[^>]*method\s*=\s*["']post/i, /type\s*=\s*["']submit/i), `${entity} Delete has no confirmation form.`);
    fail(test(model.Delete, /OnGetAsync\s*\(/, /OnPostAsync\s*\(/, /\.Remove\s*\(/, /SaveChangesAsync\s*\(/), `${entity} Delete lacks async read/delete/save behavior.`);
  }

  const productForms = [
    requireFile(/(?:^|\/)Pages\/Products\/Create\.cshtml$/i, "Product Create page"),
    requireFile(/(?:^|\/)Pages\/Products\/Edit\.cshtml$/i, "Product Edit page"),
  ].join("\n");
  const productModels = [
    requireFile(/(?:^|\/)Pages\/Products\/Create\.cshtml\.cs$/i, "Product Create model"),
    requireFile(/(?:^|\/)Pages\/Products\/Edit\.cshtml\.cs$/i, "Product Edit model"),
  ].join("\n");
  fail(
    count(productForms, /<select\b[^>]*(?:asp-for|@bind-[Vv]alue)\s*=\s*["'][^"']*CategoryId/gi) >= 2,
    "Product Create and Edit must bind CategoryId selections.",
  );
  fail(
    count(productModels, /\.Categories\b[\s\S]{0,500}(?:ToList|ToArray)Async\s*\(/gi) >= 2,
    "Product Create and Edit must load category choices from the database.",
  );
  for (const action of ["Create", "Edit"]) {
    const text = requireFile(
      new RegExp(`(?:^|/)Pages/Products/${action}\\.cshtml\\.cs$`, "i"),
      `Product ${action} model`,
    );
    const get = getMethod(methodsNamed(text, "OnGetAsync")) ?? getMethod(methodsNamed(text, "OnGet"));
    const post = methodsNamed(text, "OnPostAsync")[0] ?? methodsNamed(text, "OnPost")[0];
    const boundProduct = boundPropertyName(text, "Product");
    const loadsChoices = (body) =>
      /LoadCategories/.test(body) || /\.Categories\b[\s\S]{0,350}(?:ToList|ToArray)Async\s*\(/.test(body);
    fail(Boolean(get) && loadsChoices(get.body), `Product ${action} GET does not load category choices.`);
    fail(Boolean(post) && loadsChoices(post.body), `Product ${action} invalid POST does not reload category choices.`);
    fail(
      Boolean(post) &&
        Boolean(boundProduct) &&
        hasSubmittedCategoryLookup(post.body, boundProduct),
      `Product ${action} POST does not validate Product.CategoryId against Categories before persistence.`,
    );
  }
  const navigation = joined(/(?:Layout|Nav|Menu).*\.cshtml$/i);
  fail(
    /(?:asp-page\s*=\s*["'][^"']*Products|href\s*=\s*["'][^"']*products)/i.test(navigation) &&
      /(?:asp-page\s*=\s*["'][^"']*Categories|href\s*=\s*["'][^"']*categories)/i.test(navigation),
    "Navigation does not reach Products and Categories.",
  );
}

function validateTodoApi() {
  checkContext("TodoDbContext", ["TodoItem"], false);
  fail(
    hasActiveHostExtensionCall("MapTodoEndpoints"),
    "The Todo API host does not invoke MapTodoEndpoints().",
  );
  const records = matching(/\.cs$/i).flatMap((file) => endpointRecords(file.text, cs));
  checkApiCrud(records, "/api/todos", "Todo", {
    validation: true,
    validationType: "TodoRequest",
    validationField: "Title",
  });
  checkHttpCrud("/api/todos", "Todo", {
    requireCreateDependency: true,
    requireCreatedIdReuse: true,
    requestFields: ["title", "isComplete"],
  });
}

function methodParts(text, name) {
  return methodsNamed(text, name);
}

function postMethod(methods) {
  return methods.find((method) => /\[HttpPost\b/i.test(method.prefix));
}

function getMethod(methods) {
  return methods.find((method) => !/\[HttpPost\b/i.test(method.prefix));
}

function validateController(entity) {
  const plural = `${entity}s`;
  const controller = findFile(new RegExp(`(?:^|/)Controllers/${plural}Controller\\.cs$`, "i"));
  fail(Boolean(controller), `${plural}Controller is missing.`);
  if (!controller) return;
  const text = controller.text;
  fail(new RegExp(`class\\s+${plural}Controller\\b`).test(text), `${plural}Controller class is missing.`);
  fail(/CatalogDbContext/.test(text), `${plural}Controller does not reuse CatalogDbContext.`);

  const index = getMethod(methodParts(text, "Index"));
  fail(Boolean(index) && test(index.body, /(?:ToList|ToArray)Async\s*\(/, /View\s*\(/), `${plural} Index lacks async list behavior.`);

  const details = getMethod(methodParts(text, "Details"));
  fail(Boolean(details) && test(details.body, /(?:Find|FirstOrDefault|SingleOrDefault)Async\s*\(/, /NotFound\s*\(/, /View\s*\(/), `${plural} Details lacks async lookup/not-found/view behavior.`);

  const creates = methodParts(text, "Create");
  const createGet = getMethod(creates);
  const createPost = postMethod(creates);
  fail(Boolean(createGet) && /View\s*\(/.test(createGet.body), `${plural} Create GET is missing.`);
  fail(Boolean(createPost), `${plural} Create POST is missing.`);
  if (createPost) {
    fail(/\[ValidateAntiForgeryToken\]/.test(createPost.prefix), `${plural} Create POST lacks anti-forgery validation.`);
    fail(
      test(createPost.body, /ModelState\.IsValid/, /\.(?:Add|AddAsync)\s*\(/, /SaveChangesAsync\s*\(/, /View\s*\(/) &&
        modelStateControlsPersistence(
          createPost.body,
          [/\.(?:Add|AddAsync)\s*\(/, /SaveChangesAsync\s*\(/],
          /\breturn\s+View\s*\(/,
        ),
      `${plural} Create POST lacks validation/add/save behavior.`,
    );
  }

  const edits = methodParts(text, "Edit");
  const editGet = getMethod(edits);
  const editPost = postMethod(edits);
  fail(Boolean(editGet) && test(editGet.body, /(?:Find|FirstOrDefault|SingleOrDefault)Async\s*\(/, /NotFound\s*\(/, /View\s*\(/), `${plural} Edit GET lacks async lookup/not-found/view behavior.`);
  fail(Boolean(editPost), `${plural} Edit POST is missing.`);
  if (editPost) {
    fail(/\[ValidateAntiForgeryToken\]/.test(editPost.prefix), `${plural} Edit POST lacks anti-forgery validation.`);
    const submitted = parameterNameForType(editPost.signature, entity);
    const submittedNames = requestDerivedNames(editPost.body, submitted);
    fail(
      test(editPost.body, /ModelState\.IsValid/, /SaveChangesAsync\s*\(/, /NotFound\s*\(/, /View\s*\(/) &&
        validMvcPostEdit(editPost, entity) &&
        modelStateControlsPersistence(
          editPost.body,
          [
            (fragment) => hasSubmittedEntityMutation(fragment, submittedNames),
            /SaveChangesAsync\s*\(/,
          ],
          /\breturn\s+View\s*\(/,
        ),
      `${plural} Edit POST lacks validation/submitted-value update/save/not-found behavior.`,
    );
  }

  const deletes = [
    ...methodParts(text, "Delete"),
    ...methodParts(text, "DeleteConfirmed"),
  ];
  const deleteGet = getMethod(deletes);
  const deletePost = postMethod(deletes);
  fail(Boolean(deleteGet) && test(deleteGet.body, /(?:Find|FirstOrDefault|SingleOrDefault)Async\s*\(/, /NotFound\s*\(/, /View\s*\(/), `${plural} Delete GET lacks async lookup/not-found/view behavior.`);
  fail(Boolean(deletePost), `${plural} Delete POST is missing.`);
  if (deletePost) {
    fail(/\[ValidateAntiForgeryToken\]/.test(deletePost.prefix), `${plural} Delete POST lacks anti-forgery validation.`);
    fail(test(deletePost.body, /\.Remove\s*\(/, /SaveChangesAsync\s*\(/, /NotFound\s*\(/), `${plural} Delete POST lacks remove/save/not-found behavior.`);
  }
}

function validateMvcViews(entity) {
  const plural = `${entity}s`;
  const actions = ["Index", "Details", "Create", "Edit", "Delete"];
  const views = Object.fromEntries(actions.map((action) => [
    action,
    requireFile(new RegExp(`(?:^|/)Views/${plural}/${action}\\.cshtml$`, "i"), `${plural} ${action} view`),
  ]));

  fail(test(views.Index, /@model\b/, /(?:foreach|@for)\b/i, /asp-action\s*=\s*["']Create/i), `${plural} Index is not a real list view.`);
  for (const action of ["Edit", "Details", "Delete"]) {
    fail(new RegExp(`asp-action\\s*=\\s*["']${action}`, "i").test(views.Index), `${plural} Index does not link to ${action}.`);
  }
  fail(any(views.Details, /DisplayFor\s*\(/, /@Model\.\w+/) && /asp-action\s*=\s*["']Edit/i.test(views.Details), `${plural} Details lacks data and action links.`);
  for (const action of ["Create", "Edit"]) {
    fail(test(views[action], /<form\b/i, /method\s*=\s*["']post/i, /asp-validation-(?:summary|for)/i, /<(?:input|select)\b/i, /type\s*=\s*["']submit/i), `${plural} ${action} lacks a real validated form.`);
  }
  fail(test(views.Delete, /<form\b/i, /method\s*=\s*["']post/i, /type\s*=\s*["']submit/i, /asp-action\s*=\s*["']Index/i), `${plural} Delete lacks a confirmation form and return action.`);
  return views;
}

function validateMvc() {
  checkContext("CatalogDbContext", ["Book", "Author"], false);
  const contextClasses = [...cs.matchAll(/class\s+(\w+)[^{:\n]*(?::[^{\n]*\bDbContext\b|\([^)]*DbContextOptions)/g)].map((match) => match[1]);
  fail(contextClasses.length === 1 && contextClasses[0] === "CatalogDbContext", "MVC must reuse CatalogDbContext and must not add a second DbContext.");
  const bookModel = requireFile(/(?:^|\/)Models\/Book\.cs$/i, "Book model");
  const authorNavigation = activeSource(bookModel).match(
    /((?:\[[^\]\r\n]+\]\s*)*)public\s+(?:virtual\s+)?Author(\?)?\s+Author\b/,
  );
  fail(
    Boolean(authorNavigation) &&
      (authorNavigation[2] === "?" ||
        /\bValidateNever(?:Attribute)?\b/.test(authorNavigation[1])),
    "Book.Author must not be implicitly required during scalar form validation.",
  );

  validateController("Book");
  validateController("Author");
  const bookViews = validateMvcViews("Book");
  validateMvcViews("Author");

  for (const action of ["Create", "Edit"]) {
    fail(
      /<select\b[^>]*asp-for\s*=\s*["'][^"']*AuthorId["'][^>]*asp-items\s*=/i.test(bookViews[action]) ||
        /<select\b[^>]*asp-items\s*=[^>]*asp-for\s*=\s*["'][^"']*AuthorId["']/i.test(bookViews[action]),
      `Book ${action} must bind an AuthorId selection.`,
    );
  }
  const booksController = findFile(/(?:^|\/)Controllers\/BooksController\.cs$/i)?.text ?? "";
  fail(
    count(booksController, /\.Authors\b[\s\S]{0,400}(?:ToList|ToArray)Async\s*\(/gi) >= 1 &&
      count(booksController, /PopulateAuthors|SelectList/g) >= 2,
    "Book Create/Edit must load author choices from the database.",
  );
  for (const action of ["Create", "Edit"]) {
    const methods = methodsNamed(booksController, action);
    const get = getMethod(methods);
    const post = postMethod(methods);
    fail(Boolean(get) && /PopulateAuthors|Authors\b/.test(get.body), `Book ${action} GET does not load author choices.`);
    fail(Boolean(post) && /PopulateAuthors|Authors\b/.test(post.body), `Book ${action} invalid POST does not reload author choices.`);
    const submitted = post ? parameterNameForType(post.signature, "Book") : "";
    fail(
      Boolean(post) &&
        hasSubmittedRelatedEntityLookup(
          post.body,
          submitted,
          "Authors",
          "AuthorId",
        ),
      `Book ${action} POST does not reject a submitted AuthorId that is missing from the database.`,
    );
  }
  const navigation = joined(/(?:Layout|Nav|Menu).*\.cshtml$/i);
  fail(
    /(?:asp-controller\s*=\s*["']Books|href\s*=\s*["'][^"']*books)/i.test(navigation) &&
      /(?:asp-controller\s*=\s*["']Authors|href\s*=\s*["'][^"']*authors)/i.test(navigation),
    "MVC navigation does not reach Books and Authors.",
  );
}

function validateParentApi() {
  checkContext("StoreDbContext", ["Product", "Category"], false);
  const contextClasses = [...cs.matchAll(/class\s+(\w+)[^{:\n]*(?::[^{\n]*\bDbContext\b|\([^)]*DbContextOptions)/g)].map((match) => match[1]);
  fail(contextClasses.length === 1 && contextClasses[0] === "StoreDbContext", "Parent API must reuse StoreDbContext and must not add another DbContext.");
  fail(
    hasActiveHostExtensionCall("MapProductEndpoints"),
    "The Parent API host does not invoke MapProductEndpoints().",
  );

  const records = matching(/\.cs$/i).flatMap((file) => endpointRecords(file.text, cs));
  const category = checkApiCrud(records, "/api/categories", "Category", {
    validation: true,
    validationType: "CategoryRequest",
    validationField: "Name",
  });
  const product = checkApiCrud(records, "/api/products", "Product", {
    validation: true,
    validationType: "ProductRequest",
    validationField: "Name",
  });
  for (const [operation, record] of [["create", product.create], ["update", product.update]]) {
    if (!record) continue;
    const requestParameter = parameterNameForType(record.handlerSignature, "ProductRequest");
    fail(
      hasSubmittedCategoryLookup(record.behavior, requestParameter),
      `Product ${operation} does not validate the required CategoryId against the database.`,
    );
  }

  const categoryHttp = checkHttpCrud("/api/categories", "Category", {
    requireCreateDependency: true,
    requireCreatedIdReuse: true,
    requestFields: ["name"],
  });
  const productHttp = checkHttpCrud("/api/products", "Product", {
    requireCreateDependency: true,
    requireCreatedIdReuse: true,
    requestFields: ["name", "price", "categoryId"],
  });
  const http = joined(/\.http$/i);
  const categoryPost = http.search(/\bPOST\s+[^\n]*\/api\/categories\b/i);
  const productPost = http.search(/\bPOST\s+[^\n]*\/api\/products\b/i);
  fail(categoryPost >= 0 && productPost > categoryPost, "The .http workflow must create a Category before a Product.");
  const categoryCreate = categoryHttp?.flows.create;
  const productCreate = productHttp?.flows.create;
  if (categoryCreate && productCreate) {
    const categoryVariables = responseIdVariables(http, categoryCreate);
    fail(
      Boolean(categoryCreate.name) &&
        usesCreatedResponseId(jsonFieldValue(productCreate, "categoryId"), categoryCreate, categoryVariables),
      "Product creation must reuse the Category id returned by POST.",
    );
  }
}

function parameterNameForType(signature, type) {
  const escapedType = type.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return signature.match(new RegExp(`\\b${escapedType}\\s+(@?[A-Za-z_]\\w*)\\b`))?.[1] ?? "";
}

function entitySetPattern(entity) {
  if (entity === "Todo") return "Todo(?:Item)?s";
  if (entity.endsWith("y")) return `${entity.slice(0, -1)}(?:y|ies)`;
  return `${entity}s?`;
}

function statementStart(text, index) {
  return Math.max(
    text.lastIndexOf(";", index - 1),
    text.lastIndexOf("{", index - 1),
    text.lastIndexOf("}", index - 1),
  ) + 1;
}

function assignedAwaitResult(statement, awaitIndex) {
  const prefix = statement.slice(0, awaitIndex);
  return prefix.match(
    /\b(?:var|[A-Za-z_]\w*(?:\s*<[^;=]+>)?\??)\s+(@?[A-Za-z_]\w*)\s*=\s*$/,
  )?.[1] ??
    prefix.match(/(?:^|[;{}]\s*)(@?[A-Za-z_]\w*)\s*=\s*$/)?.[1] ??
    "";
}

function asyncTargetLookupVariables(text, entity) {
  const set = entitySetPattern(entity);
  const lookup = new RegExp(
    `\\.${set}\\b[^;]{0,600}?\\.(?:Find|FirstOrDefault|SingleOrDefault)Async\\s*\\(`,
    "gi",
  );
  const variables = [];
  for (const match of text.matchAll(lookup)) {
    const start = statementStart(text, match.index);
    const statement = statementAt(text, start);
    const relativeLookup = match.index - start;
    const awaitIndex = statement.lastIndexOf("await", relativeLookup);
    if (awaitIndex < 0) continue;
    const variable = assignedAwaitResult(statement, awaitIndex);
    if (variable && variable !== "_") variables.push(variable);
  }
  return [...new Set(variables)];
}

function hasTargetEntityMutation(text, entity, submittedEntity) {
  const targets = asyncTargetLookupVariables(text, entity);
  if (targets.length === 0 || !submittedEntity) return false;
  const submittedValues = requestDerivedNames(text, submittedEntity);

  for (const target of targets) {
    const escaped = target.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    for (const assignment of text.matchAll(
      new RegExp(`\\b${escaped}\\s*\\.\\s*[A-Za-z_]\\w*\\s*=(?!=|>)\\s*([^;]+);`, "g"),
    )) {
      if (referencesAnyIdentifier(assignment[1], submittedValues)) return true;
    }
  }

  return methodInvocations(text).some((invocation) =>
    invocation.name === "Update" &&
    invocation.arguments.some((argument) =>
      referencesAnyIdentifier(argument, submittedValues))) ||
    marksSubmittedEntityModified(text, submittedValues);
}

function boundPropertyName(text, entity) {
  const escapedEntity = entity.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return text.match(
    new RegExp(
      `\\[BindProperty(?:\\([^)]*\\))?\\][\\s\\S]{0,180}?\\b${escapedEntity}\\s+(@?[A-Za-z_]\\w*)\\b`,
    ),
  )?.[1] ?? "";
}

function marksSubmittedEntityModified(text, submittedNames) {
  if (submittedNames.size === 0) return false;
  if (
    methodInvocations(text).some((invocation) =>
      invocation.name === "Update" &&
      invocation.arguments.some((argument) =>
        referencesAnyIdentifier(argument, submittedNames)))
  ) {
    return true;
  }

  for (const name of submittedNames) {
    const escaped = name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    if (
      new RegExp(
        `\\b(?:Attach|Entry)\\s*\\(\\s*${escaped}\\s*\\)\\s*\\.\\s*State\\s*=\\s*EntityState\\.Modified\\b`,
      ).test(text)
    ) {
      return true;
    }
  }
  return false;
}

function hasSubmittedEntityMutation(text, submittedNames) {
  if (submittedNames.size === 0) return false;
  if (marksSubmittedEntityModified(text, submittedNames)) return true;

  return [...text.matchAll(
    /\b(@?[A-Za-z_]\w*)\s*\.\s*[A-Za-z_]\w*\s*=(?!=|>)\s*([^;]+);/g,
  )].some((assignment) =>
    referencesAnyIdentifier(assignment[2], submittedNames) &&
    !referencesIdentifier(assignment[2], assignment[1]));
}

function associatedAsyncTargetVariables(text, entity, requestNames) {
  const set = entitySetPattern(entity);
  const lookup = new RegExp(
    `\\.${set}\\b[^;]{0,600}?\\.(?:Find|FirstOrDefault|SingleOrDefault)Async\\s*\\(`,
    "gi",
  );
  const variables = [];
  for (const match of text.matchAll(lookup)) {
    const start = statementStart(text, match.index);
    const statement = statementAt(text, start);
    const relativeLookup = match.index - start;
    const openParen = relativeLookup + match[0].lastIndexOf("(");
    const closeParen = balancedEnd(statement, openParen, "(", ")");
    const awaitIndex = statement.lastIndexOf("await", relativeLookup);
    if (openParen < 0 || closeParen < 0 || awaitIndex < 0) continue;
    const argumentsText = statement.slice(openParen + 1, closeParen);
    if (!referencesAnyIdentifier(argumentsText, requestNames)) continue;
    const variable = assignedAwaitResult(statement, awaitIndex);
    if (variable && variable !== "_") variables.push(variable);
  }
  return [...new Set(variables)];
}

function loadedTargetReceivesSubmittedValues(text, entity, submittedNames, requestNames) {
  if (submittedNames.size === 0 || requestNames.size === 0) return false;
  const targets = associatedAsyncTargetVariables(text, entity, requestNames);
  for (const target of targets) {
    const escaped = target.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const submittedSources = new Set(submittedNames);
    submittedSources.delete(target);
    const assignments = [...text.matchAll(
      new RegExp(`\\b${escaped}\\s*\\.\\s*[A-Za-z_]\\w*\\s*=(?!=|>)\\s*([^;]+);`, "g"),
    )].filter((assignment) =>
      referencesAnyIdentifier(assignment[1], submittedSources));
    if (assignments.length === 0) continue;
    const firstAssignment = Math.min(...assignments.map((assignment) => assignment.index));
    const handlesMissingTarget = parsedIfStatements(text).some((node) => {
      if (node.start >= firstAssignment || !referencesIdentifier(node.condition, target)) {
        return false;
      }
      const nullResult = conditionResultForNullTarget(node.condition, target);
      const missingBranch = nullResult === null
        ? null
        : (nullResult ? node.thenText : node.elseText);
      return missingBranch !== null && returnsNotFoundResult(missingBranch);
    });
    if (handlesMissingTarget) return true;
  }
  return false;
}

function validRazorPostEdit(text, entity) {
  const post = methodsNamed(text, "OnPostAsync")[0] ?? methodsNamed(text, "OnPost")[0];
  if (!post || !/SaveChangesAsync\s*\(/.test(post.body)) return false;
  const submitted = boundPropertyName(text, entity);
  if (!submitted) return false;
  const submittedNames = requestDerivedNames(post.body, submitted);
  if (marksSubmittedEntityModified(post.body, submittedNames)) return true;

  const requestNames = new Set([
    ...submittedNames,
    ...methodParameterNames(post.signature),
  ].filter(Boolean));
  return loadedTargetReceivesSubmittedValues(
    post.body,
    entity,
    submittedNames,
    requestNames,
  );
}

function validMvcPostEdit(post, entity) {
  const submitted = parameterNameForType(post.signature, entity);
  if (!submitted) return false;
  const submittedNames = requestDerivedNames(post.body, submitted);
  if (marksSubmittedEntityModified(post.body, submittedNames)) return true;

  const requestNames = new Set([
    ...submittedNames,
    ...methodParameterNames(post.signature),
  ].filter(Boolean));
  return loadedTargetReceivesSubmittedValues(
    post.body,
    entity,
    submittedNames,
    requestNames,
  );
}

function requestDerivedNames(text, submittedEntity) {
  const names = new Set([submittedEntity]);
  let changed = true;
  while (changed) {
    changed = false;
    for (const assignment of text.matchAll(
      /\b(?:var|[A-Za-z_]\w*(?:\s*<[^;=]+>)?\??)\s+(@?[A-Za-z_]\w*)\s*=\s*([^;]+);/g,
    )) {
      if (!names.has(assignment[1]) && referencesAnyIdentifier(assignment[2], names)) {
        names.add(assignment[1]);
        changed = true;
      }
    }
  }
  return names;
}

function skipWhitespace(text, start) {
  let index = start;
  while (/\s/.test(text[index] ?? "")) index += 1;
  return index;
}

function controlledStatementEnd(text, start) {
  const statementStartIndex = skipWhitespace(text, start);
  if (text[statementStartIndex] === "{") {
    const end = balancedEnd(text, statementStartIndex, "{", "}");
    return end < 0 ? text.length : end + 1;
  }
  if (/^if\b/.test(text.slice(statementStartIndex))) {
    return parseIfAt(text, statementStartIndex)?.end ?? text.length;
  }
  return statementStartIndex + statementAt(text, statementStartIndex).length;
}

function parseIfAt(text, start) {
  const openParen = text.indexOf("(", start);
  if (openParen < 0) return null;
  const closeParen = balancedEnd(text, openParen, "(", ")");
  if (closeParen < 0) return null;
  const thenStart = skipWhitespace(text, closeParen + 1);
  const thenEnd = controlledStatementEnd(text, thenStart);
  let end = thenEnd;
  let elseStart = -1;
  let elseEnd = -1;
  const afterThen = skipWhitespace(text, thenEnd);
  if (/^else\b/.test(text.slice(afterThen))) {
    elseStart = skipWhitespace(text, afterThen + 4);
    elseEnd = controlledStatementEnd(text, elseStart);
    end = elseEnd;
  }
  return {
    start,
    end,
    conditionStart: openParen + 1,
    conditionEnd: closeParen,
    condition: text.slice(openParen + 1, closeParen),
    thenText: text.slice(thenStart, thenEnd),
    elseText: elseStart < 0 ? "" : text.slice(elseStart, elseEnd),
  };
}

function parsedIfStatements(text) {
  const result = [];
  for (const match of text.matchAll(/\bif\s*\(/g)) {
    const parsed = parseIfAt(text, match.index);
    if (parsed) result.push(parsed);
  }
  return result;
}

function conditionResultForModelStateValidity(condition) {
  let expression = stripOuterParentheses(condition);
  if (expression.startsWith("!")) {
    const nested = conditionResultForModelStateValidity(
      stripOuterParentheses(expression.slice(1)),
    );
    return nested === null ? null : !nested;
  }

  const compact = expression.replace(/\s+/g, "").toLowerCase();
  const valid = "modelstate.isvalid";
  if (compact === valid) return true;
  if (
    [`${valid}==true`, `${valid}istrue`, `${valid}!=false`, `${valid}isnotfalse`]
      .includes(compact)
  ) return true;
  if (
    [`${valid}==false`, `${valid}isfalse`, `${valid}!=true`, `${valid}isnottrue`]
      .includes(compact)
  ) return false;
  return null;
}

function modelStateControlsPersistence(body, requiredPatterns, rerenderPattern) {
  const matches = (requirement, text) =>
    typeof requirement === "function"
      ? requirement(text)
      : requirement.test(text);
  return parsedIfStatements(body).some((node) => {
    const conditionWhenValid = conditionResultForModelStateValidity(node.condition);
    if (conditionWhenValid === null) return false;

    const prefix = body.slice(0, node.start);
    const suffix = body.slice(node.end);
    const validBranch = conditionWhenValid ? node.thenText : node.elseText;
    const invalidBranch = conditionWhenValid ? node.elseText : node.thenText;
    const continues = (branch) => !/\breturn\b/.test(branch);
    const validFlow = `${validBranch}\n${continues(validBranch) ? suffix : ""}`;
    const invalidFlow = `${invalidBranch}\n${continues(invalidBranch) ? suffix : ""}`;

    return requiredPatterns.every((requirement) => matches(requirement, validFlow)) &&
      requiredPatterns.every((requirement) =>
        !matches(requirement, prefix) && !matches(requirement, invalidFlow)) &&
      rerenderPattern.test(invalidFlow);
  });
}

function errorResponse(text) {
  return /\b(?:NotFound|ValidationProblem|BadRequest|UnprocessableEntity)\s*\(/.test(text);
}

function isNotFoundExpression(text) {
  const expression = stripOuterParentheses(text);
  return /^(?:(?:[A-Za-z_]\w*)\s*\.\s*)*NotFound\s*\(/.test(expression);
}

function returnsNotFoundResult(text) {
  const assignedResults = [];
  for (const assignment of text.matchAll(
    /\b(?:var|[A-Za-z_]\w*(?:\s*<[^;=]+>)?\??)\s+(@?[A-Za-z_]\w*)\s*=\s*([^;]+);/g,
  )) {
    if (assignment[1] !== "_" && isNotFoundExpression(assignment[2])) {
      assignedResults.push({ name: assignment[1], index: assignment.index });
    }
  }

  for (const returned of text.matchAll(/\breturn\s+([^;]+);/g)) {
    if (isNotFoundExpression(returned[1])) return true;
    const local = stripOuterParentheses(returned[1]).match(/^(@?[A-Za-z_]\w*)$/)?.[1];
    if (
      local &&
      assignedResults.some((assignment) =>
        assignment.name === local && assignment.index < returned.index)
    ) {
      return true;
    }
  }
  return false;
}

function changedErrorStates(text) {
  return new Set([
    ...[...text.matchAll(/\b(@?[A-Za-z_]\w*)\s*\[[^\]]+\]\s*=/g)].map((match) => match[1]),
    ...[...text.matchAll(/\b(@?[A-Za-z_]\w*)\s*\.(?:Add|TryAdd|AddModelError)\s*\(/g)]
      .map((match) => match[1]),
  ]);
}

function invalidBranchForResult(node, variable, booleanResult) {
  const escaped = variable.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  if (booleanResult) {
    if (
      new RegExp(`!\\s*${escaped}\\b|\\b${escaped}\\s*(?:==|is)\\s*false\\b`).test(node.condition)
    ) return node.thenText;
    if (
      new RegExp(`\\b${escaped}\\s*(?:==|is)\\s*true\\b`).test(node.condition) ||
      new RegExp(`^\\s*${escaped}\\s*$`).test(node.condition)
    ) return node.elseText;
  } else {
    if (new RegExp(`\\b${escaped}\\s*(?:==|is)\\s*null\\b`).test(node.condition)) {
      return node.thenText;
    }
    if (new RegExp(`\\b${escaped}\\s*(?:!=|is\\s+not)\\s*null\\b`).test(node.condition)) {
      return node.elseText;
    }
  }
  return null;
}

function invalidBranchRejects(text, node, branch) {
  if (branch === null) return false;
  if (errorResponse(branch)) return true;
  const states = changedErrorStates(branch);
  if (states.size === 0) return false;
  return parsedIfStatements(text).some((later) =>
    later.start >= node.end &&
    [...states].some((state) => {
      if (/^ModelState$/i.test(state)) {
        const conditionWhenValid = conditionResultForModelStateValidity(later.condition);
        if (conditionWhenValid === null) return false;
        const invalidBranch = conditionWhenValid ? later.elseText : later.thenText;
        const fallthrough = /\breturn\b/.test(invalidBranch)
          ? ""
          : text.slice(later.end);
        return /\breturn\s+(?:Page|View)\s*\(/.test(`${invalidBranch}\n${fallthrough}`) ||
          errorResponse(`${invalidBranch}\n${fallthrough}`);
      }
      const nonEmptyResult = conditionResultForNonEmptyCollection(later.condition, state);
      if (nonEmptyResult === null) return false;
      return errorResponse(nonEmptyResult ? later.thenText : later.elseText);
    }));
}

function stripOuterParentheses(text) {
  let result = text.trim();
  while (result.startsWith("(")) {
    const close = balancedEnd(result, 0, "(", ")");
    if (close !== result.length - 1) break;
    result = result.slice(1, -1).trim();
  }
  return result;
}

function conditionResultForNullTarget(condition, variable) {
  let expression = stripOuterParentheses(condition);
  if (expression.startsWith("!")) {
    const nested = conditionResultForNullTarget(
      stripOuterParentheses(expression.slice(1)),
      variable,
    );
    return nested === null ? null : !nested;
  }

  expression = expression.replace(/\s+/g, "");
  const escaped = variable.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  if (
    new RegExp(`^(?:${escaped}(?:==|is)null|null==${escaped})$`).test(expression)
  ) return true;
  if (
    new RegExp(`^(?:${escaped}(?:!=|isnot)null|null!=${escaped})$`).test(expression)
  ) return false;
  return null;
}

function conditionResultForNullOrWhiteSpace(
  condition,
  requiredField = "",
  requestNames = new Set(),
) {
  let expression = stripOuterParentheses(condition);
  if (expression.startsWith("!")) {
    const nested = conditionResultForNullOrWhiteSpace(
      stripOuterParentheses(expression.slice(1)),
      requiredField,
      requestNames,
    );
    return nested === null ? null : !nested;
  }

  const call = expression.match(
    /^(?:(?:global::)?(?:System\.)?(?:string|String)\s*\.\s*)?IsNullOrWhiteSpace\s*\(/,
  );
  if (!call) return null;
  const openParen = expression.indexOf("(", call.index);
  const closeParen = balancedEnd(expression, openParen, "(", ")");
  if (closeParen < 0) return null;
  const argument = expression.slice(openParen + 1, closeParen);
  if (requiredField) {
    const escapedField = requiredField.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const referencesRequiredField = [...requestNames].some((requestName) => {
      const escapedRequest = requestName.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
      return new RegExp(
        `(?:^|[^A-Za-z0-9_@])${escapedRequest}\\s*\\.\\s*${escapedField}(?![A-Za-z0-9_])`,
      ).test(argument);
    });
    if (!referencesRequiredField) return null;
  }
  const suffix = expression.slice(closeParen + 1).replace(/\s+/g, "").toLowerCase();
  if (["", "==true", "istrue", "!=false", "isnotfalse"].includes(suffix)) return true;
  if (["==false", "isfalse", "!=true", "isnottrue"].includes(suffix)) return false;
  return null;
}

function conditionResultForNonEmptyCollection(condition, variable) {
  const escaped = variable.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  let expression = stripOuterParentheses(condition);
  if (expression.startsWith("!")) {
    const nested = conditionResultForNonEmptyCollection(
      stripOuterParentheses(expression.slice(1)),
      variable,
    );
    return nested === null ? null : !nested;
  }

  expression = expression.replace(/\s+/g, "");
  const anyCall = `${escaped}\\.Any\\(\\)`;
  if (new RegExp(`^${anyCall}$`).test(expression)) return true;
  if (new RegExp(`^${anyCall}(?:==|is)true$`).test(expression)) return true;
  if (new RegExp(`^${anyCall}(?:!=)false$`).test(expression)) return true;
  if (new RegExp(`^${anyCall}(?:==|is)false$`).test(expression)) return false;
  if (new RegExp(`^${anyCall}(?:!=)true$`).test(expression)) return false;

  const count = `${escaped}\\.Count`;
  if (
    new RegExp(`^${count}(?:>|!=)0$`).test(expression) ||
    new RegExp(`^${count}>=1$`).test(expression) ||
    new RegExp(`^0(?:<|!=)${count}$`).test(expression) ||
    new RegExp(`^1<=${count}$`).test(expression) ||
    new RegExp(`^${count}is(?:not0|>0|>=1)$`).test(expression)
  ) return true;
  if (
    new RegExp(`^${count}(?:==|<=)0$`).test(expression) ||
    new RegExp(`^${count}<1$`).test(expression) ||
    new RegExp(`^0(?:==|>=)${count}$`).test(expression) ||
    new RegExp(`^1>${count}$`).test(expression) ||
    new RegExp(`^${count}is(?:0|<=0|<1)$`).test(expression)
  ) return false;
  return null;
}

function validationErrorResponseUses(text, variable) {
  return methodInvocations(text).some((invocation) =>
    /^(?:ValidationProblem|BadRequest|UnprocessableEntity)$/.test(invocation.name) &&
    invocation.arguments.some((argument) => referencesIdentifier(argument, variable)));
}

function requiredValidationErrorStates(text, requiredField, requestNames) {
  const states = new Set();
  for (const node of parsedIfStatements(text)) {
    const whitespaceResult = conditionResultForNullOrWhiteSpace(
      node.condition,
      requiredField,
      requestNames,
    );
    if (whitespaceResult === null) continue;
    const requiredBranch = whitespaceResult ? node.thenText : node.elseText;
    for (const state of changedErrorStates(requiredBranch)) states.add(state);
  }
  for (const invocation of methodInvocations(text)) {
    if (invocation.name !== "TryValidateObject" || invocation.arguments.length < 3) continue;
    if (!referencesAnyIdentifier(invocation.arguments[0], requestNames)) continue;
    const resultCollection = invocation.arguments[2].match(/^\s*(@?[A-Za-z_]\w*)\s*$/)?.[1];
    if (resultCollection) states.add(resultCollection);
  }
  return states;
}

function requiredValidationErrorCollections(text, requiredField, requestParameter) {
  const requestNames = new Set([requestParameter].filter(Boolean));
  const collections = requiredValidationErrorStates(text, requiredField, requestNames);

  for (const assignment of text.matchAll(
    /\b(?:var|[A-Za-z_]\w*(?:\s*<[^;=]+>)?\??)\s+(@?[A-Za-z_]\w*)\s*=\s*(?:await\s+)?([A-Za-z_]\w*)\s*\(([^;]*)\)\s*;/g,
  )) {
    const argumentsList = splitTopLevelArguments(assignment[3]);
    const requestArgumentIndex = argumentsList.findIndex((argument) =>
      referencesAnyIdentifier(argument, requestNames));
    if (requestArgumentIndex < 0) continue;
    for (const method of methodsNamed(text, assignment[2])) {
      if (!/\b(?:IsNullOrWhiteSpace|TryValidateObject)\s*\(/.test(method.body)) continue;
      const methodRequestParameter = methodParameterNames(method.signature)[requestArgumentIndex];
      if (!methodRequestParameter) continue;
      const changed = requiredValidationErrorStates(
        method.body,
        requiredField,
        new Set([methodRequestParameter]),
      );
      if (
        [...changed].some((collection) =>
          new RegExp(`\\breturn\\s+${collection.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}\\s*;`).test(method.body))
      ) {
        collections.add(assignment[1]);
      }
    }
  }

  for (const collection of [...collections]) {
    for (const alias of requestDerivedNames(text, collection)) collections.add(alias);
  }
  return collections;
}

function nonEmptyValidationErrorsReachResponse(text, requiredField, requestParameter) {
  const validationCollections = requiredValidationErrorCollections(
    text,
    requiredField,
    requestParameter,
  );
  for (const node of parsedIfStatements(text)) {
    const variables = new Set([
      ...[...node.condition.matchAll(/\b(@?[A-Za-z_]\w*)\s*\.\s*Count\b/g)]
        .map((match) => match[1]),
      ...[...node.condition.matchAll(/\b(@?[A-Za-z_]\w*)\s*\.\s*Any\s*\(\s*\)/g)]
        .map((match) => match[1]),
    ]);
    for (const variable of variables) {
      if (!validationCollections.has(variable)) continue;
      const nonEmptyResult = conditionResultForNonEmptyCollection(node.condition, variable);
      if (nonEmptyResult === null) continue;
      const nonEmptyBranch = nonEmptyResult ? node.thenText : node.elseText;
      if (validationErrorResponseUses(nonEmptyBranch, variable)) return true;
      if (!nonEmptyResult && !node.elseText) {
        const fallthrough = text.slice(node.end)
          .split(/\n\s*(?:public|private|protected|internal)\s+/)[0];
        if (validationErrorResponseUses(fallthrough, variable)) return true;
      }
    }
  }
  return false;
}

function hasSubmittedRelatedEntityLookup(
  text,
  requestParameter,
  collectionName,
  foreignKey,
) {
  if (!requestParameter) return false;
  const escapedParameter = requestParameter.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const escapedCollection = collectionName.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const escapedForeignKey = foreignKey.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const submittedForeignKey = new RegExp(
    `(?:^|[^A-Za-z0-9_@])${escapedParameter}\\s*\\.\\s*${escapedForeignKey}\\b`,
  );
  const ifStatements = parsedIfStatements(text);
  const persistenceIndex = text.search(
    /\.(?:Add|AddAsync|Update|Attach)\s*\(|SaveChangesAsync\s*\(/,
  );
  for (const collection of text.matchAll(new RegExp(`\\.${escapedCollection}\\b`, "g"))) {
    const start = statementStart(text, collection.index);
    const statement = statementAt(text, start);
    const collectionIndex = collection.index - start;
    for (const lookup of statement.matchAll(/\.(Find|Any|FirstOrDefault|SingleOrDefault)Async\s*\(/g)) {
      if (lookup.index < collectionIndex) continue;
      const openParen = statement.indexOf("(", lookup.index);
      const closeParen = balancedEnd(statement, openParen, "(", ")");
      if (closeParen < 0) continue;
      const argumentsText = statement.slice(openParen + 1, closeParen);
      const awaitIndex = statement.lastIndexOf("await", lookup.index);
      if (awaitIndex < 0 || !submittedForeignKey.test(argumentsText)) continue;
      const absoluteLookup = start + lookup.index;
      if (persistenceIndex >= 0 && absoluteLookup >= persistenceIndex) continue;

      const variable = assignedAwaitResult(statement, awaitIndex);
      if (variable && variable !== "_") {
        const booleanResult = lookup[1].toLowerCase() === "any";
        for (const node of ifStatements) {
          if (
            node.start < start + statement.length ||
            (persistenceIndex >= 0 && node.start >= persistenceIndex)
          ) continue;
          if (!referencesIdentifier(node.condition, variable)) continue;
          const branch = invalidBranchForResult(node, variable, booleanResult);
          if (invalidBranchRejects(text, node, branch)) return true;
        }
      }

      for (const node of ifStatements) {
        if (absoluteLookup < node.conditionStart || absoluteLookup > node.conditionEnd) continue;
        if (persistenceIndex >= 0 && node.start >= persistenceIndex) continue;
        const branch = lookup[1].toLowerCase() === "any"
          ? (/!\s*await\b/.test(node.condition) ? node.thenText : node.elseText)
          : (/\bis\s+null\b|==\s*null\b/.test(node.condition) ? node.thenText : node.elseText);
        if (invalidBranchRejects(text, node, branch)) return true;
      }
    }
  }
  return false;
}

function hasSubmittedCategoryLookup(text, requestParameter) {
  return hasSubmittedRelatedEntityLookup(
    text,
    requestParameter,
    "Categories",
    "CategoryId",
  );
}

function blazorComponentGroups(entity) {
  const singular = entity.toLowerCase();
  const components = files.filter((file) => /\.razor(?:\.cs)?$/i.test(file.rel));
  const named = components.filter((file) => file.rel.toLowerCase().includes(singular));
  const selected = named.length > 0
    ? named
    : components.filter((file) => new RegExp(`\\b${entity}\\b`, "i").test(file.text));
  const groups = new Map();
  for (const file of selected) {
    const key = file.rel.replace(/\.razor(?:\.cs)?$/i, "");
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(file);
  }
  return [...groups.entries()].map(([key, grouped]) => ({
    key,
    name: key.split("/").at(-1),
    text: grouped.map((file) => `\n/* ${file.rel} */\n${file.text}`).join("\n"),
  }));
}

function formModelNames(form) {
  const explicit = form.match(/\bModel\s*=\s*["']@?([A-Za-z_]\w*)/i)?.[1];
  if (explicit) return [explicit];
  return [...new Set(
    [...form.matchAll(/@bind-[Vv]alue\s*=\s*["']@?([A-Za-z_]\w*)\./g)].map((match) => match[1]),
  )];
}

function safelyInitializedFormModel(form, text, entity) {
  const modelNames = formModelNames(form);
  if (modelNames.length === 0) return false;
  return modelNames.every((name) => {
    const escaped = name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const declaration = new RegExp(
      `\\b${entity}\\s+${escaped}\\s*(?:\\{[^}]*\\}\\s*)?=\\s*new(?:\\s+${entity})?\\s*\\(`,
      "i",
    );
    const nullForgiving = new RegExp(`\\b${entity}\\s+${escaped}\\s*=\\s*null!`, "i");
    return declaration.test(text) && !nullForgiving.test(text);
  });
}

function editForms(text) {
  return [...text.matchAll(/<EditForm\b[\s\S]*?<\/EditForm>/gi)].map((match) => match[0]);
}

function formHandler(form) {
  return form.match(/\bOnValidSubmit\s*=\s*["']@?(?:\(\s*\)\s*=>\s*)?([A-Za-z_]\w*)/i)?.[1] ?? "";
}

function reachableMethods(text, names) {
  const pending = [...names];
  const seen = new Set();
  const result = [];
  while (pending.length > 0) {
    const name = pending.shift();
    if (!name || seen.has(name)) continue;
    seen.add(name);
    for (const method of methodsNamed(text, name)) {
      result.push({ name, ...method });
      for (const match of method.body.matchAll(/\b([A-Za-z_]\w*)\s*\(/g)) pending.push(match[1]);
    }
  }
  return result;
}

function splitTopLevelArguments(text) {
  const parts = [];
  let start = 0;
  let parens = 0;
  let braces = 0;
  let brackets = 0;
  let angles = 0;
  let quote = "";
  let escaped = false;
  for (let index = 0; index < text.length; index += 1) {
    const character = text[index];
    if (quote) {
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === quote) quote = "";
      continue;
    }
    if (character === '"' || character === "'") quote = character;
    else if (character === "(") parens += 1;
    else if (character === ")") parens -= 1;
    else if (character === "{") braces += 1;
    else if (character === "}") braces -= 1;
    else if (character === "[") brackets += 1;
    else if (character === "]") brackets -= 1;
    else if (character === "<") angles += 1;
    else if (character === ">" && angles > 0) angles -= 1;
    else if (character === "," && parens === 0 && braces === 0 && brackets === 0 && angles === 0) {
      parts.push(text.slice(start, index).trim());
      start = index + 1;
    }
  }
  parts.push(text.slice(start).trim());
  return parts.filter(Boolean);
}

function methodParameterNames(signature) {
  const openParen = signature.indexOf("(");
  const closeParen = openParen < 0 ? -1 : balancedEnd(signature, openParen, "(", ")");
  if (closeParen < 0) return [];
  return splitTopLevelArguments(signature.slice(openParen + 1, closeParen)).map((parameter) =>
    parameter.match(/(@?[A-Za-z_]\w*)\s*(?:=[\s\S]*)?$/)?.[1] ?? "");
}

function methodInvocations(body) {
  const invocations = [];
  for (const match of body.matchAll(/\b(@?[A-Za-z_]\w*)\s*\(/g)) {
    const openParen = body.indexOf("(", match.index);
    const closeParen = balancedEnd(body, openParen, "(", ")");
    if (closeParen < 0) continue;
    invocations.push({
      name: match[1],
      arguments: splitTopLevelArguments(body.slice(openParen + 1, closeParen)),
    });
  }
  return invocations;
}

function referencesIdentifier(text, name) {
  const escaped = name.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return new RegExp(`(?:^|[^A-Za-z0-9_@])${escaped}(?![A-Za-z0-9_])`).test(text);
}

function referencesAnyIdentifier(text, names) {
  return [...names].some((name) => referencesIdentifier(text, name));
}

function modelAliases(body, initialNames) {
  const aliases = new Set(initialNames);
  const newlyCreated = new Set(
    [...body.matchAll(/\b(?:var|[A-Za-z_]\w*(?:\s*<[^;=]+>)?\??)\s+(@?[A-Za-z_]\w*)\s*=\s*new\b/g)]
      .map((match) => match[1]),
  );
  let changed = true;
  while (changed) {
    changed = false;
    const assignments = [
      ...body.matchAll(/\b(?:var|[A-Za-z_]\w*(?:\s*<[^;=]+>)?\??)\s+(@?[A-Za-z_]\w*)\s*=\s*([^;]+);/g),
      ...body.matchAll(/(?:^|[;{}]\s*)(@?[A-Za-z_]\w*)\s*=\s*([^;]+);/g),
    ];
    for (const assignment of assignments) {
      const name = assignment[1];
      const expression = assignment[2];
      const modelDerivedValue =
        /^\s*new\b/.test(expression) ||
        /^\s*@?[A-Za-z_]\w*\s*$/.test(expression);
      if (
        !aliases.has(name) &&
        modelDerivedValue &&
        referencesAnyIdentifier(expression, aliases)
      ) {
        aliases.add(name);
        changed = true;
      }
    }
    for (const assignment of body.matchAll(
      /\b(@?[A-Za-z_]\w*)\s*\.\s*[A-Za-z_]\w*\s*=\s*([^;]+);/g,
    )) {
      if (
        newlyCreated.has(assignment[1]) &&
        !aliases.has(assignment[1]) &&
        referencesAnyIdentifier(assignment[2], aliases)
      ) {
        aliases.add(assignment[1]);
        changed = true;
      }
    }
  }
  return aliases;
}

function analyzeFormHandler(text, handler, modelNames) {
  const pending = [{ name: handler, tainted: new Set(modelNames), path: [handler] }];
  const seen = new Set();
  const createMethods = new Set();
  const editMethods = new Set();
  const savedCreateMethods = new Set();
  const savedEditMethods = new Set();
  let handlerSaves = false;

  while (pending.length > 0) {
    const current = pending.shift();
    const stateKey = `${current.name}:${[...current.tainted].sort().join(",")}`;
    if (!current.name || seen.has(stateKey)) continue;
    seen.add(stateKey);

    for (const method of methodsNamed(text, current.name)) {
      const aliases = modelAliases(method.body, current.tainted);
      const addUsesModel = methodInvocations(method.body).some((invocation) =>
        /^(?:Add|AddAsync)$/.test(invocation.name) &&
        invocation.arguments.some((argument) => referencesAnyIdentifier(argument, aliases)));
      const trackedUpdateUsesModel = methodInvocations(method.body).some((invocation) =>
        invocation.name === "Update" &&
        invocation.arguments.some((argument) => referencesAnyIdentifier(argument, aliases))) ||
        marksSubmittedEntityModified(method.body, aliases);
      const loadsExisting = /\.(?:Find|FirstOrDefault|SingleOrDefault)Async\s*\(/.test(method.body);
      const assignsFromModel = [...method.body.matchAll(
        /\b(@?[A-Za-z_]\w*)\s*\.\s*[A-Za-z_]\w*\s*=\s*([^;]+);/g,
      )].some((assignment) =>
        !aliases.has(assignment[1]) && referencesAnyIdentifier(assignment[2], aliases));
      const saves = /SaveChangesAsync\s*\(/.test(method.body);
      const methodName = current.name;

      if (addUsesModel) {
        createMethods.add(methodName);
        if (saves) savedCreateMethods.add(methodName);
      }
      if (trackedUpdateUsesModel || (loadsExisting && assignsFromModel)) {
        editMethods.add(methodName);
        if (saves) savedEditMethods.add(methodName);
      }
      if (current.name === handler && saves) handlerSaves = true;

      for (const invocation of methodInvocations(method.body)) {
        for (const called of methodsNamed(text, invocation.name)) {
          const parameters = methodParameterNames(called.signature);
          const tainted = new Set(modelNames);
          for (let index = 0; index < Math.min(parameters.length, invocation.arguments.length); index += 1) {
            if (parameters[index] && referencesAnyIdentifier(invocation.arguments[index], aliases)) {
              tainted.add(parameters[index]);
            }
          }
          if (tainted.size > 0) {
            pending.push({
              name: invocation.name,
              tainted,
              path: [...current.path, invocation.name],
            });
          }
        }
      }
    }
  }

  return {
    create: savedCreateMethods.size > 0 || (handlerSaves && createMethods.size > 0),
    edit: savedEditMethods.size > 0 || (handlerSaves && editMethods.size > 0),
    createMethods,
    editMethods,
  };
}

function operationBehavior(text, handler, operationPattern) {
  const reachable = reachableMethods(text, [handler]);
  const operationHelpers = reachable
    .filter((method) => method.name !== handler && operationPattern.test(method.body))
    .map((method) => `${method.signature}\n${expandReferencedMethods(text, method.body)}`);
  if (operationHelpers.length > 0) {
    const sharedSave = reachable
      .filter((method) => method.name === handler && /SaveChangesAsync\s*\(/.test(method.body))
      .map((method) => method.body)
      .join("\n");
    return `${operationHelpers.join("\n")}\n${sharedSave}`;
  }
  return reachable
    .filter((method) => method.name === handler)
    .map((method) => `${method.signature}\n${expandReferencedMethods(text, method.body)}`)
    .join("\n");
}

function validatedEditForm(form) {
  return test(form, /<EditForm\b/i, /<DataAnnotationsValidator\b/i, /<(?:ValidationSummary|ValidationMessage)\b/i);
}

function departmentSelector(form) {
  return [...form.matchAll(/<InputSelect\b[^>]*>/gi)]
    .some((match) => /@bind-[Vv]alue\s*=\s*["'][^"']*DepartmentId/i.test(match[0]));
}

function databaseLoadedDepartments(form, text) {
  const selectors = [...form.matchAll(
    /<InputSelect\b(?=[^>]*@bind-[Vv]alue\s*=\s*["'][^"']*DepartmentId)[^>]*>[\s\S]*?<\/InputSelect>/gi,
  )];
  const sources = selectors.flatMap((selector) =>
    [...selector[0].matchAll(/@?foreach\s*\([^)]*\bin\s+([A-Za-z_]\w*)/gi)]
      .map((match) => match[1]));
  return sources.some((source) => {
    const escaped = source.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    return new RegExp(
      `\\b${escaped}\\s*=\\s*await\\b[^;]{0,1000}\\.Departments\\b[^;]{0,1000}(?:ToList|ToArray)Async\\s*\\(`,
    ).test(text);
  });
}

function findOperationGroup(groups, operation, predicate) {
  const explicit = groups.find((group) => group.name.toLowerCase() === operation.toLowerCase());
  return explicit ?? groups.find((group) => predicate(group.text));
}

function formRole(group, modelNames) {
  const hint = `${group.name} ${modelNames.join(" ")}`;
  const create = /\b(?:create|new|add)\w*/i.test(hint);
  const edit = /\b(?:edit|update)\w*/i.test(hint);
  if (create === edit) return "";
  return create ? "create" : "edit";
}

function blazorFormCandidates(groups) {
  return groups.flatMap((group) =>
    editForms(group.text).map((form, index) => {
      const handler = formHandler(form);
      const modelNames = formModelNames(form);
      return {
        id: `${group.key}:${index}`,
        group,
        form,
        handler,
        modelNames,
        role: formRole(group, modelNames),
        analysis: analyzeFormHandler(group.text, handler, modelNames),
      };
    }));
}

function selectOperationCandidate(candidates, operation) {
  const explicitlyRoleMatched = candidates.filter((candidate) => candidate.role === operation);
  const pool = explicitlyRoleMatched.length > 0 ? explicitlyRoleMatched : candidates;
  return pool.find((candidate) => candidate.analysis[operation]) ?? null;
}

function genuinelyBranchesBothWays(candidate) {
  if (!candidate) return false;
  const handlerMethod = methodsNamed(candidate.group.text, candidate.handler)[0];
  if (!handlerMethod) return false;

  const stateAliases = new Map();
  for (const declaration of handlerMethod.body.matchAll(
    /\b(?:var|bool)\s+(@?[A-Za-z_]\w*)\s*=\s*([^;]+);/g,
  )) {
    const trueMeansNew = conditionResultForNewModel(
      declaration[2],
      candidate.modelNames,
      stateAliases,
    );
    if (trueMeansNew !== null) stateAliases.set(declaration[1], trueMeansNew);
  }
  const operationMethods = (fragment) => {
    const names = new Set();
    for (const invocation of methodInvocations(fragment)) {
      names.add(invocation.name);
      for (const method of reachableMethods(candidate.group.text, [invocation.name])) {
        names.add(method.name);
      }
    }
    return {
      create: [...candidate.analysis.createMethods].some((name) => names.has(name)),
      edit: [...candidate.analysis.editMethods].some((name) => names.has(name)),
    };
  };

  return parsedIfStatements(handlerMethod.body).some((node) => {
    if (!node.elseText) return false;
    const trueMeansNew = conditionResultForNewModel(
      node.condition,
      candidate.modelNames,
      stateAliases,
    );
    if (trueMeansNew === null) return false;
    const thenOperations = operationMethods(node.thenText);
    const elseOperations = operationMethods(node.elseText);
    const trueBranchCreates = (
      thenOperations.create && !thenOperations.edit &&
      elseOperations.edit && !elseOperations.create
    );
    const trueBranchEdits = (
      thenOperations.edit && !thenOperations.create &&
      elseOperations.create && !elseOperations.edit
    );
    return trueMeansNew ? trueBranchCreates : trueBranchEdits;
  });
}

function conditionResultForNewModel(condition, modelNames, aliases) {
  let expression = stripOuterParentheses(condition);
  if (expression.startsWith("!")) {
    const nested = conditionResultForNewModel(
      stripOuterParentheses(expression.slice(1)),
      modelNames,
      aliases,
    );
    return nested === null ? null : !nested;
  }

  const compact = expression.replace(/\s+/g, "");
  for (const model of modelNames) {
    const escaped = model.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const id = `${escaped}\\.Id`;
    if (
      new RegExp(`^${id}(?:==|is)0$`).test(compact) ||
      new RegExp(`^0==${id}$`).test(compact)
    ) return true;
    if (
      new RegExp(`^${id}(?:!=|isnot)0$`).test(compact) ||
      new RegExp(`^0!=${id}$`).test(compact)
    ) return false;
  }

  for (const [alias, trueMeansNew] of aliases) {
    const escaped = alias.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    if (new RegExp(`^${escaped}$`).test(compact)) return trueMeansNew;
    if (new RegExp(`^${escaped}(?:==|is)true$`).test(compact)) return trueMeansNew;
    if (new RegExp(`^${escaped}(?:!=)false$`).test(compact)) return trueMeansNew;
    if (new RegExp(`^${escaped}(?:==|is)false$`).test(compact)) return !trueMeansNew;
    if (new RegExp(`^${escaped}(?:!=)true$`).test(compact)) return !trueMeansNew;
  }
  return null;
}

function clickHandlerFor(text, operationPattern) {
  const names = [...text.matchAll(/@onclick\s*=\s*["']@?(?:\(\s*\)\s*=>\s*)?([A-Za-z_]\w*)/gi)]
    .map((match) => match[1]);
  for (const name of names) {
    const behavior = operationBehavior(text, name, operationPattern);
    if (operationPattern.test(behavior)) return { name, behavior };
  }
  return null;
}

function validateBlazorEntity(entity) {
  const plural = `${entity}s`;
  const groups = blazorComponentGroups(entity);
  const text = groups.map((group) => group.text).join("\n");
  fail(Boolean(text.trim()), `${entity} components are missing.`);
  if (!text.trim()) return;
  fail(/@page\s+["'][^"']+/i.test(text), `${entity} has no routable component surface.`);
  const listGroup = groups.find((group) =>
    new RegExp(`\\.${plural}\\b[\\s\\S]{0,600}(?:ToList|ToArray)Async\\s*\\(`).test(group.text) &&
    /(?:foreach|Virtualize|QuickGrid)\b/i.test(group.text));
  fail(
    Boolean(listGroup),
    `${entity} has no async list/read surface.`,
  );
  const detailsGroup = groups.find((group) =>
    any(
      group.text,
      /selected\w*\s*=\s*await[\s\S]{0,350}(?:Find|FirstOrDefault|SingleOrDefault)Async\s*\(/i,
      /@page\s+["'][^"']*\{[^}]*id[^}]*\}[^"']*["'][\s\S]{0,1200}(?:Find|FirstOrDefault|SingleOrDefault)Async\s*\(/i,
    ) && any(group.text, /details/i, /selected\w+/i));
  fail(
    Boolean(detailsGroup),
    `${entity} has no details/by-id behavior.`,
  );

  const formCandidates = blazorFormCandidates(groups);
  const createOperation = selectOperationCandidate(formCandidates, "create");
  const editOperation = selectOperationCandidate(formCandidates, "edit");
  const createGroup = createOperation?.group;
  const editGroup = editOperation?.group;
  const createForm = createOperation?.form ?? "";
  const editForm = editOperation?.form ?? "";

  fail(validatedEditForm(createForm), `${entity} create lacks an EditForm with DataAnnotations validation.`);
  fail(validatedEditForm(editForm), `${entity} edit lacks an EditForm with DataAnnotations validation.`);
  for (const [operation, group, form] of [
    ["create", createGroup, createForm],
    ["edit", editGroup, editForm],
  ]) {
    const source = group?.text ?? "";
    fail(
      safelyInitializedFormModel(form, source, entity),
      `${entity} ${operation} bound form model is not safely initialized.`,
    );
  }
  if (formCandidates.length > 1) {
    fail(
      Boolean(createOperation) && Boolean(editOperation) && createOperation.id !== editOperation.id,
      `${entity} create and edit must use distinct forms.`,
    );
    fail(
      Boolean(createOperation) && Boolean(editOperation) && createOperation.handler !== editOperation.handler,
      `${entity} create and edit forms must not share one submit handler.`,
    );
  } else if (formCandidates.length === 1) {
    fail(
      createOperation?.id === editOperation?.id && genuinelyBranchesBothWays(createOperation),
      `${entity} shared form handler must branch to distinct create and edit helpers using its bound model.`,
    );
  }
  fail(
    Boolean(createOperation?.analysis.create),
    `${entity} create does not add and save asynchronously.`,
  );
  fail(
    Boolean(editOperation?.analysis.edit),
    `${entity} edit does not update and save asynchronously.`,
  );

  const deletePattern = /\.Remove\s*\(/;
  const deleteGroup = findOperationGroup(groups, "Delete", (source) => deletePattern.test(source));
  const deleteHandler = deleteGroup ? clickHandlerFor(deleteGroup.text, deletePattern) : null;
  fail(
    Boolean(deleteHandler) &&
      deletePattern.test(deleteHandler.behavior) &&
      /SaveChangesAsync\s*\(/.test(deleteHandler.behavior),
    `${entity} delete does not remove and save asynchronously.`,
  );

  if (entity === "Employee") {
    fail(
      departmentSelector(createForm) && databaseLoadedDepartments(createForm, createGroup?.text ?? ""),
      "Employee create must bind DepartmentId to database-loaded choices.",
    );
    fail(
      departmentSelector(editForm) && databaseLoadedDepartments(editForm, editGroup?.text ?? ""),
      "Employee edit must bind DepartmentId to database-loaded choices.",
    );
  }
}

function validateBlazor() {
  checkContext("AppDbContext", ["Employee", "Department"]);
  fail(test(cs, /AddRazorComponents\s*\(\s*\)[\s\S]{0,180}\.AddInteractiveServerComponents\s*\(/, /MapRazorComponents\s*<[^>]+>\s*\(\s*\)[\s\S]{0,180}\.AddInteractiveServerRenderMode\s*\(/), "Interactive server registration/mapping chains were not preserved.");

  validateBlazorEntity("Employee");
  validateBlazorEntity("Department");
  const models = joined(/(?:^|\/)Models\/.*\.cs$/i);
  fail(
    /class\s+Employee\b[\s\S]*?\[(?:Required|Range)\b[\s\S]*?DepartmentId/.test(models) &&
      /class\s+Department\b[\s\S]*?\[Required\b/.test(models),
    "Employee and Department models lack DataAnnotations validation.",
  );
  const navigation = joined(/(?:Nav|Menu|Layout).*\.razor$/i);
  fail(
    /(?:href|NavigateTo\s*\()\s*=?\s*["'][^"']*employees/i.test(navigation) &&
      /(?:href|NavigateTo\s*\()\s*=?\s*["'][^"']*departments/i.test(navigation),
    "Blazor navigation does not reach Employees and Departments.",
  );
}

validateNoEnsureCreated();
validateNoStartupSeeding();

switch (contract) {
  case "razor-pages": validateRazorPages(); break;
  case "todo-api": validateTodoApi(); break;
  case "mvc": validateMvc(); break;
  case "parent-api": validateParentApi(); break;
  case "blazor": validateBlazor(); break;
}

if (issues.length > 0) {
  console.error(`FAIL ${contract}: ${issues.length} validation issue${issues.length === 1 ? "" : "s"}`);
  for (const issue of issues) console.error(` - ${issue}`);
  process.exit(1);
}

console.log(`PASS ${contract}: ${root}`);
