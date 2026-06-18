import { useEffect, useState } from "react";
import { Link, NavLink, Navigate, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import {
  deleteManagedAiCredential,
  fetchAccountSummary,
  fetchAdminOverview,
  fetchDevices,
  fetchDownloadEntitlement,
  fetchManagedAiAdminInventory,
  fetchSupportOverview,
  fetchWalletHistory,
  loginAccount,
  logoutAccount,
  registerAccount,
  resendVerificationEmail,
  triggerManagedAiCatalogRefresh,
  upsertManagedAiCredential
} from "./lib/api";

const USER_SESSION_KEY = "phantom.website.user-session";
const ADMIN_SESSION_KEY = "phantom.website.admin-session";

const marketingNav = [
  { to: "/", label: "Product" },
  { to: "/pricing", label: "Pricing" },
  { to: "/download", label: "Download" },
  { to: "/login", label: "Login" }
];

const userNav = [
  { to: "/dashboard", label: "Overview" },
  { to: "/dashboard/wallet", label: "Wallet" },
  { to: "/dashboard/devices", label: "Devices" },
  { to: "/dashboard/history", label: "Usage" },
  { to: "/dashboard/support", label: "Support" }
];

const adminNav = [
  { to: "/admin", label: "Overview" },
  { to: "/admin/managed-ai", label: "Managed AI" }
];

const plans = [
  {
    name: "Free Trial",
    price: "$0",
    ribbon: "Managed Demo",
    description: "Let candidates feel the real product before they commit to credits.",
    bullets: [
      "Hosted AI with provider + model choice",
      "2 managed 15-minute demo blocks",
      "No BYO key setup",
      "Email verification required before access"
    ],
    cta: "Start Free",
    to: "/register",
    tone: "mist"
  },
  {
    name: "Pro BYO",
    price: "Credit Packs",
    ribbon: "Power Users",
    description: "The full desktop workflow for users who want provider flexibility and their own AI spend.",
    bullets: [
      "Choose any 3 supported providers",
      "Store up to 2 keys per provider",
      "Desktop usage credit wallet",
      "Offline resume and lock safeguards"
    ],
    cta: "See Pro Workflow",
    to: "/download",
    tone: "current"
  },
  {
    name: "Premium AI",
    price: "Credits + Managed AI",
    ribbon: "Hands-Off",
    description: "Hosted model operations, managed keys, and support visibility for users who want zero key management.",
    bullets: [
      "Managed ChatGPT, Claude, Gemini, Mistral, Groq, and NVIDIA lanes",
      "Provider + model choice without API keys",
      "Premium wallet with continuation support",
      "Admin-controlled failover and rotation"
    ],
    cta: "Unlock Premium",
    to: "/download",
    tone: "brass"
  }
];

const marketingHighlights = [
  {
    title: "Protected interview continuity",
    body: "Phantom keeps the session alive through shaky networks, local restarts, and provider turbulence without letting account authority drift."
  },
  {
    title: "Hosted or BYO AI lanes",
    body: "Free and Premium run on Phantom-managed providers. Pro BYO keeps the user in control of provider choice and key spend."
  },
  {
    title: "Desktop-first control",
    body: "The Windows runtime is where the interview happens. The website exists to register, verify, inspect balances, manage devices, and download builds."
  }
];

export default function App() {
  const [userSession, setUserSession] = useState(() => readStoredJson(USER_SESSION_KEY));
  const [adminSession, setAdminSession] = useState(() => readStoredJson(ADMIN_SESSION_KEY));

  function handleUserAuthenticated(session) {
    writeStoredJson(USER_SESSION_KEY, session);
    setUserSession(session);
  }

  function handleAdminAuthenticated(session) {
    writeStoredJson(ADMIN_SESSION_KEY, session);
    setAdminSession(session);
  }

  async function handleUserLogout() {
    const refreshToken = userSession?.refreshToken;
    clearStoredJson(USER_SESSION_KEY);
    setUserSession(null);

    if (refreshToken) {
      try {
        await logoutAccount(refreshToken);
      } catch {
        // Best effort logout.
      }
    }
  }

  function handleAdminLogout() {
    clearStoredJson(ADMIN_SESSION_KEY);
    setAdminSession(null);
  }

  return (
    <div className="app-shell">
      <SiteChrome
        userSession={userSession}
        adminSession={adminSession}
        onUserLogout={handleUserLogout}
        onAdminLogout={handleAdminLogout}
      />
      <Routes>
        <Route path="/" element={<LandingPage userSession={userSession} />} />
        <Route path="/pricing" element={<PricingPage />} />
        <Route path="/download" element={<DownloadPage userSession={userSession} />} />
        <Route
          path="/login"
          element={<UserLoginPage onAuthenticated={handleUserAuthenticated} userSession={userSession} />}
        />
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/desktop-return" element={<DesktopReturnPage />} />
        <Route
          path="/dashboard/*"
          element={
            userSession ? (
              <UserDashboardPage session={userSession} />
            ) : (
              <Navigate to="/login" replace />
            )
          }
        />
        <Route
          path="/admin/login"
          element={<AdminLoginPage onAuthenticated={handleAdminAuthenticated} adminSession={adminSession} />}
        />
        <Route
          path="/admin/*"
          element={
            adminSession ? (
              <AdminDashboardPage adminSession={adminSession} />
            ) : (
              <Navigate to="/admin/login" replace />
            )
          }
        />
      </Routes>
    </div>
  );
}

function SiteChrome({ userSession, adminSession, onUserLogout, onAdminLogout }) {
  const location = useLocation();
  const isUserArea = location.pathname.startsWith("/dashboard");
  const isAdminArea = location.pathname.startsWith("/admin");
  const navigation = isAdminArea ? adminNav : isUserArea ? userNav : marketingNav;

  const brandTarget = isAdminArea
    ? "/admin"
    : userSession
      ? "/dashboard"
      : "/";

  return (
    <header className={`site-header ${isUserArea || isAdminArea ? "site-header-compact" : ""}`}>
      <Link className="brandmark" to={brandTarget}>
        <span className="brandmark-glyph">P</span>
        <span>
          <strong>Phantom</strong>
          <small>{isAdminArea ? "Admin Control Plane" : "Protected Interview Runtime"}</small>
        </span>
      </Link>

      <nav className="top-nav">
        {navigation.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === "/dashboard" || item.to === "/admin" || item.to === "/"}
            className={({ isActive }) => `nav-chip ${isActive ? "nav-chip-active" : ""}`}
          >
            {item.label}
          </NavLink>
        ))}
      </nav>

      <div className="header-actions">
        {isAdminArea ? (
          <>
            <span className="header-badge header-badge-brass">Admin Session</span>
            <button className="button button-secondary button-compact" onClick={onAdminLogout}>
              Log Out
            </button>
          </>
        ) : userSession ? (
          <>
            <span className="header-badge">{userSession.email}</span>
            {!isUserArea && (
              <Link className="button button-secondary button-compact" to="/dashboard">
                Open Dashboard
              </Link>
            )}
            <button className="button button-secondary button-compact" onClick={onUserLogout}>
              Log Out
            </button>
          </>
        ) : (
          <>
            {!isUserArea && !isAdminArea && (
              <Link className="button button-secondary button-compact" to="/register">
                Register
              </Link>
            )}
            {!isAdminArea && (
              <Link className="button button-primary button-compact" to="/login">
                Sign In
              </Link>
            )}
          </>
        )}
      </div>
    </header>
  );
}

