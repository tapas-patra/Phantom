import { useEffect, useState } from "react";
import { Link, NavLink, Navigate, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import {
  loginAdmin,
  logoutAdmin,
  refreshAdminSession,
  requestAdminPasswordReset,
  resetAdminPassword,
  fetchAdminPaymentOrders,
  fetchAdminPaymentWebhooks,
  fetchGmailOAuthStatus,
  confirmPaymentCheckout,
  createPaymentCheckout,
  createHostedKnowledgeBase,
  deleteManagedAiCredential,
  fetchAccountSummary,
  fetchAdminOverview,
  fetchDevices,
  fetchDownloadEntitlement,
  fetchHostedKnowledgeBase,
  fetchManagedAiAdminInventory,
  fetchPaymentCatalog,
  fetchSupportOverview,
  fetchWalletHistory,
  fetchWalletPurchases,
  loginAccount,
  logoutAccount,
  registerAccount,
  resendVerificationEmail,
  sendPhoneOtp,
  startGmailOAuth,
  triggerManagedAiCatalogRefresh,
  uploadHostedKnowledgeBaseDocuments,
  upsertManagedAiCredential,
  verifyPhoneOtp
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
  { to: "/dashboard/knowledge-base", label: "Knowledge Base" },
  { to: "/dashboard/wallet", label: "Wallet" },
  { to: "/dashboard/devices", label: "Devices" },
  { to: "/dashboard/history", label: "Usage" },
  { to: "/dashboard/support", label: "Support" }
];

const adminNav = [
  { to: "/admin", label: "Overview" },
  { to: "/admin/payments", label: "Payments" },
  { to: "/admin/managed-ai", label: "Managed AI" }
];

const plans = [
  {
    name: "Free Trial",
    price: "₹0",
    ribbon: "Managed Demo",
    description: "Let candidates feel the real product before they commit to credits.",
    bullets: [
      "Hosted AI with provider + model choice",
      "2 trial sessions, 20 minutes each",
      "No BYO key setup",
      "Phone OTP and email verification required before access"
    ],
    cta: "Start Free",
    to: "/register",
    tone: "mist"
  },
  {
    name: "Pro BYO",
    price: "₹699 to ₹2,499",
    ribbon: "Power Users",
    description: "The full desktop workflow for users who want provider flexibility and their own AI spend.",
    bullets: [
      "Choose any 3 supported providers",
      "Store up to 2 keys per provider",
      "3, 8, or 15 Pro credits",
      "Offline resume and lock safeguards"
    ],
    cta: "See Pro Workflow",
    to: "/download",
    tone: "current"
  },
  {
    name: "Premium AI",
    price: "₹1,799 to ₹5,599",
    ribbon: "Hands-Off",
    description: "Hosted model operations, managed keys, and support visibility for users who want zero key management.",
    bullets: [
      "Managed ChatGPT, Claude, Gemini, Mistral, Groq, and NVIDIA lanes",
      "Hosted knowledge base synced across desktop devices",
      "3, 8, or 15 Premium credits",
      "Premium-only interview retrieval with credit-aware access checks",
      "Protected continuation debt can be settled directly"
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
  const [adminSessionReady, setAdminSessionReady] = useState(() => !readStoredJson(ADMIN_SESSION_KEY)?.refreshToken);

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

  async function handleAdminLogout() {
    const refreshToken = adminSession?.refreshToken;
    clearStoredJson(ADMIN_SESSION_KEY);
    setAdminSession(null);

    if (refreshToken) {
      try {
        await logoutAdmin(refreshToken);
      } catch {
        // Best effort logout.
      }
    }
  }

  useEffect(() => {
    let cancelled = false;
    let refreshTimer = 0;

    async function hydrateAdminSession() {
      if (!adminSession?.refreshToken) {
        setAdminSessionReady(true);
        return;
      }

      const expiresAt = parseUtcMillis(adminSession.expiresAtUtc);
      const shouldRefresh = !expiresAt || expiresAt <= Date.now() + 5 * 60 * 1000;
      if (!shouldRefresh) {
        setAdminSessionReady(true);
        refreshTimer = window.setTimeout(() => {
          hydrateAdminSession();
        }, Math.max(expiresAt - Date.now() - 5 * 60 * 1000, 1000));
        return;
      }

      try {
        const refreshed = await refreshAdminSession(adminSession.refreshToken);
        if (!cancelled) {
          handleAdminAuthenticated(refreshed);
        }
      } catch {
        if (!cancelled) {
          clearStoredJson(ADMIN_SESSION_KEY);
          setAdminSession(null);
        }
      } finally {
        if (!cancelled) {
          setAdminSessionReady(true);
        }
      }
    }

    hydrateAdminSession();
    return () => {
      cancelled = true;
      window.clearTimeout(refreshTimer);
    };
  }, [adminSession?.expiresAtUtc, adminSession?.refreshToken]);

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
        <Route path="/admin/forgot-password" element={<AdminForgotPasswordPage />} />
        <Route path="/admin/reset-password" element={<AdminResetPasswordPage />} />
        <Route
          path="/admin/*"
          element={
            adminSessionReady && adminSession ? (
              <AdminDashboardPage adminSession={adminSession} />
            ) : adminSessionReady ? (
              <Navigate to="/admin/login" replace />
            ) : (
              <AdminSessionLoadingPage />
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
  const hasAdminSession = Boolean(adminSession?.accessToken);
  const showAdminChrome = isAdminArea && hasAdminSession;
  const navigation = showAdminChrome ? adminNav : isUserArea ? userNav : marketingNav;

  const brandTarget = showAdminChrome
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
          <small>{showAdminChrome ? "Admin Control Plane" : "Protected Interview Runtime"}</small>
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
        {showAdminChrome ? (
          <>
            <span className="header-badge header-badge-brass">{adminSession.displayName || adminSession.email}</span>
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
  const [form, setForm] = useState({ email: "", password: "", phoneNumber: "", otpCode: "" });
  const [status, setStatus] = useState("");
  const [otpState, setOtpState] = useState({
    challengeId: "",
    verificationToken: "",
    maskedPhoneNumber: "",
    verifiedAtUtc: ""
  });
  const [submitting, setSubmitting] = useState(false);
  const [otpSubmitting, setOtpSubmitting] = useState(false);
  const deviceFingerprintHash = registerParams.get("deviceFingerprint") || getBrowserRegistrationFingerprint();

  function update(field, value) {
    setForm((current) => ({ ...current, [field]: value }));
  }

  async function handleSendOtp() {
    setOtpSubmitting(true);
    setStatus("");

    try {
      const result = await sendPhoneOtp({
        phoneNumber: form.phoneNumber,
        deviceFingerprintHash,
        installId: registerParams.get("installId") || "",
        emailHint: form.email
      });
      setOtpState((current) => ({
        ...current,
        challengeId: result.challengeId,
        maskedPhoneNumber: result.maskedPhoneNumber,
        verificationToken: "",
        verifiedAtUtc: ""
      }));
      setStatus(`OTP sent to ${result.maskedPhoneNumber}.`);
    } catch (error) {
      setStatus(error.message || "Could not send OTP.");
    } finally {
      setOtpSubmitting(false);
    }
  }

  async function handleVerifyOtp() {
    if (!otpState.challengeId) {
      setStatus("Send OTP before verifying.");
      return;
    }

    setOtpSubmitting(true);
    setStatus("");

    try {
      const result = await verifyPhoneOtp({
        challengeId: otpState.challengeId,
        otpCode: form.otpCode
      });
      setOtpState((current) => ({
        ...current,
        verificationToken: result.verificationToken,
        maskedPhoneNumber: result.maskedPhoneNumber,
        verifiedAtUtc: result.verifiedAtUtc
      }));
      setStatus(`Phone verified for ${result.maskedPhoneNumber}.`);
    } catch (error) {
      setStatus(error.message || "Could not verify OTP.");
    } finally {
      setOtpSubmitting(false);
    }
  }

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setStatus("");

    try {
      if (!otpState.verificationToken) {
        throw new Error("Verify your phone number before creating the account.");
      }

      const result = await registerAccount({
        email: form.email,
        password: form.password,
        phoneNumber: form.phoneNumber,
        phoneVerificationToken: otpState.verificationToken,
        appVersion: registerParams.get("appVersion") || "",
        installId: registerParams.get("installId") || "",
        deviceLabel: registerParams.get("deviceLabel") || "",
        deviceFingerprintHash,
        secretFingerprintHint: registerParams.get("deviceHint") || ""
      });

      const deliveryFailed = result.deliveryStatus !== "sent";
      navigate(`/desktop-return?verification=pending&email=${encodeURIComponent(result.email)}`, {
        replace: true,
        state: deliveryFailed
          ? {
              title: "Account created, but verification email failed",
              message: result.deliveryError
                ? `${result.email} was registered, but email delivery failed: ${result.deliveryError}. Reconnect Gmail delivery in admin, then resend verification.`
                : `${result.email} was registered, but the verification email could not be delivered yet.`
            }
          : undefined
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
        <div className="auth-summary-list">
          <div>
            <strong>Phone OTP required</strong>
            <span>One device and one verified mobile number per launch trial</span>
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
        <label>
          <span>Phone number</span>
          <input
            value={form.phoneNumber}
            onChange={(event) => update("phoneNumber", event.target.value)}
            placeholder="+91 9876543210"
          />
        </label>
        <div className="hero-actions">
          <button
            className="button button-secondary"
            type="button"
            onClick={handleSendOtp}
            disabled={otpSubmitting || submitting}
          >
            {otpSubmitting ? "Sending..." : "Send OTP"}
          </button>
          <span className="download-status">
            {otpState.maskedPhoneNumber
              ? `OTP challenge active for ${otpState.maskedPhoneNumber}`
              : "Phone OTP is required before registration"}
          </span>
        </div>
        <label>
          <span>OTP code</span>
          <input
            value={form.otpCode}
            onChange={(event) => update("otpCode", event.target.value)}
            placeholder="6-digit OTP"
          />
        </label>
        <button
          className="button button-secondary"
          type="button"
          onClick={handleVerifyOtp}
          disabled={otpSubmitting || submitting}
        >
          {otpSubmitting ? "Working..." : otpState.verificationToken ? "Phone Verified" : "Verify OTP"}
        </button>
        <button className="button button-primary" type="submit" disabled={submitting}>
          {submitting ? "Creating Account..." : "Create Account"}
        </button>
        {status && <p className={`status-message ${status.includes("verified") || status.includes("sent") ? "" : "status-error"}`}>{status}</p>}
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
      const result = await resendVerificationEmail(email);
      const resendSucceeded = result?.message?.toLowerCase().includes("sent")
        && !result?.message?.toLowerCase().includes("could not");

      navigate(`/desktop-return?verification=pending&email=${encodeURIComponent(email)}`, {
        replace: true,
        state: resendSucceeded
          ? {
              title: "Verification email sent",
              message: result?.message || `We sent a verification email to ${email}. Verify it, then sign in from Phantom.`
            }
          : {
              title: "Verification resend failed",
              message: result?.message || "Could not resend verification email."
            }
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
  const [knowledgeBase, setKnowledgeBase] = useState(null);
  const [walletHistory, setWalletHistory] = useState([]);
  const [walletPurchases, setWalletPurchases] = useState([]);
  const [paymentCatalog, setPaymentCatalog] = useState(null);
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

        const [
          history,
          purchases,
          deviceRows,
          downloadEntitlement,
          supportOverview,
          knowledgeBaseStatus,
          catalog
        ] = await Promise.all([
          fetchWalletHistory(account.userId),
          fetchWalletPurchases(account.userId),
          fetchDevices(account.userId),
          fetchDownloadEntitlement(account.userId),
          fetchSupportOverview(account.userId),
          fetchHostedKnowledgeBase(session.accessToken),
          fetchPaymentCatalog(session.accessToken).catch(() => null)
        ]);

        if (!cancelled) {
          setSummary(account);
          setKnowledgeBase(knowledgeBaseStatus);
          setWalletHistory(history);
          setWalletPurchases(purchases);
          setPaymentCatalog(catalog);
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
  }, [session.accessToken, session.email]);

  async function refreshWalletState() {
    const account = await fetchAccountSummary(session.email);
    if (!account) {
      throw new Error("Account summary could not be resolved.");
    }

    const [history, purchases, catalog] = await Promise.all([
      fetchWalletHistory(account.userId),
      fetchWalletPurchases(account.userId),
      fetchPaymentCatalog(session.accessToken).catch(() => null)
    ]);

    setSummary(account);
    setWalletHistory(history);
    setWalletPurchases(purchases);
    setPaymentCatalog(catalog);
  }

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
              <UserOverviewPanel
                summary={summary}
                devices={devices}
                download={download}
                support={support}
                knowledgeBase={knowledgeBase}
              />
            }
          />
          <Route
            path="knowledge-base"
            element={
              <KnowledgeBasePanel
                accessToken={session.accessToken}
                summary={summary}
                knowledgeBase={knowledgeBase}
                onKnowledgeBaseChanged={setKnowledgeBase}
              />
            }
          />
          <Route
            path="wallet"
            element={
              <WalletPanel
                accessToken={session.accessToken}
                summary={summary}
                walletHistory={walletHistory}
                walletPurchases={walletPurchases}
                paymentCatalog={paymentCatalog}
                onWalletUpdated={refreshWalletState}
              />
            }
          />
          <Route path="devices" element={<DevicesPanel devices={devices} />} />
          <Route path="history" element={<HistoryPanel walletHistory={walletHistory} />} />
          <Route path="support" element={<SupportPanel support={support} />} />
        </Routes>
      </section>
    </main>
  );
}

function UserOverviewPanel({ summary, devices, download, support, knowledgeBase }) {
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
      <article className="panel metric-panel">
        <span>Hosted KB</span>
        <strong>{knowledgeBase?.documentCount ?? 0} docs</strong>
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
      <article className="panel">
        <p className="story-tag">Premium knowledge base</p>
        <h3>{knowledgeBase?.name || "No hosted KB linked yet"}</h3>
        <p>
          {knowledgeBase?.canUseInInterview
            ? `Ready for interview retrieval across devices · ${knowledgeBase.documentCount} docs`
            : knowledgeBase?.blockedReason || "Create a hosted KB from the dashboard to sync interview context into the app."}
        </p>
      </article>
    </div>
  );
}

function KnowledgeBasePanel({ accessToken, summary, knowledgeBase, onKnowledgeBaseChanged }) {
  const [name, setName] = useState(knowledgeBase?.name || "My Premium Knowledge Base");
  const [description, setDescription] = useState(knowledgeBase?.description || "");
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    setName(knowledgeBase?.name || "My Premium Knowledge Base");
    setDescription(knowledgeBase?.description || "");
  }, [knowledgeBase?.description, knowledgeBase?.name]);

  const isPremiumBlocked = !knowledgeBase?.canManage;
  const blockedMessage = knowledgeBase?.blockedReason
    || "Hosted knowledge bases are available only while Premium access and credits are active.";

  async function handleCreate(event) {
    event.preventDefault();
    setSubmitting(true);
    setStatus("");

    try {
      const result = await createHostedKnowledgeBase(accessToken, { name, description });
      onKnowledgeBaseChanged(result);
      setStatus("Knowledge base saved.");
    } catch (error) {
      setStatus(error.message || "Could not save the knowledge base.");
    } finally {
      setSubmitting(false);
    }
  }

  async function handleUpload(event) {
    const files = event.target.files;
    if (!files?.length) {
      return;
    }

    setSubmitting(true);
    setStatus("");

    try {
      const result = await uploadHostedKnowledgeBaseDocuments(accessToken, files);
      onKnowledgeBaseChanged(result.knowledgeBase);
      setStatus(`Processed ${result.addedDocuments.length} document${result.addedDocuments.length === 1 ? "" : "s"}.`);
    } catch (error) {
      setStatus(error.message || "Could not process those documents.");
    } finally {
      setSubmitting(false);
      event.target.value = "";
    }
  }

  return (
    <div className="dashboard-grid">
      <article className="panel dashboard-hero-panel">
        <p className="eyebrow">Premium knowledge base</p>
        <h1>Upload interview context once, then let Phantom link it on every desktop.</h1>
        <p className="hero-text">
          Hosted knowledge bases are stored on the backend, embedded for retrieval, and auto-linked into
          the Windows app when this account signs in.
        </p>
      </article>

      <article className="panel">
        <p className="story-tag">Current entitlement</p>
        <h3>{summary.planLabel}</h3>
        <p>
          {knowledgeBase?.canUseInInterview
            ? `Interview retrieval enabled with ${summary.premiumAvailableCredits.toFixed(2)} Premium credits available.`
            : blockedMessage}
        </p>
      </article>

      <article className="panel">
        <p className="story-tag">Hosted status</p>
        <h3>{knowledgeBase?.status || "not_created"}</h3>
        <p>
          {knowledgeBase?.name || "No hosted KB created"} · {knowledgeBase?.documentCount ?? 0} docs ·{" "}
          {knowledgeBase?.chunkCount ?? 0} chunks
        </p>
      </article>

      <form className="panel auth-form" onSubmit={handleCreate}>
        <p className="eyebrow">1. Create or rename</p>
        <label>
          <span>Knowledge base name</span>
          <input value={name} onChange={(event) => setName(event.target.value)} disabled={isPremiumBlocked || submitting} />
        </label>
        <label>
          <span>Description</span>
          <input
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            disabled={isPremiumBlocked || submitting}
            placeholder="Role packet, company notes, architecture docs, STAR stories"
          />
        </label>
        <button className="button button-primary" type="submit" disabled={isPremiumBlocked || submitting}>
          {submitting ? "Saving..." : "Save Knowledge Base"}
        </button>
      </form>

      <article className="panel support-panel">
        <p className="eyebrow">2. Upload documents</p>
        <h2>Supported: `.txt`, `.md`, `.json`, `.csv`, `.log`, `.docx`</h2>
        <p>
          Premium limits: up to 20 docs total, 5 files per upload, 2 MB per file. If Premium credits hit zero,
          interview-time KB retrieval is blocked in the desktop app until credits return.
        </p>
        <label className="button button-secondary button-file">
          Upload Documents
          <input
            type="file"
            multiple
            onChange={handleUpload}
            disabled={isPremiumBlocked || submitting}
            accept=".txt,.md,.json,.csv,.log,.docx"
          />
        </label>
      </article>

      <article className="panel table-panel table-panel-full">
        <p className="eyebrow">Processed documents</p>
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Type</th>
              <th>Chars</th>
              <th>Chunks</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {(knowledgeBase?.documents || []).length === 0 ? (
              <tr>
                <td colSpan="5">No hosted documents processed yet.</td>
              </tr>
            ) : (
              knowledgeBase.documents.map((document) => (
                <tr key={document.documentId}>
                  <td>{document.fileName}</td>
                  <td>{document.sourceType || document.contentType || "file"}</td>
                  <td>{document.characterCount}</td>
                  <td>{document.chunkCount}</td>
                  <td>{document.status}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
        {status && <p className={`status-message ${status.includes("Could not") ? "status-error" : ""}`}>{status}</p>}
      </article>
    </div>
  );
}

function WalletPanel({ accessToken, summary, walletHistory, walletPurchases, paymentCatalog, onWalletUpdated }) {
  const [status, setStatus] = useState("");
  const [submittingTarget, setSubmittingTarget] = useState("");

  async function handleCheckout(target, packCode) {
    setSubmittingTarget(`${target}:${packCode}`);
    setStatus("");

    try {
      const checkout = await createPaymentCheckout(accessToken, { target, packCode });
      await openRazorpayCheckout(checkout, async (response) => {
        await confirmPaymentCheckout(accessToken, {
          checkoutId: checkout.checkoutId,
          razorpayOrderId: response.razorpay_order_id,
          razorpayPaymentId: response.razorpay_payment_id,
          razorpaySignature: response.razorpay_signature
        });
        await onWalletUpdated();
        setStatus("Payment acknowledged. Wallet refresh complete.");
      });
    } catch (error) {
      setStatus(error.message || "Could not start checkout.");
    } finally {
      setSubmittingTarget("");
    }
  }

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
      <article className="panel dashboard-hero-panel">
        <p className="eyebrow">Wallet checkout</p>
        <h1>Buy the lane you need and settle protected continuation debt only when it exists.</h1>
        <p className="hero-text">
          Premium takes runtime priority whenever Premium credits are available. If Premium reaches zero and Pro
          remains, Phantom falls back to Pro BYO. Premium debt settlement is shown only when debt exists.
        </p>
      </article>
      {(paymentCatalog?.proPacks || []).map((pack) => (
        <article className="panel" key={pack.packCode}>
          <p className="story-tag">Pro BYO Pack</p>
          <h3>{pack.label}</h3>
          <p>{pack.description}</p>
          <strong>{formatInr(pack.displayAmountInr)} · {pack.credits} credits</strong>
          <button
            className="button button-primary"
            onClick={() => handleCheckout(pack.target, pack.packCode)}
            disabled={submittingTarget === `${pack.target}:${pack.packCode}`}
          >
            {submittingTarget === `${pack.target}:${pack.packCode}` ? "Opening..." : "Buy Pro Credits"}
          </button>
        </article>
      ))}
      {(paymentCatalog?.premiumPacks || []).map((pack) => (
        <article className="panel" key={pack.packCode}>
          <p className="story-tag">Premium Pack</p>
          <h3>{pack.label}</h3>
          <p>{pack.description}</p>
          <strong>{formatInr(pack.displayAmountInr)} · {pack.credits} credits</strong>
          <button
            className="button button-primary"
            onClick={() => handleCheckout(pack.target, pack.packCode)}
            disabled={submittingTarget === `${pack.target}:${pack.packCode}`}
          >
            {submittingTarget === `${pack.target}:${pack.packCode}` ? "Opening..." : "Buy Premium Credits"}
          </button>
        </article>
      ))}
      {paymentCatalog?.premiumDebtSettlement ? (
        <article className="panel panel-brass">
          <p className="story-tag">Debt Settlement</p>
          <h3>Clear Premium continuation debt</h3>
          <p>
            Outstanding debt: {summary.premiumNegativeCredits.toFixed(2)} Premium credits.
            This direct payment does not add new credits.
          </p>
          <strong>{formatInr(paymentCatalog.premiumDebtSettlement.displayAmountInr)}</strong>
          <button
            className="button button-primary"
            onClick={() => handleCheckout("premium_debt_settlement", "premium_debt_settlement")}
            disabled={submittingTarget === "premium_debt_settlement:premium_debt_settlement"}
          >
            {submittingTarget === "premium_debt_settlement:premium_debt_settlement" ? "Opening..." : "Settle Debt"}
          </button>
        </article>
      ) : null}
      {status && <article className="panel table-panel table-panel-full"><p className={`status-message ${status.includes("acknowledged") ? "" : "status-error"}`}>{status}</p></article>}
      <article className="panel table-panel table-panel-full">
        <p className="eyebrow">Purchase history</p>
        <table>
          <thead>
            <tr>
              <th>Purchase</th>
              <th>Amount</th>
              <th>Credits</th>
              <th>Status</th>
              <th>Created</th>
            </tr>
          </thead>
          <tbody>
            {walletPurchases.length === 0 ? (
              <tr>
                <td colSpan="5">No credit purchases recorded yet.</td>
              </tr>
            ) : (
              walletPurchases.map((item) => (
                <tr key={item.checkoutId}>
                  <td>{item.displayLabel}</td>
                  <td>{formatInr(item.amountInr)}</td>
                  <td>{item.target === "premium_debt_settlement" ? `Debt ${item.premiumDebtCreditsCovered}` : item.credits}</td>
                  <td>{item.status}</td>
                  <td>{formatDate(item.createdAtUtc)}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
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
  const [email, setEmail] = useState(adminSession?.email || "");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (adminSession?.accessToken) {
      navigate("/admin", { replace: true });
    }
  }, [adminSession, navigate]);

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setError("");
    try {
      const session = await loginAdmin({ email, password });
      onAuthenticated(session);
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
          The admin dashboard is isolated from the user dashboard and requires a dedicated admin account.
          Browser access is session-based and password reset is handled through email.
        </p>
        <p className="download-status">
          Local fallback: if no bootstrap admin env vars are set, use <strong>admin@phantom.local</strong> and the current
          <strong> PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY</strong> after restarting the backend.
        </p>
      </section>

      <form className="panel auth-form" onSubmit={handleSubmit}>
        <label>
          <span>Admin email</span>
          <input
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            placeholder="admin@phantom.local"
          />
        </label>
        <label>
          <span>Password</span>
          <input
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            placeholder="Enter your admin password"
          />
        </label>
        <button className="button button-primary" type="submit" disabled={submitting}>
          {submitting ? "Authenticating..." : "Open Admin Dashboard"}
        </button>
        {error && <p className="status-message status-error">{error}</p>}
        <div className="auth-links-row">
          <Link className="subtle-link" to="/admin/forgot-password">
            Forgot password?
          </Link>
        </div>
      </form>
    </main>
  );
}

function AdminForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setStatus("");

    try {
      const result = await requestAdminPasswordReset(email);
      setStatus(result.message || "If that admin account exists, a password reset link has been sent.");
    } catch (error) {
      setStatus(error.message || "Could not request a password reset.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="page auth-layout auth-layout-wide">
      <section className="panel auth-panel">
        <p className="eyebrow">Admin recovery</p>
        <h1>Reset the admin password through email.</h1>
        <p className="hero-text">
          Enter the admin email address and Phantom will send a time-limited password reset link.
        </p>
      </section>

      <form className="panel auth-form" onSubmit={handleSubmit}>
        <label>
          <span>Admin email</span>
          <input
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            placeholder="admin@phantom.local"
          />
        </label>
        <button className="button button-primary" type="submit" disabled={submitting}>
          {submitting ? "Sending..." : "Send Reset Link"}
        </button>
        {status && <p className={`status-message ${status.toLowerCase().includes("could not") ? "status-error" : ""}`}>{status}</p>}
        <div className="auth-links-row">
          <Link className="subtle-link" to="/admin/login">
            Back to admin login
          </Link>
        </div>
      </form>
    </main>
  );
}

function AdminResetPasswordPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const query = new URLSearchParams(location.search);
  const token = query.get("token") || "";
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setStatus("");

    try {
      if (!token) {
        throw new Error("Reset token missing from the URL.");
      }

      if (password !== confirmPassword) {
        throw new Error("Passwords do not match.");
      }

      const result = await resetAdminPassword(token, password);
      setStatus(result.message || "Admin password reset complete.");
      setTimeout(() => {
        navigate("/admin/login", { replace: true });
      }, 1000);
    } catch (error) {
      setStatus(error.message || "Could not reset the password.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="page auth-layout auth-layout-wide">
      <section className="panel auth-panel">
        <p className="eyebrow">Admin reset</p>
        <h1>Choose a new admin password.</h1>
        <p className="hero-text">
          Reset links are single-use and time-limited. Set a strong password before returning to the admin console.
        </p>
      </section>

      <form className="panel auth-form" onSubmit={handleSubmit}>
        <label>
          <span>New password</span>
          <input
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            placeholder="At least 12 characters"
          />
        </label>
        <label>
          <span>Confirm password</span>
          <input
            type="password"
            value={confirmPassword}
            onChange={(event) => setConfirmPassword(event.target.value)}
            placeholder="Re-enter the new password"
          />
        </label>
        <button className="button button-primary" type="submit" disabled={submitting}>
          {submitting ? "Resetting..." : "Reset Password"}
        </button>
        {status && <p className={`status-message ${status.toLowerCase().includes("complete") ? "" : "status-error"}`}>{status}</p>}
      </form>
    </main>
  );
}

function AdminSessionLoadingPage() {
  return (
    <main className="page">
      <section className="panel auth-panel auth-panel-wide">
        <p className="eyebrow">Admin session</p>
        <h1>Restoring admin session…</h1>
      </section>
    </main>
  );
}

function AdminDashboardPage({ adminSession }) {
  const [overview, setOverview] = useState(null);
  const [inventory, setInventory] = useState(null);
  const [paymentOrders, setPaymentOrders] = useState([]);
  const [paymentWebhooks, setPaymentWebhooks] = useState([]);
  const [gmailStatus, setGmailStatus] = useState(null);
  const [catalogRefreshResult, setCatalogRefreshResult] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const location = useLocation();

  useEffect(() => {
    let cancelled = false;

    async function load() {
      setLoading(true);
      setError("");
      try {
        const [overviewData, inventoryData, paymentOrdersData, paymentWebhooksData, gmailStatusData] = await Promise.all([
          fetchAdminOverview(adminSession.accessToken),
          fetchManagedAiAdminInventory(adminSession.accessToken),
          fetchAdminPaymentOrders(adminSession.accessToken),
          fetchAdminPaymentWebhooks(adminSession.accessToken),
          fetchGmailOAuthStatus(adminSession.accessToken)
        ]);

        if (!cancelled) {
          setOverview(overviewData);
          setInventory(inventoryData);
          setPaymentOrders(paymentOrdersData);
          setPaymentWebhooks(paymentWebhooksData);
          setGmailStatus(gmailStatusData);
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
  }, [adminSession.accessToken]);

  async function refreshManagedInventory() {
    const nextInventory = await fetchManagedAiAdminInventory(adminSession.accessToken);
    setInventory(nextInventory);
    return nextInventory;
  }

  async function refreshPayments() {
    const [nextOverview, nextOrders, nextWebhooks, nextGmailStatus] = await Promise.all([
      fetchAdminOverview(adminSession.accessToken),
      fetchAdminPaymentOrders(adminSession.accessToken),
      fetchAdminPaymentWebhooks(adminSession.accessToken),
      fetchGmailOAuthStatus(adminSession.accessToken)
    ]);
    setOverview(nextOverview);
    setPaymentOrders(nextOrders);
    setPaymentWebhooks(nextWebhooks);
    setGmailStatus(nextGmailStatus);
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
          <Route
            index
            element={
              <AdminOverviewPanel
                overview={overview}
                inventory={inventory}
                gmailStatus={gmailStatus}
                accessToken={adminSession.accessToken}
                gmailOauthSuccess={new URLSearchParams(location.search).get("gmail_oauth") === "success"}
                onGmailStatusChanged={setGmailStatus}
              />
            }
          />
          <Route
            path="payments"
            element={
              <AdminPaymentsPanel
                overview={overview}
                paymentOrders={paymentOrders}
                paymentWebhooks={paymentWebhooks}
                onRefresh={refreshPayments}
              />
            }
          />
          <Route
            path="managed-ai"
            element={
              <ManagedAiAdminPanel
                accessToken={adminSession.accessToken}
                inventory={inventory}
                onRefresh={refreshManagedInventory}
                catalogRefreshResult={catalogRefreshResult}
                onCatalogRefreshResult={setCatalogRefreshResult}
              />
            }
          />
        </Routes>
      </section>
    </main>
  );
}

function AdminOverviewPanel({ overview, inventory, gmailStatus, accessToken, gmailOauthSuccess, onGmailStatusChanged }) {
  const credentials = inventory?.credentials || [];
  const providers = inventory?.managedProviders || [];
  const catalogProviders = inventory?.catalogs?.providers || [];
  const [gmailLoading, setGmailLoading] = useState(false);
  const [gmailMessage, setGmailMessage] = useState("");

  useEffect(() => {
    if (gmailOauthSuccess) {
      setGmailMessage("Gmail OAuth completed. Refreshing sender health.");
      fetchGmailOAuthStatus(accessToken)
        .then((status) => {
          onGmailStatusChanged(status);
          setGmailMessage(status?.StatusMessage || "Gmail OAuth status refreshed.");
        })
        .catch((error) => {
          setGmailMessage(error.message || "Could not refresh Gmail OAuth status.");
        });
    }
  }, [accessToken, gmailOauthSuccess, onGmailStatusChanged]);

  async function handleReconnectGmail() {
    setGmailLoading(true);
    setGmailMessage("");
    try {
      const result = await startGmailOAuth(accessToken);
      window.location.href = result.authorizationUrl;
    } catch (error) {
      setGmailMessage(error.message || "Could not start Gmail OAuth.");
      setGmailLoading(false);
    }
  }

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
      <article className="panel metric-panel">
        <span>Catalog models</span>
        <strong>{catalogProviders.reduce((total, provider) => total + (provider.models?.length || 0), 0)}</strong>
      </article>
      <article className="panel table-panel table-panel-full">
        <p className="eyebrow">Gmail sender health</p>
        <div className="admin-panel-head">
          <div>
            <h3>{gmailStatus?.StatusLabel || "Unknown"}</h3>
            <p>{gmailStatus?.StatusMessage || "Gmail OAuth status has not loaded yet."}</p>
          </div>
          <div className="admin-panel-actions">
            <button
              className="button button-primary button-compact"
              type="button"
              onClick={handleReconnectGmail}
              disabled={gmailLoading}
            >
              {gmailLoading ? "Redirecting..." : gmailStatus?.HasRefreshToken ? "Reconnect Gmail" : "Connect Gmail"}
            </button>
          </div>
        </div>
        <div className="support-metrics">
          <div>
            <span>Configured</span>
            <strong>{gmailStatus?.IsConfigured ? "Yes" : "No"}</strong>
          </div>
          <div>
            <span>Refresh token</span>
            <strong>{gmailStatus?.HasRefreshToken ? "Stored" : "Missing"}</strong>
          </div>
          <div>
            <span>Token valid</span>
            <strong>{gmailStatus?.HasValidRefreshToken ? "Yes" : "No"}</strong>
          </div>
          <div>
            <span>Sender</span>
            <strong>{gmailStatus?.FromEmail || "n/a"}</strong>
          </div>
        </div>
        {gmailMessage && <p className={`status-message ${gmailMessage.toLowerCase().includes("could not") ? "status-error" : ""}`}>{gmailMessage}</p>}
      </article>
      <article className="panel table-panel table-panel-full">
        <p className="eyebrow">Managed provider readiness</p>
        <table>
          <thead>
            <tr>
              <th>Provider</th>
              <th>Configured credentials</th>
              <th>Fetched models</th>
              <th>Catalog refreshed</th>
            </tr>
          </thead>
          <tbody>
            {providers.map((provider) => (
              <tr key={provider.providerId}>
                <td>{provider.label}</td>
                <td>{credentials.filter((item) => item.providerId === provider.providerId).length}</td>
                <td>{catalogProviders.find((item) => item.providerId === provider.providerId)?.models?.length || 0}</td>
                <td>{formatDate(catalogProviders.find((item) => item.providerId === provider.providerId)?.refreshedAtUtc)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </article>
    </div>
  );
}

function ManagedAiAdminPanel({
  accessToken,
  inventory,
  onRefresh,
  catalogRefreshResult,
  onCatalogRefreshResult
}) {
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
  const catalogProviders = inventory?.catalogs?.providers || [];
  const refreshProviders = catalogRefreshResult?.providers || [];

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
      await upsertManagedAiCredential(accessToken, {
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
      await deleteManagedAiCredential(accessToken, credentialId);
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
      const refreshResult = await triggerManagedAiCatalogRefresh(accessToken);
      onCatalogRefreshResult(refreshResult);
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
        <p className="eyebrow">Catalog status by provider</p>
        <table>
          <thead>
            <tr>
              <th>Provider</th>
              <th>Enabled creds</th>
              <th>Fetched models</th>
              <th>Catalog refreshed</th>
              <th>Last refresh outcome</th>
            </tr>
          </thead>
          <tbody>
            {providers.map((provider) => {
              const enabledCredentialCount = credentials.filter(
                (item) => item.providerId === provider.providerId && item.isEnabled
              ).length;
              const catalog = catalogProviders.find((item) => item.providerId === provider.providerId);
              const refreshResult = refreshProviders.find((item) => item.providerId === provider.providerId);

              return (
                <tr key={provider.providerId}>
                  <td>{provider.label}</td>
                  <td>{enabledCredentialCount}</td>
                  <td>{catalog?.models?.length || 0}</td>
                  <td>{formatDate(catalog?.refreshedAtUtc)}</td>
                  <td>{refreshResult ? refreshResult.message : "No refresh run in this session."}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </article>

      {catalogRefreshResult && (
        <article className="panel table-panel table-panel-full">
          <p className="eyebrow">Latest refresh result</p>
          <table>
            <thead>
              <tr>
                <th>Provider</th>
                <th>Status</th>
                <th>Models</th>
                <th>Catalog timestamp</th>
                <th>Message</th>
              </tr>
            </thead>
            <tbody>
              {refreshProviders.map((item) => (
                <tr key={item.providerId}>
                  <td>{item.label}</td>
                  <td>{item.succeeded ? "Success" : item.attempted ? "Failed" : "Skipped"}</td>
                  <td>{item.modelCount}</td>
                  <td>{formatDate(item.refreshedAtUtc)}</td>
                  <td>{item.message}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </article>
      )}

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

      {providers.map((provider) => {
        const catalog = catalogProviders.find((item) => item.providerId === provider.providerId);
        const providerModels = catalog?.models || [];

        return (
          <article className="panel table-panel table-panel-full" key={`${provider.providerId}-catalog`}>
            <p className="eyebrow">{provider.label} catalog</p>
            <h3>
              {providerModels.length} model{providerModels.length === 1 ? "" : "s"} fetched
            </h3>
            <p>
              Last updated: {formatDate(catalog?.refreshedAtUtc)}
            </p>
            <table>
              <thead>
                <tr>
                  <th>Model ID</th>
                  <th>Display name</th>
                  <th>Vision</th>
                </tr>
              </thead>
              <tbody>
                {providerModels.length === 0 ? (
                  <tr>
                    <td colSpan="3">No stored catalog for this provider yet.</td>
                  </tr>
                ) : (
                  providerModels.map((model) => (
                    <tr key={`${provider.providerId}-${model.modelId}`}>
                      <td>{model.modelId}</td>
                      <td>{model.displayName}</td>
                      <td>{model.supportsVision ? "Yes" : "No"}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </article>
        );
      })}
    </div>
  );
}

function AdminPaymentsPanel({ overview, paymentOrders, paymentWebhooks, onRefresh }) {
  const [statusFilter, setStatusFilter] = useState("all");
  const [query, setQuery] = useState("");
  const [selectedOrder, setSelectedOrder] = useState(null);
  const [selectedWebhook, setSelectedWebhook] = useState(null);

  const normalizedQuery = query.trim().toLowerCase();
  const filteredOrders = paymentOrders.filter((item) => {
    const matchesStatus = statusFilter === "all" || item.status === statusFilter;
    const haystack = [
      item.checkoutId,
      item.email,
      item.userId,
      item.razorpayOrderId,
      item.razorpayPaymentId,
      item.displayLabel,
      item.target
    ].join(" ").toLowerCase();
    const matchesQuery = !normalizedQuery || haystack.includes(normalizedQuery);
    return matchesStatus && matchesQuery;
  });

  const filteredWebhooks = paymentWebhooks.filter((item) => {
    const haystack = [
      item.externalEventId,
      item.eventType,
      item.payloadJson
    ].join(" ").toLowerCase();
    return !normalizedQuery || haystack.includes(normalizedQuery);
  });

  return (
    <div className="dashboard-grid admin-grid">
      <article className="panel admin-hero-panel">
        <p className="eyebrow">Payments operations</p>
        <h1>Monitor checkout state, credit application, and Razorpay webhook processing from one admin surface.</h1>
        <p className="hero-text">
          Orders should progress from `created` to `client_confirmed` to `credited`. Webhook visibility here helps
          diagnose when a payment succeeded at checkout but wallet credit did not arrive.
        </p>
      </article>

      <article className="panel metric-panel">
        <span>Payment orders</span>
        <strong>{overview?.paymentOrderCount ?? 0}</strong>
      </article>
      <article className="panel metric-panel">
        <span>Credited orders</span>
        <strong>{overview?.creditedPaymentCount ?? 0}</strong>
      </article>
      <article className="panel metric-panel">
        <span>Webhook events</span>
        <strong>{overview?.paymentWebhookCount ?? 0}</strong>
      </article>
      <article className="panel metric-panel">
        <span>Processed webhooks</span>
        <strong>{overview?.processedWebhookCount ?? 0}</strong>
      </article>

      <article className="panel admin-form-panel">
        <div className="admin-panel-head">
          <div>
            <p className="story-tag">Filters</p>
            <h3>Transactions and callbacks</h3>
          </div>
          <div className="admin-panel-actions">
            <button className="button button-secondary button-compact" type="button" onClick={onRefresh}>
              Refresh
            </button>
          </div>
        </div>
        <div className="admin-form">
          <label>
            Status
            <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
              <option value="all">All</option>
              <option value="created">created</option>
              <option value="client_confirmed">client_confirmed</option>
              <option value="credited">credited</option>
            </select>
          </label>
          <label>
            Search
            <input
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="email, checkout ID, Razorpay order ID, payment ID"
            />
          </label>
        </div>
      </article>

      <article className="panel table-panel table-panel-full">
        <p className="eyebrow">Recent payment orders</p>
        <table>
          <thead>
            <tr>
              <th>User</th>
              <th>Purchase</th>
              <th>Amount</th>
              <th>Status</th>
              <th>Ops state</th>
              <th>Checkout</th>
              <th>Created</th>
              <th>Detail</th>
            </tr>
          </thead>
          <tbody>
            {filteredOrders.length === 0 ? (
              <tr>
                <td colSpan="8">No payment orders matched the current filters.</td>
              </tr>
            ) : (
              filteredOrders.map((item) => (
                <tr key={item.checkoutId}>
                  <td>{item.email}</td>
                  <td>{item.displayLabel}</td>
                  <td>{formatInr(item.amountInr)}</td>
                  <td>{item.status}</td>
                  <td>{renderPaymentOpsState(item)}</td>
                  <td>{item.checkoutId}</td>
                  <td>{formatDate(item.createdAtUtc)}</td>
                  <td>
                    <button className="table-action" type="button" onClick={() => setSelectedOrder(item)}>
                      Inspect
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </article>

      {selectedOrder && (
        <article className="panel table-panel table-panel-full">
          <p className="eyebrow">Selected order detail</p>
          <table>
            <tbody>
              <tr><th>Checkout ID</th><td>{selectedOrder.checkoutId}</td></tr>
              <tr><th>User</th><td>{selectedOrder.email} · {selectedOrder.userId}</td></tr>
              <tr><th>Target</th><td>{selectedOrder.target}</td></tr>
              <tr><th>Pack</th><td>{selectedOrder.packCode}</td></tr>
              <tr><th>Amount</th><td>{formatInr(selectedOrder.amountInr)}</td></tr>
              <tr><th>Credits</th><td>{selectedOrder.credits}</td></tr>
              <tr><th>Debt covered</th><td>{selectedOrder.premiumDebtCreditsCovered}</td></tr>
              <tr><th>Status</th><td>{selectedOrder.status}</td></tr>
              <tr><th>Ops state</th><td>{describePaymentOpsState(selectedOrder)}</td></tr>
              <tr><th>Client confirmed</th><td>{selectedOrder.clientConfirmed ? "Yes" : "No"}</td></tr>
              <tr><th>Razorpay order</th><td>{selectedOrder.razorpayOrderId || "n/a"}</td></tr>
              <tr><th>Razorpay payment</th><td>{selectedOrder.razorpayPaymentId || "n/a"}</td></tr>
              <tr><th>Credited at</th><td>{formatDate(selectedOrder.creditedAtUtc)}</td></tr>
              <tr><th>Updated</th><td>{formatDate(selectedOrder.updatedAtUtc)}</td></tr>
            </tbody>
          </table>
        </article>
      )}

      <article className="panel table-panel table-panel-full">
        <p className="eyebrow">Recent webhook callbacks</p>
        <table>
          <thead>
            <tr>
              <th>Event ID</th>
              <th>Type</th>
              <th>Created</th>
              <th>Processed</th>
              <th>Detail</th>
            </tr>
          </thead>
          <tbody>
            {filteredWebhooks.length === 0 ? (
              <tr>
                <td colSpan="5">No webhook events matched the current filters.</td>
              </tr>
            ) : (
              filteredWebhooks.map((item) => (
                <tr key={item.eventRecordId}>
                  <td>{item.externalEventId}</td>
                  <td>{item.eventType}</td>
                  <td>{formatDate(item.createdAtUtc)}</td>
                  <td>{formatDate(item.processedAtUtc)}</td>
                  <td>
                    <button className="table-action" type="button" onClick={() => setSelectedWebhook(item)}>
                      Inspect
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </article>

      {selectedWebhook && (
        <article className="panel table-panel table-panel-full">
          <p className="eyebrow">Selected webhook payload</p>
          <p className="hero-text">
            {selectedWebhook.eventType} · {selectedWebhook.externalEventId}
          </p>
          <pre className="payload-preview">{prettyJson(selectedWebhook.payloadJson)}</pre>
        </article>
      )}
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

function parseUtcMillis(value) {
  if (!value) {
    return 0;
  }

  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? 0 : parsed;
}

function getBrowserRegistrationFingerprint() {
  const existing = readStoredJson("phantom.website.device-profile");
  if (existing?.deviceFingerprintHash) {
    return existing.deviceFingerprintHash;
  }

  const installId = `web-${crypto.randomUUID()}`;
  const deviceProfile = {
    appVersion: "phantom-website-dashboard",
    installId,
    deviceLabel: "Browser Dashboard",
    deviceFingerprintHash: `browser-${installId}`,
    secretFingerprintHint: "browser"
  };
  writeStoredJson("phantom.website.device-profile", deviceProfile);
  return deviceProfile.deviceFingerprintHash;
}

function formatInr(value) {
  return new Intl.NumberFormat("en-IN", {
    style: "currency",
    currency: "INR",
    maximumFractionDigits: 0
  }).format(value || 0);
}

function prettyJson(value) {
  if (!value) {
    return "n/a";
  }

  try {
    return JSON.stringify(typeof value === "string" ? JSON.parse(value) : value, null, 2);
  } catch {
    return String(value);
  }
}

function getPaymentOpsState(order) {
  if (order.creditedAtUtc || order.status === "credited") {
    return "credited";
  }

  if (order.clientConfirmed || order.status === "client_confirmed") {
    const createdAt = order.createdAtUtc ? new Date(order.createdAtUtc).getTime() : 0;
    const minutesOpen = createdAt ? (Date.now() - createdAt) / 60000 : 0;
    return minutesOpen >= 2 ? "stuck_waiting_webhook" : "waiting_webhook";
  }

  return "created";
}

function describePaymentOpsState(order) {
  const state = getPaymentOpsState(order);
  switch (state) {
    case "credited":
      return "Wallet mutation applied after trusted backend confirmation.";
    case "waiting_webhook":
      return "Checkout succeeded in the browser. Backend is waiting for the Razorpay webhook to credit the wallet.";
    case "stuck_waiting_webhook":
      return "Client confirmed but still not credited after 2+ minutes. Check ngrok delivery, webhook URL, webhook secret, and backend logs.";
    default:
      return "Order created, but browser confirmation has not been recorded yet.";
  }
}

function renderPaymentOpsState(order) {
  const state = getPaymentOpsState(order);
  const label = state === "credited"
    ? "Credited"
    : state === "waiting_webhook"
      ? "Waiting webhook"
      : state === "stuck_waiting_webhook"
        ? "Stuck"
        : "Created";

  return <span className={`header-badge ${state === "stuck_waiting_webhook" ? "header-badge-brass" : ""}`}>{label}</span>;
}

async function loadRazorpayScript() {
  if (window.Razorpay) {
    return;
  }

  await new Promise((resolve, reject) => {
    const existing = document.querySelector('script[data-phantom-razorpay="true"]');
    if (existing) {
      existing.addEventListener("load", resolve, { once: true });
      existing.addEventListener("error", reject, { once: true });
      return;
    }

    const script = document.createElement("script");
    script.src = "https://checkout.razorpay.com/v1/checkout.js";
    script.async = true;
    script.dataset.phantomRazorpay = "true";
    script.onload = resolve;
    script.onerror = () => reject(new Error("Could not load Razorpay checkout."));
    document.body.appendChild(script);
  });
}

async function openRazorpayCheckout(checkout, onSuccess) {
  await loadRazorpayScript();

  await new Promise((resolve, reject) => {
    const razorpay = new window.Razorpay({
      key: checkout.razorpayKeyId,
      amount: checkout.amountMinor,
      currency: checkout.currency,
      name: "Phantom",
      description: checkout.displayLabel,
      order_id: checkout.razorpayOrderId,
      handler: async (response) => {
        try {
          await onSuccess(response);
          resolve();
        } catch (error) {
          reject(error);
        }
      },
      modal: {
        ondismiss: () => reject(new Error("Checkout was dismissed."))
      }
    });

    razorpay.open();
  });
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
