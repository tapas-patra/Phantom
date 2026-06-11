import { useEffect, useState } from "react";
import { Link, NavLink, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import {
  fetchAccountSummary,
  fetchDevices,
  fetchDownloadEntitlement,
  registerAccount,
  resendVerificationEmail,
  fetchSupportOverview,
  fetchWalletHistory
} from "./lib/api";

const primaryNav = [
  { to: "/", label: "Signal" },
  { to: "/pricing", label: "Pricing" },
  { to: "/download", label: "Download" },
  { to: "/login", label: "Login" }
];

const dashboardNav = [
  { to: "/dashboard", label: "Overview" },
  { to: "/dashboard/wallet", label: "Wallet" },
  { to: "/dashboard/devices", label: "Devices" },
  { to: "/dashboard/history", label: "Usage History" },
  { to: "/dashboard/support", label: "Support" }
];

const plans = [
  {
    label: "Free",
    accent: "Mist",
    price: "$0",
    note: "Account access and dashboard visibility",
    bullets: ["Create account", "Verify phone", "See balances and devices", "Cannot start interviews without credits"]
  },
  {
    label: "Pro BYO",
    accent: "Current",
    price: "Credit-pack",
    note: "Desktop usage credits with your own provider keys",
    bullets: ["App-usage credit wallet", "One active interview lock", "Offline resume safeguards", "Desktop-first workflow"]
  },
  {
    label: "Premium",
    accent: "Brass",
    price: "Credit-pack + managed AI",
    note: "Managed AI orchestration and premium balance support",
    bullets: ["Premium wallet lane", "Managed model takeover", "Negative-balance inspection", "Support/admin assistance surfaces"]
  }
];

const stats = [
  { value: "24h", label: "Offline launch lease" },
  { value: "15m", label: "Charge block granularity" },
  { value: "1", label: "Active interview per account" },
  { value: "0.5", label: "Protected debt continuation cap" }
];

export default function App() {
  return (
    <div className="app-shell">
      <SiteChrome />
      <Routes>
        <Route path="/" element={<LandingPage />} />
        <Route path="/pricing" element={<PricingPage />} />
        <Route path="/download" element={<DownloadPage />} />
        <Route path="/login" element={<AuthPage mode="login" />} />
        <Route path="/register" element={<AuthPage mode="register" />} />
        <Route path="/magic-link" element={<MagicLinkPage />} />
        <Route path="/desktop-return" element={<DesktopReturnPage />} />
        <Route path="/dashboard/*" element={<DashboardPage />} />
      </Routes>
    </div>
  );
}

function SiteChrome() {
  const location = useLocation();
  const isDashboard = location.pathname.startsWith("/dashboard");

  return (
    <header className={`site-header ${isDashboard ? "site-header-compact" : ""}`}>
      <Link className="brandmark" to="/">
        <span className="brandmark-glyph">P</span>
        <span>
          <strong>Phantom</strong>
          <small>Protected Desktop Runtime</small>
        </span>
      </Link>

      <nav className="top-nav">
        {(isDashboard ? dashboardNav : primaryNav).map((item) => (
          <NavLink
            className={({ isActive }) => `nav-chip ${isActive ? "nav-chip-active" : ""}`}
            key={item.to}
            to={item.to}
            end={item.to === "/dashboard"}
          >
            {item.label}
          </NavLink>
        ))}
      </nav>
    </header>
  );
}

function LandingPage() {
  return (
    <main className="page">
      <section className="hero">
        <div className="hero-copy panel panel-hero">
          <p className="eyebrow">Windows-hosted interview runtime</p>
          <h1>
            The desktop surface for
            <span> guarded AI interviews.</span>
          </h1>
          <p className="hero-text">
            Phantom keeps the interview session device-bound, credit-aware, and resumable under
            failure pressure. The website is the command deck: registration, login, wallet, devices,
            downloads, and support.
          </p>
          <div className="hero-actions">
            <Link className="button button-primary" to="/register">
              Register Account
            </Link>
            <Link className="button button-secondary" to="/download">
              Download Desktop
            </Link>
          </div>
        </div>

        <div className="hero-stage">
          <div className="signal-orbit signal-orbit-a" />
          <div className="signal-orbit signal-orbit-b" />
          <div className="signal-core">
            <span>device trust</span>
            <span>lease validation</span>
            <span>credit continuity</span>
          </div>
        </div>
      </section>

      <section className="stat-ribbon">
        {stats.map((item, index) => (
          <article className="stat-card" key={item.label} style={{ animationDelay: `${index * 120}ms` }}>
            <strong>{item.value}</strong>
            <span>{item.label}</span>
          </article>
        ))}
      </section>

      <section className="story-grid">
        <article className="panel story-card story-card-large">
          <p className="eyebrow">Why it exists</p>
          <h2>The interview should survive turbulence, but not lose authority.</h2>
          <p>
            Phantom separates the protected Windows runtime from the hosted surfaces that govern who
            may enter, what device may continue, and whether a session is allowed to start.
          </p>
        </article>
        <article className="panel story-card">
          <p className="story-tag">Lock Authority</p>
          <h3>One active session</h3>
          <p>Startup checks and heartbeat locks prevent silent concurrent interview drift across devices.</p>
        </article>
        <article className="panel story-card">
          <p className="story-tag">Wallet Discipline</p>
          <h3>Separate Pro and Premium lanes</h3>
          <p>The app only mutates usage through the backend. The dashboard lets users inspect the outcome.</p>
        </article>
        <article className="panel story-card">
          <p className="story-tag">Recovery Design</p>
          <h3>Offline when justified</h3>
          <p>Cached leases and resumable local locks allow continuity without opening uncontrolled starts.</p>
        </article>
      </section>
    </main>
  );
}

function PricingPage() {
  return (
    <main className="page">
      <section className="section-heading">
        <p className="eyebrow">Pricing structure</p>
        <h1>Three operating postures, one protected runtime.</h1>
      </section>
      <section className="pricing-grid">
        {plans.map((plan, index) => (
          <article className={`plan-card plan-card-${plan.accent.toLowerCase()}`} key={plan.label}>
            <div className="plan-card-top">
              <p>{plan.label}</p>
              <strong>{plan.price}</strong>
            </div>
            <h2>{plan.note}</h2>
            <ul>
              {plan.bullets.map((bullet) => (
                <li key={bullet}>{bullet}</li>
              ))}
            </ul>
            <Link className="button button-secondary" to={index === 0 ? "/register" : "/download"}>
              {index === 0 ? "Create Account" : "Get Desktop Access"}
            </Link>
          </article>
        ))}
      </section>
    </main>
  );
}

function DownloadPage() {
  return (
    <main className="page download-layout">
      <section className="panel panel-hero">
        <p className="eyebrow">Desktop delivery</p>
        <h1>Phantom for Windows</h1>
        <p className="hero-text">
          Manual installer delivery, Evergreen WebView2 bootstrap, and a hosted sign-in gate before
          the app becomes interactive.
        </p>
        <div className="download-meta">
          <div>
            <span>Channel</span>
            <strong>Hosted Preview</strong>
          </div>
          <div>
            <span>Runtime</span>
            <strong>net8.0-windows</strong>
          </div>
          <div>
            <span>Installer</span>
            <strong>Manual Download</strong>
          </div>
        </div>
        <a className="button button-primary" href="#installer">
          Download Installer
        </a>
      </section>

      <section className="panel detail-stack">
        <div>
          <p className="story-tag">Before install</p>
          <h3>What users should expect</h3>
        </div>
        <ul className="detail-list">
          <li>Admin privileges required</li>
          <li>Windows 10 build 19041 or later</li>
          <li>Hosted login or register on first open</li>
          <li>Backend account-check screen before entering the app</li>
        </ul>
      </section>
    </main>
  );
}

function AuthPage({ mode }) {
  const navigate = useNavigate();
  const location = useLocation();
  const isRegister = mode === "register";
  const registerParams = new URLSearchParams(location.search);
  const [form, setForm] = useState({
    email: isRegister ? "" : "pro.user@phantom.app",
    password: isRegister ? "" : "PhantomPro123!",
    magicLink: false
  });
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);

  function update(field, value) {
    setForm((current) => ({ ...current, [field]: value }));
  }

  async function handleSubmit(event) {
    event.preventDefault();

    if (isRegister) {
      setSubmitting(true);
      setStatus("");
      try
      {
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
          throw new Error(result.deliveryError || "Registration completed but verification email could not be delivered.");
        }
        navigate(
          `/desktop-return?verification=pending&email=${encodeURIComponent(result.email)}`
        );
      }
      catch (error)
      {
        setStatus(error.message || "Registration failed.");
      }
      finally
      {
        setSubmitting(false);
      }
      return;
    }

    navigate(`/dashboard?email=${encodeURIComponent(form.email)}`);
  }

  return (
    <main className="page auth-layout">
      <section className="panel auth-panel">
        <p className="eyebrow">{isRegister ? "Website registration" : "In-app compatible login"}</p>
        <h1>{isRegister ? "Create a Phantom account." : "Return to your protected workspace."}</h1>
        <p className="hero-text">
          {isRegister
            ? "Desktop registration now creates the hosted account and sends a real verification email before first sign-in."
            : "The desktop app supports password and magic-link auth. This page mirrors those paths so the UX stays coherent across web and Windows."}
        </p>
      </section>

      <form className="panel auth-form" onSubmit={handleSubmit}>
        <label>
          <span>Email</span>
          <input value={form.email} onChange={(event) => update("email", event.target.value)} />
        </label>

        {!isRegister && (
          <>
            <label>
              <span>Password</span>
              <input
                type="password"
                value={form.password}
                onChange={(event) => update("password", event.target.value)}
              />
            </label>
            <label className="toggle-row">
              <input
                type="checkbox"
                checked={form.magicLink}
                onChange={(event) => update("magicLink", event.target.checked)}
              />
              <span>Use magic-link instead of password</span>
            </label>
          </>
        )}

        {isRegister && (
          <>
            <label>
              <span>Password</span>
              <input
                type="password"
                value={form.password}
                onChange={(event) => update("password", event.target.value)}
              />
            </label>
          </>
        )}

        <button className="button button-primary" type="submit">
          {isRegister ? (submitting ? "Creating Account..." : "Create Account") : form.magicLink ? "Continue With Magic Link" : "Open Dashboard"}
        </button>

        {status && <p className="hero-text">{status}</p>}

        {!isRegister && (
          <Link className="subtle-link" to="/magic-link">
            See magic-link handoff UX
          </Link>
        )}
      </form>
    </main>
  );
}