function LandingPage({ userSession }) {
  return (
    <main className="page">
      <section className="hero hero-marketing">
        <div className="hero-copy panel panel-hero">
          <p className="eyebrow">Windows interview copilot with runtime discipline</p>
          <h1>
            Walk into the interview
            <span> with a guarded AI system, not a toy overlay.</span>
          </h1>
          <p className="hero-text">
            Phantom is built for real interview pressure. It keeps the session device-bound,
            meter-aware, resumable, and operational even when providers fail or connectivity drops.
          </p>
          <div className="hero-actions">
            <Link className="button button-primary" to={userSession ? "/dashboard" : "/register"}>
              {userSession ? "Open Dashboard" : "Create Your Account"}
            </Link>
            <Link className="button button-secondary" to="/pricing">
              Compare Plans
            </Link>
          </div>
          <div className="hero-proof">
            <div>
              <strong>4</strong>
              <span>Managed providers in Premium</span>
            </div>
            <div>
              <strong>24h</strong>
              <span>Offline launch lease window</span>
            </div>
            <div>
              <strong>15m</strong>
              <span>Billing block precision</span>
            </div>
          </div>
        </div>

        <div className="hero-stage">
          <div className="signal-orbit signal-orbit-a" />
          <div className="signal-orbit signal-orbit-b" />
          <div className="signal-core">
            <span>device trust</span>
            <span>session lock</span>
            <span>credit continuity</span>
          </div>
        </div>
      </section>

      <section className="stat-ribbon">
        <article className="stat-card">
          <strong>Stealth-safe</strong>
          <span>Protected Windows runtime designed for live interviews</span>
        </article>
        <article className="stat-card">
          <strong>Managed + BYO</strong>
          <span>Premium hosted AI and Pro bring-your-own-provider in one product</span>
        </article>
        <article className="stat-card">
          <strong>Recovery-first</strong>
          <span>Active sessions continue when networks and provider lanes become unreliable</span>
        </article>
        <article className="stat-card">
          <strong>Auditable</strong>
          <span>Wallet, device, and lease state stay visible from the hosted dashboard</span>
        </article>
      </section>

      <section className="story-grid">
        <article className="panel story-card story-card-large">
          <p className="eyebrow">What makes Phantom different</p>
          <h2>It is engineered like interview infrastructure, not a generic AI wrapper.</h2>
          <p>
            Most tools stop at “chat with a model.” Phantom handles lock authority, offline continuity,
            provider separation, tiered entitlement, and post-session reconciliation so the app stays useful
            when it matters.
          </p>
        </article>
        {marketingHighlights.map((item) => (
          <article className="panel story-card" key={item.title}>
            <p className="story-tag">Capability</p>
            <h3>{item.title}</h3>
            <p>{item.body}</p>
          </article>
        ))}
      </section>

      <section className="panel marketing-band">
        <div>
          <p className="eyebrow">Built for real user paths</p>
          <h2>Register on the web. Authenticate in the app. Manage everything from the dashboard.</h2>
        </div>
        <div className="marketing-band-grid">
          <article>
            <strong>1</strong>
            <p>Create or verify the hosted account</p>
          </article>
          <article>
            <strong>2</strong>
            <p>Download the Windows desktop runtime</p>
          </article>
          <article>
            <strong>3</strong>
            <p>Use Free, Pro BYO, or Premium depending the operating mode you want</p>
          </article>
        </div>
      </section>
    </main>
  );
}

