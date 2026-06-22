import { useEffect, useMemo, useRef, useState } from "react";
import { Link, NavLink, Navigate, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import {
  clearAdminLock,
  confirmPaymentCheckout,
  createHostedKnowledgeBase,
  createPaymentCheckout,
  deleteManagedAiCredential,
  fetchAccountSummary,
  fetchAdminOverview,
  fetchAdminPaymentOrders,
  fetchAdminPaymentWebhooks,
  fetchAdminUser,
  fetchAdminUsers,
  fetchCurrentAdminSession,
  fetchCurrentUserSession,
  fetchDevices,
  fetchDownloadEntitlement,
  fetchGmailOAuthStatus,
  fetchHostedKnowledgeBase,
  fetchManagedAiAdminInventory,
  fetchPaymentCatalog,
  fetchSupportOverview,
  fetchWalletHistory,
  fetchWalletPurchases,
  grantAdminCredits,
  loginAccount,
  loginAdmin,
  logoutAccount,
  logoutAdmin,
  refreshAccountSession,
  refreshAdminSession,
  registerAccount,
  requestAdminPasswordReset,
  resendVerificationEmail,
  resetAdminPassword,
  sendPhoneOtp,
  startGmailOAuth,
  triggerManagedAiCatalogRefresh,
  updateAdminUser,
  updateManagedAiModelVision,
  updateManagedAiRuntimeSelection,
  uploadHostedKnowledgeBaseDocuments,
  upsertManagedAiCredential,
  verifyPhoneOtp,
  waiveAdminPremiumDebt
} from "./lib/api";

const publicNav = [
  { to: "/", label: "Product" },
  { to: "/pricing", label: "Plans" },
  { to: "/download", label: "Download" },
  { to: "/privacy", label: "Privacy" }
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
  { to: "/admin/users", label: "Users" },
  { to: "/admin/payments", label: "Payments" },
  { to: "/admin/managed-ai", label: "Managed AI" }
];

const plans = [
  {
    name: "Free Trial",
    badge: "Managed Demo",
    price: "₹0",
    tone: "mist",
    summary: "Get a hosted trial lane before you commit to credits or provider setup.",
    bullets: [
      "Phantom-managed AI lane",
      "2 guided trial sessions, 20 minutes each",
      "Phone OTP and email verification required",
      "No provider key setup"
    ],
    cta: "Create Free Account",
    to: "/register"
  },
  {
    name: "Pro BYO",
    badge: "Operator Control",
    price: "₹699 to ₹2,499",
    tone: "signal",
    summary: "Use the Phantom runtime while keeping model choice and API spend in your own provider accounts.",
    bullets: [
      "Up to 3 supported providers",
      "Up to 2 keys per provider",
      "3, 8, or 15 Pro credit packs",
      "Offline resume and lock safeguards"
    ],
    cta: "Review Download Path",
    to: "/download"
  },
  {
    name: "Premium AI",
    badge: "Hands-Off Hosted",
    price: "₹1,799 to ₹5,599",
    tone: "emerald",
    summary: "Run Premium with managed providers, hosted knowledge base sync, and operational support visibility.",
    bullets: [
      "Managed provider lanes across major model vendors",
      "Hosted knowledge base synced into the desktop runtime",
      "3, 8, or 15 Premium credit packs",
      "Protected continuation debt can be settled explicitly"
    ],
    cta: "See Premium Workflow",
    to: "/pricing"
  }
];

const publicHighlights = [
  {
    title: "Built for live pressure",
    copy:
      "Phantom is designed for the moments when interviews move fast and you need your thinking, preparation, and AI support to stay within reach."
  },
  {
    title: "Flexible from day one",
    copy:
      "Start with managed AI, switch to your own provider stack when you want more control, or move into Premium when you want a more complete setup."
  },
  {
    title: "Preparation that stays close",
    copy:
      "Keep role research, company notes, personal stories, and premium knowledge-base context ready without turning your interview into a tab-management exercise."
  }
];

const publicFeatureRows = [
  { label: "Live support", value: "Fast AI help when the conversation shifts" },
  { label: "Flexible setup", value: "Hosted AI or your own providers" },
  { label: "Context ready", value: "Prep material close when you need it" },
  { label: "Windows workspace", value: "Focused app built for live rounds" }
];

const publicJourney = [
  {
    title: "Prepare with intent",
    body:
      "Organize role context, company research, notes, and the stories you want ready before the interview starts."
  },
  {
    title: "Launch with confidence",
    body:
      "Open Phantom and start with the AI setup that fits you, whether that means managed AI or your own provider lane."
  },
  {
    title: "Stay sharp in the moment",
    body:
      "Use the workspace to clarify ideas, shape answers, and recover quickly when the interview gets fast or unpredictable."
  }
];

const publicValueProps = [
  {
    title: "A real interview workspace",
    detail:
      "Phantom gives you a dedicated environment for live rounds instead of forcing everything into a browser tab."
  },
  {
    title: "Hosted or BYO AI",
    detail:
      "You can begin with Phantom-managed AI and move to your own model stack when you want more control over providers and spend."
  },
  {
    title: "Premium context when it matters",
    detail:
      "Premium workflows can keep deeper context available through a hosted knowledge base so the product feels more prepared with every round."
  }
];

const publicFaqs = [
  {
    question: "What is Phantom?",
    answer:
      "Phantom is a Windows interview workspace that helps you stay prepared and responsive in live rounds. It combines a focused desktop experience with account, wallet, and context management on the web."
  },
  {
    question: "Is Phantom browser-based?",
    answer:
      "The core interview workspace runs on Windows. The website is where you create your account, manage access, compare plans, review wallet activity, and handle premium knowledge-base setup."
  },
  {
    question: "Do I need my own API keys?",
    answer:
      "Not necessarily. You can start on Phantom-managed AI. If you prefer more control, Pro BYO lets you connect your own provider accounts later."
  },
  {
    question: "Can I try Phantom before choosing a paid plan?",
    answer:
      "Yes. The free trial is built for that. It lets you experience the workflow before you commit to a paid lane."
  }
];

const privacySections = [
  {
    title: "1. Scope",
    body:
      "This Privacy Policy applies to the Phantom website, the user dashboard, the admin dashboard, and the hosted services that support the Phantom desktop runtime. It covers what Phantom collects, why it is used, and how that data supports account access, payments, device trust, support, and hosted AI features."
  },
  {
    title: "2. Data Phantom collects",
    body:
      "Phantom may collect account identifiers such as email address and password hash, phone-verification details, browser or device profile identifiers, desktop installation identifiers, session and lock metadata, payment and wallet records, hosted knowledge-base documents and derived metadata, support and operational telemetry, admin-auth records, and browser session cookies required to keep dashboard access working."
  },
  {
    title: "3. Why the data is used",
    body:
      "Phantom uses this data to create and secure accounts, verify phone and email ownership, determine download and runtime eligibility, reconcile wallet usage, process payments, support premium knowledge-base features, investigate lock or session issues, monitor abuse, and maintain operational reliability for the desktop and hosted control surfaces."
  },
  {
    title: "4. Third-party service providers",
    body:
      "Phantom may rely on third parties that are necessary for the service, such as payment processors, OTP or messaging providers, email delivery providers, cloud hosting vendors, analytics or telemetry processors, and AI model providers used for hosted lanes. Phantom should only share the minimum information required for those integrations to operate."
  },
  {
    title: "5. Hosted knowledge-base content",
    body:
      "If you upload documents to the hosted knowledge base, Phantom may store the files, extracted text, chunks, embeddings, and related metadata needed to make retrieval available in the desktop runtime. Do not upload regulated, confidential, or third-party material unless you have the right to do so and the deployment is configured to handle it."
  },
  {
    title: "6. Payments and financial data",
    body:
      "Phantom should not store raw card details. Payment processors should handle payment instruments directly. Phantom may store order identifiers, payment status, wallet credits, debt-settlement records, webhook events, and other transaction metadata required to reconcile credit packs and support audits."
  },
  {
    title: "7. Security and retention",
    body:
      "Phantom uses reasonable administrative, technical, and operational safeguards to protect account, payment, device, and hosted-content data. Data should be retained only for as long as it is needed for product operation, support, fraud prevention, financial reconciliation, legal compliance, or dispute resolution."
  },
  {
    title: "8. Your choices and requests",
    body:
      "Users should be able to request access, correction, or deletion of account-linked data where applicable, subject to security, billing, abuse-prevention, and legal-retention constraints. The correct contact route is the support channel configured for the Phantom deployment you use."
  },
  {
    title: "9. Children and sensitive use",
    body:
      "Phantom is not intended for children. It should not be used to process sensitive personal data, regulated records, or protected third-party information unless the deployment operator has separately implemented the controls, notices, and agreements required for that data class."
  },
  {
    title: "10. Changes",
    body:
      "Phantom may update this policy as the product changes. Material changes should be published on the website with an updated effective date before the new terms are relied on in production."
  }
];

const termsSections = [
  {
    title: "1. Service boundaries",
    body:
      "Phantom is a hosted website and dashboard layer paired with a Windows desktop runtime. The website is for registration, verification, payments, hosted knowledge-base management, device visibility, and admin operations. It is not the live interview runtime itself."
  },
  {
    title: "2. Account responsibility",
    body:
      "You are responsible for the accuracy of registration information, the security of your credentials, and all activity that occurs under your account. You must keep access credentials confidential and notify the deployment operator if you suspect unauthorized access."
  },
  {
    title: "3. Acceptable use",
    body:
      "You may only use Phantom for lawful, authorized purposes. You must comply with the rules of the interview, exam, employer, institution, or platform where Phantom is used. You must not use Phantom to bypass proctoring, impersonate another person, violate confidentiality obligations, upload unauthorized third-party content, attempt to extract provider secrets, or interfere with system integrity."
  },
  {
    title: "4. AI output and user judgment",
    body:
      "AI outputs can be incomplete, inaccurate, or inappropriate for the situation. You remain responsible for reviewing and deciding whether to rely on any generated content, suggestions, or retrieved knowledge-base material."
  },
  {
    title: "5. Payments, credits, and debt settlement",
    body:
      "Credit packs, hosted usage, and debt-settlement flows must follow the wallet rules defined by the deployment operator. Phantom may suspend access or limit premium features when credits are exhausted, balances become negative, or payment confirmation cannot be trusted."
  },
  {
    title: "6. Hosted content",
    body:
      "You represent that you have the right to upload and process any document, prompt, key, note, or other material you submit to Phantom. Do not upload confidential or regulated material unless your deployment is explicitly authorized for that use."
  },
  {
    title: "7. Suspension and termination",
    body:
      "Phantom may suspend or terminate access for security incidents, unpaid balances, abuse, fraud risk, policy violations, or system-protection reasons. Admin operators may also correct account state, clear locks, or revoke access when required to preserve service integrity."
  },
  {
    title: "8. No warranty for uninterrupted availability",
    body:
      "Phantom aims for reliable service, but hosted components may be interrupted by provider outages, payment failures, verification issues, network disruptions, or maintenance. Availability of the website does not guarantee the availability of any third-party provider lane."
  },
  {
    title: "9. Limitation and operator terms",
    body:
      "These terms should be read together with any deployment-specific commercial, legal, or support terms published by the operator of your Phantom environment. Where local law requires additional notices, refunds, disclosures, or rights, those rules continue to apply."
  },
  {
    title: "10. Contact",
    body:
      "For legal, privacy, billing, or support requests, use the support route exposed by the Phantom deployment you use, including the dashboard support surface or the contact details published by the operator."
  }
];

export default function App() {
  const location = useLocation();
  const [userSession, setUserSession] = useState(null);
  const [userSessionReady, setUserSessionReady] = useState(false);
  const [adminSession, setAdminSession] = useState(null);
  const [adminSessionReady, setAdminSessionReady] = useState(false);
  const [userSessionHydrationEnabled, setUserSessionHydrationEnabled] = useState(true);
  const [adminSessionHydrationEnabled, setAdminSessionHydrationEnabled] = useState(true);
  const userLogoutInFlightRef = useRef(false);
  const adminLogoutInFlightRef = useRef(false);

  function handleUserAuthenticated(session) {
    setUserSessionHydrationEnabled(true);
    setUserSession(session);
  }

  function handleAdminAuthenticated(session) {
    setAdminSessionHydrationEnabled(true);
    setAdminSession(session);
  }

  async function handleUserLogout() {
    userLogoutInFlightRef.current = true;
    setUserSessionHydrationEnabled(false);
    try {
      await logoutAccount();
    } catch {
      // Best-effort logout.
    } finally {
      setUserSession(null);
      setUserSessionReady(true);
      userLogoutInFlightRef.current = false;
    }
  }

  async function handleAdminLogout() {
    adminLogoutInFlightRef.current = true;
    setAdminSessionHydrationEnabled(false);
    try {
      await logoutAdmin();
    } catch {
      // Best-effort logout.
    } finally {
      setAdminSession(null);
      setAdminSessionReady(true);
      adminLogoutInFlightRef.current = false;
    }
  }

  useEffect(() => {
    let cancelled = false;
    let refreshTimer = 0;

    async function hydrateUserSession() {
      if (userLogoutInFlightRef.current) {
        if (!cancelled) {
          setUserSessionReady(true);
        }
        return;
      }

      if (!userSession?.isAuthenticated && !userSessionHydrationEnabled) {
        if (!cancelled) {
          setUserSessionReady(true);
        }
        return;
      }

      if (!userSession?.isAuthenticated) {
        try {
          const refreshed = await refreshAccountSession();
          if (!cancelled) {
            handleUserAuthenticated(refreshed);
          }
        } catch {
          try {
            const current = await fetchCurrentUserSession();
            if (!cancelled) {
              handleUserAuthenticated(current);
            }
          } catch {
            if (!cancelled) {
              setUserSession(null);
            }
          } finally {
            if (!cancelled) {
              setUserSessionReady(true);
            }
          }
        }
        return;
      }

      const expiresAt = parseUtcMillis(userSession.expiresAtUtc);
      const shouldRefresh = !expiresAt || expiresAt <= Date.now() + 5 * 60 * 1000;

      if (!shouldRefresh) {
        if (!cancelled) {
          setUserSessionReady(true);
        }
        refreshTimer = window.setTimeout(() => {
          hydrateUserSession();
        }, Math.max(expiresAt - Date.now() - 5 * 60 * 1000, 1000));
        return;
      }

      try {
        const refreshed = await refreshAccountSession();
        if (!cancelled) {
          handleUserAuthenticated(refreshed);
        }
      } catch {
        if (!cancelled) {
          setUserSession(null);
        }
      } finally {
        if (!cancelled) {
          setUserSessionReady(true);
        }
      }
    }

    hydrateUserSession();
    return () => {
      cancelled = true;
      window.clearTimeout(refreshTimer);
    };
  }, [userSession?.expiresAtUtc, userSession?.isAuthenticated, userSessionHydrationEnabled]);

  useEffect(() => {
    let cancelled = false;
    let refreshTimer = 0;

    async function hydrateAdminSession() {
      if (adminLogoutInFlightRef.current) {
        if (!cancelled) {
          setAdminSessionReady(true);
        }
        return;
      }

      if (!adminSession?.isAuthenticated && !adminSessionHydrationEnabled) {
        if (!cancelled) {
          setAdminSessionReady(true);
        }
        return;
      }

      if (!adminSession?.isAuthenticated) {
        try {
          const refreshed = await refreshAdminSession();
          if (!cancelled) {
            handleAdminAuthenticated(refreshed);
          }
        } catch {
          try {
            const current = await fetchCurrentAdminSession();
            if (!cancelled) {
              handleAdminAuthenticated(current);
            }
          } catch {
            if (!cancelled) {
              setAdminSession(null);
            }
          } finally {
            if (!cancelled) {
              setAdminSessionReady(true);
            }
          }
        }
        return;
      }

      const expiresAt = parseUtcMillis(adminSession.expiresAtUtc);
      const shouldRefresh = !expiresAt || expiresAt <= Date.now() + 5 * 60 * 1000;

      if (!shouldRefresh) {
        if (!cancelled) {
          setAdminSessionReady(true);
        }
        refreshTimer = window.setTimeout(() => {
          hydrateAdminSession();
        }, Math.max(expiresAt - Date.now() - 5 * 60 * 1000, 1000));
        return;
      }

      try {
        const refreshed = await refreshAdminSession();
        if (!cancelled) {
          handleAdminAuthenticated(refreshed);
        }
      } catch {
        if (!cancelled) {
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
  }, [adminSession?.expiresAtUtc, adminSession?.isAuthenticated, adminSessionHydrationEnabled]);

  const surface = location.pathname.startsWith("/admin")
    ? "admin"
    : location.pathname.startsWith("/dashboard")
      ? "user"
      : "public";

  return (
    <div className={`app-shell surface-${surface}`}>
      <div className="ambient ambient-one" />
      <div className="ambient ambient-two" />
      <SiteHeader
        surface={surface}
        userSession={userSession}
        adminSession={adminSession}
        onUserLogout={handleUserLogout}
        onAdminLogout={handleAdminLogout}
      />

      <Routes>
        <Route
          path="/"
          element={<MarketingPage userSession={userSession} adminSession={adminSession} />}
        />
        <Route path="/pricing" element={<PricingPage />} />
        <Route path="/download" element={<DownloadPage userSession={userSession} />} />
        <Route
          path="/login"
          element={
            <UserLoginPage userSession={userSession} onAuthenticated={handleUserAuthenticated} />
          }
        />
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/desktop-return" element={<DesktopReturnPage />} />
        <Route path="/privacy" element={<PrivacyPolicyPage />} />
        <Route path="/terms" element={<TermsPage />} />
        <Route
          path="/dashboard/*"
          element={
            <RequireUserSession ready={userSessionReady} session={userSession}>
              <UserDashboardPage session={userSession} />
            </RequireUserSession>
          }
        />
        <Route
          path="/admin/login"
          element={
            <AdminLoginPage adminSession={adminSession} onAuthenticated={handleAdminAuthenticated} />
          }
        />
        <Route path="/admin/forgot-password" element={<AdminForgotPasswordPage />} />
        <Route path="/admin/reset-password" element={<AdminResetPasswordPage />} />
        <Route
          path="/admin/*"
          element={
            <RequireAdminSession ready={adminSessionReady} session={adminSession}>
              <AdminDashboardPage adminSession={adminSession} />
            </RequireAdminSession>
          }
        />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>

      {surface === "public" ? <PublicFooter /> : null}
    </div>
  );
}

function SiteHeader({ surface, userSession, adminSession, onUserLogout, onAdminLogout }) {
  const location = useLocation();
  const navItems = surface === "admin" ? adminNav : surface === "user" ? userNav : publicNav;
  const session = surface === "admin" ? adminSession : userSession;

  return (
    <header className="site-header">
      <Link className="brandmark" to="/">
        <span className="brandmark-mark">P</span>
        <span>
          <strong>Phantom</strong>
          <small>Protected Interview Runtime</small>
        </span>
      </Link>

      <nav className="top-nav" aria-label="Primary">
        {navItems.map((item) => (
          <NavLink
            key={item.to}
            className={({ isActive }) => `nav-chip ${isActive ? "nav-chip-active" : ""}`}
            to={item.to}
            end={item.to === "/" || item.to === "/dashboard" || item.to === "/admin"}
          >
            {item.label}
          </NavLink>
        ))}
      </nav>

      <div className="header-actions">
        {surface === "public" ? (
          <>
            <Link className="button button-ghost button-compact" to="/login">
              Sign In
            </Link>
            <Link className="button button-primary button-compact" to="/register">
              Get Started
            </Link>
          </>
        ) : session?.email ? (
          <>
            <span className="header-badge">
              {surface === "admin" ? "Admin" : "User"} · {session.email}
            </span>
            <button
              className="button button-ghost button-compact"
              type="button"
              onClick={surface === "admin" ? onAdminLogout : onUserLogout}
            >
              Sign Out
            </button>
          </>
        ) : location.pathname.startsWith("/admin") ? (
          <Link className="button button-ghost button-compact" to="/admin/login">
            Admin Login
          </Link>
        ) : (
          <Link className="button button-ghost button-compact" to="/login">
            Sign In
          </Link>
        )}
      </div>
    </header>
  );
}

function PublicFooter() {
  return (
    <footer className="site-footer">
      <div className="footer-grid">
        <div>
          <p className="eyebrow">Phantom</p>
          <p className="footer-copy">
            A focused interview workspace for people who want to feel more prepared in live rounds.
          </p>
        </div>
        <div className="footer-links">
          <Link to="/pricing">Plans</Link>
          <Link to="/download">Download</Link>
          <Link to="/privacy">Privacy Policy</Link>
          <Link to="/terms">Terms of Use</Link>
        </div>
      </div>
    </footer>
  );
}

function Seo({ title, description, noindex = false, structuredData = null }) {
  useEffect(() => {
    document.title = title;
    setMeta("description", description);
    setMeta("og:title", title, "property");
    setMeta("og:description", description, "property");
    setMeta("og:type", "website", "property");
    setMeta("twitter:card", "summary_large_image");
    setMeta("twitter:title", title);
    setMeta("twitter:description", description);
    setMeta("robots", noindex ? "noindex,nofollow" : "index,follow");

    let link = document.querySelector('link[rel="canonical"]');
    if (!link) {
      link = document.createElement("link");
      link.setAttribute("rel", "canonical");
      document.head.appendChild(link);
    }
    link.setAttribute("href", window.location.href);

    let schemaNode = document.getElementById("phantom-structured-data");
    if (structuredData) {
      if (!schemaNode) {
        schemaNode = document.createElement("script");
        schemaNode.type = "application/ld+json";
        schemaNode.id = "phantom-structured-data";
        document.head.appendChild(schemaNode);
      }
      schemaNode.textContent = JSON.stringify(structuredData);
    } else if (schemaNode) {
      schemaNode.remove();
    }
  }, [description, noindex, structuredData, title]);

  return null;
}

function MarketingPage({ userSession }) {
  const structuredData = {
    "@context": "https://schema.org",
    "@type": "SoftwareApplication",
    name: "Phantom",
    applicationCategory: "ProductivityApplication",
    operatingSystem: "Windows"
  };

  return (
    <main className="page">
      <Seo
        title="Phantom | Interview Workspace For Live Rounds"
        description="Phantom is a focused Windows interview workspace that keeps AI support, preparation, and premium context close when live interviews move fast."
        structuredData={structuredData}
      />

      <section className="hero-grid">
        <article className="glass-panel hero-panel hero-panel-primary">
          <p className="eyebrow">Interview workspace for live rounds</p>
          <h1>
            Walk into every interview with your preparation and AI support already in place.
          </h1>
          <p className="lead-copy">
            Phantom gives you a focused Windows workspace for high-pressure interviews, so your notes, company
            context, and AI assistance stay close when you need to think, respond, and adapt quickly.
          </p>
          <div className="hero-actions">
            <Link className="button button-primary" to={userSession?.isAuthenticated ? "/dashboard" : "/register"}>
              {userSession?.isAuthenticated ? "Open Dashboard" : "Start Free Trial"}
            </Link>
            <Link className="button button-secondary" to="/pricing">
              See Plans
            </Link>
          </div>
          <div className="signal-strip">
            <span>Private Windows workspace</span>
            <span>Hosted or BYO AI</span>
            <span>Premium knowledge base</span>
            <span>Built for live rounds</span>
          </div>
        </article>

        <article className="glass-panel hero-panel hero-panel-side">
          <p className="eyebrow">Why people choose Phantom</p>
          <div className="stack-list">
            {publicFeatureRows.map((item) => (
              <InfoRow key={item.label} label={item.label} value={item.value} />
            ))}
          </div>
          <div className="status-band">
            <span className="status-pill status-pill-good">Start free</span>
            <span className="status-pill">Use your own stack later</span>
            <span className="status-pill">Keep context ready</span>
          </div>
        </article>
      </section>

      <section className="triple-grid">
        {publicHighlights.map((item) => (
          <article className="glass-panel story-card" key={item.title}>
            <p className="story-tag">Core principle</p>
            <h2>{item.title}</h2>
            <p>{item.copy}</p>
          </article>
        ))}
      </section>

      <section className="glass-panel section-panel">
        <div className="section-heading">
          <p className="eyebrow">How it fits your workflow</p>
          <h2>Prepare once. Show up sharper every round.</h2>
          <p>
            Phantom works best when it feels like an extension of your preparation, not another thing to manage
            when the interview is already underway.
          </p>
        </div>
        <div className="timeline-grid">
          {publicJourney.map((item, index) => (
            <TimelineStep
              key={item.title}
              index={`0${index + 1}`}
              title={item.title}
              body={item.body}
            />
          ))}
        </div>
      </section>

      <section className="glass-panel comparison-panel">
        <div className="section-heading">
          <p className="eyebrow">What makes it feel different</p>
          <h2>More than a prompt box. More focused than a browser setup.</h2>
        </div>
        <div className="comparison-grid">
          {publicValueProps.map((item) => (
            <MetricDefinition key={item.title} title={item.title} detail={item.detail} />
          ))}
        </div>
      </section>

      <section className="glass-panel page-intro">
        <p className="eyebrow">Ready to try it?</p>
        <h1>Start free, then choose the setup that matches how you want to work.</h1>
        <p>
          Begin with the free trial, explore the workflow, and move into the plan that fits how you want to
          prepare, practice, and show up in live interviews.
        </p>
        <div className="hero-actions">
          <Link className="button button-primary" to={userSession?.isAuthenticated ? "/dashboard" : "/register"}>
            {userSession?.isAuthenticated ? "Open Dashboard" : "Create Free Account"}
          </Link>
          <Link className="button button-secondary" to="/pricing">
            Compare Plans
          </Link>
          <Link className="button button-ghost" to="/download">
            Download Path
          </Link>
        </div>
      </section>

      <section className="glass-panel faq-panel">
        <div className="section-heading">
          <p className="eyebrow">FAQ</p>
          <h2>Questions people ask before they start.</h2>
        </div>
        <div className="faq-list">
          {publicFaqs.map((item) => (
            <article key={item.question}>
              <h3>{item.question}</h3>
              <p>{item.answer}</p>
            </article>
          ))}
        </div>
      </section>
    </main>
  );
}

function PricingPage() {
  return (
    <main className="page">
      <Seo
        title="Pricing | Phantom"
        description="Compare Phantom's Free Trial, Pro BYO, and Premium AI plans for protected desktop interview workflows."
      />
      <section className="glass-panel page-intro">
        <p className="eyebrow">Pricing architecture</p>
        <h1>Price the operating model, not just the seat.</h1>
        <p>
          Phantom has to reconcile hosted model cost, provider ownership, premium retrieval, and protected
          continuation. The pricing surface should reflect those realities directly.
        </p>
      </section>

      <section className="triple-grid">
        {plans.map((plan) => (
          <article className={`glass-panel plan-card tone-${plan.tone}`} key={plan.name}>
            <div className="plan-head">
              <span className="status-pill">{plan.badge}</span>
              <strong>{plan.price}</strong>
            </div>
            <h2>{plan.name}</h2>
            <p>{plan.summary}</p>
            <ul>
              {plan.bullets.map((bullet) => (
                <li key={bullet}>{bullet}</li>
              ))}
            </ul>
            <Link className="button button-primary" to={plan.to}>
              {plan.cta}
            </Link>
          </article>
        ))}
      </section>

      <section className="glass-panel comparison-panel">
        <div className="section-heading">
          <p className="eyebrow">Decision guide</p>
          <h2>Choose the lane that matches who should own the AI spend and setup burden.</h2>
        </div>
        <div className="comparison-grid">
          <MetricDefinition title="Free Trial" detail="Use when you need product validation before buying credits or adding keys." />
          <MetricDefinition title="Pro BYO" detail="Use when you want Phantom's desktop runtime but your own provider accounts and billing." />
          <MetricDefinition title="Premium AI" detail="Use when you want managed model lanes, premium context, and less operator setup." />
        </div>
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
      if (!userSession?.accessToken) {
        setEntitlement(null);
        return;
      }

      try {
        const result = await fetchDownloadEntitlement(userSession.accessToken);
        if (!cancelled) {
          setEntitlement(result);
          setError("");
        }
      } catch (loadError) {
        if (!cancelled) {
          setError(loadError.message || "Could not load installer entitlement.");
        }
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [userSession?.accessToken]);

  return (
    <main className="page">
      <Seo
        title="Download | Phantom"
        description="Understand Phantom's Windows desktop installer path, verification requirements, and download eligibility."
      />
      <section className="hero-grid">
        <article className="glass-panel hero-panel hero-panel-primary">
          <p className="eyebrow">Windows runtime delivery</p>
          <h1>The interview happens in the desktop runtime, not in the browser.</h1>
          <p className="lead-copy">
            This page exists to explain installer eligibility, verification gates, and what happens after the
            Windows app launches and checks hosted account state.
          </p>
          <div className="stats-grid">
            <MetricCard label="Runtime" value=".NET 8 + WebView2" />
            <MetricCard label="Gate" value="Hosted account check" />
            <MetricCard label="Channel" value={entitlement?.releaseChannel || "Account gated"} />
          </div>
          <div className="hero-actions">
            {userSession?.isAuthenticated ? (
              <Link className="button button-primary" to="/dashboard">
                Open Your Dashboard
              </Link>
            ) : (
              <>
                <Link className="button button-primary" to="/login">
                  Sign In
                </Link>
                <Link className="button button-secondary" to="/register">
                  Create Account
                </Link>
              </>
            )}
          </div>
          {error ? <p className="status-message status-error">{error}</p> : null}
        </article>

        <article className="glass-panel hero-panel hero-panel-side">
          <p className="eyebrow">Current installer state</p>
          <div className="stack-list">
            <InfoRow label="Installer" value={entitlement?.installerLabel || "Sign in to resolve"} />
            <InfoRow label="Version" value={entitlement?.installerVersion || "Pending"} />
            <InfoRow label="Eligibility" value={entitlement?.canDownload ? "Ready" : "Verification required"} />
          </div>
        </article>
      </section>

      <section className="glass-panel section-panel">
        <div className="section-heading">
          <p className="eyebrow">Flow</p>
          <h2>What a user needs before the desktop app can proceed.</h2>
        </div>
        <div className="timeline-grid">
          <TimelineStep index="01" title="Create account" body="Register on the website and complete phone OTP plus email verification." />
          <TimelineStep index="02" title="Resolve eligibility" body="Use the dashboard to confirm plan, credits, devices, and download access." />
          <TimelineStep index="03" title="Launch Phantom" body="The Windows runtime checks hosted state before the live session becomes interactive." />
        </div>
      </section>
    </main>
  );
}

function UserLoginPage({ onAuthenticated, userSession }) {
  const navigate = useNavigate();
  const [form, setForm] = useState({ email: "", password: "" });
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (userSession?.isAuthenticated) {
      navigate("/dashboard", { replace: true });
    }
  }, [navigate, userSession]);

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setStatus("");
    try {
      const session = await loginAccount(form);
      onAuthenticated(session);
      navigate("/dashboard", { replace: true });
    } catch (error) {
      setStatus(error.message || "Could not sign in.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="page">
      <Seo
        title="User Login | Phantom"
        description="Open the Phantom user dashboard to review account, device, wallet, and premium knowledge-base state."
        noindex
      />
      <section className="auth-shell">
        <article className="glass-panel auth-aside">
          <p className="eyebrow">User dashboard</p>
          <h1>Check hosted account state without touching the live runtime.</h1>
          <p>
            Sign in here to see balances, devices, download readiness, purchase history, and hosted
            knowledge-base status.
          </p>
          <ul>
            <li>Browser session uses the same hosted account authority</li>
            <li>Wallet and device state remain visible outside the desktop app</li>
            <li>Premium context management stays on the website</li>
          </ul>
        </article>

        <form className="glass-panel auth-form" onSubmit={handleSubmit}>
          <label>
            <span>Email</span>
            <input
              type="email"
              value={form.email}
              onChange={(event) => setForm((current) => ({ ...current, email: event.target.value }))}
              placeholder="name@example.com"
            />
          </label>
          <label>
            <span>Password</span>
            <input
              type="password"
              value={form.password}
              onChange={(event) => setForm((current) => ({ ...current, password: event.target.value }))}
              placeholder="Enter your password"
            />
          </label>
          <button className="button button-primary" type="submit" disabled={submitting}>
            {submitting ? "Signing in..." : "Open User Dashboard"}
          </button>
          {status ? <p className="status-message status-error">{status}</p> : null}
          <div className="link-row">
            <Link to="/register">Create account</Link>
            <Link to="/admin/login">Admin console</Link>
          </div>
        </form>
      </section>
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
      navigate("/desktop-return?verification=pending", {
        replace: true,
        state: deliveryFailed
          ? {
              title: "Account created, email delivery needs admin attention",
              email: result.email || form.email,
              message:
                result.deliveryError
                || "The account was created, but verification mail could not be delivered yet."
            }
          : {
              title: "Verification email sent",
              email: result.email || form.email,
              message: "Check your inbox, verify the account, then sign in from Phantom."
            }
      });
    } catch (error) {
      setStatus(error.message || "Registration failed.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="page">
      <Seo
        title="Register | Phantom"
        description="Create a Phantom account and complete phone OTP plus email verification before the first desktop sign-in."
        noindex
      />
      <section className="auth-shell">
        <article className="glass-panel auth-aside">
          <p className="eyebrow">Account setup</p>
          <h1>Register on the web, then return to the Windows runtime.</h1>
          <p>
            Registration creates the hosted identity, associates the browser or desktop profile, and starts the
            verification path that unlocks dashboard and installer access.
          </p>
          <ul>
            <li>Phone OTP is required before account creation</li>
            <li>Email verification is required before normal sign-in</li>
            <li>Device metadata can be passed from the desktop handoff</li>
          </ul>
        </article>

        <form className="glass-panel auth-form" onSubmit={handleSubmit}>
          <label>
            <span>Email</span>
            <input
              type="email"
              value={form.email}
              onChange={(event) => setForm((current) => ({ ...current, email: event.target.value }))}
              placeholder="name@example.com"
            />
          </label>
          <label>
            <span>Password</span>
            <input
              type="password"
              value={form.password}
              onChange={(event) => setForm((current) => ({ ...current, password: event.target.value }))}
              placeholder="Choose a strong password"
            />
          </label>
          <label>
            <span>Phone number</span>
            <input
              value={form.phoneNumber}
              onChange={(event) => setForm((current) => ({ ...current, phoneNumber: event.target.value }))}
              placeholder="+91 9876543210"
            />
          </label>

          <div className="inline-actions">
            <button
              className="button button-secondary"
              type="button"
              onClick={handleSendOtp}
              disabled={otpSubmitting || submitting}
            >
              {otpSubmitting ? "Sending..." : "Send OTP"}
            </button>
            <span className="inline-note">
              {otpState.maskedPhoneNumber
                ? `OTP challenge active for ${otpState.maskedPhoneNumber}`
                : "Phone OTP is required"}
            </span>
          </div>

          <label>
            <span>OTP code</span>
            <input
              value={form.otpCode}
              onChange={(event) => setForm((current) => ({ ...current, otpCode: event.target.value }))}
              placeholder="6-digit OTP"
            />
          </label>

          <button
            className="button button-ghost"
            type="button"
            onClick={handleVerifyOtp}
            disabled={otpSubmitting || submitting}
          >
            {otpSubmitting ? "Working..." : otpState.verificationToken ? "Phone Verified" : "Verify OTP"}
          </button>
          <button className="button button-primary" type="submit" disabled={submitting}>
            {submitting ? "Creating account..." : "Create Account"}
          </button>

          {status ? (
            <p className={`status-message ${status.toLowerCase().includes("verified") || status.toLowerCase().includes("sent") ? "" : "status-error"}`}>
              {status}
            </p>
          ) : null}
        </form>
      </section>
    </main>
  );
}

function DesktopReturnPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const query = new URLSearchParams(location.search);
  const email = location.state?.email || "";
  const verificationState = query.get("verification");
  const gmailOauthState = query.get("gmail_oauth");

  const pageState = location.state || (
    verificationState === "pending"
      ? {
          title: "Verification email sent",
          message: "Verify the account, then sign in from Phantom."
        }
      : verificationState === "success"
        ? {
            title: "Email verified",
            message: "Your account is verified. You can now sign in from the desktop runtime or the user dashboard."
          }
        : gmailOauthState === "success"
          ? {
              title: "Gmail delivery connected",
              message: "Verification and recovery mail can now be sent through Gmail."
            }
          : {
              title: "Desktop callback ready",
              message: "Return to Phantom to continue the desktop flow."
            }
  );

  async function handleResendVerification() {
    if (!email) {
      return;
    }

    try {
      const result = await resendVerificationEmail(email);
      navigate("/desktop-return?verification=pending", {
        replace: true,
        state: {
          title: "Verification email sent",
          email,
          message: result?.message || "We sent another verification email."
        }
      });
    } catch (error) {
      navigate("/desktop-return", {
        replace: true,
        state: {
          title: "Verification resend failed",
          message: error.message || "Could not resend verification email."
        }
      });
    }
  }

  return (
    <main className="page">
      <Seo
        title="Return to Phantom | Desktop Handoff"
        description="Continue the Phantom desktop handoff after registration, verification, or admin Gmail setup."
        noindex
      />
      <section className="glass-panel page-intro">
        <p className="eyebrow">Desktop return</p>
        <h1>{pageState.title}</h1>
        <p>{pageState.message}</p>
        <div className="hero-actions">
          {verificationState === "pending" ? (
            <>
              <button className="button button-primary" type="button" onClick={handleResendVerification} disabled={!email}>
                Resend Verification Email
              </button>
              <Link className="button button-secondary" to="/login">
                Back To Login
              </Link>
            </>
          ) : gmailOauthState === "success" ? (
            <Link className="button button-primary" to="/admin/login">
              Open Admin Login
            </Link>
          ) : (
            <Link className="button button-primary" to="/login">
              Go To Login
            </Link>
          )}
        </div>
      </section>
    </main>
  );
}

function PrivacyPolicyPage() {
  return (
    <main className="page">
      <Seo
        title="Privacy Policy | Phantom"
        description="Read how Phantom handles account, device, payment, telemetry, and hosted knowledge-base data."
      />
      <LegalPage
        eyebrow="Privacy Policy"
        title="Privacy rules for a desktop-linked, account-controlled product."
        intro="This policy is written for Phantom's actual architecture: hosted account services, browser dashboards, desktop-linked identity checks, wallet records, hosted knowledge-base uploads, payment reconciliation, and admin operations."
        sections={privacySections}
      />
    </main>
  );
}

function TermsPage() {
  return (
    <main className="page">
      <Seo
        title="Terms of Use | Phantom"
        description="Read Phantom's terms of use and acceptable-use rules for website, dashboard, and desktop-linked operations."
      />
      <LegalPage
        eyebrow="Terms Of Use"
        title="Product rules that match what Phantom actually does."
        intro="These terms are designed for a hosted website paired with a Windows runtime, not a generic marketing site. They focus on account responsibility, lawful use, hosted content, payment-linked credits, and operational controls."
        sections={termsSections}
      />
    </main>
  );
}

function LegalPage({ eyebrow, title, intro, sections }) {
  return (
    <>
      <section className="glass-panel page-intro">
        <p className="eyebrow">{eyebrow}</p>
        <h1>{title}</h1>
        <p>{intro}</p>
      </section>
      <section className="legal-stack">
        {sections.map((section) => (
          <article className="glass-panel legal-card" key={section.title}>
            <h2>{section.title}</h2>
            <p>{section.body}</p>
          </article>
        ))}
      </section>
    </>
  );
}

function RequireUserSession({ ready, session, children }) {
  if (!ready) {
    return <SessionLoadingPage label="User session" title="Restoring dashboard access..." />;
  }

  if (!session?.isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return children;
}

function RequireAdminSession({ ready, session, children }) {
  if (!ready) {
    return <SessionLoadingPage label="Admin session" title="Restoring admin access..." />;
  }

  if (!session?.isAuthenticated) {
    return <Navigate to="/admin/login" replace />;
  }

  return children;
}

function SessionLoadingPage({ label, title }) {
  return (
    <main className="page">
      <section className="glass-panel page-intro">
        <p className="eyebrow">{label}</p>
        <h1>{title}</h1>
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
        const account = await fetchAccountSummary(session.accessToken);
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
          fetchWalletHistory(session.accessToken),
          fetchWalletPurchases(session.accessToken),
          fetchDevices(session.accessToken),
          fetchDownloadEntitlement(session.accessToken),
          fetchSupportOverview(session.accessToken),
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
          setError(loadError.message || "Could not load the user dashboard.");
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
  }, [session.accessToken, session.email, session.expiresAtUtc]);

  async function refreshWalletState() {
    const account = await fetchAccountSummary(session.accessToken);
    const [history, purchases, catalog] = await Promise.all([
      fetchWalletHistory(session.accessToken),
      fetchWalletPurchases(session.accessToken),
      fetchPaymentCatalog(session.accessToken).catch(() => null)
    ]);
    setSummary(account);
    setWalletHistory(history);
    setWalletPurchases(purchases);
    setPaymentCatalog(catalog);
  }

  if (loading) {
    return <SessionLoadingPage label="User dashboard" title="Loading hosted account state..." />;
  }

  if (error || !summary) {
    return (
      <main className="page">
        <Seo title="User Dashboard | Phantom" description="User dashboard" noindex />
        <section className="glass-panel page-intro">
          <p className="eyebrow">User dashboard</p>
          <h1>Dashboard unavailable</h1>
          <p>{error || "Account summary could not be resolved."}</p>
        </section>
      </main>
    );
  }

  return (
    <main className="page">
      <Seo
        title="User Dashboard | Phantom"
        description="Review Phantom account health, wallet activity, devices, and premium knowledge-base state."
        noindex
      />
      <section className="dashboard-shell">
        <aside className="glass-panel dashboard-rail">
          <p className="eyebrow">Account snapshot</p>
          <h2>{summary.email}</h2>
          <div className="status-band">
            <span className="status-pill">{summary.planLabel}</span>
            <span className={`status-pill ${summary.phoneVerified ? "status-pill-good" : "status-pill-warn"}`}>
              {summary.phoneVerified ? "Phone verified" : "Verification required"}
            </span>
          </div>
          <div className="stack-list">
            <InfoRow label="Pro credits" value={summary.proAvailableCredits.toFixed(2)} />
            <InfoRow label="Premium credits" value={summary.premiumAvailableCredits.toFixed(2)} />
            <InfoRow label="Premium debt" value={summary.premiumNegativeCredits.toFixed(2)} />
            <InfoRow label="Active devices" value={String(summary.activeDeviceCount)} />
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
                  walletHistory={walletHistory}
                  walletPurchases={walletPurchases}
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
      </section>
    </main>
  );
}

function UserOverviewPanel({ summary, devices, download, support, knowledgeBase, walletHistory, walletPurchases }) {
  const activeDeviceCount = devices.filter((item) => item.isActive).length;
  const purchaseStates = countBy(walletPurchases, (item) => item.status || "unknown");

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero">
        <p className="eyebrow">User dashboard</p>
        <h1>One place to check account health before you return to the desktop.</h1>
        <p>
          This surface should lead with clarity: plan, credits, download readiness, recent usage, and premium
          context state. It should not try to mimic the desktop runtime itself.
        </p>
      </article>

      <MetricCard label="Plan" value={summary.planLabel} />
      <MetricCard label="Premium credits" value={summary.premiumAvailableCredits.toFixed(2)} />
      <MetricCard label="Active devices" value={String(activeDeviceCount)} />
      <MetricCard label="KB documents" value={String(knowledgeBase?.documentCount ?? 0)} />

      <article className="glass-panel">
        <p className="story-tag">Download entitlement</p>
        <h3>{download?.installerLabel || "Installer access pending"}</h3>
        <p>
          {download?.releaseChannel || "Unavailable"} · {download?.installerVersion || "Version pending"}
        </p>
      </article>

      <article className="glass-panel">
        <p className="story-tag">Current access guard</p>
        <h3>{summary.phoneVerified ? "Ready for app access" : "Phone verification required"}</h3>
        <p>
          Lease expiry: {formatDate(summary.leaseExpiresAtUtc)} · Last activity: {formatDate(summary.lastActivityAtUtc)}
        </p>
      </article>

      <article className="glass-panel chart-panel">
        <p className="story-tag">Usage trend</p>
        <h3>Recent wallet charges</h3>
        <SimpleSparkline
          values={walletHistory.slice(0, 12).map((item) => Number(item.chargedCredits || 0)).reverse()}
        />
      </article>

      <article className="glass-panel chart-panel">
        <p className="story-tag">Purchase states</p>
        <h3>Payment pipeline snapshot</h3>
        <MiniBarList
          items={[
            { label: "Credited", value: purchaseStates.credited || 0 },
            { label: "Confirmed", value: purchaseStates.client_confirmed || 0 },
            { label: "Created", value: purchaseStates.created || 0 }
          ]}
        />
      </article>

      <article className="glass-panel">
        <p className="story-tag">Support state</p>
        <h3>{support?.openLockSessionId || "No active support event"}</h3>
        <p>{support?.supportMessage || "Support state is not available."}</p>
      </article>

      <article className="glass-panel">
        <p className="story-tag">Hosted knowledge base</p>
        <h3>{knowledgeBase?.name || "No hosted KB linked yet"}</h3>
        <p>
          {knowledgeBase?.canUseInInterview
            ? `Ready for desktop retrieval · ${knowledgeBase.documentCount} docs`
            : knowledgeBase?.blockedReason || "Create a hosted KB to sync premium retrieval context."}
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
  const blockedMessage =
    knowledgeBase?.blockedReason
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
      const totalBytes = Array.from(files).reduce((sum, file) => sum + (file.size || 0), 0);
      if (totalBytes > 8 * 1024 * 1024) {
        throw new Error("Combined upload exceeds the 8 MB per-request limit.");
      }

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
      <article className="glass-panel dashboard-hero">
        <p className="eyebrow">Premium knowledge base</p>
        <h1>Upload context once and keep the hosted retrieval layer aligned with the desktop runtime.</h1>
        <p>
          Premium knowledge bases belong on the website because they are hosted, processed, and synced across
          devices. The desktop app should consume this state, not manage the source-of-truth content.
        </p>
      </article>

      <article className="glass-panel">
        <p className="story-tag">Current entitlement</p>
        <h3>{summary.planLabel}</h3>
        <p>
          {knowledgeBase?.canUseInInterview
            ? `Interview retrieval enabled with ${summary.premiumAvailableCredits.toFixed(2)} Premium credits available.`
            : blockedMessage}
        </p>
      </article>

      <article className="glass-panel">
        <p className="story-tag">Hosted status</p>
        <h3>{knowledgeBase?.status || "not_created"}</h3>
        <p>
          {knowledgeBase?.documentCount ?? 0} docs · {knowledgeBase?.chunkCount ?? 0} chunks
        </p>
      </article>

      <form className="glass-panel auth-form" onSubmit={handleCreate}>
        <p className="eyebrow">Create or rename</p>
        <label>
          <span>Knowledge base name</span>
          <input value={name} onChange={(event) => setName(event.target.value)} disabled={isPremiumBlocked || submitting} />
        </label>
        <label>
          <span>Description</span>
          <textarea
            rows={4}
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            disabled={isPremiumBlocked || submitting}
            placeholder="Role packet, architecture notes, company research, STAR stories"
          />
        </label>
        <button className="button button-primary" type="submit" disabled={isPremiumBlocked || submitting}>
          {submitting ? "Saving..." : "Save Knowledge Base"}
        </button>
      </form>

      <article className="glass-panel upload-panel">
        <p className="eyebrow">Upload documents</p>
        <h3>Supported: `.txt`, `.md`, `.json`, `.csv`, `.log`, `.docx`</h3>
        <p>
          Premium upload limits are enforced on the backend. Keep the website strict and honest about request
          size, file count, and processing limits.
        </p>
        <label className={`button button-secondary button-file ${isPremiumBlocked || submitting ? "button-disabled" : ""}`}>
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

      <div className="glass-panel table-panel table-span-full">
        <div className="table-header">
          <div>
            <p className="eyebrow">Processed documents</p>
            <h3>{knowledgeBase?.name || "Knowledge base inventory"}</h3>
          </div>
        </div>
        <TableScroll>
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
        </TableScroll>
        {status ? (
          <p className={`status-message ${status.toLowerCase().includes("could not") ? "status-error" : ""}`}>
            {status}
          </p>
        ) : null}
      </div>
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
      <MetricCard label="Pro available" value={summary.proAvailableCredits.toFixed(2)} tone="signal" />
      <MetricCard label="Premium available" value={summary.premiumAvailableCredits.toFixed(2)} tone="signal" />
      <MetricCard label="Premium debt" value={summary.premiumNegativeCredits.toFixed(2)} tone="warn" />

      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Wallet checkout</p>
        <h1>Buy the lane you need and settle continuation debt only when it exists.</h1>
        <p>
          Premium takes priority whenever Premium credits exist. If Premium reaches zero and Pro remains,
          Phantom falls back to Pro BYO. Debt settlement is a separate, explicit flow.
        </p>
      </article>

      {(paymentCatalog?.proPacks || []).map((pack) => (
        <PackCard
          key={pack.packCode}
          label="Pro BYO Pack"
          pack={pack}
          buttonLabel="Buy Pro Credits"
          loading={submittingTarget === `${pack.target}:${pack.packCode}`}
          onClick={() => handleCheckout(pack.target, pack.packCode)}
        />
      ))}

      {(paymentCatalog?.premiumPacks || []).map((pack) => (
        <PackCard
          key={pack.packCode}
          label="Premium Pack"
          pack={pack}
          buttonLabel="Buy Premium Credits"
          loading={submittingTarget === `${pack.target}:${pack.packCode}`}
          onClick={() => handleCheckout(pack.target, pack.packCode)}
        />
      ))}

      {paymentCatalog?.premiumDebtSettlement ? (
        <article className="glass-panel">
          <p className="story-tag">Debt settlement</p>
          <h3>Clear Premium continuation debt</h3>
          <p>
            Outstanding debt: {summary.premiumNegativeCredits.toFixed(2)} Premium credits.
          </p>
          <strong>{formatInr(paymentCatalog.premiumDebtSettlement.displayAmountInr)}</strong>
          <button
            className="button button-primary"
            type="button"
            onClick={() => handleCheckout("premium_debt_settlement", "premium_debt_settlement")}
            disabled={submittingTarget === "premium_debt_settlement:premium_debt_settlement"}
          >
            {submittingTarget === "premium_debt_settlement:premium_debt_settlement" ? "Opening..." : "Settle Debt"}
          </button>
        </article>
      ) : null}

      {status ? (
        <article className="glass-panel table-span-full">
          <p className={`status-message ${status.toLowerCase().includes("acknowledged") ? "" : "status-error"}`}>
            {status}
          </p>
        </article>
      ) : null}

      <DataTable
        title="Purchase history"
        columns={["Purchase", "Amount", "Credits", "Status", "Created"]}
        rows={
          walletPurchases.length === 0
            ? null
            : walletPurchases.map((item) => [
                item.displayLabel,
                formatInr(item.amountInr),
                item.target === "premium_debt_settlement"
                  ? `Debt ${item.premiumDebtCreditsCovered}`
                  : item.credits,
                item.status,
                formatDate(item.createdAtUtc)
              ])
        }
        emptyLabel="No credit purchases recorded yet."
      />

      <DataTable
        title="Wallet history"
        columns={["Session", "Credits", "Blocks", "Debt", "Created"]}
        rows={
          walletHistory.length === 0
            ? null
            : walletHistory.map((item) => [
                item.sessionId,
                item.chargedCredits,
                item.chargedBlocks,
                item.addedPremiumDebt,
                formatDate(item.createdAtUtc)
              ])
        }
        emptyLabel="No wallet entries recorded yet."
      />
    </div>
  );
}

function DevicesPanel({ devices }) {
  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Device inventory</p>
        <h1>Keep device visibility in the dashboard instead of guessing from the desktop.</h1>
        <p>The dashboard should show active and historical browser or desktop identities without exposing more than operators need.</p>
      </article>
      {devices.length === 0 ? (
        <article className="glass-panel">
          <h3>No device sessions recorded yet.</h3>
        </article>
      ) : (
        devices.map((device) => (
          <article className="glass-panel device-card" key={`${device.deviceInstallId}-${device.deviceFingerprintHash}`}>
            <span className={`status-pill ${device.isActive ? "status-pill-good" : ""}`}>
              {device.isActive ? "Active" : "Historical"}
            </span>
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
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Usage history</p>
        <h1>Show charge records clearly enough for support and self-serve review.</h1>
      </article>
      <DataTable
        title="Usage charge history"
        columns={["Ledger entry", "Session", "Credits", "Debt", "Created"]}
        rows={
          walletHistory.length === 0
            ? null
            : walletHistory.map((item) => [
                item.ledgerEntryId,
                item.sessionId,
                item.chargedCredits,
                item.addedPremiumDebt,
                formatDate(item.createdAtUtc)
              ])
        }
        emptyLabel="No usage ledger history recorded yet."
      />
    </div>
  );
}

function SupportPanel({ support }) {
  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Support preview</p>
        <h1>Expose the signals users actually need before they contact support.</h1>
      </article>
      <article className="glass-panel table-span-full">
        <h3>{support?.openLockSessionId || "No active support event"}</h3>
        <p>{support?.supportMessage || "Support state is not available."}</p>
        <div className="stats-grid">
          <MetricCard label="Last charge" value={String(support?.lastUsageChargeCredits ?? 0)} />
          <MetricCard label="Lease hours left" value={String(support?.offlineLeaseHoursRemaining ?? 0)} />
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
    if (adminSession?.isAuthenticated) {
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
    <main className="page">
      <Seo
        title="Admin Login | Phantom"
        description="Open the Phantom admin control plane for managed providers, users, payment operations, and email delivery health."
        noindex
      />
      <section className="auth-shell">
        <article className="glass-panel auth-aside">
          <p className="eyebrow">Admin control plane</p>
          <h1>Separate operational access from the user dashboard.</h1>
          <p>
            The admin plane exists for managed provider operations, payment visibility, Gmail delivery health,
            and user-level corrections. It should never be blended into the public or user surfaces.
          </p>
        </article>

        <form className="glass-panel auth-form" onSubmit={handleSubmit}>
          <label>
            <span>Admin email</span>
            <input type="email" value={email} onChange={(event) => setEmail(event.target.value)} placeholder="admin@example.com" />
          </label>
          <label>
            <span>Password</span>
            <input type="password" value={password} onChange={(event) => setPassword(event.target.value)} placeholder="Enter your admin password" />
          </label>
          <button className="button button-primary" type="submit" disabled={submitting}>
            {submitting ? "Authenticating..." : "Open Admin Dashboard"}
          </button>
          {error ? <p className="status-message status-error">{error}</p> : null}
          <div className="link-row">
            <Link to="/admin/forgot-password">Forgot password?</Link>
          </div>
        </form>
      </section>
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
      setStatus(result.message || "If that admin account exists, a reset link has been sent.");
    } catch (error) {
      setStatus(error.message || "Could not request a password reset.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="page">
      <Seo
        title="Admin Password Reset | Phantom"
        description="Request an admin password reset for the Phantom control plane."
        noindex
      />
      <section className="auth-shell">
        <article className="glass-panel auth-aside">
          <p className="eyebrow">Admin recovery</p>
          <h1>Reset the admin password through email.</h1>
          <p>Use this only for admin accounts. User dashboard authentication remains separate.</p>
        </article>
        <form className="glass-panel auth-form" onSubmit={handleSubmit}>
          <label>
            <span>Admin email</span>
            <input type="email" value={email} onChange={(event) => setEmail(event.target.value)} placeholder="admin@example.com" />
          </label>
          <button className="button button-primary" type="submit" disabled={submitting}>
            {submitting ? "Sending..." : "Send Reset Link"}
          </button>
          {status ? <p className={`status-message ${status.toLowerCase().includes("could not") ? "status-error" : ""}`}>{status}</p> : null}
          <div className="link-row">
            <Link to="/admin/login">Back to admin login</Link>
          </div>
        </form>
      </section>
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
    <main className="page">
      <Seo title="Admin Reset Password | Phantom" description="Set a new password for Phantom admin access." noindex />
      <section className="auth-shell">
        <article className="glass-panel auth-aside">
          <p className="eyebrow">Admin reset</p>
          <h1>Choose a new admin password.</h1>
          <p>Reset links are single-use and time-limited.</p>
        </article>
        <form className="glass-panel auth-form" onSubmit={handleSubmit}>
          <label>
            <span>New password</span>
            <input type="password" value={password} onChange={(event) => setPassword(event.target.value)} placeholder="At least 12 characters" />
          </label>
          <label>
            <span>Confirm password</span>
            <input type="password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} placeholder="Re-enter the new password" />
          </label>
          <button className="button button-primary" type="submit" disabled={submitting}>
            {submitting ? "Resetting..." : "Reset Password"}
          </button>
          {status ? <p className={`status-message ${status.toLowerCase().includes("complete") ? "" : "status-error"}`}>{status}</p> : null}
        </form>
      </section>
    </main>
  );
}

function AdminDashboardPage({ adminSession }) {
  const [overview, setOverview] = useState(null);
  const [users, setUsers] = useState([]);
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
        const [overviewData, usersData, inventoryData, paymentOrdersData, paymentWebhooksData, gmailStatusData] = await Promise.all([
          fetchAdminOverview(adminSession.accessToken),
          fetchAdminUsers(adminSession.accessToken),
          fetchManagedAiAdminInventory(adminSession.accessToken),
          fetchAdminPaymentOrders(adminSession.accessToken),
          fetchAdminPaymentWebhooks(adminSession.accessToken),
          fetchGmailOAuthStatus(adminSession.accessToken)
        ]);

        if (!cancelled) {
          setOverview(overviewData);
          setUsers(usersData);
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
  }, [adminSession.accessToken, adminSession.email, adminSession.expiresAtUtc]);

  async function refreshManagedInventory() {
    const nextInventory = await fetchManagedAiAdminInventory(adminSession.accessToken);
    setInventory(nextInventory);
    return nextInventory;
  }

  async function refreshUsers() {
    const nextUsers = await fetchAdminUsers(adminSession.accessToken);
    setUsers(nextUsers);
    return nextUsers;
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
    return <SessionLoadingPage label="Admin dashboard" title="Loading control plane..." />;
  }

  if (error) {
    return (
      <main className="page">
        <Seo title="Admin Dashboard | Phantom" description="Admin dashboard" noindex />
        <section className="glass-panel page-intro">
          <p className="eyebrow">Admin dashboard</p>
          <h1>Admin access failed</h1>
          <p>{error}</p>
        </section>
      </main>
    );
  }

  return (
    <main className="page">
      <Seo
        title="Admin Dashboard | Phantom"
        description="Operate managed providers, users, payments, and email delivery health for Phantom."
        noindex
      />
      <section className="dashboard-shell">
        <aside className="glass-panel dashboard-rail">
          <p className="eyebrow">Control plane</p>
          <h2>Hosted operations</h2>
          <div className="status-band">
            <span className="status-pill">Managed AI</span>
            <span className="status-pill">Payments</span>
            <span className="status-pill">Users</span>
          </div>
          <div className="stack-list">
            <InfoRow label="Accounts" value={String(overview?.accountCount ?? 0)} />
            <InfoRow label="Active locks" value={String(overview?.activeLockCount ?? 0)} />
            <InfoRow label="Managed keys" value={String(overview?.managedCredentialCount ?? 0)} />
            <InfoRow label="Payment orders" value={String(overview?.paymentOrderCount ?? 0)} />
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
              path="users"
              element={
                <AdminUsersPanel
                  accessToken={adminSession.accessToken}
                  users={users}
                  onUsersChanged={refreshUsers}
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
          setGmailMessage(status?.statusMessage || "Gmail OAuth status refreshed.");
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
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Admin overview</p>
        <h1>Monitor hosted operations without leaking internal mechanics into the public site.</h1>
        <p>
          This console should stay operational: counts, provider readiness, email delivery health, and payment
          state. It is not a second marketing site.
        </p>
      </article>

      <MetricCard label="Live sessions" value={String(overview?.activeSessionCount ?? 0)} />
      <MetricCard label="Ledger entries" value={String(overview?.ledgerEntryCount ?? 0)} />
      <MetricCard label="Managed providers" value={String(providers.length)} />
      <MetricCard
        label="Catalog models"
        value={String(catalogProviders.reduce((total, provider) => total + (provider.models?.length || 0), 0))}
      />

      <article className="glass-panel table-span-full">
        <div className="table-header">
          <div>
            <p className="eyebrow">Gmail sender health</p>
            <h3>{gmailStatus?.statusLabel || "Unknown"}</h3>
          </div>
          <button className="button button-primary button-compact" type="button" onClick={handleReconnectGmail} disabled={gmailLoading}>
            {gmailLoading ? "Redirecting..." : gmailStatus?.hasRefreshToken ? "Reconnect Gmail" : "Connect Gmail"}
          </button>
        </div>
        <p>{gmailStatus?.statusMessage || "Gmail OAuth status has not loaded yet."}</p>
        <div className="stats-grid">
          <MetricCard label="Configured" value={gmailStatus?.isConfigured ? "Yes" : "No"} />
          <MetricCard label="Refresh token" value={gmailStatus?.hasRefreshToken ? "Stored" : "Missing"} />
          <MetricCard label="Token valid" value={gmailStatus?.hasValidRefreshToken ? "Yes" : "No"} />
          <MetricCard label="Sender" value={gmailStatus?.fromEmail || "n/a"} />
        </div>
        {gmailMessage ? <p className={`status-message ${gmailMessage.toLowerCase().includes("could not") ? "status-error" : ""}`}>{gmailMessage}</p> : null}
      </article>

      <div className="glass-panel table-panel table-span-full">
        <div className="table-header">
          <div>
            <p className="eyebrow">Managed provider readiness</p>
            <h3>Catalog and credential coverage</h3>
          </div>
        </div>
        <TableScroll>
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
        </TableScroll>
      </div>
    </div>
  );
}

function ManagedAiAdminPanel({ accessToken, inventory, onRefresh, catalogRefreshResult, onCatalogRefreshResult }) {
  const [providerId, setProviderId] = useState("ChatGPT");
  const [label, setLabel] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [priority, setPriority] = useState("0");
  const [isEnabled, setIsEnabled] = useState(true);
  const [selectionProviderId, setSelectionProviderId] = useState("");
  const [selectionModelId, setSelectionModelId] = useState("");
  const [savingSelection, setSavingSelection] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [refreshingCatalog, setRefreshingCatalog] = useState(false);
  const [localError, setLocalError] = useState("");
  const [success, setSuccess] = useState("");

  const providers = inventory?.managedProviders || [];
  const credentials = inventory?.credentials || [];
  const catalogProviders = inventory?.catalogs?.providers || [];
  const currentSelection = inventory?.selection || null;
  const refreshProviders = catalogRefreshResult?.providers || [];

  useEffect(() => {
    if (providers.length > 0 && !providers.some((item) => item.providerId === providerId)) {
      setProviderId(providers[0].providerId);
    }
  }, [providerId, providers]);

  useEffect(() => {
    const catalogProviderIds = catalogProviders.map((item) => item.providerId);
    const nextProviderId =
      (currentSelection?.providerId && catalogProviderIds.includes(currentSelection.providerId)
        ? currentSelection.providerId
        : catalogProviderIds[0]) || "";
    setSelectionProviderId(nextProviderId);

    const nextProvider = catalogProviders.find((item) => item.providerId === nextProviderId);
    const nextModelId =
      (currentSelection?.providerId === nextProviderId &&
      nextProvider?.models?.some((model) => model.modelId === currentSelection?.modelId)
        ? currentSelection.modelId
        : nextProvider?.models?.[0]?.modelId) || "";
    setSelectionModelId(nextModelId);
  }, [catalogProviders, currentSelection?.modelId, currentSelection?.providerId]);

  const selectedCatalogProvider = catalogProviders.find((item) => item.providerId === selectionProviderId);
  const selectedCatalogModels = selectedCatalogProvider?.models || [];

  function handleSelectionProviderChange(nextProviderId) {
    setSelectionProviderId(nextProviderId);
    const nextProvider = catalogProviders.find((item) => item.providerId === nextProviderId);
    setSelectionModelId(nextProvider?.models?.[0]?.modelId || "");
  }

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

  async function handleSelectionSave(event) {
    event.preventDefault();
    setSavingSelection(true);
    setLocalError("");
    setSuccess("");
    try {
      await updateManagedAiRuntimeSelection(accessToken, {
        providerId: selectionProviderId,
        modelId: selectionModelId
      });
      await onRefresh();
      setSuccess("Managed runtime selection updated.");
    } catch (selectionError) {
      setLocalError(selectionError.message || "Could not update managed runtime selection.");
    } finally {
      setSavingSelection(false);
    }
  }

  async function handleVisionToggle(nextProviderId, modelId, supportsVision) {
    setLocalError("");
    setSuccess("");
    try {
      await updateManagedAiModelVision(accessToken, {
        providerId: nextProviderId,
        modelId,
        supportsVision: !supportsVision
      });
      await onRefresh();
      setSuccess("Model vision support updated.");
    } catch (toggleError) {
      setLocalError(toggleError.message || "Could not update model vision support.");
    }
  }

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Managed AI inventory</p>
        <h1>Operate provider credentials for hosted lanes without exposing secrets to users.</h1>
        <p>
          Provider inventory belongs in admin. Managed users should inherit the single active provider and model
          chosen here, while BYO users keep their own provider controls.
        </p>
      </article>

      <article className="glass-panel admin-form-panel">
        <div className="table-header">
          <div>
            <p className="eyebrow">Managed runtime selection</p>
            <h3>Choose the provider and model used for managed users</h3>
          </div>
        </div>
        <p>
          Premium and other managed lanes consume this selection as the global active hosted runtime. The desktop
          app should no longer expose provider or model switching for managed users.
        </p>
        <form className="admin-form" onSubmit={handleSelectionSave}>
          <div className="admin-form-inline">
            <label>
              Active provider
              <select value={selectionProviderId} onChange={(event) => handleSelectionProviderChange(event.target.value)}>
                {catalogProviders.map((provider) => (
                  <option key={provider.providerId} value={provider.providerId}>
                    {provider.label}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Active model
              <select value={selectionModelId} onChange={(event) => setSelectionModelId(event.target.value)}>
                {selectedCatalogModels.map((model) => (
                  <option key={model.modelId} value={model.modelId}>
                    {model.displayName}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <button
            className="button button-primary"
            type="submit"
            disabled={savingSelection || !selectionProviderId || !selectionModelId}
          >
            {savingSelection ? "Saving..." : "Set Active Runtime"}
          </button>
        </form>
        <div className="stack-list">
          <InfoRow
            label="Current selection"
            value={
              currentSelection?.isConfigured
                ? `${currentSelection.providerLabel || currentSelection.providerId} · ${currentSelection.modelDisplayName || currentSelection.modelId}`
                : "Not configured"
            }
          />
          <InfoRow
            label="Selection status"
            value={
              currentSelection?.isConfigured
                ? currentSelection?.isResolved
                  ? "Resolved"
                  : "Configured but not present in the latest catalog"
                : "Missing"
            }
          />
          <InfoRow label="Updated" value={formatDate(currentSelection?.updatedAtUtc)} />
        </div>
      </article>

      <article className="glass-panel admin-form-panel">
        <div className="table-header">
          <div>
            <p className="eyebrow">Create or rotate credential</p>
            <h3>Managed provider control</h3>
          </div>
          <div className="inline-actions">
            <button className="button button-secondary button-compact" type="button" onClick={onRefresh}>
              Refresh
            </button>
            <button className="button button-primary button-compact" type="button" onClick={handleCatalogRefresh} disabled={refreshingCatalog}>
              {refreshingCatalog ? "Updating..." : "Update Models"}
            </button>
          </div>
        </div>

        {localError ? <p className="status-message status-error">{localError}</p> : null}
        {success ? <p className="status-message">{success}</p> : null}

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
            <input value={label} onChange={(event) => setLabel(event.target.value)} placeholder="Primary lane / backup lane" />
          </label>
          <label>
            API key
            <textarea rows={4} value={apiKey} onChange={(event) => setApiKey(event.target.value)} placeholder="Paste managed provider key" />
          </label>
          <div className="admin-form-inline">
            <label>
              Priority
              <input value={priority} onChange={(event) => setPriority(event.target.value)} />
            </label>
            <label className="admin-toggle">
              <input type="checkbox" checked={isEnabled} onChange={(event) => setIsEnabled(event.target.checked)} />
              <span>Enabled for rotation</span>
            </label>
          </div>
          <button className="button button-primary" type="submit" disabled={submitting}>
            {submitting ? "Saving..." : "Add Managed Credential"}
          </button>
        </form>
      </article>

      <DataTable
        title="Catalog status by provider"
        columns={["Provider", "Enabled creds", "Fetched models", "Catalog refreshed", "Last refresh outcome"]}
        rows={providers.map((provider) => {
          const enabledCredentialCount = credentials.filter(
            (item) => item.providerId === provider.providerId && item.isEnabled
          ).length;
          const catalog = catalogProviders.find((item) => item.providerId === provider.providerId);
          const refreshResult = refreshProviders.find((item) => item.providerId === provider.providerId);

          return [
            provider.label,
            enabledCredentialCount,
            catalog?.models?.length || 0,
            formatDate(catalog?.refreshedAtUtc),
            refreshResult ? refreshResult.message : "No refresh run in this session."
          ];
        })}
      />

      <DataTable
        title="Current managed credential roster"
        columns={["Provider", "Label", "Priority", "Status", "Selection order", "Updated", "Action"]}
        rows={
          credentials.length === 0
            ? null
            : credentials.map((item) => [
                item.providerId,
                item.label,
                item.priority,
                item.isEnabled ? "Enabled" : "Disabled",
                item.priority === 0 ? "Primary candidate" : `Fallback after priority ${item.priority - 1}`,
                formatDate(item.updatedAtUtc),
                <button className="table-action" type="button" onClick={() => handleDelete(item.credentialId)}>
                  Remove
                </button>
              ])
        }
        emptyLabel="No managed credentials configured yet."
      />

      {providers.map((provider) => {
        const catalog = catalogProviders.find((item) => item.providerId === provider.providerId);
        const providerModels = catalog?.models || [];

        return (
          <div className="glass-panel table-panel table-span-full" key={`${provider.providerId}-catalog`}>
            <div className="table-header">
              <div>
                <p className="eyebrow">{provider.label} catalog</p>
                <h3>{providerModels.length} fetched models</h3>
              </div>
            </div>
            <TableScroll>
              <table>
                <thead>
                  <tr>
                    <th>Model ID</th>
                    <th>Display name</th>
                    <th>Vision</th>
                    <th>Action</th>
                  </tr>
                </thead>
                <tbody>
                  {providerModels.length === 0 ? (
                    <tr>
                      <td colSpan="4">No stored catalog for this provider yet.</td>
                    </tr>
                  ) : (
                    providerModels.map((model) => (
                      <tr key={`${provider.providerId}-${model.modelId}`}>
                        <td>{model.modelId}</td>
                        <td>{model.displayName}</td>
                        <td>{model.supportsVision ? "Yes" : "No"}</td>
                        <td>
                          <button
                            className="table-action"
                            type="button"
                            onClick={() => handleVisionToggle(provider.providerId, model.modelId, model.supportsVision)}
                          >
                            Mark {model.supportsVision ? "Non-Vision" : "Vision"}
                          </button>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </TableScroll>
          </div>
        );
      })}
    </div>
  );
}

function AdminUsersPanel({ accessToken, users, onUsersChanged }) {
  const [query, setQuery] = useState("");
  const [selectedUserId, setSelectedUserId] = useState("");
  const [selectedUser, setSelectedUser] = useState(null);
  const [accountForm, setAccountForm] = useState({
    accessTier: "free",
    proAvailableCredits: "0",
    premiumAvailableCredits: "0",
    premiumNegativeCredits: "0",
    offlineModeEnabled: false,
    reason: ""
  });
  const [creditForm, setCreditForm] = useState({
    proCreditsToAdd: "0",
    premiumCreditsToAdd: "0",
    reason: ""
  });
  const [lockReason, setLockReason] = useState("Admin manual lock clear");
  const [status, setStatus] = useState("");
  const [error, setError] = useState("");
  const [busyAction, setBusyAction] = useState("");

  const normalizedQuery = query.trim().toLowerCase();
  const filteredUsers = users.filter((item) => {
    const haystack = [item.email, item.userId, item.accessTier, item.planLabel].join(" ").toLowerCase();
    return !normalizedQuery || haystack.includes(normalizedQuery);
  });

  useEffect(() => {
    if (!selectedUserId && filteredUsers.length > 0) {
      setSelectedUserId(filteredUsers[0].userId);
    }
  }, [filteredUsers, selectedUserId]);

  useEffect(() => {
    let cancelled = false;

    async function loadUser() {
      if (!selectedUserId) {
        setSelectedUser(null);
        return;
      }

      try {
        const detail = await fetchAdminUser(accessToken, selectedUserId);
        if (!cancelled) {
          setSelectedUser(detail);
          setAccountForm({
            accessTier: detail.accessTier || "free",
            proAvailableCredits: String(detail.proAvailableCredits ?? 0),
            premiumAvailableCredits: String(detail.premiumAvailableCredits ?? 0),
            premiumNegativeCredits: String(detail.premiumNegativeCredits ?? 0),
            offlineModeEnabled: Boolean(detail.offlineModeEnabled),
            reason: ""
          });
          setCreditForm({
            proCreditsToAdd: "0",
            premiumCreditsToAdd: "0",
            reason: ""
          });
        }
      } catch (loadError) {
        if (!cancelled) {
          setError(loadError.message || "Could not load the selected user.");
        }
      }
    }

    loadUser();
    return () => {
      cancelled = true;
    };
  }, [accessToken, selectedUserId]);

  async function syncSelectedUser(nextUserId = selectedUserId) {
    const nextDetail = nextUserId ? await fetchAdminUser(accessToken, nextUserId) : null;
    await onUsersChanged();
    setSelectedUser(nextDetail);
  }

  async function handleAccountUpdate(event) {
    event.preventDefault();
    if (!selectedUserId) {
      return;
    }

    setBusyAction("account");
    setStatus("");
    setError("");
    try {
      const updated = await updateAdminUser(accessToken, {
        userId: selectedUserId,
        accessTier: accountForm.accessTier,
        proAvailableCredits: Number(accountForm.proAvailableCredits) || 0,
        premiumAvailableCredits: Number(accountForm.premiumAvailableCredits) || 0,
        premiumNegativeCredits: Number(accountForm.premiumNegativeCredits) || 0,
        offlineModeEnabled: accountForm.offlineModeEnabled,
        reason: accountForm.reason
      });
      await onUsersChanged();
      setSelectedUser(updated);
      setStatus("User account settings updated.");
    } catch (updateError) {
      setError(updateError.message || "Could not update the user account.");
    } finally {
      setBusyAction("");
    }
  }

  async function handleCreditGrant(event) {
    event.preventDefault();
    if (!selectedUserId) {
      return;
    }

    setBusyAction("credits");
    setStatus("");
    setError("");
    try {
      await grantAdminCredits(accessToken, {
        userId: selectedUserId,
        proCreditsToAdd: Number(creditForm.proCreditsToAdd) || 0,
        premiumCreditsToAdd: Number(creditForm.premiumCreditsToAdd) || 0,
        reason: creditForm.reason
      });
      await syncSelectedUser();
      setCreditForm({
        proCreditsToAdd: "0",
        premiumCreditsToAdd: "0",
        reason: ""
      });
      setStatus("Credits granted.");
    } catch (grantError) {
      setError(grantError.message || "Could not grant credits.");
    } finally {
      setBusyAction("");
    }
  }

  async function handleWaiveDebt() {
    if (!selectedUserId) {
      return;
    }

    setBusyAction("waive");
    setStatus("");
    setError("");
    try {
      await waiveAdminPremiumDebt(accessToken, {
        userId: selectedUserId,
        reason: "Admin waived premium debt"
      });
      await syncSelectedUser();
      setStatus("Premium debt waived.");
    } catch (waiveError) {
      setError(waiveError.message || "Could not waive premium debt.");
    } finally {
      setBusyAction("");
    }
  }

  async function handleClearLock() {
    if (!selectedUserId) {
      return;
    }

    setBusyAction("lock");
    setStatus("");
    setError("");
    try {
      await clearAdminLock(accessToken, {
        userId: selectedUserId,
        reason: lockReason
      });
      await syncSelectedUser();
      setStatus("Active lock cleared.");
    } catch (lockError) {
      setError(lockError.message || "Could not clear the active lock.");
    } finally {
      setBusyAction("");
    }
  }

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Users</p>
        <h1>Inspect account state and apply manual corrections without mixing this into user-facing flows.</h1>
        <p>User operations here should be deliberate, auditable, and narrow.</p>
      </article>

      <article className="glass-panel admin-form-panel">
        <div className="table-header">
          <div>
            <p className="eyebrow">Directory</p>
            <h3>User roster</h3>
          </div>
          <button className="button button-secondary button-compact" type="button" onClick={onUsersChanged}>
            Refresh
          </button>
        </div>
        <label>
          Search
          <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="email, user ID, or tier" />
        </label>
      </article>

      <DataTable
        title="Accounts"
        columns={["User", "Tier", "Pro", "Premium", "Debt", "Phone", "Detail"]}
        rows={
          filteredUsers.length === 0
            ? null
            : filteredUsers.map((item) => [
                item.email,
                item.planLabel,
                item.proAvailableCredits,
                item.premiumAvailableCredits,
                item.premiumNegativeCredits,
                item.phoneVerified ? "Verified" : "Pending",
                <button className="table-action" type="button" onClick={() => setSelectedUserId(item.userId)}>
                  Inspect
                </button>
              ])
        }
        emptyLabel="No users matched the current search."
      />

      {selectedUser ? (
        <>
          <div className="glass-panel table-panel table-span-full">
            <div className="table-header">
              <div>
                <p className="eyebrow">Selected user</p>
                <h3>{selectedUser.email}</h3>
              </div>
            </div>
            <TableScroll>
              <table>
                <tbody>
                  <tr><th>Email</th><td>{selectedUser.email}</td></tr>
                  <tr><th>User ID</th><td>{selectedUser.userId}</td></tr>
                  <tr><th>Tier</th><td>{selectedUser.planLabel} ({selectedUser.accessTier})</td></tr>
                  <tr><th>Email verified</th><td>{selectedUser.emailVerified ? "Yes" : "No"}</td></tr>
                  <tr><th>Phone verified</th><td>{selectedUser.phoneVerified ? "Yes" : "No"}</td></tr>
                  <tr><th>Pro credits</th><td>{selectedUser.proAvailableCredits}</td></tr>
                  <tr><th>Premium credits</th><td>{selectedUser.premiumAvailableCredits}</td></tr>
                  <tr><th>Premium debt</th><td>{selectedUser.premiumNegativeCredits}</td></tr>
                  <tr><th>Offline mode</th><td>{selectedUser.offlineModeEnabled ? "Enabled" : "Disabled"}</td></tr>
                  <tr><th>Active lock</th><td>{selectedUser.activeLockSessionId || "No active lock"}</td></tr>
                  <tr><th>Last validated</th><td>{formatDate(selectedUser.lastValidatedAtUtc)}</td></tr>
                </tbody>
              </table>
            </TableScroll>
          </div>

          <form className="glass-panel admin-form-panel" onSubmit={handleAccountUpdate}>
            <p className="eyebrow">Update account state</p>
            <div className="admin-form">
              <label>
                User type
                <select value={accountForm.accessTier} onChange={(event) => setAccountForm((current) => ({ ...current, accessTier: event.target.value }))}>
                  <option value="free">Free</option>
                  <option value="pro_byo">Pro BYO</option>
                  <option value="premium">Premium</option>
                </select>
              </label>
              <label>
                Pro credits
                <input value={accountForm.proAvailableCredits} onChange={(event) => setAccountForm((current) => ({ ...current, proAvailableCredits: event.target.value }))} />
              </label>
              <label>
                Premium credits
                <input value={accountForm.premiumAvailableCredits} onChange={(event) => setAccountForm((current) => ({ ...current, premiumAvailableCredits: event.target.value }))} />
              </label>
              <label>
                Premium debt
                <input value={accountForm.premiumNegativeCredits} onChange={(event) => setAccountForm((current) => ({ ...current, premiumNegativeCredits: event.target.value }))} />
              </label>
              <label className="admin-toggle">
                <input type="checkbox" checked={accountForm.offlineModeEnabled} onChange={(event) => setAccountForm((current) => ({ ...current, offlineModeEnabled: event.target.checked }))} />
                <span>Offline mode enabled</span>
              </label>
              <label>
                Reason
                <input value={accountForm.reason} onChange={(event) => setAccountForm((current) => ({ ...current, reason: event.target.value }))} placeholder="Why this manual update is needed" />
              </label>
            </div>
            <button className="button button-primary" type="submit" disabled={busyAction === "account"}>
              {busyAction === "account" ? "Saving..." : "Save Account Changes"}
            </button>
          </form>

          <form className="glass-panel admin-form-panel" onSubmit={handleCreditGrant}>
            <p className="eyebrow">Grant credits</p>
            <div className="admin-form">
              <label>
                Add Pro credits
                <input value={creditForm.proCreditsToAdd} onChange={(event) => setCreditForm((current) => ({ ...current, proCreditsToAdd: event.target.value }))} />
              </label>
              <label>
                Add Premium credits
                <input value={creditForm.premiumCreditsToAdd} onChange={(event) => setCreditForm((current) => ({ ...current, premiumCreditsToAdd: event.target.value }))} />
              </label>
              <label>
                Reason
                <input value={creditForm.reason} onChange={(event) => setCreditForm((current) => ({ ...current, reason: event.target.value }))} placeholder="Promo credit, support fix, manual correction" />
              </label>
            </div>
            <button className="button button-primary" type="submit" disabled={busyAction === "credits"}>
              {busyAction === "credits" ? "Applying..." : "Grant Credits"}
            </button>
          </form>

          <article className="glass-panel admin-form-panel">
            <p className="eyebrow">Recovery controls</p>
            <div className="admin-form">
              <label>
                Lock clear reason
                <input value={lockReason} onChange={(event) => setLockReason(event.target.value)} />
              </label>
            </div>
            <div className="inline-actions">
              <button className="button button-secondary" type="button" onClick={handleClearLock} disabled={busyAction === "lock"}>
                {busyAction === "lock" ? "Clearing..." : "Clear Active Lock"}
              </button>
              <button className="button button-ghost" type="button" onClick={handleWaiveDebt} disabled={busyAction === "waive"}>
                {busyAction === "waive" ? "Waiving..." : "Waive Premium Debt"}
              </button>
            </div>
          </article>

          <DataTable
            title="Recent ledger entries"
            columns={["Ledger entry", "Session", "Credits", "Debt", "Created"]}
            rows={
              (selectedUser.recentLedgerEntries || []).length === 0
                ? null
                : selectedUser.recentLedgerEntries.map((item) => [
                    item.ledgerEntryId,
                    item.sessionId,
                    item.chargedCredits,
                    item.addedPremiumDebt,
                    formatDate(item.createdAtUtc)
                  ])
            }
            emptyLabel="No recent ledger activity for this user."
          />
        </>
      ) : null}

      {error ? <article className="glass-panel table-span-full"><p className="status-message status-error">{error}</p></article> : null}
      {status ? <article className="glass-panel table-span-full"><p className="status-message">{status}</p></article> : null}
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
    const haystack = [item.externalEventId, item.eventType, item.payloadJson].join(" ").toLowerCase();
    return !normalizedQuery || haystack.includes(normalizedQuery);
  });

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Payment operations</p>
        <h1>Track checkout, confirmation, and wallet credit application in one place.</h1>
      </article>

      <MetricCard label="Payment orders" value={String(overview?.paymentOrderCount ?? 0)} />
      <MetricCard label="Credited orders" value={String(overview?.creditedPaymentCount ?? 0)} />
      <MetricCard label="Webhook events" value={String(overview?.paymentWebhookCount ?? 0)} />
      <MetricCard label="Processed webhooks" value={String(overview?.processedWebhookCount ?? 0)} />

      <article className="glass-panel admin-form-panel">
        <div className="table-header">
          <div>
            <p className="eyebrow">Filters</p>
            <h3>Transactions and callbacks</h3>
          </div>
          <button className="button button-secondary button-compact" type="button" onClick={onRefresh}>
            Refresh
          </button>
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
            <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="email, checkout ID, payment ID" />
          </label>
        </div>
      </article>

      <DataTable
        title="Recent payment orders"
        columns={["User", "Purchase", "Amount", "Status", "Ops state", "Checkout", "Created", "Detail"]}
        rows={
          filteredOrders.length === 0
            ? null
            : filteredOrders.map((item) => [
                item.email,
                item.displayLabel,
                formatInr(item.amountInr),
                item.status,
                describePaymentOpsState(item),
                item.checkoutId,
                formatDate(item.createdAtUtc),
                <button className="table-action" type="button" onClick={() => setSelectedOrder(item)}>
                  Inspect
                </button>
              ])
        }
        emptyLabel="No payment orders matched the current filters."
      />

      {selectedOrder ? (
        <div className="glass-panel table-panel table-span-full">
          <div className="table-header">
            <div>
              <p className="eyebrow">Selected order detail</p>
              <h3>{selectedOrder.checkoutId}</h3>
            </div>
          </div>
          <TableScroll>
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
          </TableScroll>
        </div>
      ) : null}

      <DataTable
        title="Recent webhook callbacks"
        columns={["Event ID", "Type", "Created", "Processed", "Detail"]}
        rows={
          filteredWebhooks.length === 0
            ? null
            : filteredWebhooks.map((item) => [
                item.externalEventId,
                item.eventType,
                formatDate(item.createdAtUtc),
                formatDate(item.processedAtUtc),
                <button className="table-action" type="button" onClick={() => setSelectedWebhook(item)}>
                  Inspect
                </button>
              ])
        }
        emptyLabel="No webhook events matched the current filters."
      />

      {selectedWebhook ? (
        <article className="glass-panel table-span-full">
          <p className="eyebrow">Selected webhook payload</p>
          <h3>{selectedWebhook.eventType} · {selectedWebhook.externalEventId}</h3>
          <pre className="payload-preview">{prettyJson(selectedWebhook.payloadJson)}</pre>
        </article>
      ) : null}
    </div>
  );
}

function PackCard({ label, pack, buttonLabel, loading, onClick }) {
  return (
    <article className="glass-panel">
      <p className="story-tag">{label}</p>
      <h3>{pack.label}</h3>
      <p>{pack.description}</p>
      <strong>{formatInr(pack.displayAmountInr)} · {pack.credits} credits</strong>
      <button className="button button-primary" type="button" onClick={onClick} disabled={loading}>
        {loading ? "Opening..." : buttonLabel}
      </button>
    </article>
  );
}

function DataTable({ title, columns, rows, emptyLabel = "No records found." }) {
  return (
    <div className="glass-panel table-panel table-span-full">
      <div className="table-header">
        <div>
          <p className="eyebrow">{title}</p>
        </div>
      </div>
      <TableScroll>
        <table>
          <thead>
            <tr>
              {columns.map((column) => (
                <th key={column}>{column}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {!rows || rows.length === 0 ? (
              <tr>
                <td colSpan={columns.length}>{emptyLabel}</td>
              </tr>
            ) : (
              rows.map((row, index) => (
                <tr key={`${title}-${index}`}>
                  {row.map((cell, cellIndex) => (
                    <td key={`${title}-${index}-${cellIndex}`}>{cell}</td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </TableScroll>
    </div>
  );
}

function TableScroll({ children }) {
  return <div className="table-scroll">{children}</div>;
}

function TimelineStep({ index, title, body }) {
  return (
    <article className="timeline-step">
      <span>{index}</span>
      <h3>{title}</h3>
      <p>{body}</p>
    </article>
  );
}

function MetricCard({ label, value, tone = "default" }) {
  return (
    <article className={`glass-panel metric-card tone-${tone}`}>
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  );
}

function MetricDefinition({ title, detail }) {
  return (
    <article className="definition-card">
      <h3>{title}</h3>
      <p>{detail}</p>
    </article>
  );
}

function InfoRow({ label, value }) {
  return (
    <div className="info-row">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function MiniBarList({ items }) {
  const maxValue = Math.max(...items.map((item) => item.value), 1);
  return (
    <div className="mini-bars">
      {items.map((item) => (
        <div className="mini-bar-row" key={item.label}>
          <div className="mini-bar-meta">
            <span>{item.label}</span>
            <strong>{item.value}</strong>
          </div>
          <div className="mini-bar-track">
            <div className="mini-bar-fill" style={{ width: `${(item.value / maxValue) * 100}%` }} />
          </div>
        </div>
      ))}
    </div>
  );
}

function SimpleSparkline({ values }) {
  const points = useMemo(() => {
    const usable = values.length > 0 ? values : [0, 0, 0];
    const max = Math.max(...usable, 1);
    return usable
      .map((value, index) => {
        const x = (index / Math.max(usable.length - 1, 1)) * 100;
        const y = 100 - (Number(value || 0) / max) * 100;
        return `${x},${y}`;
      })
      .join(" ");
  }, [values]);

  return (
    <svg className="sparkline" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
      <polyline fill="none" stroke="url(#spark-gradient)" strokeWidth="4" points={points} />
      <defs>
        <linearGradient id="spark-gradient" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stopColor="#4cc9f0" />
          <stop offset="100%" stopColor="#34d399" />
        </linearGradient>
      </defs>
    </svg>
  );
}

function setMeta(name, content, attribute = "name") {
  let node = document.querySelector(`meta[${attribute}="${name}"]`);
  if (!node) {
    node = document.createElement("meta");
    node.setAttribute(attribute, name);
    document.head.appendChild(node);
  }
  node.setAttribute("content", content);
}

function parseUtcMillis(value) {
  if (!value) {
    return 0;
  }
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? 0 : parsed;
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

function formatDate(value) {
  if (!value) {
    return "n/a";
  }

  return new Intl.DateTimeFormat("en-US", {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(new Date(value));
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

function countBy(items, keyFn) {
  return items.reduce((accumulator, item) => {
    const key = keyFn(item);
    accumulator[key] = (accumulator[key] || 0) + 1;
    return accumulator;
  }, {});
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
      return "Checkout succeeded in the browser and the backend is waiting for the webhook to credit the wallet.";
    case "stuck_waiting_webhook":
      return "Client confirmed but still not credited after 2+ minutes. Check delivery, webhook URL, secret, and backend logs.";
    default:
      return "Order created, but browser confirmation has not been recorded yet.";
  }
}
