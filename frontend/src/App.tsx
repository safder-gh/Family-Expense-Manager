import { startTransition, useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import './App.css'

type Role = 'Head' | 'Dependent'
type AuthResponse = {
  token: string
  userId: string
  displayName: string
  email: string
  role: Role
  familyId: string
  familyName: string
}
type Member = {
  userId: string
  displayName: string
  email: string
  role: Role
  balanceMinor: number
}
type Family = {
  familyId: string
  familyName: string
  currentUserId: string
  currentRole: Role
  currentBalanceMinor: number
  members: Member[]
}
type LedgerEntry = {
  id: string
  type: 'TopUp' | 'Transfer' | 'Expense'
  amountMinor: number
  description: string
  createdAtUtc: string
  fromUserId: string | null
  toUserId: string | null
  fromDisplayName: string | null
  toDisplayName: string | null
  createdByDisplayName: string
  categoryName: string | null
}
type ExpenseCategory = { id: string; name: string }
type Invite = { code: string; expiresAtUtc: string }
type AuthMode = 'login' | 'create' | 'join'

async function api<T>(path: string, token?: string | null, init: RequestInit = {}): Promise<T> {
  path = path.startsWith('/') ? `http://localhost:5261${path}` :path  
  const headers = new Headers(init.headers)
  headers.set('Content-Type', 'application/json')
  if (token) headers.set('Authorization', `Bearer ${token}`)

  const response = await fetch(path, { ...init, headers })
  const body: unknown = await response.json().catch(() => null)
  if (!response.ok) {
    const record = body && typeof body === 'object' ? body as Record<string, unknown> : null
    const rawErrors = record?.errors
    const validationErrors = Array.isArray(rawErrors)
      ? rawErrors.filter((item): item is string => typeof item === 'string').join(' ')
      : rawErrors && typeof rawErrors === 'object'
        ? Object.values(rawErrors).flatMap((value) => Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : []).join(' ')
        : ''
    const detail = validationErrors || (typeof record?.message === 'string' ? record.message : '')
    throw new Error(detail || `Request failed (${response.status}).`)
  }
  return body as T
}

function formatMoney(minor: number) {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(minor / 100)
}

function App() {
  const [token, setToken] = useState<string | null>(() => sessionStorage.getItem('family-token'))
  const [mode, setMode] = useState<AuthMode>('login')
  const [todayLabel] = useState(() =>
    new Intl.DateTimeFormat(undefined, { weekday: 'long', month: 'long', day: 'numeric' })
      .format(new Date())
      .toUpperCase(),
  )
  const [family, setFamily] = useState<Family | null>(null)
  const [transactions, setTransactions] = useState<LedgerEntry[]>([])
  const [categories, setCategories] = useState<ExpenseCategory[]>([])
  const [invite, setInvite] = useState<Invite | null>(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)

  const refresh = useCallback(async () => {
    if (!token) return
    const [familyData, ledger, familyCategories] = await Promise.all([
      api<Family>('/api/family', token),
      api<LedgerEntry[]>('/api/transactions', token),
      api<ExpenseCategory[]>('/api/expense-categories', token),
    ])
    startTransition(() => {
      setFamily(familyData)
      setTransactions(ledger)
      setCategories(familyCategories)
    })
  }, [token])

  const reportLoadError = useCallback((reason: unknown) => {
    startTransition(() => {
      setError(reason instanceof Error ? reason.message : 'Could not load family data.')
    })
  }, [])

  useEffect(() => {
    if (!token) return
    void refresh().catch(reportLoadError)
  }, [refresh, reportLoadError, token])

  const dependents = useMemo(
    () => family?.members.filter((member) => member.role === 'Dependent') ?? [],
    [family],
  )

  function signOut() {
    sessionStorage.removeItem('family-token')
    setToken(null)
    setFamily(null)
    setTransactions([])
    setCategories([])
    setInvite(null)
    setError('')
    setNotice('')
  }

  async function submitAuth(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError('')
    setBusy(true)
    const values = Object.fromEntries(new FormData(event.currentTarget).entries())
    const endpoint = mode === 'login'
      ? '/api/auth/login'
      : mode === 'create'
        ? '/api/auth/register/head'
        : '/api/auth/register/dependent'
    const payload = mode === 'login'
      ? { email: values.email, password: values.password }
      : mode === 'create'
        ? {
            displayName: values.displayName,
            email: values.email,
            password: values.password,
            familyName: values.familyName,
          }
        : {
            displayName: values.displayName,
            email: values.email,
            password: values.password,
            inviteCode: values.inviteCode,
          }

    try {
      const result = await api<AuthResponse>(endpoint, null, {
        method: 'POST',
        body: JSON.stringify(payload),
      })
      sessionStorage.setItem('family-token', result.token)
      setToken(result.token)
      setNotice(`Welcome to ${result.familyName}.`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not sign in.')
    } finally {
      setBusy(false)
    }
  }

  async function submitTransaction(
    event: FormEvent<HTMLFormElement>,
    endpoint: string,
    extra: Record<string, string> = {},
    success = 'Transaction saved.',
  ) {
    event.preventDefault()
    if (!token) return
    const form = event.currentTarget
    setError('')
    setNotice('')
    setBusy(true)
    const values = Object.fromEntries(new FormData(event.currentTarget).entries())
    try {
      await api(endpoint, token, {
        method: 'POST',
        body: JSON.stringify({
          amount: Number(values.amount),
          description: values.description,
          ...(values.categoryId ? { categoryId: values.categoryId } : {}),
          ...extra,
          ...(values.recipientUserId ? { recipientUserId: values.recipientUserId } : {}),
        }),
      })
      form.reset()
      await refresh()
      setNotice(success)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not save this transaction.')
    } finally {
      setBusy(false)
    }
  }

  async function addCategory(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!token) return
    const form = event.currentTarget
    const values = Object.fromEntries(new FormData(form).entries())
    setError('')
    setNotice('')
    setBusy(true)
    try {
      await api('/api/expense-categories', token, {
        method: 'POST',
        body: JSON.stringify({ name: values.name }),
      })
      form.reset()
      await refresh()
      setNotice('Category created.')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not create the category.')
    } finally {
      setBusy(false)
    }
  }

  async function renameCategory(event: FormEvent<HTMLFormElement>, categoryId: string) {
    event.preventDefault()
    if (!token) return
    const form = event.currentTarget
    const values = Object.fromEntries(new FormData(form).entries())
    setError('')
    setNotice('')
    setBusy(true)
    try {
      await api(`/api/expense-categories/${categoryId}`, token, {
        method: 'PUT',
        body: JSON.stringify({ name: values.name }),
      })
      await refresh()
      setNotice('Category renamed.')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not rename the category.')
    } finally {
      setBusy(false)
    }
  }

  async function deleteCategory(categoryId: string) {
    if (!token) return
    setError('')
    setNotice('')
    setBusy(true)
    try {
      await api(`/api/expense-categories/${categoryId}`, token, { method: 'DELETE' })
      await refresh()
      setNotice('Category deleted.')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not delete the category.')
    } finally {
      setBusy(false)
    }
  }

  async function createInvite() {
    if (!token) return
    setError('')
    setNotice('')
    setBusy(true)
    try {
      const result = await api<Invite>('/api/family/invites', token, { method: 'POST' })
      setInvite(result)
      setNotice('Invite created. Share this code with one family member; it expires in 24 hours.')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not create an invite.')
    } finally {
      setBusy(false)
    }
  }

  if (!token) {
    return (
      <main className="auth-page">
        <section className="auth-intro">
          <a className="brand brand-light" href="/">
            <span className="brand-mark">F</span>
            <span>Famly</span>
          </a>
          <div className="intro-copy">
            <span className="eyebrow">MONEY, MADE A FAMILY MATTER</span>
            <h1>A little more<br />together.</h1>
            <p>Give everyone a clear view of shared money, without losing the personal touch.</p>
            <div className="intro-note">
              <span className="note-mark">+</span>
              <span>Set family goals, share funds, and keep every expense in view.</span>
            </div>
          </div>
          <span className="intro-footer">A calmer way to handle family finances.</span>
        </section>

        <section className="auth-panel">
          <div className="auth-card">
            <div className="mobile-brand">
              <a className="brand" href="/">
                <span className="brand-mark">F</span>
                <span>Famly</span>
              </a>
            </div>
            <span className="eyebrow">YOUR FAMILY WALLET</span>
            <h2>{mode === 'login' ? 'Welcome back' : mode === 'create' ? 'Start your family' : 'Join your family'}</h2>
            <p className="muted">
              {mode === 'login'
                ? 'Sign in to pick up where your family left off.'
                : mode === 'create'
                  ? 'Create a family space and invite your people.'
                  : 'Enter the invite code shared by your family head.'}
            </p>
            <div className="auth-tabs" role="tablist" aria-label="Account options">
              <button className={mode === 'login' ? 'active' : ''} onClick={() => setMode('login')} type="button">Sign in</button>
              <button className={mode === 'create' ? 'active' : ''} onClick={() => setMode('create')} type="button">Create family</button>
              <button className={mode === 'join' ? 'active' : ''} onClick={() => setMode('join')} type="button">Join</button>
            </div>
            <form className="form-stack" onSubmit={submitAuth}>
              {mode !== 'login' && (
                <label>
                  Your name
                  <input autoComplete="name" name="displayName" placeholder="e.g. Alex Morgan" required minLength={2} maxLength={80} />
                </label>
              )}
              {mode === 'create' && (
                <label>
                  Family name
                  <input name="familyName" placeholder="e.g. The Morgan family" required minLength={2} maxLength={100} />
                </label>
              )}
              {mode === 'join' && (
                <label>
                  Invite code
                  <input name="inviteCode" autoComplete="off" placeholder="Paste your one-time code" required maxLength={128} />
                </label>
              )}
              <label>
                Email address
                <input type="email" name="email" autoComplete="email" placeholder="you@example.com" required maxLength={256} />
              </label>
              <label>
                Password
                <input type="password" name="password" autoComplete={mode === 'login' ? 'current-password' : 'new-password'} placeholder="At least 10 characters" required minLength={mode === 'login' ? 1 : 10} maxLength={100} />
              </label>
              {mode !== 'login' && <p className="field-hint">Use uppercase and lowercase letters, a number, and a symbol.</p>}
              {error && <p className="message error" role="alert">{error}</p>}
              <button className="button button-primary full-width" disabled={busy} type="submit">
                {busy ? 'Please wait…' : mode === 'login' ? 'Sign in' : mode === 'create' ? 'Create family account' : 'Join family'}
                <span aria-hidden="true">→</span>
              </button>
            </form>
            <p className="auth-footnote">Your wallet starts at zero. Your family head can record funds added externally.</p>
          </div>
        </section>
      </main>
    )
  }

  if (!family) {
    return (
      <main className="loading-screen">
        <div className="spinner" />
        <p>{error || 'Loading your family wallet…'}</p>
        {error && <button className="button button-secondary" onClick={signOut}>Sign out</button>}
      </main>
    )
  }

  const currentUser = family.members.find((member) => member.userId === family.currentUserId)

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <a className="brand" href="/">
          <span className="brand-mark">F</span>
          <span>Famly</span>
        </a>
        <div className="family-switcher">
          <span className="family-avatar">{family.familyName.slice(0, 1).toUpperCase()}</span>
          <span><strong>{family.familyName}</strong><small>Family space</small></span>
          <span className="switcher-caret">⌄</span>
        </div>
        <nav className="side-nav" aria-label="Main navigation">
          <span className="nav-label">YOUR SPACE</span>
          <a className="nav-item active" href="#overview"><span className="nav-icon">⌂</span>Overview</a>
          <a className="nav-item" href="#activity"><span className="nav-icon">↗</span>Activity</a>
          <span className="nav-label nav-label-spaced">FAMILY</span>
          <a className="nav-item" href="#members"><span className="nav-icon">♧</span>People</a>
          {family.currentRole === 'Head' && <button className="nav-item nav-button" onClick={() => void createInvite()} disabled={busy}><span className="nav-icon">＋</span>Invite someone</button>}
        </nav>
        <div className="sidebar-bottom">
          <div className="help-card">
            <span className="help-icon">?</span>
            <strong>Need a hand?</strong>
            <p>Your family money, made a little simpler.</p>
          </div>
          <button className="profile-button" onClick={signOut}>
            <span className="profile-avatar">{(currentUser?.displayName ?? 'F').slice(0, 1).toUpperCase()}</span>
            <span><strong>{currentUser?.displayName ?? 'Family member'}</strong><small>{family.currentRole === 'Head' ? 'Family head' : 'Family member'}</small></span>
            <span className="profile-menu">···</span>
          </button>
        </div>
      </aside>

      <main className="main-content" id="overview">
        <header className="topbar">
          <div className="breadcrumb">Family space <span>/</span> <strong>Overview</strong></div>
          <div className="topbar-right">
            <span className="secure-label"><span className="secure-dot" />Private family space</span>
            <button className="profile-avatar top-avatar" aria-label="Sign out" onClick={signOut}>{(currentUser?.displayName ?? 'F').slice(0, 1).toUpperCase()}</button>
          </div>
        </header>

        <div className="page-content">
          <section className="welcome-row">
            <div>
              <span className="eyebrow">{todayLabel}</span>
              <h1>Good to see you, {currentUser?.displayName.split(' ')[0] ?? 'there'} <span className="wave">✳</span></h1>
              <p>Here’s how your family’s money is looking today.</p>
            </div>
            <button className="button button-secondary refresh-button" onClick={() => void refresh().catch((reason: unknown) => setError(reason instanceof Error ? reason.message : 'Refresh failed.'))}>↻ <span>Refresh</span></button>
          </section>

          {(error || notice) && (
            <div className={`message banner ${error ? 'error' : 'success'}`} role={error ? 'alert' : 'status'}>
              <span>{error || notice}</span>
              <button onClick={() => { setError(''); setNotice('') }} aria-label="Dismiss message">×</button>
            </div>
          )}

          <section className="summary-grid" aria-label="Family balance summary">
            <article className="balance-card">
              <div className="balance-topline"><span>YOUR AVAILABLE BALANCE</span><span className="wallet-icon">◈</span></div>
              <strong className="balance-amount">{formatMoney(family.currentBalanceMinor)}</strong>
              <div className="balance-bottom"><span>Ready for everyday spending</span><span className="balance-pill">● Available</span></div>
            </article>
            <article className="stat-card">
              <span className="stat-icon mint">♧</span>
              <span className="stat-label">FAMILY MEMBERS</span>
              <strong>{family.members.length}</strong>
              <span className="stat-caption">{family.members.length === 1 ? 'Growing together' : 'Sharing the journey'}</span>
            </article>
            <article className="stat-card">
              <span className="stat-icon lilac">↗</span>
              <span className="stat-label">RECENT ACTIVITY</span>
              <strong>{transactions.length}</strong>
              <span className="stat-caption">Latest family transactions</span>
            </article>
          </section>

          {invite && (
            <section className="invite-banner">
              <div><strong>One-time family invite</strong><p>Share this code privately. It can be used once and expires {new Date(invite.expiresAtUtc).toLocaleString()}.</p></div>
              <code>{invite.code}</code>
              <button className="button button-secondary" onClick={() => void navigator.clipboard.writeText(invite.code).then(() => setNotice('Invite code copied.'))}>Copy code</button>
            </section>
          )}

          <section className="workspace-grid">
            <div className="left-column">
              <div className="section-heading">
                <div><span className="eyebrow">TAKE ACTION</span><h2>Make a move</h2></div>
                <span className="section-caption">Money stays in the family</span>
              </div>
              <div className="action-grid">
                {family.currentRole === 'Head' && (
                  <form className="action-card" onSubmit={(event) => void submitTransaction(event, '/api/wallet/top-ups', {}, 'Top-up recorded.')}>
                    <span className="action-icon green">＋</span>
                    <h3>Add family funds</h3>
                    <p>Record money added from outside this app.</p>
                    <label>Amount<input name="amount" type="number" min="0.01" step="0.01" placeholder="0.00" required /></label>
                    <label>Note <span className="optional">(optional)</span><input name="description" maxLength={160} placeholder="e.g. Monthly allowance" /></label>
                    <button className="button button-primary full-width" disabled={busy}>Record top-up <span>→</span></button>
                  </form>
                )}
                {family.currentRole === 'Head' && dependents.length > 0 && (
                  <form className="action-card" onSubmit={(event) => void submitTransaction(event, '/api/wallet/transfers', {}, 'Money transferred.')}>
                    <span className="action-icon violet">↗</span>
                    <h3>Send to family</h3>
                    <p>Move money from your wallet to a dependent.</p>
                    <label>Send to
                      <select name="recipientUserId" required defaultValue="">
                        <option value="" disabled>Choose a family member</option>
                        {dependents.map((member) => <option key={member.userId} value={member.userId}>{member.displayName}</option>)}
                      </select>
                    </label>
                    <label>Amount<input name="amount" type="number" min="0.01" step="0.01" placeholder="0.00" required /></label>
                    <label>Note <span className="optional">(optional)</span><input name="description" maxLength={160} placeholder="e.g. School lunch" /></label>
                    <button className="button button-primary full-width" disabled={busy}>Send money <span>→</span></button>
                  </form>
                )}
                <form className="action-card" onSubmit={(event) => void submitTransaction(event, '/api/expenses', {}, 'Expense recorded.')}>
                  <span className="action-icon coral">−</span>
                  <h3>Log an expense</h3>
                  <p>Keep track of spending from your wallet.</p>
                  <label>Amount<input name="amount" type="number" min="0.01" step="0.01" placeholder="0.00" required /></label>
                  <label>Category
                    <select name="categoryId" required defaultValue="">
                      <option value="" disabled>Choose a category</option>
                      {categories.map((category) => <option key={category.id} value={category.id}>{category.name}</option>)}
                    </select>
                  </label>
                  <label>What was it for?<input name="description" maxLength={160} placeholder="e.g. Groceries" /></label>
                  <button className="button button-primary full-width" disabled={busy}>Add expense <span>→</span></button>
                </form>
              </div>
              <section className="category-manager" aria-labelledby="category-heading">
                <div>
                  <span className="eyebrow">FAMILY SETTINGS</span>
                  <h3 id="category-heading">Expense categories</h3>
                  <p>Manage the categories everyone in your family can use.</p>
                </div>
                <form className="category-create" onSubmit={(event) => void addCategory(event)}>
                  <label className="visually-hidden" htmlFor="new-category">New category name</label>
                  <input id="new-category" name="name" placeholder="e.g. Education" maxLength={60} required />
                  <button className="button button-secondary" disabled={busy}>Add</button>
                </form>
                <div className="category-list">
                  {categories.map((category) => (
                    <form className="category-item" key={`${category.id}-${category.name}`} onSubmit={(event) => void renameCategory(event, category.id)}>
                      <label className="visually-hidden" htmlFor={`category-${category.id}`}>Rename {category.name}</label>
                      <input id={`category-${category.id}`} name="name" defaultValue={category.name} maxLength={60} required />
                      <button className="button button-secondary" disabled={busy}>Save</button>
                      <button className="category-delete" type="button" disabled={busy} onClick={() => void deleteCategory(category.id)}>Delete</button>
                    </form>
                  ))}
                </div>
                <span className="field-hint">Categories attached to expenses cannot be deleted.</span>
              </section>
              {family.currentRole === 'Head' && dependents.length === 0 && (
                <div className="empty-members"><span>♧</span><strong>Bring your family in</strong><p>Create a secure, one-time invite so a dependent can join and receive money.</p><button className="button button-secondary" onClick={() => void createInvite()} disabled={busy}>Create an invite</button></div>
              )}

              <section className="activity-section" id="activity">
                <div className="section-heading">
                  <div><span className="eyebrow">THE PAPER TRAIL</span><h2>Recent activity</h2></div>
                  <span className="section-caption">Last 100 transactions</span>
                </div>
                {transactions.length === 0 ? (
                  <div className="empty-state"><span className="empty-icon">↗</span><strong>Your story starts here</strong><p>Family top-ups, transfers, and expenses will appear here.</p></div>
                ) : (
                  <div className="activity-list">
                    {transactions.map((entry) => {
                      const incoming = entry.toUserId === family.currentUserId
                      const outgoing = entry.fromUserId === family.currentUserId
                      const isExpense = entry.type === 'Expense' && outgoing
                      return (
                        <article className="activity-row" key={entry.id}>
                          <span className={`activity-icon ${entry.type.toLowerCase()}`}>{entry.type === 'TopUp' ? '＋' : entry.type === 'Transfer' ? '↗' : '−'}</span>
                          <div className="activity-description">
                            <strong>{entry.description}</strong>
                            <span>{entry.type === 'TopUp' ? 'Family funds added' : entry.type === 'Transfer' ? `${entry.fromDisplayName ?? 'Family'} → ${entry.toDisplayName ?? 'Family'}` : `${entry.createdByDisplayName} · Expense${entry.categoryName ? ` · ${entry.categoryName}` : ''}`}</span>
                          </div>
                          <time dateTime={entry.createdAtUtc}>{new Date(entry.createdAtUtc).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}</time>
                          <strong className={`activity-amount ${isExpense || outgoing ? 'negative' : incoming ? 'positive' : 'neutral'}`}>
                            {isExpense || outgoing ? '−' : incoming ? '+' : ''}{formatMoney(entry.amountMinor)}
                          </strong>
                        </article>
                      )
                    })}
                  </div>
                )}
              </section>
            </div>

            <aside className="right-column" id="members">
              <div className="section-heading">
                <div><span className="eyebrow">YOUR PEOPLE</span><h2>Family members</h2></div>
                {family.currentRole === 'Head' && <button className="icon-button" aria-label="Create invite" onClick={() => void createInvite()}>＋</button>}
              </div>
              <div className="member-list">
                {family.members.map((member, index) => (
                  <article className="member-row" key={member.userId}>
                    <span className={`member-avatar avatar-${index % 4}`}>{member.displayName.slice(0, 1).toUpperCase()}</span>
                    <span className="member-info"><strong>{member.displayName}{member.userId === family.currentUserId ? ' (you)' : ''}</strong><small>{member.role === 'Head' ? 'Family head' : 'Dependent'}</small></span>
                    <span className="member-balance">{formatMoney(member.balanceMinor)}</span>
                  </article>
                ))}
              </div>
              <div className="family-note">
                <span className="note-mark">i</span>
                <p>Everyone has their own wallet. Only your family head can add funds or send money to dependents.</p>
              </div>
            </aside>
          </section>

          <footer className="page-footer"><span>Made for the people who matter.</span><span>Private by design <span className="footer-dot">●</span></span></footer>
        </div>
      </main>
    </div>
  )
}

export default App