function PricingPage() {
  return (
    <main className="page">
      <section className="section-heading">
        <p className="eyebrow">Pricing architecture</p>
        <h1>Choose the AI operating model, not just a monthly tier.</h1>
        <p className="section-copy">
          Phantom pricing maps directly to how the interview runtime is financed and controlled.
          Free and Premium use Phantom-managed AI. Pro BYO keeps the model spend in the user’s own provider accounts.
        </p>
      </section>

      <section className="pricing-grid pricing-grid-refined">
        {plans.map((plan) => (
          <article className={`plan-card plan-card-${plan.tone}`} key={plan.name}>
            <div className="plan-card-top">
              <p>{plan.ribbon}</p>
              <strong>{plan.price}</strong>
            </div>
            <div className="plan-card-copy">
              <h2>{plan.name}</h2>
              <p>{plan.description}</p>
            </div>
            <ul>
              {plan.bullets.map((bullet) => (
                <li key={bullet}>{bullet}</li>
              ))}
            </ul>
            <Link className="button button-secondary" to={plan.to}>
              {plan.cta}
            </Link>
          </article>
        ))}
      </section>
    </main>
  );
}

function DownloadPage({ userSession }) {
  const [entitlement, setEntitlement] = useState(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;

    async function load() {
      if (!userSession?.userId) {
        return;
      }

      try {
        const result = await fetchDownloadEntitlement(userSession.userId);
        if (!cancelled) {
          setEntitlement(result);
          setError("");
        }
      } catch (loadError) {
        if (!cancelled) {
          setError(loadError.message);
        }
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [userSession]);

  return (
    <main className="page download-layout">
      <section className="panel panel-hero">
        <p className="eyebrow">Download center</p>
        <h1>Install the Windows runtime that actually runs the interview.</h1>
        <p className="hero-text">
          The website is not the interview tool. This page explains how access works, what the installer
          delivers, and how your hosted account state gates the desktop runtime before the app becomes interactive.
        </p>
        <div className="download-meta">
          <div>
            <span>Installer type</span>
            <strong>Manual downloadable package</strong>
          </div>
          <div>
            <span>Runtime</span>
            <strong>.NET 8 + WebView2</strong>
          </div>
          <div>
            <span>Startup guard</span>
            <strong>Hosted account-check screen</strong>
          </div>
        </div>
        {userSession ? (
          <div className="hero-actions">
            <Link className="button button-primary" to="/dashboard">
              Open Your Dashboard
            </Link>
            <span className="download-status">
              {entitlement?.releaseChannel || "Resolve your entitlement from the dashboard"}
            </span>
          </div>
        ) : (
          <div className="hero-actions">
            <Link className="button button-primary" to="/login">
              Sign In To Unlock Download
            </Link>
            <Link className="button button-secondary" to="/register">
              Create Account First
            </Link>
          </div>
        )}
        {error && <p className="status-message status-error">{error}</p>}
      </section>

      <section className="panel detail-stack">
        <div>
          <p className="story-tag">What this section does</p>
          <h3>It tells the user whether the desktop build is available and what happens after install.</h3>
        </div>
        <ul className="detail-list">
          <li>Confirms whether the account can download the installer</li>
          <li>Explains the hosted login and verification requirement on first launch</li>
          <li>Clarifies that interview usage happens inside the Windows app, not inside the browser</li>
          <li>Directs the user back to the dashboard for device, wallet, and support state</li>
        </ul>
        {userSession ? (
          <div className="download-cta-stack">
            <div className="download-status-card">
              <span>Current entitlement</span>
              <strong>{entitlement?.installerLabel || "Installer unlock pending"}</strong>
              <p>
                {entitlement?.installerVersion || "No installer version resolved"} ·{" "}
                {entitlement?.releaseChannel || "Dashboard review required"}
              </p>
            </div>
            <Link className="button button-secondary" to="/dashboard/devices">
              Review Device Access
            </Link>
          </div>
        ) : (
          <div className="download-status-card">
            <span>Account required</span>
            <strong>Sign in before install</strong>
            <p>The hosted account state decides whether the desktop runtime can proceed past account check.</p>
          </div>
        )}
      </section>
    </main>
  );
}

function UserLoginPage({ onAuthenticated, userSession }) {
  const navigate = useNavigate();
  const [form, setForm] = useState({
    email: "",
    password: ""
  });
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (userSession?.isAuthenticated) {
      navigate("/dashboard", { replace: true });
    }
  }, [navigate, userSession]);

  function update(field, value) {
    setForm((current) => ({ ...current, [field]: value }));
  }

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setStatus("");

    try {
      const session = await loginAccount({
        email: form.email,
        password: form.password
      });
      onAuthenticated(session);
      navigate("/dashboard", { replace: true });
    } catch (error) {
      setStatus(error.message || "Sign-in failed.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="page auth-layout auth-layout-wide">
      <section className="panel auth-panel">
        <p className="eyebrow">User sign-in</p>
        <h1>Open the user dashboard without breaking the desktop auth model.</h1>
        <p className="hero-text">
          This page validates the same account against the Windows backend, then keeps a browser-only
          dashboard session so the user can inspect balances, devices, download state, and support data.
        </p>
        <div className="auth-summary-list">
          <div>
            <strong>Password login</strong>
            <span>Validates against the hosted backend</span>
          </div>
        </div>
      </section>

      <form className="panel auth-form" onSubmit={handleSubmit}>
        <label>
          <span>Email</span>
          <input value={form.email} onChange={(event) => update("email", event.target.value)} />
        </label>

        <label>
          <span>Password</span>
          <input
            type="password"
            value={form.password}
            onChange={(event) => update("password", event.target.value)}
          />
        </label>

        <button className="button button-primary" type="submit" disabled={submitting}>
          {submitting ? "Working..." : "Open User Dashboard"}
        </button>

        {status && <p className={`status-message ${status.includes("failed") ? "status-error" : ""}`}>{status}</p>}

        <div className="auth-links-row">
          <Link className="subtle-link" to="/register">
            Need an account?
          </Link>
          <Link className="subtle-link" to="/admin/login">
            Admin console
          </Link>
        </div>
      </form>
    </main>
  );
}

function RegisterPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const registerParams = new URLSearchParams(location.search);
  const [form, setForm] = useState({ email: "", password: "" });
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);

  function update(field, value) {
    setForm((current) => ({ ...current, [field]: value }));
  }

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setStatus("");

    try {
      const result = await registerAccount({
        email: form.email,
        password: form.password,
        appVersion: registerParams.get("appVersion") || "",
        installId: registerParams.get("installId") || "",
        deviceLabel: registerParams.get("deviceLabel") || "",
        deviceFingerprintHash: registerParams.get("deviceFingerprint") || "",
        secretFingerprintHint: registerParams.get("deviceHint") || ""
      });

      if (result.deliveryStatus !== "sent") {
        throw new Error(result.deliveryError || "Verification email could not be delivered.");
      }

      navigate(`/desktop-return?verification=pending&email=${encodeURIComponent(result.email)}`, {
        replace: true
      });
    } catch (error) {
      setStatus(error.message || "Registration failed.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="page auth-layout auth-layout-wide">
      <section className="panel auth-panel">
        <p className="eyebrow">Create account</p>
        <h1>Register on the website, then return to the desktop runtime.</h1>
        <p className="hero-text">
          Registration creates the hosted identity, sends the verification email, and prepares the account
          for login from the Windows app or the browser dashboard.
        </p>
      </section>

      <form className="panel auth-form" onSubmit={handleSubmit}>
        <label>
          <span>Email</span>
          <input value={form.email} onChange={(event) => update("email", event.target.value)} />
        </label>
        <label>
          <span>Password</span>
          <input
            type="password"
            value={form.password}
            onChange={(event) => update("password", event.target.value)}
          />
        </label>
        <button className="button button-primary" type="submit" disabled={submitting}>
          {submitting ? "Creating Account..." : "Create Account"}
        </button>
        {status && <p className="status-message status-error">{status}</p>}
      </form>
    </main>
  );
}

function DesktopReturnPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const query = new URLSearchParams(location.search);
  const email = query.get("email") || "";
  const verificationState = query.get("verification");
  const gmailOauthState = query.get("gmail_oauth");

  const state = location.state || (
    verificationState === "pending"
      ? {
          title: "Verification email sent",
          message: `We sent a verification email to ${email || "your inbox"}. Verify it, then sign in from Phantom.`
        }
      : verificationState === "success"
        ? {
            title: "Email verified",
            message: `${email || "Your account"} is now verified. You can sign in from the desktop app or the user dashboard.`
          }
        : gmailOauthState === "success"
          ? {
              title: "Gmail delivery connected",
              message: "The backend can now send verification mail through Gmail."
            }
          : {
              title: "Desktop callback ready",
              message: "Return to Phantom to complete the hosted callback flow."
            }
  );

  async function handleResendVerification() {
    if (!email) {
      return;
    }

    try {
      await resendVerificationEmail(email);
      navigate(`/desktop-return?verification=pending&email=${encodeURIComponent(email)}`, {
        replace: true
      });
    } catch (error) {
      navigate("/desktop-return", {
        replace: true,
        state: {
          title: "Verification retry failed",
          message: error.message || "Could not resend verification email."
        }
      });
    }
  }

  return (
    <main className="page">
      <section className="panel auth-panel auth-panel-wide">
        <p className="eyebrow">Desktop return</p>
        <h1>{state.title}</h1>
        <p className="hero-text">{state.message}</p>
        {verificationState === "pending" ? (
          <div className="hero-actions">
            <button className="button button-primary" onClick={handleResendVerification}>
              Resend Verification Email
            </button>
            <Link className="button button-secondary" to="/login">
              Back To Login
            </Link>
          </div>
        ) : verificationState === "success" ? (
          <div className="hero-actions">
            <Link className="button button-primary" to="/login">
              Go To Login
            </Link>
            <Link className="button button-secondary" to="/download">
              Open Download Center
            </Link>
          </div>
        ) : gmailOauthState === "success" ? (
          <div className="hero-actions">
            <Link className="button button-primary" to="/admin/login">
              Open Admin Login
            </Link>
          </div>
        ) : (
          <div className="hero-actions">
            <Link className="button button-primary" to="/login">
              Go To Login
            </Link>
          </div>
        )}
      </section>
    </main>
  );
}