function MagicLinkPage() {
  return (
    <main className="page">
      <section className="panel auth-panel auth-panel-wide">
        <p className="eyebrow">Desktop callback handoff</p>
        <h1>Magic link generated. Return the session to Phantom.</h1>
        <p className="hero-text">
          The website should issue the link, optionally email it, and then return through the custom
          protocol `phantom://auth/callback`. This page is the human-readable fallback when the desktop
          handoff needs to be explained or copied manually.
        </p>
        <div className="code-block">
          phantom://auth/callback?token=opaque-desktop-auth-token
        </div>
      </section>
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
          title: "Verification Email Sent",
          message: `We sent a verification email to ${email || "your inbox"}. Verify the address, then sign in from the desktop app.`
        }
      : verificationState === "success"
        ? {
            title: "Email Verified",
            message: `${email || "Your account"} is now verified. Return to the desktop app and sign in.`
          }
        : gmailOauthState === "success"
          ? {
              title: "Gmail Delivery Connected",
              message: "The backend now has a Gmail refresh token and can send verification and sign-in email."
            }
          : {
              title: "Desktop callback ready",
              message: "Open Phantom to complete the hosted callback flow."
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
          title: "Verification Retry Failed",
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
              Download Desktop
            </Link>
          </div>
        ) : gmailOauthState === "success" ? (
          <div className="hero-actions">
            <Link className="button button-primary" to="/register">
              Open Registration
            </Link>
          </div>
        ) : (
          <a className="button button-primary" href="phantom://auth/callback?token=demo-token">
            Open Phantom
          </a>
        )}
      </section>
    </main>
  );
}

