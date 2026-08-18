import { Button, MessageBar, MessageBarBody, Spinner, Text } from '@fluentui/react-components'
import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import type { Contribution, GitHubSession, SubmissionIntent } from '../../api/githubSubmissionModels'
import { githubSubmissionClient } from '../../api/githubSubmissionClient'
import { GitHubSignInPanel } from './components/GitHubSignInPanel'
import { SubmissionReview } from './components/SubmissionReview'

export function GitHubSubmissionPage({ file }: { file: File }) {
  const [session, setSession] = useState<GitHubSession>()
  const [intent, setIntent] = useState<SubmissionIntent>()
  const [contribution, setContribution] = useState<Contribution>()
  const [confirmed, setConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const popup = useRef<Window | null>(null)
  const popupWatcher = useRef<number | undefined>(undefined)

  const stopWatchingPopup = () => {
    if (popupWatcher.current !== undefined) window.clearInterval(popupWatcher.current)
    popupWatcher.current = undefined
    popup.current = null
  }

  const refresh = async () => {
    try {
      setSession(await githubSubmissionClient.session())
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : String(reason))
    }
  }

  useEffect(() => {
    void refresh()
    const receive = (event: MessageEvent) => {
      if (event.origin !== window.location.origin || event.data?.type !== 'skillcatalog:github-auth-complete') return
      stopWatchingPopup()
      void refresh()
    }
    window.addEventListener('message', receive)
    return () => {
      window.removeEventListener('message', receive)
      stopWatchingPopup()
    }
  }, [])

  const signIn = async () => {
    setBusy(true)
    setError('')
    try {
      const start = await githubSubmissionClient.start()
      popup.current = window.open(start.authorizationUrl, 'skillcatalog-github-auth', 'popup,width=720,height=760')
      if (!popup.current) throw new Error('GitHub sign-in popup was blocked. Allow popups and try again.')
      popupWatcher.current = window.setInterval(() => {
        if (!popup.current?.closed) return
        stopWatchingPopup()
        setError('GitHub sign-in was closed before it completed. Your uploaded package is still ready; try again when convenient.')
      }, 500)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Sign-in could not start.')
    } finally {
      setBusy(false)
    }
  }

  const prepare = async () => {
    setBusy(true)
    setError('')
    try {
      setIntent(await githubSubmissionClient.createIntent(file))
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Review could not be prepared.')
    } finally {
      setBusy(false)
    }
  }

  const submit = async () => {
    if (!intent) return
    setBusy(true)
    setError('')
    try {
      setContribution(await githubSubmissionClient.submit(intent.id, file, crypto.randomUUID()))
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Contribution could not be created.')
    } finally {
      setBusy(false)
    }
  }

  return <section className="github-submission" aria-labelledby="github-submission-title">
    <h2 id="github-submission-title">Contribute through GitHub</h2>
    <GitHubSignInPanel
      session={session}
      busy={busy}
      error={error}
      onSignIn={signIn}
      onLogout={async () => {
        await githubSubmissionClient.logout()
        setSession({ authenticated: false })
      }}
    />
    {session?.authenticated && !intent && !contribution && <Button onClick={prepare} disabled={busy}>Prepare pull request</Button>}
    {intent && !contribution && <SubmissionReview intent={intent} confirmed={confirmed} busy={busy} onConfirmed={setConfirmed} onSubmit={submit} />}
    {busy && <Spinner label="Working with GitHub" />}
    {contribution && <MessageBar intent="success"><MessageBarBody>
      Pull request created. <a href={contribution.pullRequestUrl} target="_blank" rel="noreferrer">Open pull request</a>. Current state: {contribution.state}. <Link to={`/contributions/${contribution.id}`}>View contribution progress</Link>.
    </MessageBarBody></MessageBar>}
    <Text block size={200}>No uploaded bytes are retained after each request.</Text>
  </section>
}

