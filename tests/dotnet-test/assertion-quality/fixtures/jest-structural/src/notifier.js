export async function notify(sendEmail, recipient, message) {
  const payload = {
    recipient,
    message,
    status: "queued",
  };

  await sendEmail(recipient, message);
  return payload;
}
