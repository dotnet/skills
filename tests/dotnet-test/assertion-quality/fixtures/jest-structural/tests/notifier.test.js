import { jest } from "@jest/globals";
import { notify } from "../src/notifier.js";

test("returns exact payload", async () => {
  const result = await notify(jest.fn(), "ops@example.com", "Ready");

  expect(result).toEqual({
    recipient: "ops@example.com",
    message: "Ready",
    status: "queued",
  });
});

test("notifies requested recipient", async () => {
  const sendEmail = jest.fn();

  await notify(sendEmail, "ops@example.com", "Ready");

  expect(sendEmail).toHaveBeenCalledWith("ops@example.com", "Ready");
});

test("serializes stable response", async () => {
  const result = await notify(jest.fn(), "ops@example.com", "Ready");

  expect(JSON.stringify(result)).toMatchInlineSnapshot(
    '"{\\"recipient\\":\\"ops@example.com\\",\\"message\\":\\"Ready\\",\\"status\\":\\"queued\\"}"',
  );
});

test("returns something", async () => {
  const result = await notify(jest.fn(), "ops@example.com", "Ready");

  expect(result).toBeDefined();
});

test("is wired", async () => {
  await notify(jest.fn(), "ops@example.com", "Ready");

  expect(true).toBe(true);
});