function UserDashboardPage({ session }) {
  const [summary, setSummary] = useState(null);
  const [walletHistory, setWalletHistory] = useState([]);
  const [devices, setDevices] = useState([]);
  const [download, setDownload] = useState(null);
  const [support, setSupport] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;

    async function load() {
      setLoading(true);
      setError("");
      try {
        const account = await fetchAccountSummary(session.email);
        if (!account || cancelled) {
          return;
        }

        const [history, deviceRows, downloadEntitlement, supportOverview] = await Promise.all([
          fetchWalletHistory(account.userId),
          fetchDevices(account.userId),
          fetchDownloadEntitlement(account.userId),
          fetchSupportOverview(account.userId)
        ]);

        if (!cancelled) {
          setSummary(account);
          setWalletHistory(history);
          setDevices(deviceRows);
          setDownload(downloadEntitlement);
          setSupport(supportOverview);
        }
      } catch (loadError) {
        if (!cancelled) {
          setError(loadError.message || "Could not load the dashboard.");
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [session.email]);

  if (loading) {
    return (
      <main className="page">
        <section className="panel auth-panel auth-panel-wide">
          <p className="eyebrow">User dashboard</p>
          <h1>Loading hosted account state…</h1>
        </section>
      </main>
    );
  }

  if (error || !summary) {
    return (
      <main className="page">
        <section className="panel auth-panel auth-panel-wide">
          <p className="eyebrow">User dashboard</p>
          <h1>Dashboard unavailable.</h1>
          <p className="hero-text">{error || "Account summary could not be resolved."}</p>
        </section>
      </main>
    );
  }

  return (
    <main className="page dashboard-layout">
      <aside className="dashboard-rail panel">
        <p className="eyebrow">Signed in account</p>
        <h2>{summary.email}</h2>
        <div className="rail-badges">
          <span>{summary.planLabel}</span>
          <span>{summary.phoneVerified ? "Verified" : "Verification required"}</span>
        </div>
        <div className="rail-metrics">
          <div>
            <span>Pro credits</span>
            <strong>{summary.proAvailableCredits.toFixed(2)}</strong>
          </div>
          <div>
            <span>Premium credits</span>
            <strong>{summary.premiumAvailableCredits.toFixed(2)}</strong>
          </div>
          <div>
            <span>Premium debt</span>
            <strong>{summary.premiumNegativeCredits.toFixed(2)}</strong>
          </div>
        </div>
      </aside>

      <section className="dashboard-main">
        <Routes>
          <Route
            index
            element={
              <UserOverviewPanel summary={summary} devices={devices} download={download} support={support} />
            }
          />
          <Route path="wallet" element={<WalletPanel summary={summary} walletHistory={walletHistory} />} />
          <Route path="devices" element={<DevicesPanel devices={devices} />} />
          <Route path="history" element={<HistoryPanel walletHistory={walletHistory} />} />
          <Route path="support" element={<SupportPanel support={support} />} />
        </Routes>
      </section>
    </main>
  );
}

function UserOverviewPanel({ summary, devices, download, support }) {
  const activeDeviceCount = devices.filter((item) => item.isActive).length;

  return (
    <div className="dashboard-grid">
      <article className="panel dashboard-hero-panel">
        <p className="eyebrow">User dashboard</p>
        <h1>Your hosted account state, without touching the desktop runtime.</h1>
        <p className="hero-text">
          This dashboard is for users: balances, devices, last activity, download entitlement, and support state.
          It is intentionally separate from the admin control plane.
        </p>
      </article>

      <article className="panel metric-panel">
        <span>Tier</span>
        <strong>{summary.planLabel}</strong>
      </article>
      <article className="panel metric-panel">
        <span>Active devices</span>
        <strong>{activeDeviceCount}</strong>
      </article>
      <article className="panel metric-panel">
        <span>Premium credits</span>
        <strong>{summary.premiumAvailableCredits.toFixed(2)}</strong>
      </article>
      <article className="panel">
        <p className="story-tag">Download entitlement</p>
        <h3>{download?.installerLabel || "Installer access pending"}</h3>
        <p>
          {download?.releaseChannel || "Unavailable"} · {download?.installerVersion || "No version resolved"}
        </p>
      </article>
      <article className="panel">
        <p className="story-tag">Current access guard</p>
        <h3>{summary.phoneVerified ? "Ready for app access" : "Phone verification required"}</h3>
        <p>
          Lease expiry: {formatDate(summary.leaseExpiresAtUtc)} · Last activity:{" "}
          {formatDate(summary.lastActivityAtUtc)}
        </p>
      </article>
      <article className="panel">
        <p className="story-tag">Support state</p>
        <h3>{support?.openLockSessionId || "No active lock issue"}</h3>
        <p>{support?.supportMessage || "No support signal available."}</p>
      </article>
    </div>
  );
}

function WalletPanel({ summary, walletHistory }) {
  return (
    <div className="dashboard-grid">
      <article className="panel metric-panel panel-brass">
        <span>Pro available</span>
        <strong>{summary.proAvailableCredits.toFixed(2)}</strong>
      </article>
      <article className="panel metric-panel panel-brass">
        <span>Premium available</span>
        <strong>{summary.premiumAvailableCredits.toFixed(2)}</strong>
      </article>
      <article className="panel metric-panel panel-brass">
        <span>Premium debt</span>
        <strong>{summary.premiumNegativeCredits.toFixed(2)}</strong>
      </article>
      <article className="panel table-panel table-panel-full">
        <p className="eyebrow">Wallet history</p>
        <table>
          <thead>
            <tr>
              <th>Session</th>
              <th>Credits</th>
              <th>Blocks</th>
              <th>Debt</th>
              <th>Created</th>
            </tr>
          </thead>
          <tbody>
            {walletHistory.length === 0 ? (
              <tr>
                <td colSpan="5">No wallet entries recorded yet.</td>
              </tr>
            ) : (
              walletHistory.map((item) => (
                <tr key={item.ledgerEntryId}>
                  <td>{item.sessionId}</td>
                  <td>{item.chargedCredits}</td>
                  <td>{item.chargedBlocks}</td>
                  <td>{item.addedPremiumDebt}</td>
                  <td>{formatDate(item.createdAtUtc)}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </article>
    </div>
  );
}

function DevicesPanel({ devices }) {
  return (
    <div className="dashboard-grid devices-grid">
      {devices.length === 0 ? (
        <article className="panel support-panel">
          <p className="eyebrow">Devices</p>
          <h2>No device sessions recorded yet.</h2>
        </article>
      ) : (
        devices.map((device) => (
          <article className="panel device-card" key={`${device.deviceInstallId}-${device.deviceFingerprintHash}`}>
            <p className="story-tag">{device.isActive ? "Active" : "Historical"}</p>
            <h3>{device.deviceInstallId}</h3>
            <p>Fingerprint: {device.deviceFingerprintHash}</p>
            <p>Auth method: {device.authMethod}</p>
            <p>Last seen: {formatDate(device.lastAuthenticatedAtUtc)}</p>
          </article>
        ))
      )}
    </div>
  );
}

function HistoryPanel({ walletHistory }) {
  return (
    <div className="dashboard-grid">
      <article className="panel table-panel table-panel-full">
        <p className="eyebrow">Usage charge history</p>
        <table>
          <thead>
            <tr>
              <th>Ledger entry</th>
              <th>Session</th>
              <th>Credits</th>
              <th>Debt</th>
              <th>Created</th>
            </tr>
          </thead>
          <tbody>
            {walletHistory.length === 0 ? (
              <tr>
                <td colSpan="5">No usage ledger history recorded yet.</td>
              </tr>
            ) : (
              walletHistory.map((item) => (
                <tr key={item.ledgerEntryId}>
                  <td>{item.ledgerEntryId}</td>
                  <td>{item.sessionId}</td>
                  <td>{item.chargedCredits}</td>
                  <td>{item.addedPremiumDebt}</td>
                  <td>{formatDate(item.createdAtUtc)}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </article>
    </div>
  );
}

function SupportPanel({ support }) {
  return (
    <div className="dashboard-grid">
      <article className="panel support-panel">
        <p className="eyebrow">Support preview</p>
        <h2>{support?.openLockSessionId || "No active support event"}</h2>
        <p>{support?.supportMessage || "Support state is not available."}</p>
        <div className="support-metrics">
          <div>
            <span>Last charge</span>
            <strong>{support?.lastUsageChargeCredits ?? 0}</strong>
          </div>
          <div>
            <span>Lease hours left</span>
            <strong>{support?.offlineLeaseHoursRemaining ?? 0}</strong>
          </div>
        </div>
      </article>
    </div>
  );
}

function AdminLoginPage({ onAuthenticated, adminSession }) {
  const navigate = useNavigate();
  const [apiKey, setApiKey] = useState(adminSession?.apiKey || "");
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (adminSession?.apiKey) {
      navigate("/admin", { replace: true });
    }
  }, [adminSession, navigate]);

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setError("");
    try {
      await fetchAdminOverview(apiKey);
      onAuthenticated({ apiKey });
      navigate("/admin", { replace: true });
    } catch (loginError) {
      setError(loginError.message || "Admin sign-in failed.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="page auth-layout auth-layout-wide">
      <section className="panel auth-panel">
        <p className="eyebrow">Admin console</p>
        <h1>Separate control plane for managed providers and hosted runtime operations.</h1>
        <p className="hero-text">
          The admin dashboard is not part of the user dashboard. It uses the dashboard admin API key,
          then proxies operational actions to the Windows backend where needed.
        </p>
      </section>

      <form className="panel auth-form" onSubmit={handleSubmit}>
        <label>
          <span>Admin API key</span>
          <input
            type="password"
            value={apiKey}
            onChange={(event) => setApiKey(event.target.value)}
            placeholder="Enter PHANTOM_DASHBOARD_ADMIN_API_KEY"
          />
        </label>
        <button className="button button-primary" type="submit" disabled={submitting}>
          {submitting ? "Authenticating..." : "Open Admin Dashboard"}
        </button>
        {error && <p className="status-message status-error">{error}</p>}
      </form>
    </main>
  );
}

function AdminDashboardPage({ adminSession }) {
  const [overview, setOverview] = useState(null);
  const [inventory, setInventory] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;

    async function load() {
      setLoading(true);
      setError("");
      try {
        const [overviewData, inventoryData] = await Promise.all([
          fetchAdminOverview(adminSession.apiKey),
          fetchManagedAiAdminInventory(adminSession.apiKey)
        ]);

        if (!cancelled) {
          setOverview(overviewData);
          setInventory(inventoryData);
        }
      } catch (loadError) {
        if (!cancelled) {
          setError(loadError.message || "Could not load admin dashboard.");
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [adminSession.apiKey]);

  async function refreshManagedInventory() {
    const nextInventory = await fetchManagedAiAdminInventory(adminSession.apiKey);
    setInventory(nextInventory);
    return nextInventory;
  }

  if (loading) {
    return (
      <main className="page">
        <section className="panel auth-panel auth-panel-wide">
          <p className="eyebrow">Admin dashboard</p>
          <h1>Loading control plane…</h1>
        </section>
      </main>
    );
  }

  if (error) {
    return (
      <main className="page">
        <section className="panel auth-panel auth-panel-wide">
          <p className="eyebrow">Admin dashboard</p>
          <h1>Admin access failed.</h1>
          <p className="hero-text">{error}</p>
        </section>
      </main>
    );
  }

  return (
    <main className="page dashboard-layout admin-layout">
      <aside className="dashboard-rail panel">
        <p className="eyebrow">Admin control plane</p>
        <h2>Hosted operations</h2>
        <div className="rail-badges">
          <span>Managed AI</span>
          <span>Runtime authority</span>
        </div>
        <div className="rail-metrics">
          <div>
            <span>Accounts</span>
            <strong>{overview?.accountCount ?? 0}</strong>
          </div>
          <div>
            <span>Active locks</span>
            <strong>{overview?.activeLockCount ?? 0}</strong>
          </div>
          <div>
            <span>Managed keys</span>
            <strong>{overview?.managedCredentialCount ?? 0}</strong>
          </div>
        </div>
      </aside>

      <section className="dashboard-main">
        <Routes>
          <Route index element={<AdminOverviewPanel overview={overview} inventory={inventory} />} />
          <Route
            path="managed-ai"
            element={
              <ManagedAiAdminPanel
                adminApiKey={adminSession.apiKey}
                inventory={inventory}
                onRefresh={refreshManagedInventory}
              />
            }
          />
        </Routes>
      </section>
    </main>
  );
}

function AdminOverviewPanel({ overview, inventory }) {
  const credentials = inventory?.credentials || [];
  const providers = inventory?.managedProviders || [];

  return (
    <div className="dashboard-grid admin-grid">
      <article className="panel admin-hero-panel">
        <p className="eyebrow">Admin overview</p>
        <h1>The backend authority layer for managed AI, not a user-facing wallet view.</h1>
        <p className="hero-text">
          Use this console to monitor operational counts and manage the provider credentials that power
          Free Trial and Premium hosted AI lanes.
        </p>
      </article>

      <article className="panel metric-panel">
        <span>Live desktop sessions</span>
        <strong>{overview?.activeSessionCount ?? 0}</strong>
      </article>
      <article className="panel metric-panel">
        <span>Usage ledger entries</span>
        <strong>{overview?.ledgerEntryCount ?? 0}</strong>
      </article>
      <article className="panel metric-panel">
        <span>Managed providers</span>
        <strong>{providers.length}</strong>
      </article>
      <article className="panel table-panel table-panel-full">
        <p className="eyebrow">Managed provider readiness</p>
        <table>
          <thead>
            <tr>
              <th>Provider</th>
              <th>Configured credentials</th>
            </tr>
          </thead>
          <tbody>
            {providers.map((provider) => (
              <tr key={provider.providerId}>
                <td>{provider.label}</td>
                <td>{credentials.filter((item) => item.providerId === provider.providerId).length}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </article>
    </div>
  );
}

function ManagedAiAdminPanel({ adminApiKey, inventory, onRefresh }) {
  const [providerId, setProviderId] = useState("ChatGPT");
  const [label, setLabel] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [priority, setPriority] = useState("0");
  const [isEnabled, setIsEnabled] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [refreshingCatalog, setRefreshingCatalog] = useState(false);
  const [localError, setLocalError] = useState("");
  const [success, setSuccess] = useState("");

  const providers = inventory?.managedProviders || [];
  const credentials = inventory?.credentials || [];

  useEffect(() => {
    if (providers.length > 0 && !providers.some((item) => item.providerId === providerId)) {
      setProviderId(providers[0].providerId);
    }
  }, [providerId, providers]);

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setLocalError("");
    setSuccess("");

    try {
      await upsertManagedAiCredential(adminApiKey, {
        providerId,
        label,
        apiKey,
        isEnabled,
        priority: Number(priority) || 0
      });
      setLabel("");
      setApiKey("");
      setPriority("0");
      setSuccess("Managed credential saved.");
      await onRefresh();
    } catch (saveError) {
      setLocalError(saveError.message || "Could not save managed credential.");
    } finally {
      setSubmitting(false);
    }
  }

  async function handleDelete(credentialId) {
    setLocalError("");
    setSuccess("");
    try {
      await deleteManagedAiCredential(adminApiKey, credentialId);
      setSuccess("Managed credential removed.");
      await onRefresh();
    } catch (deleteError) {
      setLocalError(deleteError.message || "Could not remove credential.");
    }
  }

  async function handleCatalogRefresh() {
    setRefreshingCatalog(true);
    setLocalError("");
    setSuccess("");
    try {
      await triggerManagedAiCatalogRefresh(adminApiKey);
      await onRefresh();
      setSuccess("Managed model catalog refreshed.");
    } catch (refreshError) {
      setLocalError(refreshError.message || "Could not refresh managed model catalog.");
    } finally {
      setRefreshingCatalog(false);
    }
  }

  return (
    <div className="dashboard-grid admin-grid">
      <article className="panel admin-hero-panel">
        <p className="eyebrow">Managed AI inventory</p>
        <h1>Operate provider credentials for Free Trial and Premium without exposing keys to the desktop app.</h1>
        <p className="hero-text">
          The website admin console manages the rotation inventory. The Windows backend stores and uses the keys.
          Users only choose provider and model; they never see the credential layer.
        </p>
      </article>

      <article className="panel admin-form-panel">
        <div className="admin-panel-head">
          <div>
            <p className="story-tag">Create or rotate credential</p>
            <h3>Managed provider control</h3>
          </div>
          <div className="admin-panel-actions">
            <button className="button button-secondary button-compact" type="button" onClick={onRefresh}>
              Refresh
            </button>
            <button
              className="button button-primary button-compact"
              type="button"
              onClick={handleCatalogRefresh}
              disabled={refreshingCatalog}
            >
              {refreshingCatalog ? "Updating..." : "Update Models"}
            </button>
          </div>
        </div>

        {localError && <p className="admin-error">{localError}</p>}
        {success && <p className="admin-success">{success}</p>}

        <div className="admin-guidance">
          <strong>Rotation model</strong>
          <p>
            Add one API key per save. To configure rotation or failover for the same provider, create multiple
            rows with different labels and priorities. Lower priority numbers are preferred first.
          </p>
        </div>

        <form className="admin-form" onSubmit={handleSubmit}>
          <label>
            Provider
            <select value={providerId} onChange={(event) => setProviderId(event.target.value)}>
              {providers.map((provider) => (
                <option key={provider.providerId} value={provider.providerId}>
                  {provider.label}
                </option>
              ))}
            </select>
          </label>

          <label>
            Label
            <input
              value={label}
              onChange={(event) => setLabel(event.target.value)}
              placeholder="Primary lane / backup lane / reserve lane"
            />
          </label>

          <label>
            API key
            <textarea
              rows={4}
              value={apiKey}
              onChange={(event) => setApiKey(event.target.value)}
              placeholder="Paste managed provider key"
            />
          </label>

          <div className="admin-form-inline">
            <label>
              Priority
              <input value={priority} onChange={(event) => setPriority(event.target.value)} />
            </label>
            <label className="admin-toggle">
              <input
                type="checkbox"
                checked={isEnabled}
                onChange={(event) => setIsEnabled(event.target.checked)}
              />
              <span>Enabled for rotation</span>
            </label>
          </div>

          <button className="button button-primary" type="submit" disabled={submitting}>
            {submitting ? "Saving..." : "Add Managed Credential"}
          </button>
        </form>
      </article>

      <article className="panel table-panel table-panel-full">
        <p className="eyebrow">Current managed credential roster</p>
        <table>
          <thead>
            <tr>
              <th>Provider</th>
              <th>Label</th>
              <th>Priority</th>
              <th>Status</th>
              <th>Selection order</th>
              <th>Updated</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            {credentials.length === 0 ? (
              <tr>
                <td colSpan="7">No managed credentials configured yet.</td>
              </tr>
            ) : (
              credentials.map((item) => (
                <tr key={item.credentialId}>
                  <td>{item.providerId}</td>
                  <td>{item.label}</td>
                  <td>{item.priority}</td>
                  <td>{item.isEnabled ? "Enabled" : "Disabled"}</td>
                  <td>{item.priority === 0 ? "Primary candidate" : `Fallback after priority ${item.priority - 1}`}</td>
                  <td>{formatDate(item.updatedAtUtc)}</td>
                  <td>
                    <button className="table-action" type="button" onClick={() => handleDelete(item.credentialId)}>
                      Remove
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </article>
    </div>
  );
}

function readStoredJson(key) {
  const value = window.localStorage.getItem(key);
  if (!value) {
    return null;
  }

  try {
    return JSON.parse(value);
  } catch {
    return null;
  }
}

function writeStoredJson(key, value) {
  window.localStorage.setItem(key, JSON.stringify(value));
}

function clearStoredJson(key) {
  window.localStorage.removeItem(key);
}

function formatDate(value) {
  if (!value) {
    return "n/a";
  }

  return new Intl.DateTimeFormat("en-US", {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(new Date(value));
}