function DashboardPage() {
  const location = useLocation();
  const emailParam = new URLSearchParams(location.search).get("email") || "premium.user@phantom.app";
  const [summary, setSummary] = useState(null);
  const [walletHistory, setWalletHistory] = useState([]);
  const [devices, setDevices] = useState([]);
  const [download, setDownload] = useState(null);
  const [support, setSupport] = useState(null);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      const account = await fetchAccountSummary(emailParam);
      if (!account || cancelled) {
        return;
      }

      setSummary(account);
      const [history, deviceRows, downloadEntitlement, supportOverview] = await Promise.all([
        fetchWalletHistory(account.userId),
        fetchDevices(account.userId),
        fetchDownloadEntitlement(account.userId),
        fetchSupportOverview(account.userId)
      ]);

      if (!cancelled) {
        setWalletHistory(history);
        setDevices(deviceRows);
        setDownload(downloadEntitlement);
        setSupport(supportOverview);
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [emailParam]);

  if (!summary) {
    return (
      <main className="page">
        <section className="panel auth-panel auth-panel-wide">
          <p className="eyebrow">Dashboard loading</p>
          <h1>Hydrating hosted account state...</h1>
        </section>
      </main>
    );
  }

  return (
    <main className="page dashboard-layout">
      <aside className="dashboard-rail panel">
        <p className="eyebrow">Account</p>
        <h2>{summary.email}</h2>
        <div className="rail-badges">
          <span>{summary.planLabel}</span>
          <span>{summary.phoneVerified ? "Phone verified" : "Verification required"}</span>
        </div>
        <div className="rail-metrics">
          <div>
            <span>Pro</span>
            <strong>{summary.proAvailableCredits.toFixed(2)}</strong>
          </div>
          <div>
            <span>Premium</span>
            <strong>{summary.premiumAvailableCredits.toFixed(2)}</strong>
          </div>
          <div>
            <span>Debt</span>
            <strong>{summary.premiumNegativeCredits.toFixed(2)}</strong>
          </div>
        </div>
      </aside>

      <section className="dashboard-main">
        <Routes>
          <Route
            index
            element={<OverviewPanel summary={summary} download={download} devices={devices} support={support} />}
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

function OverviewPanel({ summary, download, devices, support }) {
  return (
    <div className="dashboard-grid">
      <article className="panel dashboard-hero-panel">
        <p className="eyebrow">Hosted overview</p>
        <h1>Desktop authority, visible from the web.</h1>
        <p className="hero-text">
          Lease expiry, device count, wallet state, and download entitlement should all resolve before
          the Windows app becomes interactive.
        </p>
      </article>
      <article className="panel metric-panel">
        <span>Lease expires</span>
        <strong>{formatDate(summary.leaseExpiresAtUtc)}</strong>
      </article>
      <article className="panel metric-panel">
        <span>Active devices</span>
        <strong>{devices.length}</strong>
      </article>
      <article className="panel metric-panel">
        <span>Latest activity</span>
        <strong>{formatDate(summary.lastActivityAtUtc)}</strong>
      </article>
      <article className="panel">
        <p className="story-tag">Installer access</p>
        <h3>{download?.installerLabel || "Installer entitlement pending"}</h3>
        <p>
          {download?.releaseChannel || "Preview"} · {download?.installerVersion || "not resolved"}
        </p>
      </article>
      <article className="panel">
        <p className="story-tag">Support state</p>
        <h3>{support?.openLockSessionId || "No lock alert"}</h3>
        <p>{support?.supportMessage || "Support overview available."}</p>
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
            {walletHistory.map((item) => (
              <tr key={item.ledgerEntryId}>
                <td>{item.sessionId}</td>
                <td>{item.chargedCredits}</td>
                <td>{item.chargedBlocks}</td>
                <td>{item.addedPremiumDebt}</td>
                <td>{formatDate(item.createdAtUtc)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </article>
    </div>
  );
}

function DevicesPanel({ devices }) {
  return (
    <div className="dashboard-grid devices-grid">
      {devices.map((device) => (
        <article className="panel device-card" key={`${device.deviceInstallId}-${device.deviceFingerprintHash}`}>
          <p className="story-tag">{device.isActive ? "Active" : "Historical"}</p>
          <h3>{device.deviceInstallId}</h3>
          <p>Fingerprint: {device.deviceFingerprintHash}</p>
          <p>Auth method: {device.authMethod}</p>
          <p>Last seen: {formatDate(device.lastAuthenticatedAtUtc)}</p>
        </article>
      ))}
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
              <th>Ledger</th>
              <th>Session</th>
              <th>Credits</th>
              <th>Debt</th>
              <th>Created</th>
            </tr>
          </thead>
          <tbody>
            {walletHistory.map((item) => (
              <tr key={item.ledgerEntryId}>
                <td>{item.ledgerEntryId}</td>
                <td>{item.sessionId}</td>
                <td>{item.chargedCredits}</td>
                <td>{item.addedPremiumDebt}</td>
                <td>{formatDate(item.createdAtUtc)}</td>
              </tr>
            ))}
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
        <p>{support?.supportMessage}</p>
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

function formatDate(value) {
  if (!value) {
    return "n/a";
  }

  return new Intl.DateTimeFormat("en-US", {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(new Date(value));
}
