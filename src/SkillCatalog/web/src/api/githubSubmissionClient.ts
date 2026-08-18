import type { Contribution, GitHubSession, SubmissionIntent } from './githubSubmissionModels'

async function read<T>(response: Response): Promise<T> {
  if (response.ok) return response.json() as Promise<T>
  let message = `Request failed (${response.status})`
  try {
    const body = await response.json()
    message = body.detail ?? body.message ?? body.title ?? message
  } catch {
    // The status code remains a useful fallback for non-JSON responses.
  }
  throw new Error(message)
}

function form(file: File) {
  const data = new FormData()
  data.append('file', file, file.name)
  return data
}

let csrfToken: string | undefined
async function securityHeaders(): Promise<Record<string, string>> {
  if (!csrfToken) {
    const response = await fetch('/api/auth/csrf', { credentials: 'include' })
    csrfToken = (await read<{ token: string }>(response)).token
  }
  return { 'X-CSRF-TOKEN': csrfToken }
}

export const githubSubmissionClient = {
  session: () => fetch('/api/auth/session', { credentials: 'include' }).then(read<GitHubSession>),
  start: () => fetch('/api/auth/github/start', {
    method: 'POST',
    credentials: 'include',
    headers: { Origin: window.location.origin },
  }).then(read<{ authorizationUrl: string; transactionId: string; expiresAt: string }>),
  logout: async () => fetch('/api/auth/session', {
    method: 'DELETE',
    credentials: 'include',
    headers: await securityHeaders(),
  }),
  createIntent: async (file: File) => fetch('/api/contributions/intents', {
    method: 'POST',
    credentials: 'include',
    headers: await securityHeaders(),
    body: form(file),
  }).then(read<SubmissionIntent>),
  submit: async (intentId: string, file: File, key: string) => fetch(`/api/contributions/intents/${intentId}/submit`, {
    method: 'POST',
    credentials: 'include',
    headers: { ...(await securityHeaders()), 'Idempotency-Key': key, 'X-Confirm-Update': 'true' },
    body: form(file),
  }).then(read<Contribution>),
  contribution: (id: string, refresh = false) => fetch(`/api/contributions/${id}${refresh ? '?refresh=true' : ''}`, { credentials: 'include' }).then(read<Contribution>),
}



