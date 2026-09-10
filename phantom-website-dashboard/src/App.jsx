import { useDeferredValue, useEffect, useId, useMemo, useRef, useState } from "react";
import { Link, NavLink, Navigate, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import {
  clearAdminLock,
  confirmPaymentCheckout,
  createSignedDownloadLink,
  createHostedKnowledgeBase,
  createHostedKnowledgeBaseExperience,
  createPaymentCheckout,
  createUserSupportTicket,
  deleteHostedKnowledgeBaseDocument,
  deleteHostedKnowledgeBaseExperience,
  deleteManagedAiCredential,
  deleteManagedSpeechCredential,
  fetchAccountSummary,
  fetchAdminOverview,
  fetchAdminAudit,
  fetchAdminFeedback,
  fetchAdminPaymentOrders,
  fetchAdminSupportTickets,
  fetchAdminPaymentWebhooks,
  fetchAdminUser,
  fetchAdminUserLedger,
  fetchAdminUsers,
  fetchCurrentAdminSession,
  fetchCurrentUserSession,
  fetchDevices,
  fetchDownloadEntitlement,
  fetchGmailOAuthStatus,
  fetchHostedKnowledgeBase,
  fetchHostedKnowledgeBaseDocument,
  fetchInterviewQuestionBanks,
  fetchPublicReviews,
  markHostedKnowledgeBaseProjectRecent,
  pasteHostedKnowledgeBaseDocument,
  fetchManagedAiAdminInventory,
  fetchManagedSpeechAdminInventory,
  fetchManagedAiLatencyStatus,
  fetchPaymentCatalog,
  fetchRegistrationSettings,
  fetchSupportOverview,
  fetchWalletHistory,
  fetchWalletPurchases,
  grantAdminCredits,
  isCurrentBrowserDevice,
  loginAccount,
  loginAdmin,
  logoutAccount,
  logoutAdmin,
  refreshAccountSession,
  refreshAdminSession,
  registerAccount,
  requestAdminPasswordReset,
  requestUserPasswordReset,
  resendVerificationEmail,
  resetAdminPassword,
  resetUserPassword,
  revokeDeviceSession,
  sendPhoneOtp,
  setAdminManualLock,
  startGmailOAuth,
  submitPublicFeedback,
  sendManagedAiAdminTest,
  triggerManagedAiCatalogRefresh,
  triggerManagedSpeechCatalogRefresh,
  triggerManagedAiLatencyCheck,
  updateAdminUser,
  updateAdminFeedback,
  updateAdminSupportTicket,
  updateHostedKnowledgeBaseProfile,
  updateHostedKnowledgeBaseExperience,
  updateHostedKnowledgeBaseProject,
  updateKnowledgeBaseEmbeddingConfig,
  updateInterviewQuestionBank,
  updateManagedAiModelFlags,
  updateManagedAiRuntimeSelection,
  upsertManagedAiCatalogModel,
  updateManagedSpeechRuntimeSelection,
  updateRegistrationSettings,
  uploadHostedKnowledgeBaseDocuments,
  upsertManagedAiCredential,
  upsertManagedSpeechCredential,
  fetchUserSupportTickets,
  verifyPhoneOtp,
  verifyAdminOtp,
  waiveAdminPremiumDebt
} from "./lib/api";
import { maskIdentifier, parseRequiredInteger, parseRequiredNonNegativeNumber } from "./lib/validation";
import { DashboardSkeleton, RetryNotice, SectionSkeleton } from "./components/AsyncState";
import { DataTable, PaginationBar, TableScroll } from "./components/DataTable";
import { InfoRow, MetricCard, MetricDefinition, MiniBarList, SimpleSparkline, TimelineStep } from "./components/DashboardPrimitives";
import {
  countBy,
  describePaymentOpsState,
  formatDate,
  formatInr,
  formatManagedAiLatencyStatus,
  getPaymentOpsState,
  parseUtcMillis,
  prettyJson,
  toDateTimeLocal,
  trimAdminTesterHistory
} from "./lib/format";

const USER_SESSION_STORAGE_KEY = "phantom.website.user-session";
const ADMIN_SESSION_STORAGE_KEY = "phantom.website.admin-session";
const PASSWORD_REQUIREMENTS = "Use 12+ characters with uppercase, lowercase, a number, and a special character. Spaces are not allowed.";

function getPasswordPolicyError(password) {
  if (!password || password.length < 12) return PASSWORD_REQUIREMENTS;
  if (!/[A-Z]/.test(password) || !/[a-z]/.test(password) || !/\d/.test(password) || !/[^A-Za-z0-9\s]/.test(password) || /\s/.test(password)) {
    return PASSWORD_REQUIREMENTS;
  }
  return "";
}

function PasswordField({ label, id, ...inputProps }) {
  const generatedId = useId();
  const inputId = id || generatedId;
  const [visible, setVisible] = useState(false);

  return (
    <div className="password-field">
      <label htmlFor={inputId}>{label}</label>
      <div className="password-input-wrap">
        <input id={inputId} {...inputProps} type={visible ? "text" : "password"} />
        <button
          className="password-visibility"
          type="button"
          aria-label={visible ? `Hide ${label.toLowerCase()}` : `Show ${label.toLowerCase()}`}
          aria-pressed={visible}
          onClick={() => setVisible((current) => !current)}
        >
          {visible ? (
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 3l18 18M10.6 10.7a2 2 0 002.7 2.7M9.9 4.2A10.8 10.8 0 0112 4c5.5 0 9 6 9 6a17.7 17.7 0 01-2.1 2.8M6.6 6.7C4.3 8.2 3 10 3 10s3.5 6 9 6a9.8 9.8 0 004.1-.9" /></svg>
          ) : (
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 12s3.5-6 9-6 9 6 9 6-3.5 6-9 6-9-6-9-6z" /><circle cx="12" cy="12" r="2.5" /></svg>
          )}
        </button>
      </div>
    </div>
  );
}

function clearStoredSession(storageKey) {
  if (typeof window !== "undefined") {
    window.localStorage.removeItem(storageKey);
  }
}

const publicNav = [
  { to: "/", label: "Product", section: "product" },
  { to: "/#features", label: "Features", section: "features" },
  { to: "/#workflow", label: "How it works", section: "workflow" },
  { to: "/pricing", label: "Pricing" },
  { to: "/#reviews", label: "Reviews", section: "reviews" },
  { to: "/download", label: "Download" }
];

const userNav = [
  { to: "/dashboard", label: "Overview" },
  { to: "/dashboard/knowledge-base", label: "Knowledge Base" },
  { to: "/dashboard/wallet", label: "Wallet" },
  { to: "/dashboard/devices", label: "Devices" },
  { to: "/dashboard/history", label: "Usage" },
  { to: "/dashboard/questions", label: "Questions" },
  { to: "/dashboard/support", label: "Support" }
];

const adminNav = [
  { to: "/admin", label: "Overview" },
  { to: "/admin/users", label: "Users" },
  { to: "/admin/payments", label: "Payments" },
  { to: "/admin/tickets", label: "Tickets" },
  { to: "/admin/feedback", label: "Feedback" },
  { to: "/admin/managed-ai", label: "Managed AI" },
  { to: "/admin/managed-speech", label: "Speech Recognition" },
  { to: "/admin/audit", label: "Audit" },
  { to: "/admin/settings", label: "Settings" }
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
      "2 hosted trial blocks, 15 minutes each",
      "Email verification required; phone OTP follows deployment policy",
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

const publicFeatureRows = [
  { label: "Input", value: "Voice, text, and screenshots" },
  { label: "Context", value: "Resume, projects, and role research" },
  { label: "AI lane", value: "Managed models or your own providers" },
  { label: "Runtime", value: "Focused Windows and macOS workspace" }
];

const publicJourney = [
  {
    title: "Build your interview context",
    body:
      "Organise your resume, project stories, company research, and role notes into one reusable workspace."
  },
  {
    title: "Check readiness before the call",
    body:
      "Confirm your account, credits, model lane, device, and knowledge status before you enter a live round."
  },
  {
    title: "Respond with relevant context",
    body:
      "Phantom retrieves the material that fits the question and streams a focused response inside the desktop workspace."
  }
];

const publicValueProps = [
  {
    title: "Context that follows the conversation",
    detail:
      "Your profile, projects, recent work, and interview notes become useful precisely when the question calls for them."
  },
  {
    title: "A model setup that fits you",
    detail:
      "Begin with Phantom-managed AI, connect supported provider accounts for more control, or use the full Premium workflow."
  },
  {
    title: "Built for Windows and macOS",
    detail:
      "The website prepares the account and context. The live experience stays in Phantom's focused desktop application."
  }
];

const publicFeatureCards = [
  { index: "01", title: "Live voice input", detail: "Capture the question by microphone and keep your hands free while the conversation moves." },
  { index: "02", title: "Visual context", detail: "Attach a targeted screenshot when code, diagrams, or shared material need to become part of the prompt." },
  { index: "03", title: "Interview memory", detail: "Turn resumes, project stories, role notes, and company research into reusable context." },
  { index: "04", title: "Managed or BYO models", detail: "Start with Phantom-managed AI or connect supported provider accounts when you want direct control." },
  { index: "05", title: "Windows and macOS", detail: "Use the same account, wallet, and hosted context across focused native desktop experiences." },
  { index: "06", title: "Operational safeguards", detail: "Device visibility, signed downloads, session locks, usage reconciliation, and clear account readiness checks." }
];

const publicFaqs = [
  {
    question: "What is Phantom?",
    answer:
      "Phantom is a Windows and macOS interview workspace that helps you stay prepared and responsive in live rounds. It combines a focused desktop experience with account, wallet, and context management on the web."
  },
  {
    question: "Is Phantom browser-based?",
    answer:
      "The core interview workspace runs on Windows and macOS. The website is where you create your account, manage access, compare plans, review wallet activity, and handle premium knowledge-base setup."
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
    title: "1. Your agreement and privacy acknowledgement",
    body:
      "By creating a Phantom account, you agree to these Terms of Use and acknowledge the Privacy Policy. You understand that Phantom processes the account, verification, device, usage, payment, support, and optional knowledge-base data described there to provide and protect the service."
  },
  {
    title: "2. Service boundaries",
    body:
      "Phantom is a hosted website and dashboard layer paired with Windows and macOS desktop runtimes. The website is for registration, verification, payments, hosted knowledge-base management, device visibility, and admin operations. It is not the live interview runtime itself."
  },
  {
    title: "3. Account responsibility",
    body:
      "You are responsible for the accuracy of registration information, the security of your credentials, and all activity that occurs under your account. You must keep access credentials confidential and notify the deployment operator if you suspect unauthorized access."
  },
  {
    title: "4. Acceptable use",
    body:
      "You may only use Phantom for lawful, authorized purposes. You must comply with the rules of the interview, exam, employer, institution, or platform where Phantom is used. You must not use Phantom to bypass proctoring, impersonate another person, violate confidentiality obligations, upload unauthorized third-party content, attempt to extract provider secrets, or interfere with system integrity."
  },
  {
    title: "5. Screen-sharing visibility and user responsibility",
    body:
      "Phantom requests platform-level capture exclusion, but invisibility is not guaranteed on every operating-system version, meeting client, capture tool, display mode, or configuration. You must verify the actual meeting, recording, and screen-sharing preview before use. If Phantom is visible, you must stop sharing or close it; if you continue, you accept responsibility for that exposure. Phantom and its operators are not responsible for exposure caused by unsupported or ineffective capture protection, except where liability cannot legally be excluded."
  },
  {
    title: "6. AI output and user judgment",
    body:
      "AI outputs can be incomplete, inaccurate, or inappropriate for the situation. You remain responsible for reviewing and deciding whether to rely on any generated content, suggestions, or retrieved knowledge-base material."
  },
  {
    title: "7. Payments, credits, and debt settlement",
    body:
      "Credit packs, hosted usage, and debt-settlement flows must follow the wallet rules defined by the deployment operator. Phantom may suspend access or limit premium features when credits are exhausted, balances become negative, or payment confirmation cannot be trusted."
  },
  {
    title: "8. Hosted content",
    body:
      "You represent that you have the right to upload and process any document, prompt, key, note, or other material you submit to Phantom. Do not upload confidential or regulated material unless your deployment is explicitly authorized for that use."
  },
  {
    title: "9. Suspension and termination",
    body:
      "Phantom may suspend or terminate access for security incidents, unpaid balances, abuse, fraud risk, policy violations, or system-protection reasons. Admin operators may also correct account state, clear locks, or revoke access when required to preserve service integrity."
  },
  {
    title: "10. No warranty for uninterrupted availability",
    body:
      "Phantom aims for reliable service, but hosted components may be interrupted by provider outages, payment failures, verification issues, network disruptions, or maintenance. Availability of the website does not guarantee the availability of any third-party provider lane."
  },
  {
    title: "11. Limitation and operator terms",
    body:
      "These terms should be read together with any deployment-specific commercial, legal, or support terms published by the operator of your Phantom environment. Where local law requires additional notices, refunds, disclosures, or rights, those rules continue to apply."
  },
  {
    title: "12. Contact",
    body:
      "For legal, privacy, billing, or support requests, use the support route exposed by the Phantom deployment you use, including the dashboard support surface or the contact details published by the operator."
  }
];

const refundSections = [
  {
    title: "1. When a refund may be requested",
    body: "You may request a refund within 7 calendar days of purchase when a paid credit pack has not been used. Duplicate charges, confirmed payment errors, and charges for a service Phantom could not provide will also be reviewed. Nothing in this policy limits rights that cannot be excluded under applicable consumer law."
  },
  {
    title: "2. Digital credits and partial use",
    body: "Phantom credit packs are digital services made available to your account after payment confirmation. Once any credit from a pack has been consumed, that pack is normally non-refundable because the service has begun. If a verified service failure affected only part of a pack, Phantom may offer a proportionate credit restoration or refund after reviewing usage records."
  },
  {
    title: "3. Failed, pending, or duplicate payments",
    body: "A payment that appears debited but was not confirmed by Phantom may be automatically reversed by the bank or payment provider. Contact support with the payment date, amount, account email, and Razorpay payment or order ID. Never send a card number, CVV, OTP, UPI PIN, or banking password. Duplicate captured payments are eligible for review and refund."
  },
  {
    title: "4. How to request a refund",
    body: "Submit a billing ticket from the signed-in dashboard or email official.phantomai@gmail.com from the address on your Phantom account. Include the reason for the request and the relevant payment or order ID. Requests are acknowledged as soon as reasonably possible and are assessed against payment and usage records."
  },
  {
    title: "5. Approved refunds and timing",
    body: "Approved refunds are returned to the original payment method. Phantom will initiate the refund promptly after approval. Banking and payment-provider processing can take approximately 7 to 10 working days after initiation, and the exact timing depends on the payment method and financial institution."
  },
  {
    title: "6. Non-refundable situations",
    body: "Refunds may be declined when credits have been used, the request is outside the stated window without a legal or service-failure basis, account access was suspended for abuse or a material policy violation, or the request cannot be matched to a captured payment. This does not override any mandatory remedy available under applicable law."
  },
  {
    title: "7. Cancellations and account closure",
    body: "Phantom currently sells credit packs rather than automatically renewing subscriptions. Closing an account does not automatically refund used or expired credits. If recurring billing is introduced, its cancellation terms will be disclosed before purchase and this policy will be updated."
  },
  {
    title: "8. Disputes and contact",
    body: "Please contact Phantom first so the payment and usage record can be investigated. If a refund has been initiated, the refund reference supplied by the payment provider can be used with your bank. For billing questions, use the dashboard support route or official.phantomai@gmail.com."
  }
];

export default function App() {
  const location = useLocation();
  const isAdminRoute = location.pathname.startsWith("/admin");
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

  useEffect(() => {
    clearStoredSession(USER_SESSION_STORAGE_KEY);
    clearStoredSession(ADMIN_SESSION_STORAGE_KEY);
  }, []);

  useEffect(() => {
    if (!location.hash) {
      window.scrollTo({ top: 0, behavior: "auto" });
      return;
    }

    window.requestAnimationFrame(() => {
      document.getElementById(location.hash.slice(1))?.scrollIntoView({ behavior: "smooth" });
    });
  }, [location.pathname, location.hash]);

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
      clearStoredSession(USER_SESSION_STORAGE_KEY);
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
      clearStoredSession(ADMIN_SESSION_STORAGE_KEY);
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
          const refreshed = await refreshAccountSession(userSession?.refreshToken);
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
              clearStoredSession(USER_SESSION_STORAGE_KEY);
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
        const refreshed = await refreshAccountSession(userSession?.refreshToken);
        if (!cancelled) {
          handleUserAuthenticated(refreshed);
        }
      } catch {
        if (!cancelled) {
          setUserSession(null);
          clearStoredSession(USER_SESSION_STORAGE_KEY);
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
      if (!isAdminRoute) {
        if (!cancelled) {
          setAdminSessionReady(true);
        }
        return;
      }

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
          const refreshed = await refreshAdminSession(adminSession?.refreshToken);
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
              clearStoredSession(ADMIN_SESSION_STORAGE_KEY);
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
        const refreshed = await refreshAdminSession(adminSession?.refreshToken);
        if (!cancelled) {
          handleAdminAuthenticated(refreshed);
        }
      } catch {
        if (!cancelled) {
          setAdminSession(null);
          clearStoredSession(ADMIN_SESSION_STORAGE_KEY);
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
  }, [adminSession?.expiresAtUtc, adminSession?.isAuthenticated, adminSessionHydrationEnabled, isAdminRoute]);

  const surface = isAdminRoute
    ? "admin"
    : location.pathname.startsWith("/dashboard")
      ? "user"
      : "public";

  return (
    <div className={`app-shell surface-${surface}`}>
      <a className="skip-link" href="#main-content">Skip to main content</a>
      <div className="ambient ambient-one" />
      <div className="ambient ambient-two" />
      <SiteHeader
        surface={surface}
        userSession={userSession}
        adminSession={adminSession}
        onUserLogout={handleUserLogout}
        onAdminLogout={handleAdminLogout}
      />

      <div id="main-content" tabIndex={-1}>
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
        <Route path="/forgot-password" element={<UserForgotPasswordPage />} />
        <Route path="/reset-password" element={<UserResetPasswordPage />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/desktop-return" element={<DesktopReturnPage />} />
        <Route path="/privacy" element={<PrivacyPolicyPage />} />
        <Route path="/terms" element={<TermsPage />} />
        <Route path="/refund-policy" element={<RefundPolicyPage />} />
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
      </div>

      {surface === "public" ? <PublicFooter /> : null}
    </div>
  );
}

function SiteHeader({ surface, userSession, adminSession, onUserLogout, onAdminLogout }) {
  const location = useLocation();
  const navItems = surface === "admin" ? adminNav : surface === "user" ? userNav : publicNav;
  const session = surface === "admin" ? adminSession : userSession;
  const [activePublicSection, setActivePublicSection] = useState("product");

  useEffect(() => {
    if (surface !== "public" || location.pathname !== "/") {
      return undefined;
    }

    function updateActiveSection() {
      const marker = window.scrollY + Math.min(window.innerHeight * 0.35, 280);
      const activeSection = publicNav
        .filter((item) => item.section)
        .reduce((current, item) => {
          const section = document.getElementById(item.section);
          return section && section.offsetTop <= marker ? item.section : current;
        }, "product");
      setActivePublicSection(activeSection);
    }

    updateActiveSection();
    window.addEventListener("scroll", updateActiveSection, { passive: true });
    window.addEventListener("resize", updateActiveSection);
    return () => {
      window.removeEventListener("scroll", updateActiveSection);
      window.removeEventListener("resize", updateActiveSection);
    };
  }, [location.pathname, surface]);

  return (
    <header className="site-header">
      <Link className="brandmark" to="/">
        <img className="brandmark-mark brandmark-logo" src="/brand/phantom-mark.svg" alt="" />
        <span>
          <strong>Phantom</strong>
          <small>{surface === "public" ? "Interview intelligence for Windows + macOS" : surface === "admin" ? "Operations" : "Workspace"}</small>
        </span>
      </Link>

      <nav className="top-nav" aria-label="Primary">
        {navItems.map((item) => {
          if (surface === "public") {
            const isActive = item.section
              ? location.pathname === "/" && activePublicSection === item.section
              : location.pathname === item.to;
            return (
              <Link
                key={item.to}
                className={`nav-chip ${isActive ? "nav-chip-active" : ""}`}
                to={item.to}
                aria-current={isActive ? "page" : undefined}
              >
                {item.label}
              </Link>
            );
          }

          return (
            <NavLink
              key={item.to}
              className={({ isActive }) => `nav-chip ${isActive ? "nav-chip-active" : ""}`}
              to={item.to}
              end={item.to === "/dashboard" || item.to === "/admin"}
            >
              {item.label}
            </NavLink>
          );
        })}
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
        <div className="footer-brand">
          <Link className="brandmark" to="/">
            <img className="brandmark-mark brandmark-logo" src="/brand/phantom-mark.svg" alt="" />
            <span><strong>Phantom</strong><small>Interview intelligence for Windows + macOS</small></span>
          </Link>
          <p className="footer-copy">
            Prepare deeply. Stay present. Bring the right context into every live round.
          </p>
        </div>
        <div className="footer-column">
          <strong>Product</strong>
          <Link to="/#workflow">How it works</Link>
          <Link to="/#features">Features</Link>
          <Link to="/#reviews">Reviews</Link>
          <Link to="/pricing">Pricing</Link>
          <Link to="/download">Download</Link>
        </div>
        <div className="footer-column">
          <strong>Account</strong>
          <Link to="/register">Create account</Link>
          <Link to="/login">Sign in</Link>
          <Link to="/dashboard/support">Support</Link>
        </div>
        <div className="footer-column">
          <strong>Legal</strong>
          <Link to="/privacy">Privacy Policy</Link>
          <Link to="/terms">Terms of Use</Link>
          <Link to="/refund-policy">Refund Policy</Link>
        </div>
      </div>
      <div className="footer-bottom">
        <span>© {new Date().getFullYear()} Phantom</span>
        <span>Windows and macOS desktop applications · AI output requires human judgement</span>
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
    setMeta("og:url", window.location.href, "property");
    setMeta("og:image", `${window.location.origin}/brand/phantom-logo-512.png`, "property");
    setMeta("twitter:card", "summary_large_image");
    setMeta("twitter:title", title);
    setMeta("twitter:description", description);
    setMeta("twitter:image", `${window.location.origin}/brand/phantom-logo-512.png`);
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
  const [reviews, setReviews] = useState([]);

  useEffect(() => {
    let cancelled = false;
    fetchPublicReviews(6)
      .then((result) => { if (!cancelled) setReviews(result?.items || []); })
      .catch(() => { if (!cancelled) setReviews([]); });
    return () => { cancelled = true; };
  }, []);

  const structuredData = {
    "@context": "https://schema.org",
    "@type": "SoftwareApplication",
    name: "Phantom",
    applicationCategory: "ProductivityApplication",
    operatingSystem: ["Windows 10", "macOS 12.3"]
  };

  return (
    <main className="page marketing-page">
      <Seo
        title="Phantom | Think clearly when the room gets loud"
        description="Bring your preparation, project stories, and AI support into one focused Windows and macOS interview workspace."
        structuredData={structuredData}
      />

      <section className="marketing-hero" id="product">
        <div className="marketing-hero-copy">
          <p className="eyebrow">A calmer way to show up prepared</p>
          <h1>Think clearly when the room gets <em>loud.</em></h1>
          <p className="lead-copy">
            Phantom brings your preparation, project stories, and AI support into one focused Windows or macOS workspace—ready when the interview changes direction.
          </p>
          <div className="hero-actions">
            <Link className="button button-primary" to={userSession?.isAuthenticated ? "/dashboard" : "/register"}>
              {userSession?.isAuthenticated ? "Open your workspace" : "Start your free trial"}
            </Link>
            <a className="button button-secondary" href="#workflow">See how Phantom works</a>
          </div>
          <div className="hero-meta" aria-label="Product facts">
            <span>Windows 10/11</span>
            <span>macOS 12.3+</span>
            <span>Hosted or BYO AI</span>
            <span>Voice + screenshots</span>
          </div>
        </div>

        <article className="phantom-demo" aria-label="Example of Phantom using interview context">
          <div className="demo-toolbar"><span>Interview · System design</span><span>18:42</span></div>
          <div className="demo-transcript">
            <div><small>Interviewer</small><p>How did you reduce risk during the payment migration?</p></div>
            <div><small>Your preparation</small><p>Migration notes · rollback plan · impact metrics</p></div>
          </div>
          <div className="phantom-lens">
            <div className="lens-header"><strong>Phantom Lens</strong><span>Premium context</span></div>
            <p>Lead with the zero-downtime result, then explain the shadow-traffic rollout and the rollback window you reduced from hours to minutes.</p>
            <div className="lens-footer"><span>Resume + Payment migration</span><span>Ready</span></div>
          </div>
        </article>
      </section>

      <section className="product-ribbon" aria-label="Phantom capabilities">
        {publicFeatureRows.map((item) => <InfoRow key={item.label} label={item.label} value={item.value} />)}
      </section>

      <section className="features-section" id="features">
        <div className="section-heading">
          <p className="eyebrow">Features built around the live moment</p>
          <h2>Everything you need to prepare, retrieve, and respond without losing the conversation.</h2>
        </div>
        <div className="feature-card-grid">
          {publicFeatureCards.map((feature) => (
            <article className="feature-card" key={feature.title}>
              <span>{feature.index}</span>
              <h3>{feature.title}</h3>
              <p>{feature.detail}</p>
            </article>
          ))}
        </div>
      </section>

      <section className="story-intro" id="workflow">
        <div>
          <p className="eyebrow">One continuous workflow</p>
          <h2>From preparation to live support, without the tab chaos.</h2>
        </div>
        <p>Phantom is designed around the interview itself. The website organises access and context; the Windows and macOS apps keep both close when the conversation starts moving.</p>
      </section>

      <section className="triple-grid journey-grid">
        {publicJourney.map((item, index) => <TimelineStep key={item.title} index={`0${index + 1}`} title={item.title} body={item.body} />)}
      </section>

      <section className="feature-showcase feature-showcase-dark">
        <div className="feature-copy">
          <p className="eyebrow">The context layer</p>
          <h2>Your experience becomes useful at the exact moment a question calls for it.</h2>
          <p>Premium knowledge spaces turn resumes, project notes, architecture decisions, company research, and recent work into structured interview context.</p>
          <Link className="text-link" to={userSession?.isAuthenticated ? "/dashboard/knowledge-base" : "/register"}>Build your knowledge space <span>→</span></Link>
        </div>
        <div className="context-map" aria-label="Example knowledge context">
          <div className="context-source"><small>Profile</small><strong>Backend engineer</strong><span>8 years · Fintech</span></div>
          <div className="context-source"><small>Recent project</small><strong>Payment migration</strong><span>.NET · PostgreSQL · Kafka</span></div>
          <div className="context-result"><small>Retrieved for this question</small><strong>Zero-downtime migration story</strong><span>Impact, trade-offs, and rollback plan ready</span></div>
        </div>
      </section>

      <section className="feature-showcase">
        <div className="feature-copy">
          <p className="eyebrow">One workspace, your choice of AI</p>
          <h2>Start managed. Bring your own stack when you want more control.</h2>
          <p>Free Trial lets you experience the workflow without configuring a provider. Pro BYO uses supported provider accounts. Premium combines managed models with hosted interview context.</p>
          <Link className="text-link" to="/pricing">Compare every plan <span>→</span></Link>
        </div>
        <div className="lane-list">
          <div><span>Free</span><strong>Explore Phantom with managed trial access</strong></div>
          <div><span>Pro BYO</span><strong>Use your own provider keys and model spend</strong></div>
          <div><span>Premium</span><strong>Managed AI with the complete context workflow</strong></div>
        </div>
      </section>

      <section className="trust-section" id="security">
        <div className="section-heading">
          <p className="eyebrow">Designed with clear boundaries</p>
          <h2>Your account, device, usage, and interview context stay deliberately separated.</h2>
        </div>
        <div className="comparison-grid">
          {publicValueProps.map((item) => <MetricDefinition key={item.title} title={item.title} detail={item.detail} />)}
        </div>
        <p className="responsible-note">Phantom supports preparation and authorised live assistance. Always follow the applicable rules. Capture invisibility is not guaranteed: verify the actual meeting and screen-sharing preview before use, and do not continue if Phantom is visible.</p>
      </section>

      <section className="reviews-section" id="reviews">
        <div className="section-heading">
          <p className="eyebrow">Reviews from the people using Phantom</p>
          <h2>Published only with permission—never manufactured.</h2>
          <p>Every review below comes from feedback a person explicitly allowed Phantom to publish and an admin approved.</p>
        </div>
        {reviews.length > 0 ? (
          <div className="review-grid">
            {reviews.map((review) => (
              <article className="review-card" key={review.feedbackId}>
                <div className="review-stars" aria-label={`${review.rating} out of 5 stars`}>{"★".repeat(review.rating)}<span>{"★".repeat(5 - review.rating)}</span></div>
                <blockquote>“{review.message}”</blockquote>
                <p>{review.name}</p>
              </article>
            ))}
          </div>
        ) : (
          <div className="reviews-empty">
            <strong>We are collecting our first publishable reviews.</strong>
            <p>Use the feedback form below to share an honest experience. Nothing is published without explicit consent and admin review.</p>
          </div>
        )}
      </section>

      <PublicFeedbackSection />

      <section className="triple-grid marketing-pricing">
        {plans.map((plan) => (
          <article className={`plan-card tone-${plan.tone}`} key={plan.name}>
            <div className="plan-head"><span>{plan.badge}</span><strong>{plan.price}</strong></div>
            <h2>{plan.name}</h2><p>{plan.summary}</p>
            <Link className="text-link" to={plan.to}>{plan.cta} <span>→</span></Link>
          </article>
        ))}
      </section>

      <section className="closing-cta">
        <p className="eyebrow">Ready before the call</p>
        <h2>Bring your best context into the room.</h2>
        <p>Start with two hosted 15-minute trial blocks. No provider keys required.</p>
        <div className="hero-actions">
          <Link className="button button-primary" to={userSession?.isAuthenticated ? "/dashboard" : "/register"}>{userSession?.isAuthenticated ? "Open your workspace" : "Create your free account"}</Link>
          <Link className="button button-secondary" to="/pricing">View pricing</Link>
        </div>
      </section>

      <section className="faq-panel">
        <div className="section-heading">
          <p className="eyebrow">Common questions</p>
          <h2>Know what Phantom is—and what happens after you sign up.</h2>
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

function PublicFeedbackSection() {
  const initialForm = { name: "", email: "", category: "product", rating: "5", message: "", consentToPublish: false, website: "" };
  const [form, setForm] = useState(initialForm);
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setStatus("");
    try {
      await submitPublicFeedback({ ...form, rating: Number(form.rating) });
      setForm(initialForm);
      setStatus("Thank you. Your feedback has been received for review.");
    } catch (error) {
      setStatus(error.message || "Could not submit feedback. Please try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section className="feedback-section" id="feedback">
      <div className="feedback-copy">
        <p className="eyebrow">Help shape Phantom</p>
        <h2>Tell us what felt useful—and what still gets in your way.</h2>
        <p>Product feedback goes directly into the admin review queue. For account or billing help, use the signed-in support dashboard so we can investigate securely.</p>
        <Link className="text-link" to="/dashboard/support">Open account support <span>→</span></Link>
      </div>
      <form className="public-feedback-form" onSubmit={handleSubmit}>
        <div className="form-grid-two">
          <label><span>Name</span><input value={form.name} onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))} minLength={2} maxLength={80} autoComplete="name" required /></label>
          <label><span>Email</span><input type="email" value={form.email} onChange={(event) => setForm((current) => ({ ...current, email: event.target.value }))} maxLength={254} autoComplete="email" required /></label>
          <label><span>Feedback type</span><select value={form.category} onChange={(event) => setForm((current) => ({ ...current, category: event.target.value }))}><option value="product">Product experience</option><option value="feature-request">Feature request</option><option value="bug">Bug report</option><option value="billing">Billing experience</option><option value="other">Other</option></select></label>
          <label><span>Rating</span><select value={form.rating} onChange={(event) => setForm((current) => ({ ...current, rating: event.target.value }))}><option value="5">5 — Excellent</option><option value="4">4 — Good</option><option value="3">3 — Okay</option><option value="2">2 — Needs work</option><option value="1">1 — Poor</option></select></label>
        </div>
        <label><span>Your feedback</span><textarea value={form.message} onChange={(event) => setForm((current) => ({ ...current, message: event.target.value }))} minLength={20} maxLength={2000} placeholder="What happened, what worked, or what would make Phantom better?" required /></label>
        <label className="feedback-honeypot" aria-hidden="true"><span>Website</span><input value={form.website} onChange={(event) => setForm((current) => ({ ...current, website: event.target.value }))} tabIndex={-1} autoComplete="off" /></label>
        <label className="consent-row"><input type="checkbox" checked={form.consentToPublish} onChange={(event) => setForm((current) => ({ ...current, consentToPublish: event.target.checked }))} /><span>Phantom may publish my first name, rating, and feedback as a review. My email will remain private.</span></label>
        <button className="button button-primary" type="submit" disabled={submitting}>{submitting ? "Sending feedback..." : "Send feedback"}</button>
        {status ? <p className={`status-message ${status.startsWith("Thank") ? "status-success" : "status-error"}`} role="status" aria-live="polite">{status}</p> : null}
      </form>
    </section>
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
        <p className="eyebrow">Simple paths into Phantom</p>
        <h1>Choose who manages the AI—and how much context comes with it.</h1>
        <p>
          Try the workflow free, connect your own supported providers for more control, or use Premium when you want managed models and the complete knowledge experience.
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
          <p className="eyebrow">Which plan fits?</p>
          <h2>The difference is simple: setup, provider ownership, and context depth.</h2>
        </div>
        <div className="comparison-grid">
          <MetricDefinition title="Free Trial" detail="Use when you need product validation before buying credits or adding keys." />
          <MetricDefinition title="Pro BYO" detail="Use when you want Phantom's desktop runtime but your own provider accounts and billing." />
          <MetricDefinition title="Premium AI" detail="Use when you want managed models, richer context, and no provider setup." />
        </div>
      </section>
    </main>
  );
}

function DownloadPage({ userSession }) {
  const [entitlement, setEntitlement] = useState(null);
  const [error, setError] = useState("");
  const [downloadingPlatform, setDownloadingPlatform] = useState("");

  useEffect(() => {
    let cancelled = false;

    async function load() {
      if (!userSession?.isAuthenticated) {
        setEntitlement(null);
        return;
      }

      try {
        const result = await fetchDownloadEntitlement();
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
  }, [userSession?.isAuthenticated, userSession?.email]);

  async function startDownload(platform) {
    setDownloadingPlatform(platform);
    setError("");
    try {
      const result = await createSignedDownloadLink(null, platform);
      if (!result?.url) {
        throw new Error("A secure download link could not be created.");
      }
      window.location.assign(result.url);
    } catch (downloadError) {
      setError(downloadError.message || "Could not start the download.");
    } finally {
      setDownloadingPlatform("");
    }
  }

  return (
    <main className="page">
      <Seo
        title="Download | Phantom"
        description="Download Phantom for Windows or macOS and check your account's installer eligibility."
      />
      <section className="hero-grid">
        <article className="glass-panel hero-panel hero-panel-primary">
          <p className="eyebrow">Phantom for Windows and macOS</p>
          <h1>Your live workspace belongs on the desktop.</h1>
          <p className="lead-copy">
            Prepare on the web, then move into the focused desktop experience on Windows or macOS for voice, screenshots, context retrieval, and live AI support.
          </p>
          <div className="stats-grid">
            <MetricCard label="System" value="Windows 10/11 · macOS 12.3+" />
            <MetricCard label="Account" value="Verified sign-in" />
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
          {error ? <p className="status-message status-error" role="alert">{error}</p> : null}
        </article>

        <article className="glass-panel hero-panel hero-panel-side">
          <p className="eyebrow">Your download status</p>
          <div className="stack-list">
            <InfoRow label="Installer" value={entitlement?.installerLabel || "Sign in to resolve"} />
            <InfoRow label="Version" value={entitlement?.installerVersion || "Pending"} />
            <InfoRow label="Eligibility" value={entitlement?.canDownload ? "Ready" : "Verification required"} />
          </div>
          {entitlement?.canDownload ? (
            <div className="hero-actions">
              <button className="button button-primary" type="button" disabled={Boolean(downloadingPlatform)} onClick={() => startDownload("windows")}>
                {downloadingPlatform === "windows" ? "Securing link..." : "Download for Windows"}
              </button>
              <button className="button button-secondary" type="button" disabled={Boolean(downloadingPlatform)} onClick={() => startDownload("macos")}>
                {downloadingPlatform === "macos" ? "Securing link..." : "Download for macOS"}
              </button>
            </div>
          ) : null}
        </article>
      </section>

      <section className="glass-panel section-panel">
        <div className="section-heading">
          <p className="eyebrow">Three simple steps</p>
          <h2>Move from account setup to a ready desktop workspace.</h2>
        </div>
        <div className="timeline-grid">
          <TimelineStep index="01" title="Create account" body="Register on the website and complete the verification steps required for your deployment." />
          <TimelineStep index="02" title="Check your workspace" body="Confirm your plan, credits, device status, and download access in the dashboard." />
          <TimelineStep index="03" title="Launch Phantom" body="Sign in on Windows or macOS, choose your AI lane, and enter the interview ready." />
        </div>
      </section>

      <section className="glass-panel section-panel">
        <div className="section-heading">
          <p className="eyebrow">System requirements</p>
          <h2>Check compatibility before installing.</h2>
        </div>
        <div className="comparison-grid">
          <MetricDefinition title="Windows" detail="Windows 10 or 11, 64-bit CPU, WebView2, microphone access, and screen-capture permission for visual context." />
          <MetricDefinition title="macOS" detail="macOS 12.3 or newer, Apple Silicon or Intel, plus Microphone, Speech Recognition, Accessibility, and Screen Recording permissions." />
          <MetricDefinition title="Network and account" detail="A verified Phantom email and an internet connection are required for sign-in, hosted AI, payments, and knowledge-base sync." />
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
          <p className="eyebrow">Welcome back</p>
          <h1>Your preparation is still here.</h1>
          <p>
            Sign in to review your knowledge space, credits, devices, downloads, and recent interview questions before returning to the desktop app.
          </p>
          <ul>
            <li>Keep account and device readiness visible</li>
            <li>Manage Premium context outside live sessions</li>
            <li>Review usage and interview question banks</li>
          </ul>
        </article>

        <form className="glass-panel auth-form" onSubmit={handleSubmit}>
          <label>
            <span>Email</span>
            <input
              type="email"
              required
              autoComplete="username"
              value={form.email}
              onChange={(event) => setForm((current) => ({ ...current, email: event.target.value }))}
              placeholder="name@example.com"
            />
          </label>
          <PasswordField
            label="Password"
            required
            autoComplete="current-password"
            value={form.password}
            onChange={(event) => setForm((current) => ({ ...current, password: event.target.value }))}
            placeholder="Enter your password"
          />
          <button className="button button-primary" type="submit" disabled={submitting}>
            {submitting ? "Signing in..." : "Sign in to Phantom"}
          </button>
          {status ? <p className="status-message status-error" role="alert">{status}</p> : null}
          <div className="link-row">
            <Link to="/register">Create account</Link>
            <Link to="/forgot-password">Forgot password?</Link>
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
  const [acceptedTerms, setAcceptedTerms] = useState(false);
  const [registrationSettings, setRegistrationSettings] = useState(null);
  const deviceFingerprintHash = registerParams.get("deviceFingerprint") || getBrowserRegistrationFingerprint();
  const phoneVerificationRequired = registrationSettings?.phoneVerificationRequired === true;
  const passwordError = getPasswordPolicyError(form.password);

  useEffect(() => {
    let cancelled = false;
    fetchRegistrationSettings()
      .then((settings) => {
        if (!cancelled) {
          setRegistrationSettings(settings);
        }
      })
      .catch((error) => {
        if (!cancelled) {
          setStatus(error.message || "Could not load signup requirements.");
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

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
      if (!acceptedTerms) {
        throw new Error("Accept the Terms of Use and Privacy Policy before creating your account.");
      }

      if (!registrationSettings) {
        throw new Error("Signup requirements are still loading. Try again in a moment.");
      }

      if (passwordError) {
        throw new Error(passwordError);
      }

      if (phoneVerificationRequired && !otpState.verificationToken) {
        throw new Error("Verify your phone number before creating the account.");
      }

      const result = await registerAccount({
        email: form.email,
        password: form.password,
        phoneNumber: phoneVerificationRequired ? form.phoneNumber : "",
        phoneVerificationToken: phoneVerificationRequired ? otpState.verificationToken : "",
        appVersion: registerParams.get("appVersion") || "",
        installId: registerParams.get("installId") || "",
        deviceLabel: registerParams.get("deviceLabel") || "",
        deviceFingerprintHash,
        secretFingerprintHint: registerParams.get("deviceHint") || "",
        acceptedTerms: true
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
        description={`Create a Phantom account and complete email verification${phoneVerificationRequired ? " plus phone OTP" : ""} before the first desktop sign-in.`}
        noindex
      />
      <section className="auth-shell">
        <article className="glass-panel auth-aside">
          <p className="eyebrow">Start free</p>
          <h1>Create the workspace that follows you into every round.</h1>
          <p>
            Create your account here, {phoneVerificationRequired ? "complete phone and email verification" : "verify your email"}, then download Phantom for Windows or macOS.
          </p>
          <ul>
            <li>Two hosted 15-minute trial blocks</li>
            <li>No provider keys required to begin</li>
            <li>Upgrade only when the workflow fits you</li>
          </ul>
        </article>

        <form className="glass-panel auth-form" onSubmit={handleSubmit}>
          <label>
            <span>Email</span>
            <input
              type="email"
              required
              autoComplete="email"
              value={form.email}
              onChange={(event) => setForm((current) => ({ ...current, email: event.target.value }))}
              placeholder="name@example.com"
            />
          </label>
          <PasswordField
            label="Password"
            required
            autoComplete="new-password"
            value={form.password}
            onChange={(event) => setForm((current) => ({ ...current, password: event.target.value }))}
            placeholder="Choose a strong password"
            minLength={12}
          />
          <p className={`inline-note ${form.password ? passwordError ? "inline-note-error" : "inline-note-success" : ""}`}>
            {form.password && !passwordError ? "Password meets the security requirements." : PASSWORD_REQUIREMENTS}
          </p>
          {phoneVerificationRequired ? (
            <>
              <label>
                <span>Phone number</span>
                <input
                  required
                  type="tel"
                  autoComplete="tel"
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
                  required
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  pattern="[0-9]{6}"
                  maxLength={6}
                  value={form.otpCode}
                  onChange={(event) => setForm((current) => ({ ...current, otpCode: event.target.value.replace(/\D/g, "").slice(0, 6) }))}
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
            </>
          ) : registrationSettings ? (
            <p className="inline-note">Phone verification is currently not required. Email verification still protects account access.</p>
          ) : (
            <p className="inline-note">Loading signup requirements…</p>
          )}
          <label className="consent-row">
            <input
              type="checkbox"
              checked={acceptedTerms}
              onChange={(event) => setAcceptedTerms(event.target.checked)}
              required
            />
            <span>
              I agree to the <Link to="/terms" target="_blank" rel="noreferrer">Terms of Use</Link> and acknowledge the <Link to="/privacy" target="_blank" rel="noreferrer">Privacy Policy</Link>, including what data Phantom collects and why it is used.
            </span>
          </label>
          <button className="button button-primary" type="submit" disabled={submitting || !registrationSettings || Boolean(passwordError)}>
            {submitting ? "Creating account..." : "Create Account"}
          </button>

          {status ? (
            <p className={`status-message ${status.toLowerCase().includes("verified") || status.toLowerCase().includes("sent") ? "" : "status-error"}`} role="status" aria-live="polite">
              {status}
            </p>
          ) : null}
        </form>
      </section>
    </main>
  );
}

function UserForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setStatus("");
    try {
      const result = await requestUserPasswordReset(email);
      setStatus(result.message || "If that account exists, a reset link has been sent.");
    } catch (error) {
      setStatus(error.message || "Could not request a password reset.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="page">
      <Seo
        title="Password Reset | Phantom"
        description="Request a Phantom account password reset."
        noindex
      />
      <section className="auth-shell">
        <article className="glass-panel auth-aside">
          <p className="eyebrow">Account recovery</p>
          <h1>Reset the user dashboard password by email.</h1>
          <p>This flow is only for account holders using the main Phantom dashboard.</p>
        </article>
        <form className="glass-panel auth-form" onSubmit={handleSubmit}>
          <label>
            <span>Account email</span>
            <input type="email" value={email} onChange={(event) => setEmail(event.target.value)} placeholder="name@example.com" />
          </label>
          <button className="button button-primary" type="submit" disabled={submitting}>
            {submitting ? "Sending..." : "Send Reset Link"}
          </button>
          {status ? <p className={`status-message ${status.toLowerCase().includes("could not") ? "status-error" : ""}`} role="status" aria-live="polite">{status}</p> : null}
          <div className="link-row">
            <Link to="/login">Back to login</Link>
          </div>
        </form>
      </section>
    </main>
  );
}

function UserResetPasswordPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const query = new URLSearchParams(location.search);
  const token = query.get("token") || "";
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const passwordError = getPasswordPolicyError(password);

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setStatus("");
    try {
      if (!token) {
        throw new Error("Reset token missing from the URL.");
      }
      if (passwordError) {
        throw new Error(passwordError);
      }
      if (password !== confirmPassword) {
        throw new Error("Passwords do not match.");
      }
      const result = await resetUserPassword(token, password);
      setStatus(result.message || "Account password reset complete.");
      setTimeout(() => {
        navigate("/login", { replace: true });
      }, 1000);
    } catch (error) {
      setStatus(error.message || "Could not reset the password.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="page">
      <Seo title="Reset Password | Phantom" description="Set a new password for the Phantom user dashboard." noindex />
      <section className="auth-shell">
        <article className="glass-panel auth-aside">
          <p className="eyebrow">Account reset</p>
          <h1>Choose a new account password.</h1>
          <p>Reset links are single-use and time-limited.</p>
        </article>
        <form className="glass-panel auth-form" onSubmit={handleSubmit}>
          <PasswordField label="New password" value={password} onChange={(event) => setPassword(event.target.value)} placeholder="Create a strong password" autoComplete="new-password" minLength={12} required />
          <p className={`inline-note ${password ? passwordError ? "inline-note-error" : "inline-note-success" : ""}`}>
            {password && !passwordError ? "Password meets the security requirements." : PASSWORD_REQUIREMENTS}
          </p>
          <PasswordField label="Confirm password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} placeholder="Re-enter the new password" autoComplete="new-password" required />
          <button className="button button-primary" type="submit" disabled={submitting || Boolean(passwordError) || password !== confirmPassword}>
            {submitting ? "Resetting..." : "Reset Password"}
          </button>
          {status ? <p className={`status-message ${status.toLowerCase().includes("complete") ? "" : "status-error"}`} role="status" aria-live="polite">{status}</p> : null}
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
            <span className="status-pill status-pill-good">Gmail OAuth complete. Continue from the `/admin` route.</span>
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
        title="What Phantom collects, why it is needed, and how it is protected."
        intro="Phantom collects only the account, verification, device, usage, payment, support, and optional knowledge-base data needed to provide the service. We use reasonable technical and operational safeguards to protect it and explain each category below."
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
        title="Clear terms for using Phantom responsibly."
        intro="These terms explain your account responsibilities, acceptable use, AI limitations, payments, and how your agreement works together with Phantom's Privacy Policy."
        sections={termsSections}
      />
    </main>
  );
}

function RefundPolicyPage() {
  return (
    <main className="page">
      <Seo
        title="Refund Policy | Phantom"
        description="Read Phantom's refund eligibility, request process, and payment-provider processing timelines."
      />
      <LegalPage
        eyebrow="Refund Policy · Effective September 5, 2026"
        title="A clear path for unused credits, duplicate charges, and service failures."
        intro="This policy explains when a Phantom payment may be refunded, how to make a request, and what happens after approval. Refunds are assessed against the payment and credit-usage record for the account."
        sections={refundSections}
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
  const [download, setDownload] = useState(null);
  const [support, setSupport] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      setLoading(true);
      setError("");
      const results = await Promise.allSettled([
        fetchAccountSummary(session.accessToken),
        fetchDownloadEntitlement(session.accessToken),
        fetchSupportOverview(session.accessToken),
        fetchHostedKnowledgeBase(session.accessToken)
      ]);

      if (!cancelled) {
        const [accountResult, downloadResult, supportResult, knowledgeBaseResult] = results;
        if (accountResult.status === "fulfilled") setSummary(accountResult.value);
        if (downloadResult.status === "fulfilled") setDownload(downloadResult.value);
        if (supportResult.status === "fulfilled") setSupport(supportResult.value);
        if (knowledgeBaseResult.status === "fulfilled") setKnowledgeBase(knowledgeBaseResult.value);
        const failedCount = results.filter((result) => result.status === "rejected").length;
        if (failedCount > 0) {
          const accountError = accountResult.status === "rejected" ? accountResult.reason?.message : "";
          setError(accountError || `${failedCount} dashboard section${failedCount === 1 ? "" : "s"} could not be loaded.`);
        }
      }

      if (!cancelled) setLoading(false);
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [session.accessToken, session.email, session.expiresAtUtc, reloadKey]);

  async function refreshSummary() {
    const nextSummary = await fetchAccountSummary(session.accessToken);
    setSummary(nextSummary);
  }

  if (loading) {
    return <DashboardSkeleton label="User dashboard" />;
  }

  if (!summary) {
    return (
      <main className="page">
        <Seo title="User Dashboard | Phantom" description="User dashboard" noindex />
        <section className="glass-panel page-intro">
          <p className="eyebrow">User dashboard</p>
          <h1>Dashboard unavailable</h1>
          <p>{error || "Account summary could not be resolved."}</p>
          <button className="button button-primary" type="button" onClick={() => setReloadKey((value) => value + 1)}>Try again</button>
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
          <div className="dashboard-rail-heading">
            <p className="eyebrow">Your workspace</p>
            <p className="dashboard-rail-title">{summary.email}</p>
          </div>
          <nav className="dashboard-nav" aria-label="Workspace navigation">
            {userNav.map((item) => (
              <NavLink key={item.to} to={item.to} end={item.to === "/dashboard"} className={({ isActive }) => isActive ? "dashboard-nav-active" : ""}>
                <span>{item.label}</span>
              </NavLink>
            ))}
          </nav>
          <div className="status-band">
            <span className="status-pill">{summary.planLabel}</span>
            {summary.canUseDesktopPowerFeatures ? <span className="status-pill status-pill-power">Power user</span> : null}
            <span className={`status-pill ${summary.emailVerified ? "status-pill-good" : "status-pill-warn"}`}>
              {summary.emailVerified ? "Email verified" : "Email verification required"}
            </span>
            {summary.phoneVerified ? <span className="status-pill status-pill-good">Phone verified</span> : null}
          </div>
          <div className="stack-list">
            <InfoRow label="Pro credits" value={summary.proAvailableCredits.toFixed(2)} />
            <InfoRow label="Premium credits" value={summary.premiumAvailableCredits.toFixed(2)} />
            <InfoRow label="Premium debt" value={summary.premiumNegativeCredits.toFixed(2)} />
            <InfoRow label="Active devices" value={String(summary.activeDeviceCount)} />
          </div>
        </aside>

        <section className="dashboard-main" aria-busy={loading || undefined}>
          <RetryNotice message={error} onRetry={() => setReloadKey((value) => value + 1)} />
          <Routes>
            <Route
              index
              element={
                <UserOverviewPanel
                  accessToken={session.accessToken}
                  summary={summary}
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
                  onSummaryChanged={refreshSummary}
                />
              }
            />
            <Route path="devices" element={<DevicesPanel accessToken={session.accessToken} />} />
            <Route path="history" element={<HistoryPanel accessToken={session.accessToken} />} />
            <Route path="questions" element={<QuestionBanksPanel accessToken={session.accessToken} />} />
            <Route path="support" element={<SupportPanel accessToken={session.accessToken} support={support} />} />
          </Routes>
        </section>
      </section>
    </main>
  );
}

function UserOverviewPanel({ accessToken, summary, download, support, knowledgeBase }) {
  const [devicesPage, setDevicesPage] = useState({ items: [] });
  const [walletHistoryPage, setWalletHistoryPage] = useState({ items: [] });
  const [walletPurchasesPage, setWalletPurchasesPage] = useState({ items: [] });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;

    setLoading(true);
    setError("");
    Promise.allSettled([
      fetchDevices(accessToken, 1, 6),
      fetchWalletHistory(accessToken, 1, 12),
      fetchWalletPurchases(accessToken, 1, 12)
    ])
      .then((results) => {
        const [nextDevices, nextHistory, nextPurchases] = results;
        if (!cancelled) {
          if (nextDevices.status === "fulfilled") setDevicesPage(nextDevices.value || { items: [] });
          if (nextHistory.status === "fulfilled") setWalletHistoryPage(nextHistory.value || { items: [] });
          if (nextPurchases.status === "fulfilled") setWalletPurchasesPage(nextPurchases.value || { items: [] });
          const failed = results.filter((result) => result.status === "rejected");
          if (failed.length) setError(failed[0].reason?.message || "Recent activity could not be loaded.");
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [accessToken, reloadKey]);

  const devices = devicesPage.items || [];
  const walletHistory = walletHistoryPage.items || [];
  const walletPurchases = walletPurchasesPage.items || [];
  const activeDeviceCount = devices.filter((item) => item.isActive).length;
  const purchaseStates = countBy(walletPurchases, (item) => item.status || "unknown");

  if (loading && devices.length === 0 && walletHistory.length === 0 && walletPurchases.length === 0) {
    return <SectionSkeleton rows={8} label="Loading account overview" />;
  }

  return (
    <div className="dashboard-grid">
      <RetryNotice message={error} onRetry={() => setReloadKey((value) => value + 1)} className="table-span-full" />
      <article className="glass-panel dashboard-hero">
        <p className="eyebrow">{summary.emailVerified && download?.canDownload ? "Ready to launch" : "Action required"}</p>
        <h1>{summary.emailVerified && download?.canDownload ? "Your Phantom workspace is ready." : "Complete email verification to unlock Phantom."}</h1>
        <p>{summary.emailVerified ? "Your email is verified. Review context, credits, and recent interview activity before opening the desktop app." : "Verify your email, then return here to download and launch the Windows or macOS app."}</p>
        <div className="hero-actions">
          <Link className="button button-primary" to="/download">{download?.canDownload ? "Download Phantom" : "Check download access"}</Link>
          <Link className="button button-secondary" to="/dashboard/knowledge-base">Review knowledge</Link>
        </div>
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
  const [activeKnowledgeEditor, setActiveKnowledgeEditor] = useState(() => knowledgeBase?.knowledgeBaseId ? "profile" : "base");
  const [name, setName] = useState(knowledgeBase?.name || "My Premium Knowledge Base");
  const [description, setDescription] = useState(knowledgeBase?.description || "");
  const [status, setStatus] = useState("");
  const [savingBase, setSavingBase] = useState(false);
  const [savingProfile, setSavingProfile] = useState(false);
  const [savingProject, setSavingProject] = useState(false);
  const [processingDocuments, setProcessingDocuments] = useState(false);
  const [savingExperience, setSavingExperience] = useState(false);
  const [selectedDocument, setSelectedDocument] = useState(null);
  const [documentLoadingId, setDocumentLoadingId] = useState("");
  const [deletingDocumentId, setDeletingDocumentId] = useState("");
  const [selectedSection, setSelectedSection] = useState("profile");
  const [pasteTitle, setPasteTitle] = useState("");
  const [pasteContent, setPasteContent] = useState("");
  const [selectedProjectId, setSelectedProjectId] = useState("");
  const [selectedExperienceId, setSelectedExperienceId] = useState("");
  const [profileDraft, setProfileDraft] = useState({
    fullName: "",
    candidateInfo: "",
    shortIntro: "",
    strengths: "",
    skills: "",
    domains: ""
  });
  const [experienceDraft, setExperienceDraft] = useState({
    experienceCardId: "",
    company: "",
    role: "",
    isCurrent: false,
    sortOrder: 0,
    startDate: "",
    endDate: "",
    summary: "",
    responsibilities: "",
    skills: ""
  });
  const [projectDraft, setProjectDraft] = useState({
    projectCardId: "",
    title: "",
    isRecent: false,
    sortOrder: 0,
    role: "",
    summary: "",
    stack: "",
    architecture: "",
    challenges: "",
    impact: ""
  });

  useEffect(() => {
    setName(knowledgeBase?.name || "My Premium Knowledge Base");
    setDescription(knowledgeBase?.description || "");
  }, [knowledgeBase?.description, knowledgeBase?.name]);

  useEffect(() => {
    const profile = knowledgeBase?.profileCard;
    setProfileDraft({
      fullName: profile?.fullName || "",
      candidateInfo: profile?.candidateInfo || profile?.resumeText || "",
      shortIntro: profile?.shortIntro || "",
      strengths: (profile?.strengths || []).join(", "),
      skills: (profile?.skills || []).join(", "),
      domains: (profile?.domains || []).join(", ")
    });
  }, [knowledgeBase?.profileCard]);

  useEffect(() => {
    const experiences = knowledgeBase?.experienceCards || [];
    if (selectedExperienceId === "new") {
      return;
    }
    const nextId = experiences.some((item) => item.experienceCardId === selectedExperienceId)
      ? selectedExperienceId
      : experiences[0]?.experienceCardId || "";
    setSelectedExperienceId(nextId);
    const selected = experiences.find((item) => item.experienceCardId === nextId);
    setExperienceDraft({
      experienceCardId: selected?.experienceCardId || "",
      company: selected?.company || "",
      role: selected?.role || "",
      isCurrent: selected?.isCurrent || false,
      sortOrder: selected?.sortOrder || 0,
      startDate: selected?.startDate || "",
      endDate: selected?.endDate || "",
      summary: selected?.summary || "",
      responsibilities: selected?.responsibilities || "",
      skills: (selected?.skills || []).join(", ")
    });
  }, [knowledgeBase?.experienceCards, selectedExperienceId]);

  useEffect(() => {
    const projects = knowledgeBase?.projectCards || [];
    const fallbackProjectId = projects[0]?.projectCardId || "";
    const nextProjectId = projects.some((project) => project.projectCardId === selectedProjectId)
      ? selectedProjectId
      : fallbackProjectId;
    setSelectedProjectId(nextProjectId);
    const selectedProject = projects.find((project) => project.projectCardId === nextProjectId);
    setProjectDraft({
      projectCardId: selectedProject?.projectCardId || "",
      title: selectedProject?.title || "",
      isRecent: selectedProject?.isRecent || false,
      sortOrder: selectedProject?.sortOrder || 0,
      role: selectedProject?.role || "",
      summary: selectedProject?.summary || "",
      stack: (selectedProject?.stack || []).join(", "),
      architecture: selectedProject?.architecture || "",
      challenges: selectedProject?.challenges || "",
      impact: selectedProject?.impact || ""
    });
  }, [knowledgeBase?.projectCards, selectedProjectId]);

  const isPremiumBlocked = !knowledgeBase?.canManage;
  const blockedMessage =
    knowledgeBase?.blockedReason
    || "Hosted knowledge bases are available only while Premium access and credits are active.";

  useEffect(() => {
    if (!selectedDocument?.documentId) {
      return;
    }

    const nextDocument = (knowledgeBase?.documents || []).find((item) => item.documentId === selectedDocument.documentId);
    if (!nextDocument) {
      setSelectedDocument(null);
    }
  }, [knowledgeBase?.documents, selectedDocument?.documentId]);

  async function reloadKnowledgeBase() {
    const nextKnowledgeBase = await fetchHostedKnowledgeBase(accessToken);
    onKnowledgeBaseChanged(nextKnowledgeBase);
    return nextKnowledgeBase;
  }

  async function handleCreate(event) {
    event.preventDefault();
    setSavingBase(true);
    setStatus("");
    try {
      const result = await createHostedKnowledgeBase(accessToken, { name, description });
      onKnowledgeBaseChanged(result);
      setStatus("Knowledge base saved.");
    } catch (error) {
      setStatus(error.message || "Could not save the knowledge base.");
    } finally {
      setSavingBase(false);
    }
  }

  async function handleUpload(event) {
    const files = event.target.files;
    if (!files?.length) {
      return;
    }

    setProcessingDocuments(true);
    setStatus("");
    try {
      const totalBytes = Array.from(files).reduce((sum, file) => sum + (file.size || 0), 0);
      if (totalBytes > 8 * 1024 * 1024) {
        throw new Error("Combined upload exceeds the 8 MB per-request limit.");
      }

      const result = await uploadHostedKnowledgeBaseDocuments(accessToken, files, selectedSection);
      onKnowledgeBaseChanged(result.knowledgeBase);
      setStatus(`Processed ${result.addedDocuments.length} document${result.addedDocuments.length === 1 ? "" : "s"}.`);
    } catch (error) {
      setStatus(error.message || "Could not process those documents.");
    } finally {
      setProcessingDocuments(false);
      event.target.value = "";
    }
  }

  async function handlePaste(event) {
    event.preventDefault();
    if (!pasteContent.trim()) {
      return;
    }

    setProcessingDocuments(true);
    setStatus("");
    try {
      const result = await pasteHostedKnowledgeBaseDocument(accessToken, {
        section: selectedSection,
        title: pasteTitle,
        content: pasteContent
      });
      onKnowledgeBaseChanged(result.knowledgeBase);
      setPasteTitle("");
      setPasteContent("");
      setStatus("Pasted content processed into the hosted knowledge base.");
    } catch (error) {
      setStatus(error.message || "Could not process pasted content.");
    } finally {
      setProcessingDocuments(false);
    }
  }

  async function handleViewDocument(documentId) {
    setDocumentLoadingId(documentId);
    setStatus("");
    try {
      const document = await fetchHostedKnowledgeBaseDocument(accessToken, documentId);
      setSelectedDocument(document);
    } catch (error) {
      setStatus(error.message || "Could not load that document.");
    } finally {
      setDocumentLoadingId("");
    }
  }

  async function handleDeleteDocument(documentId) {
    const document = (knowledgeBase?.documents || []).find((item) => item.documentId === documentId);
    if (!document) {
      return;
    }

    const confirmed = window.confirm(`Delete '${document.fileName}' from the hosted knowledge base?`);
    if (!confirmed) {
      return;
    }

    setDeletingDocumentId(documentId);
    setStatus("");
    try {
      const nextKnowledgeBase = await deleteHostedKnowledgeBaseDocument(accessToken, documentId);
      onKnowledgeBaseChanged(nextKnowledgeBase);
      if (selectedDocument?.documentId === documentId) {
        setSelectedDocument(null);
      }
      setStatus("Document deleted from the hosted knowledge base.");
    } catch (error) {
      setStatus(error.message || "Could not delete that document.");
    } finally {
      setDeletingDocumentId("");
    }
  }

  async function handleSaveProfile(event) {
    event.preventDefault();
    setSavingProfile(true);
    setStatus("");
    try {
      await updateHostedKnowledgeBaseProfile(accessToken, {
        fullName: profileDraft.fullName,
        candidateInfo: profileDraft.candidateInfo,
        shortIntro: profileDraft.shortIntro,
        currentRole: "",
        yearsOfExperience: 0,
        strengths: parseListInput(profileDraft.strengths),
        skills: parseListInput(profileDraft.skills),
        domains: parseListInput(profileDraft.domains)
      });
      await reloadKnowledgeBase();
      setStatus("Profile card updated.");
    } catch (error) {
      setStatus(error.message || "Could not update the profile card.");
    } finally {
      setSavingProfile(false);
    }
  }

  function handleNewExperience() {
    setSelectedExperienceId("new");
    setExperienceDraft({
      experienceCardId: "",
      company: "",
      role: "",
      isCurrent: !(knowledgeBase?.experienceCards || []).some((item) => item.isCurrent),
      sortOrder: (knowledgeBase?.experienceCards || []).length,
      startDate: "",
      endDate: "",
      summary: "",
      responsibilities: "",
      skills: ""
    });
  }

  async function handleSaveExperience(event) {
    event.preventDefault();
    setSavingExperience(true);
    setStatus("");
    try {
      const payload = {
        company: experienceDraft.company,
        role: experienceDraft.role,
        isCurrent: experienceDraft.isCurrent,
        sortOrder: parseRequiredInteger(experienceDraft.sortOrder, "Experience order", 0, 10000),
        startDate: experienceDraft.startDate,
        endDate: experienceDraft.isCurrent ? "" : experienceDraft.endDate,
        summary: experienceDraft.summary,
        responsibilities: experienceDraft.responsibilities,
        skills: parseListInput(experienceDraft.skills)
      };
      const saved = experienceDraft.experienceCardId
        ? await updateHostedKnowledgeBaseExperience(accessToken, experienceDraft.experienceCardId, payload)
        : await createHostedKnowledgeBaseExperience(accessToken, payload);
      setSelectedExperienceId(saved.experienceCardId);
      await reloadKnowledgeBase();
      setStatus("Experience updated.");
    } catch (error) {
      setStatus(error.message || "Could not update that experience.");
    } finally {
      setSavingExperience(false);
    }
  }

  async function handleDeleteExperience() {
    if (!experienceDraft.experienceCardId || !window.confirm(`Delete the ${experienceDraft.role} experience at ${experienceDraft.company}?`)) {
      return;
    }
    setSavingExperience(true);
    setStatus("");
    try {
      await deleteHostedKnowledgeBaseExperience(accessToken, experienceDraft.experienceCardId);
      setSelectedExperienceId("");
      await reloadKnowledgeBase();
      setStatus("Experience deleted.");
    } catch (error) {
      setStatus(error.message || "Could not delete that experience.");
    } finally {
      setSavingExperience(false);
    }
  }

  async function handleSaveProject(event) {
    event.preventDefault();
    if (!projectDraft.projectCardId) {
      return;
    }

    setSavingProject(true);
    setStatus("");
    try {
      await updateHostedKnowledgeBaseProject(accessToken, projectDraft.projectCardId, {
        title: projectDraft.title,
        isRecent: projectDraft.isRecent,
        sortOrder: parseRequiredInteger(projectDraft.sortOrder, "Project order", 0, 10000),
        role: projectDraft.role,
        summary: projectDraft.summary,
        stack: parseListInput(projectDraft.stack),
        architecture: projectDraft.architecture,
        challenges: projectDraft.challenges,
        impact: projectDraft.impact
      });
      await reloadKnowledgeBase();
      setStatus("Project card updated.");
    } catch (error) {
      setStatus(error.message || "Could not update the project card.");
    } finally {
      setSavingProject(false);
    }
  }

  async function handleMarkRecent(projectCardId) {
    setSavingProject(true);
    setStatus("");
    try {
      await markHostedKnowledgeBaseProjectRecent(accessToken, projectCardId);
      await reloadKnowledgeBase();
      setStatus("Recent project updated.");
    } catch (error) {
      setStatus(error.message || "Could not update the recent project.");
    } finally {
      setSavingProject(false);
    }
  }

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero">
        <p className="eyebrow">Your knowledge space</p>
        <h1>Turn your experience into context Phantom can retrieve when it matters.</h1>
        <p>
          Add candidate information, company-specific experiences, project stories, and preferences here. Phantom keeps each context source ready for your desktop sessions.
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

      <nav className="glass-panel section-switcher table-span-full" aria-label="Knowledge base editors">
        {[
          ["base", "Knowledge base"],
          ["documents", "Documents"],
          ["profile", "Profile"],
          ["experience", "Experience"],
          ["project", "Projects"]
        ].map(([value, label]) => (
          <button
            className={`button button-compact ${activeKnowledgeEditor === value ? "button-primary" : "button-ghost"}`}
            type="button"
            aria-pressed={activeKnowledgeEditor === value}
            key={value}
            onClick={() => setActiveKnowledgeEditor(value)}
          >
            {label}
          </button>
        ))}
      </nav>

      {activeKnowledgeEditor === "base" ? <form className="glass-panel auth-form table-span-full" onSubmit={handleCreate}>
        <p className="eyebrow">Create or rename</p>
        <label>
          <span>Knowledge base name</span>
          <input value={name} onChange={(event) => setName(event.target.value)} disabled={isPremiumBlocked || savingBase} />
        </label>
        <label>
          <span>Description</span>
          <textarea
            rows={4}
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            disabled={isPremiumBlocked || savingBase}
            placeholder="Role packet, architecture notes, company research, STAR stories"
          />
        </label>
        <button className="button button-primary" type="submit" disabled={isPremiumBlocked || savingBase || processingDocuments}>
          {savingBase ? "Saving..." : "Save Knowledge Base"}
        </button>
      </form> : null}

      {activeKnowledgeEditor === "documents" ? <article className="glass-panel upload-panel table-span-full">
        <p className="eyebrow">Sectioned memory</p>
        <h3>Supported: `.txt`, `.md`, `.json`, `.csv`, `.log`, `.docx`</h3>
        <p>
          Choose where this content belongs first. The backend extracts structured interview memory immediately,
          then keeps the raw source for deep retrieval. Experience documents may contain one or multiple company/role periods.
        </p>
        <label>
          <span>Section</span>
          <select value={selectedSection} onChange={(event) => setSelectedSection(event.target.value)} disabled={isPremiumBlocked || processingDocuments}>
            <option value="profile">Profile</option>
            <option value="experience">Experience</option>
            <option value="project">Project</option>
            <option value="general_reference">General preferences</option>
          </select>
        </label>
        <label className={`button button-secondary button-file ${isPremiumBlocked || processingDocuments ? "button-disabled" : ""}`}>
          {processingDocuments ? "Processing documents…" : "Upload Documents"}
          <input
            type="file"
            multiple
            onChange={handleUpload}
            disabled={isPremiumBlocked || processingDocuments}
            accept=".txt,.md,.json,.csv,.log,.docx"
          />
        </label>
        <form className="auth-form" onSubmit={handlePaste}>
          <label>
            <span>Paste title</span>
            <input value={pasteTitle} onChange={(event) => setPasteTitle(event.target.value)} disabled={isPremiumBlocked || processingDocuments} placeholder="Senior backend profile / PocketPad project" />
          </label>
          <label>
            <span>Paste content</span>
            <textarea
              rows={8}
              value={pasteContent}
              onChange={(event) => setPasteContent(event.target.value)}
              disabled={isPremiumBlocked || processingDocuments}
              placeholder="Paste candidate information, work history, a project note, or interview preferences here."
            />
          </label>
          <button className="button button-primary" type="submit" disabled={isPremiumBlocked || processingDocuments || !pasteContent.trim()}>
            {processingDocuments ? "Processing..." : "Process Pasted Content"}
          </button>
        </form>
      </article> : null}

      {activeKnowledgeEditor === "profile" ? <form className="glass-panel auth-form table-span-full" onSubmit={handleSaveProfile}>
        <p className="eyebrow">Profile</p>
        <label>
          <span>Full name</span>
          <input value={profileDraft.fullName} onChange={(event) => setProfileDraft((current) => ({ ...current, fullName: event.target.value }))} disabled={isPremiumBlocked || savingProfile} />
        </label>
        <label>
          <span>Short intro</span>
          <textarea rows={5} value={profileDraft.shortIntro} onChange={(event) => setProfileDraft((current) => ({ ...current, shortIntro: event.target.value }))} disabled={isPremiumBlocked || savingProfile} />
        </label>
        <label>
          <span>Strengths</span>
          <input value={profileDraft.strengths} onChange={(event) => setProfileDraft((current) => ({ ...current, strengths: event.target.value }))} disabled={isPremiumBlocked || savingProfile} placeholder="Distributed systems, ownership, debugging" />
        </label>
        <label>
          <span>Skills</span>
          <input value={profileDraft.skills} onChange={(event) => setProfileDraft((current) => ({ ...current, skills: event.target.value }))} disabled={isPremiumBlocked || savingProfile} placeholder="C#, .NET, PostgreSQL, Redis" />
        </label>
        <label>
          <span>Domains</span>
          <input value={profileDraft.domains} onChange={(event) => setProfileDraft((current) => ({ ...current, domains: event.target.value }))} disabled={isPremiumBlocked || savingProfile} placeholder="Fintech, SaaS, AI" />
        </label>
        <label>
          <span>Candidate info</span>
          <textarea rows={8} value={profileDraft.candidateInfo} onChange={(event) => setProfileDraft((current) => ({ ...current, candidateInfo: event.target.value }))} disabled={isPremiumBlocked || savingProfile} placeholder="Education, certifications, location, work preferences, and other useful details. Keep company-specific responsibilities in Experience." />
        </label>
        <button className="button button-primary" type="submit" disabled={isPremiumBlocked || savingProfile || processingDocuments || !knowledgeBase?.knowledgeBaseId}>
          {savingProfile ? "Saving..." : "Save Profile"}
        </button>
      </form> : null}

      {activeKnowledgeEditor === "experience" ? <form className="glass-panel auth-form table-span-full" onSubmit={handleSaveExperience}>
        <div className="section-heading-row">
          <div>
            <p className="eyebrow">Experience</p>
            <h3>Keep each company and role separate</h3>
          </div>
          <button className="button button-secondary button-compact" type="button" onClick={handleNewExperience} disabled={isPremiumBlocked || savingExperience}>
            Add experience
          </button>
        </div>
        {(knowledgeBase?.experienceCards || []).length > 0 && (
          <label>
            <span>Selected experience</span>
            <select value={selectedExperienceId} onChange={(event) => setSelectedExperienceId(event.target.value)} disabled={isPremiumBlocked || savingExperience}>
              {selectedExperienceId === "new" && <option value="new">New experience</option>}
              {(knowledgeBase?.experienceCards || []).map((experience) => (
                <option key={experience.experienceCardId} value={experience.experienceCardId}>
                  {experience.isCurrent ? "Current · " : ""}{experience.role} at {experience.company}
                </option>
              ))}
            </select>
          </label>
        )}
        <div className="form-grid-two">
          <label>
            <span>Company</span>
            <input value={experienceDraft.company} onChange={(event) => setExperienceDraft((current) => ({ ...current, company: event.target.value }))} disabled={isPremiumBlocked || savingExperience} required />
          </label>
          <label>
            <span>Role</span>
            <input value={experienceDraft.role} onChange={(event) => setExperienceDraft((current) => ({ ...current, role: event.target.value }))} disabled={isPremiumBlocked || savingExperience} required />
          </label>
          <label>
            <span>Start date</span>
            <input type="month" value={experienceDraft.startDate} onChange={(event) => setExperienceDraft((current) => ({ ...current, startDate: event.target.value }))} disabled={isPremiumBlocked || savingExperience} />
          </label>
          <label>
            <span>End date</span>
            <input type="month" value={experienceDraft.endDate} onChange={(event) => setExperienceDraft((current) => ({ ...current, endDate: event.target.value }))} disabled={isPremiumBlocked || savingExperience || experienceDraft.isCurrent} />
          </label>
        </div>
        <label className="checkbox-row">
          <input type="checkbox" checked={experienceDraft.isCurrent} onChange={(event) => setExperienceDraft((current) => ({ ...current, isCurrent: event.target.checked, endDate: event.target.checked ? "" : current.endDate }))} disabled={isPremiumBlocked || savingExperience} />
          <span>Current experience — prioritise this role for general interview answers</span>
        </label>
        <label>
          <span>Role summary</span>
          <textarea rows={3} value={experienceDraft.summary} onChange={(event) => setExperienceDraft((current) => ({ ...current, summary: event.target.value }))} disabled={isPremiumBlocked || savingExperience} />
        </label>
        <label>
          <span>Responsibilities and achievements</span>
          <textarea rows={6} value={experienceDraft.responsibilities} onChange={(event) => setExperienceDraft((current) => ({ ...current, responsibilities: event.target.value }))} disabled={isPremiumBlocked || savingExperience} placeholder="Describe day-to-day work, ownership, outcomes, and examples specific to this role." />
        </label>
        <label>
          <span>Skills and tools</span>
          <input value={experienceDraft.skills} onChange={(event) => setExperienceDraft((current) => ({ ...current, skills: event.target.value }))} disabled={isPremiumBlocked || savingExperience} placeholder="API automation, Playwright, Selenium, CI/CD" />
        </label>
        <div className="table-actions" aria-live="polite">
          <button className="button button-primary" type="submit" disabled={isPremiumBlocked || savingExperience || processingDocuments || !experienceDraft.company.trim() || !experienceDraft.role.trim()}>
            {savingExperience ? "Saving..." : experienceDraft.experienceCardId ? "Save Experience" : "Create Experience"}
          </button>
          {experienceDraft.experienceCardId && (
            <button className="button button-secondary" type="button" onClick={handleDeleteExperience} disabled={isPremiumBlocked || savingExperience || processingDocuments}>
              Delete
            </button>
          )}
        </div>
      </form> : null}

      {activeKnowledgeEditor === "project" ? <form className="glass-panel auth-form table-span-full" onSubmit={handleSaveProject}>
        <p className="eyebrow">Project card</p>
        <label>
          <span>Selected project</span>
          <select value={selectedProjectId} onChange={(event) => setSelectedProjectId(event.target.value)} disabled={isPremiumBlocked || savingProject || !(knowledgeBase?.projectCards || []).length}>
            {(knowledgeBase?.projectCards || []).map((project) => (
              <option key={project.projectCardId} value={project.projectCardId}>
                {project.isRecent ? "Recent · " : ""}{project.title}
              </option>
            ))}
          </select>
        </label>
        <label>
          <span>Title</span>
          <input value={projectDraft.title} onChange={(event) => setProjectDraft((current) => ({ ...current, title: event.target.value }))} disabled={isPremiumBlocked || savingProject} />
        </label>
        <label>
          <span>Role</span>
          <input value={projectDraft.role} onChange={(event) => setProjectDraft((current) => ({ ...current, role: event.target.value }))} disabled={isPremiumBlocked || savingProject} />
        </label>
        <label>
          <span>Summary</span>
          <textarea rows={4} value={projectDraft.summary} onChange={(event) => setProjectDraft((current) => ({ ...current, summary: event.target.value }))} disabled={isPremiumBlocked || savingProject} />
        </label>
        <label>
          <span>Stack</span>
          <input value={projectDraft.stack} onChange={(event) => setProjectDraft((current) => ({ ...current, stack: event.target.value }))} disabled={isPremiumBlocked || savingProject} placeholder="React, Node.js, Redis, PostgreSQL" />
        </label>
        <label>
          <span>Architecture</span>
          <textarea rows={4} value={projectDraft.architecture} onChange={(event) => setProjectDraft((current) => ({ ...current, architecture: event.target.value }))} disabled={isPremiumBlocked || savingProject} />
        </label>
        <label>
          <span>Challenges</span>
          <textarea rows={4} value={projectDraft.challenges} onChange={(event) => setProjectDraft((current) => ({ ...current, challenges: event.target.value }))} disabled={isPremiumBlocked || savingProject} />
        </label>
        <label>
          <span>Impact</span>
          <textarea rows={4} value={projectDraft.impact} onChange={(event) => setProjectDraft((current) => ({ ...current, impact: event.target.value }))} disabled={isPremiumBlocked || savingProject} />
        </label>
        <div className="table-actions">
          <button className="button button-primary" type="submit" disabled={isPremiumBlocked || savingProject || processingDocuments || !projectDraft.projectCardId}>
            {savingProject ? "Saving..." : "Save Project Card"}
          </button>
          <button className="button button-secondary" type="button" disabled={isPremiumBlocked || savingProject || processingDocuments || !projectDraft.projectCardId} onClick={() => handleMarkRecent(projectDraft.projectCardId)}>
            Mark as Recent
          </button>
        </div>
      </form> : null}

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
                <th>Section</th>
                <th>Type</th>
                <th>Chars</th>
                <th>Chunks</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {(knowledgeBase?.documents || []).length === 0 ? (
                <tr>
                  <td colSpan="7">No hosted documents processed yet.</td>
                </tr>
              ) : (
                knowledgeBase.documents.map((document) => (
                  <tr key={document.documentId}>
                    <td>{document.fileName}</td>
                    <td>{document.section === "general_reference" ? "General preferences" : document.section || "General preferences"}</td>
                    <td>{document.sourceType || document.contentType || "file"}</td>
                    <td>{document.characterCount}</td>
                    <td>{document.chunkCount}</td>
                    <td>{document.status}</td>
                    <td>
                      <div className="table-actions">
                        <button
                          className="button button-secondary button-compact"
                          type="button"
                          onClick={() => handleViewDocument(document.documentId)}
                          disabled={processingDocuments || deletingDocumentId === document.documentId}
                        >
                          {documentLoadingId === document.documentId ? "Loading..." : "View"}
                        </button>
                        <button
                          className="button button-secondary button-compact"
                          type="button"
                          onClick={() => handleDeleteDocument(document.documentId)}
                          disabled={processingDocuments || deletingDocumentId === document.documentId}
                        >
                          {deletingDocumentId === document.documentId ? "Deleting..." : "Delete"}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </TableScroll>
        {status ? (
          <p className={`status-message ${status.toLowerCase().includes("could not") ? "status-error" : ""}`} role="status" aria-live="polite">
            {status}
          </p>
        ) : null}
      </div>

      {selectedDocument ? (
        <article className="glass-panel table-span-full">
          <div className="table-header">
            <div>
              <p className="eyebrow">Document content</p>
              <h3>{selectedDocument.fileName}</h3>
            </div>
            <button className="button button-secondary button-compact" type="button" onClick={() => setSelectedDocument(null)}>
              Close
            </button>
          </div>
          <p>
            {selectedDocument.section === "general_reference" ? "General preferences" : selectedDocument.section || "General preferences"} · {selectedDocument.sourceType || selectedDocument.contentType || "file"} · {selectedDocument.characterCount} chars ·{" "}
            {selectedDocument.chunkCount} chunks
          </p>
          <div className="document-preview">
            <pre>{selectedDocument.extractedText || "No extracted text is available for this document."}</pre>
          </div>
        </article>
      ) : null}
    </div>
  );
}

function parseListInput(value) {
  return String(value || "")
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);
}

function WalletPanel({ accessToken, summary, onSummaryChanged }) {
  const [walletHistoryPage, setWalletHistoryPage] = useState({ items: [], page: 1, hasNextPage: false, totalCount: 0 });
  const [walletPurchasesPage, setWalletPurchasesPage] = useState({ items: [], page: 1, hasNextPage: false, totalCount: 0 });
  const [paymentCatalog, setPaymentCatalog] = useState(null);
  const [status, setStatus] = useState("");
  const [statusType, setStatusType] = useState("success");
  const [submittingTarget, setSubmittingTarget] = useState("");
  const [loading, setLoading] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setStatus("");

    Promise.allSettled([
      fetchWalletHistory(accessToken, 1, 12),
      fetchWalletPurchases(accessToken, 1, 12),
      fetchPaymentCatalog(accessToken)
    ]).then(([historyResult, purchasesResult, catalogResult]) => {
      if (cancelled) {
        return;
      }

      setWalletHistoryPage(
        historyResult.status === "fulfilled"
          ? historyResult.value
          : { items: [], page: 1, hasNextPage: false, totalCount: 0 }
      );
      setWalletPurchasesPage(
        purchasesResult.status === "fulfilled"
          ? purchasesResult.value
          : { items: [], page: 1, hasNextPage: false, totalCount: 0 }
      );
      setPaymentCatalog(catalogResult.status === "fulfilled" ? catalogResult.value : null);
      if (historyResult.status === "rejected" || purchasesResult.status === "rejected" || catalogResult.status === "rejected") {
        setStatusType("error");
        setStatus("Some wallet information could not be loaded. Existing balances remain unchanged.");
      }
      setLoading(false);
    });

    return () => {
      cancelled = true;
    };
  }, [accessToken, reloadKey]);

  async function refreshWalletState() {
    const [history, purchases, catalog] = await Promise.all([
      fetchWalletHistory(accessToken, walletHistoryPage.page || 1, 12),
      fetchWalletPurchases(accessToken, walletPurchasesPage.page || 1, 12),
      fetchPaymentCatalog(accessToken).catch(() => null)
    ]);
    await onSummaryChanged();
    setWalletHistoryPage(history || { items: [], page: 1, hasNextPage: false, totalCount: 0 });
    setWalletPurchasesPage(purchases || { items: [], page: 1, hasNextPage: false, totalCount: 0 });
    setPaymentCatalog(catalog);
  }

  async function handleCheckout(target, packCode) {
    setSubmittingTarget(`${target}:${packCode}`);
    setStatus("");
    setStatusType("success");

    try {
      const checkout = await createPaymentCheckout(accessToken, { target, packCode });
      await openRazorpayCheckout(checkout, async (response) => {
        const confirmation = await confirmPaymentCheckout(accessToken, {
          checkoutId: checkout.checkoutId,
          razorpayOrderId: response.razorpay_order_id,
          razorpayPaymentId: response.razorpay_payment_id,
          razorpaySignature: response.razorpay_signature
        });
        await refreshWalletState();
        setStatusType("success");
        setStatus(confirmation?.message || "Payment verified. Wallet refresh complete.");
      });
    } catch (error) {
      setStatusType("error");
      setStatus(error.message || "Could not start checkout.");
    } finally {
      setSubmittingTarget("");
    }
  }

  return (
    <div className="dashboard-grid">
      {statusType === "error" ? <RetryNotice message={status} onRetry={() => setReloadKey((value) => value + 1)} className="table-span-full" /> : null}
      <MetricCard label="Pro available" value={summary.proAvailableCredits.toFixed(2)} tone="signal" />
      <MetricCard label="Premium available" value={summary.premiumAvailableCredits.toFixed(2)} tone="signal" />
      <MetricCard label="Premium debt" value={summary.premiumNegativeCredits.toFixed(2)} tone="warn" />

      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Credits and billing</p>
        <h1>Choose the AI lane that fits how you want to work.</h1>
        <p>
          Premium credits use Phantom's managed AI first. When they run out, available Pro credits let you continue with your own connected provider.
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

      {status && statusType !== "error" ? (
        <article className="glass-panel table-span-full">
          <p className={`status-message status-${statusType}`} role="status" aria-live="polite">
            {status}
          </p>
        </article>
      ) : null}

      <DataTable
        loading={loading}
        title="Purchase history"
        columns={["Purchase", "Amount", "Credits", "Status", "Created"]}
        rows={
          (walletPurchasesPage.items || []).length === 0
            ? null
            : walletPurchasesPage.items.map((item) => [
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
        footer={
          <PaginationBar
            page={walletPurchasesPage.page || 1}
            hasNextPage={Boolean(walletPurchasesPage.hasNextPage)}
            totalCount={walletPurchasesPage.totalCount || 0}
            onPrevious={() => loadWalletPurchasesPage(accessToken, walletPurchasesPage.page - 1, setWalletPurchasesPage)}
            onNext={() => loadWalletPurchasesPage(accessToken, (walletPurchasesPage.page || 1) + 1, setWalletPurchasesPage)}
          />
        }
      />

      <DataTable
        loading={loading}
        title="Wallet history"
        columns={["Session", "Credits", "Blocks", "Debt", "Created"]}
        rows={
          (walletHistoryPage.items || []).length === 0
            ? null
            : walletHistoryPage.items.map((item) => [
                item.sessionId,
                item.chargedCredits,
                item.chargedBlocks,
                item.addedPremiumDebt,
                formatDate(item.createdAtUtc)
              ])
        }
        emptyLabel="No wallet entries recorded yet."
        footer={
          <PaginationBar
            page={walletHistoryPage.page || 1}
            hasNextPage={Boolean(walletHistoryPage.hasNextPage)}
            totalCount={walletHistoryPage.totalCount || 0}
            onPrevious={() => loadWalletHistoryPage(accessToken, walletHistoryPage.page - 1, setWalletHistoryPage)}
            onNext={() => loadWalletHistoryPage(accessToken, (walletHistoryPage.page || 1) + 1, setWalletHistoryPage)}
          />
        }
      />
    </div>
  );
}

function DevicesPanel({ accessToken }) {
  const [devicesPage, setDevicesPage] = useState({ items: [], page: 1, hasNextPage: false, totalCount: 0 });
  const [busyDevice, setBusyDevice] = useState("");
  const [status, setStatus] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  async function load(page = 1) {
    setLoading(true);
    setError("");
    try {
      await loadDevicesPage(accessToken, page, setDevicesPage);
    } catch (loadError) {
      setError(loadError.message || "Could not load device sessions.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load(1);
  }, [accessToken]);

  const devices = devicesPage.items || [];

  async function handleRevoke(device) {
    const deviceKey = `${device.deviceInstallId}-${device.deviceFingerprintHash}`;
    if (!window.confirm("Sign this device out? It will need to authenticate again.")) return;
    setBusyDevice(deviceKey);
    setStatus("");
    try {
      await revokeDeviceSession(accessToken, device.deviceInstallId, device.deviceFingerprintHash);
      if (isCurrentBrowserDevice(device.deviceInstallId)) {
        window.location.assign("/login");
        return;
      }
      await load(devicesPage.page || 1);
      setStatus("Device session revoked.");
    } catch (error) {
      setError(error.message || "Could not revoke the device session.");
    } finally {
      setBusyDevice("");
    }
  }

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Your devices</p>
        <h1>Know exactly where your Phantom account has been used.</h1>
        <p>Review active and previous browser or desktop sessions, including their sign-in method and last activity.</p>
      </article>
      <RetryNotice message={error} onRetry={() => load(devicesPage.page || 1)} className="table-span-full" />
      {loading && devices.length === 0 ? <SectionSkeleton label="Loading device sessions" /> : devices.length === 0 ? (
        <article className="glass-panel">
          <h3>No device sessions recorded yet.</h3>
        </article>
      ) : (
        devices.map((device) => (
          <article className="glass-panel device-card" key={`${device.deviceInstallId}-${device.deviceFingerprintHash}`}>
            <span className={`status-pill ${device.isActive ? "status-pill-good" : ""}`}>
              {device.isActive ? "Active" : "Historical"}
            </span>
            <h3>{device.deviceInstallId.startsWith("web-") ? "Browser Dashboard" : "Phantom Desktop"}</h3>
            <p>Device reference: {maskIdentifier(device.deviceInstallId)}</p>
            <p>Auth method: {device.authMethod}</p>
            <p>Last seen: {formatDate(device.lastAuthenticatedAtUtc)}</p>
            {device.isActive ? (
              <button className="button button-secondary button-compact" type="button" disabled={busyDevice === `${device.deviceInstallId}-${device.deviceFingerprintHash}`} onClick={() => handleRevoke(device)}>
                {busyDevice === `${device.deviceInstallId}-${device.deviceFingerprintHash}` ? "Signing out..." : "Sign out device"}
              </button>
            ) : null}
          </article>
        ))
      )}
      <article className="glass-panel table-span-full">
        <PaginationBar
          page={devicesPage.page || 1}
          hasNextPage={Boolean(devicesPage.hasNextPage)}
          totalCount={devicesPage.totalCount || 0}
          disabled={loading}
          onPrevious={() => load(devicesPage.page - 1)}
          onNext={() => load((devicesPage.page || 1) + 1)}
        />
      </article>
      {status ? <p className="status-message table-span-full" role="status">{status}</p> : null}
    </div>
  );
}

function HistoryPanel({ accessToken }) {
  const [walletHistoryPage, setWalletHistoryPage] = useState({ items: [], page: 1, hasNextPage: false, totalCount: 0 });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  async function load(page = 1) {
    setLoading(true);
    setError("");
    try {
      await loadWalletHistoryPage(accessToken, page, setWalletHistoryPage);
    } catch (loadError) {
      setError(loadError.message || "Could not load usage history.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load(1);
  }, [accessToken]);

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Usage history</p>
        <h1>A clear record of every credit charge.</h1>
        <p>Review session-level usage, credit deductions, and any Premium continuation balance in one place.</p>
      </article>
      <RetryNotice message={error} onRetry={() => load(walletHistoryPage.page || 1)} className="table-span-full" />
      <DataTable
        loading={loading}
        title="Usage charge history"
        columns={["Ledger entry", "Session", "Credits", "Debt", "Created"]}
        rows={
          (walletHistoryPage.items || []).length === 0
            ? null
            : walletHistoryPage.items.map((item) => [
                item.ledgerEntryId,
                item.sessionId,
                item.chargedCredits,
                item.addedPremiumDebt,
                formatDate(item.createdAtUtc)
              ])
        }
        emptyLabel="No usage ledger history recorded yet."
        footer={
          <PaginationBar
            page={walletHistoryPage.page || 1}
            hasNextPage={Boolean(walletHistoryPage.hasNextPage)}
            totalCount={walletHistoryPage.totalCount || 0}
            disabled={loading}
            onPrevious={() => load(walletHistoryPage.page - 1)}
            onNext={() => load((walletHistoryPage.page || 1) + 1)}
          />
        }
      />
    </div>
  );
}

function QuestionBanksPanel({ accessToken }) {
  const [banksPage, setBanksPage] = useState({ items: [], page: 1, hasNextPage: false, totalCount: 0 });
  const [editingSessionId, setEditingSessionId] = useState("");
  const [draft, setDraft] = useState({ interviewName: "", questions: [] });
  const [saving, setSaving] = useState(false);
  const [status, setStatus] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  async function load(page = 1) {
    setLoading(true);
    setError("");
    try {
      await loadQuestionBanksPage(accessToken, page, setBanksPage);
    } catch (loadError) {
      setError(loadError.message || "Could not load interview question banks.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load(1);
  }, [accessToken]);

  function startEditing(bank) {
    setEditingSessionId(bank.sessionId);
    setDraft({
      interviewName: bank.interviewName || `Interview · ${formatDate(bank.interviewEndedAtUtc)}`,
      questions: [...(bank.questions || [])]
    });
    setStatus("");
  }

  function updateQuestion(index, value) {
    setDraft((current) => ({
      ...current,
      questions: current.questions.map((question, questionIndex) => questionIndex === index ? value : question)
    }));
  }

  function mergeWithPrevious(index) {
    setDraft((current) => {
      const questions = [...current.questions];
      const followUp = questions[index].trim();
      const previous = questions[index - 1].trim().replace(/[?.!]+$/, "");
      const normalizedFollowUp = followUp ? `${followUp.charAt(0).toLowerCase()}${followUp.slice(1)}` : "";
      questions[index - 1] = [previous, normalizedFollowUp].filter(Boolean).join("; ");
      questions.splice(index, 1);
      return { ...current, questions };
    });
  }

  async function saveChanges(sessionId) {
    setSaving(true);
    setStatus("");
    try {
      const updated = await updateInterviewQuestionBank(accessToken, sessionId, draft);
      setBanksPage((current) => ({
        ...current,
        items: current.items.map((bank) => bank.sessionId === sessionId ? updated : bank)
      }));
      setEditingSessionId("");
      setStatus("Interview question bank updated.");
    } catch (error) {
      setStatus(error.message || "Could not update the interview question bank.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Interview question banks</p>
        <h1>Review the questions from each completed interview.</h1>
        <p>Phantom keeps the cleaned, grouped questions only. Interview answers and the full chat are not stored here.</p>
      </article>

      <RetryNotice message={error} onRetry={() => load(banksPage.page || 1)} className="table-span-full" />

      {loading && (banksPage.items || []).length === 0 ? <SectionSkeleton label="Loading interview question banks" /> : (banksPage.items || []).length === 0 ? (
        <article className="glass-panel table-span-full">
          <h3>No interview question banks yet.</h3>
          <p>A bank appears after the desktop session ends and background processing completes.</p>
        </article>
      ) : (
        banksPage.items.map((bank) => (
          <article className="glass-panel table-span-full" key={bank.sessionId}>
            {editingSessionId === bank.sessionId ? (
              <div className="question-bank-editor">
                <label>
                  <span>Interview name</span>
                  <input
                    maxLength={120}
                    value={draft.interviewName}
                    onChange={(event) => setDraft((current) => ({ ...current, interviewName: event.target.value }))}
                    placeholder="Example: Senior backend interview"
                  />
                </label>
                {draft.questions.map((question, index) => (
                  <div className="question-edit-row" key={`${bank.sessionId}-${index}`}>
                    <label>
                      <span>Question {index + 1}</span>
                      <textarea
                        rows="2"
                        maxLength={2000}
                        value={question}
                        onChange={(event) => updateQuestion(index, event.target.value)}
                      />
                    </label>
                    {index > 0 ? (
                      <button className="button button-ghost button-compact" type="button" onClick={() => mergeWithPrevious(index)}>
                        Merge with previous
                      </button>
                    ) : null}
                  </div>
                ))}
                <div className="table-actions">
                  <button className="button button-primary button-compact" type="button" disabled={saving} onClick={() => saveChanges(bank.sessionId)}>
                    {saving ? "Saving..." : "Save changes"}
                  </button>
                  <button className="button button-ghost button-compact" type="button" disabled={saving} onClick={() => setEditingSessionId("")}>
                    Cancel
                  </button>
                </div>
              </div>
            ) : (
              <>
                <div className="question-bank-heading">
                  <div>
                    <p className="story-tag">Interview · {formatDate(bank.interviewEndedAtUtc)}</p>
                    <h3>{bank.interviewName || `Interview · ${formatDate(bank.interviewEndedAtUtc)}`}</h3>
                  </div>
                  <button className="button button-secondary button-compact" type="button" onClick={() => startEditing(bank)}>
                    Edit interview
                  </button>
                </div>
                <p>{(bank.questions || []).length} grouped question{(bank.questions || []).length === 1 ? "" : "s"}</p>
                {(bank.questions || []).length > 0 ? (
                  <ol className="question-bank-list">
                    {(bank.questions || []).map((question, index) => (
                      <li key={`${bank.sessionId}-${index}`}>{question}</li>
                    ))}
                  </ol>
                ) : <p>No questions were captured for this completed session.</p>}
              </>
            )}
          </article>
        ))
      )}

      {status ? (
        <article className="glass-panel table-span-full">
          <p className={`status-message ${status.toLowerCase().includes("could not") ? "status-error" : ""}`} role="status" aria-live="polite">{status}</p>
        </article>
      ) : null}

      <article className="glass-panel table-span-full">
        <PaginationBar
          page={banksPage.page || 1}
          hasNextPage={Boolean(banksPage.hasNextPage)}
          totalCount={banksPage.totalCount || 0}
          disabled={loading}
          onPrevious={() => load(banksPage.page - 1)}
          onNext={() => load((banksPage.page || 1) + 1)}
        />
      </article>
    </div>
  );
}

function SupportPanel({ accessToken, support }) {
  const [ticketsPage, setTicketsPage] = useState({ items: [], page: 1, hasNextPage: false, totalCount: 0 });
  const [ticketForm, setTicketForm] = useState({
    subject: "",
    category: "general",
    priority: "normal",
    description: ""
  });
  const [status, setStatus] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState("");

  async function load(page = 1) {
    setLoading(true);
    setLoadError("");
    try {
      await loadSupportTicketsPage(accessToken, page, setTicketsPage);
    } catch (error) {
      setLoadError(error.message || "Could not load support tickets.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load(1);
  }, [accessToken]);

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setStatus("");
    try {
      await createUserSupportTicket(accessToken, ticketForm);
      setTicketForm({
        subject: "",
        category: "general",
        priority: "normal",
        description: ""
      });
      await load(1);
      setStatus("Support ticket created.");
    } catch (error) {
      setStatus(error.message || "Could not create the support ticket.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Support</p>
        <h1>Tell us what happened. Include the details that will help us resolve it faster.</h1>
      </article>
      <RetryNotice message={loadError} onRetry={() => load(ticketsPage.page || 1)} className="table-span-full" />
      <article className="glass-panel table-span-full">
        <h3>{support?.openLockSessionId || "No active support event"}</h3>
        <p>{support?.supportMessage || "Support state is not available."}</p>
        <p><strong>Response target:</strong> within 2 business days. Use urgent priority for account access or payment incidents.</p>
        <div className="stats-grid">
          <MetricCard label="Last charge" value={String(support?.lastUsageChargeCredits ?? 0)} />
          <MetricCard label="Lease hours left" value={String(support?.offlineLeaseHoursRemaining ?? 0)} />
        </div>
      </article>
      <form className="glass-panel auth-form table-span-full" onSubmit={handleSubmit}>
        <p className="eyebrow">Create support ticket</p>
        <div className="admin-form">
          <label>
            Subject
            <input value={ticketForm.subject} onChange={(event) => setTicketForm((current) => ({ ...current, subject: event.target.value }))} placeholder="Issue summary" minLength={5} maxLength={160} required />
          </label>
          <label>
            Category
            <select value={ticketForm.category} onChange={(event) => setTicketForm((current) => ({ ...current, category: event.target.value }))}>
              <option value="general">General</option>
              <option value="billing">Billing</option>
              <option value="device_lock">Device lock</option>
              <option value="access">Access</option>
              <option value="knowledge_base">Knowledge base</option>
            </select>
          </label>
          <label>
            Priority
            <select value={ticketForm.priority} onChange={(event) => setTicketForm((current) => ({ ...current, priority: event.target.value }))}>
              <option value="low">Low</option>
              <option value="normal">Normal</option>
              <option value="high">High</option>
              <option value="urgent">Urgent</option>
            </select>
          </label>
          <label className="table-span-full">
            Description
            <textarea rows={5} value={ticketForm.description} onChange={(event) => setTicketForm((current) => ({ ...current, description: event.target.value }))} placeholder="What happened, what you expected, and any relevant checkout/session/device details." minLength={10} maxLength={5000} required />
          </label>
        </div>
        <button className="button button-primary" type="submit" disabled={submitting}>
          {submitting ? "Creating..." : "Create Ticket"}
        </button>
        {status ? <p className={`status-message ${status.toLowerCase().includes("could not") ? "status-error" : ""}`} role="status" aria-live="polite">{status}</p> : null}
      </form>
      <DataTable
        loading={loading}
        title="Your support tickets"
        columns={["Ticket", "Category", "Priority", "Status", "Updated"]}
        rows={
          (ticketsPage.items || []).length === 0
            ? null
            : ticketsPage.items.map((item) => [
                item.subject,
                item.category,
                item.priority,
                item.status,
                formatDate(item.updatedAtUtc)
              ])
        }
        emptyLabel="No support tickets created yet."
        footer={
          <PaginationBar
            page={ticketsPage.page || 1}
            hasNextPage={Boolean(ticketsPage.hasNextPage)}
            totalCount={ticketsPage.totalCount || 0}
            disabled={loading}
            onPrevious={() => load(ticketsPage.page - 1)}
            onNext={() => load((ticketsPage.page || 1) + 1)}
          />
        }
      />
    </div>
  );
}

function AdminLoginPage({ onAuthenticated, adminSession }) {
  const navigate = useNavigate();
  const [email, setEmail] = useState(adminSession?.email || "");
  const [password, setPassword] = useState("");
  const [otpCode, setOtpCode] = useState("");
  const [challenge, setChallenge] = useState(null);
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
      if (!challenge) {
        const nextChallenge = await loginAdmin({ email, password });
        setChallenge(nextChallenge);
        setPassword("");
        setOtpCode("");
      } else {
        const session = await verifyAdminOtp(challenge.challengeId, otpCode);
        onAuthenticated(session);
        navigate("/admin", { replace: true });
      }
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
          {!challenge ? (
            <>
              <label>
                <span>Admin email</span>
                <input type="email" value={email} onChange={(event) => setEmail(event.target.value)} placeholder="admin@example.com" autoComplete="username" required />
              </label>
              <PasswordField label="Password" value={password} onChange={(event) => setPassword(event.target.value)} placeholder="Enter your admin password" autoComplete="current-password" required />
            </>
          ) : (
            <>
              <div className="status-message" role="status">
                A 6-digit verification code was sent to {challenge.maskedEmail}. It expires at {formatDate(challenge.expiresAtUtc)}.
              </div>
              <label>
                <span>Email verification code</span>
                <input
                  type="text"
                  value={otpCode}
                  onChange={(event) => setOtpCode(event.target.value.replace(/\D/g, "").slice(0, 6))}
                  placeholder="000000"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  pattern="[0-9]{6}"
                  maxLength={6}
                  required
                  autoFocus
                />
              </label>
            </>
          )}
          <button className="button button-primary" type="submit" disabled={submitting}>
            {submitting ? "Authenticating..." : challenge ? "Verify and Open Dashboard" : "Continue with Email Verification"}
          </button>
          {challenge ? (
            <button className="button button-secondary" type="button" disabled={submitting} onClick={() => { setChallenge(null); setOtpCode(""); setError(""); }}>
              Use a different account
            </button>
          ) : null}
          {error ? <p className="status-message status-error" role="alert">{error}</p> : null}
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
          {status ? <p className={`status-message ${status.toLowerCase().includes("could not") ? "status-error" : ""}`} role="status" aria-live="polite">{status}</p> : null}
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
  const passwordError = getPasswordPolicyError(password);

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setStatus("");
    try {
      if (!token) {
        throw new Error("Reset token missing from the URL.");
      }
      if (passwordError) {
        throw new Error(passwordError);
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
          <PasswordField label="New password" value={password} onChange={(event) => setPassword(event.target.value)} placeholder="Create a strong password" autoComplete="new-password" minLength={12} required />
          <p className={`inline-note ${password ? passwordError ? "inline-note-error" : "inline-note-success" : ""}`}>
            {password && !passwordError ? "Password meets the security requirements." : PASSWORD_REQUIREMENTS}
          </p>
          <PasswordField label="Confirm password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} placeholder="Re-enter the new password" autoComplete="new-password" required />
          <button className="button button-primary" type="submit" disabled={submitting || Boolean(passwordError) || password !== confirmPassword}>
            {submitting ? "Resetting..." : "Reset Password"}
          </button>
          {status ? <p className={`status-message ${status.toLowerCase().includes("complete") ? "" : "status-error"}`} role="status" aria-live="polite">{status}</p> : null}
        </form>
      </section>
    </main>
  );
}

function AdminDashboardPage({ adminSession }) {
  const [overview, setOverview] = useState(null);
  const [inventory, setInventory] = useState(null);
  const [speechInventory, setSpeechInventory] = useState(null);
  const [latencyStatus, setLatencyStatus] = useState(null);
  const [gmailStatus, setGmailStatus] = useState(null);
  const [catalogRefreshResult, setCatalogRefreshResult] = useState(null);
  const [speechCatalogRefreshResult, setSpeechCatalogRefreshResult] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [reloadKey, setReloadKey] = useState(0);
  const location = useLocation();

  useEffect(() => {
    let cancelled = false;

    async function load() {
      setLoading(true);
      setError("");
      const results = await Promise.allSettled([
        fetchAdminOverview(adminSession.accessToken),
        fetchManagedAiAdminInventory(adminSession.accessToken),
        fetchManagedAiLatencyStatus(adminSession.accessToken),
        fetchGmailOAuthStatus(adminSession.accessToken),
        fetchManagedSpeechAdminInventory(adminSession.accessToken)
      ]);
      if (!cancelled) {
        const [overviewResult, inventoryResult, latencyResult, gmailResult, speechInventoryResult] = results;
        if (overviewResult.status === "fulfilled") setOverview(overviewResult.value);
        if (inventoryResult.status === "fulfilled") setInventory(inventoryResult.value);
        if (latencyResult.status === "fulfilled") setLatencyStatus(latencyResult.value);
        if (gmailResult.status === "fulfilled") setGmailStatus(gmailResult.value);
        if (speechInventoryResult.status === "fulfilled") setSpeechInventory(speechInventoryResult.value);
        const failedCount = results.filter((result) => result.status === "rejected").length;
        if (failedCount > 0) {
          const overviewError = overviewResult.status === "rejected" ? overviewResult.reason?.message : "";
          setError(overviewError || `${failedCount} admin section${failedCount === 1 ? "" : "s"} could not be loaded.`);
        }
        setLoading(false);
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [adminSession.accessToken, adminSession.email, adminSession.expiresAtUtc, reloadKey]);

  async function refreshManagedInventory() {
    const nextInventory = await fetchManagedAiAdminInventory(adminSession.accessToken);
    setInventory(nextInventory);
    return nextInventory;
  }

  async function refreshManagedAiState() {
    const [nextInventory, nextLatencyStatus] = await Promise.all([
      fetchManagedAiAdminInventory(adminSession.accessToken),
      fetchManagedAiLatencyStatus(adminSession.accessToken)
    ]);
    setInventory(nextInventory);
    setLatencyStatus(nextLatencyStatus);
    return {
      inventory: nextInventory,
      latencyStatus: nextLatencyStatus
    };
  }

  async function refreshManagedSpeechState() {
    const nextInventory = await fetchManagedSpeechAdminInventory(adminSession.accessToken);
    setSpeechInventory(nextInventory);
    return nextInventory;
  }

  async function refreshOverview() {
    const [nextOverview, nextGmailStatus] = await Promise.all([
      fetchAdminOverview(adminSession.accessToken),
      fetchGmailOAuthStatus(adminSession.accessToken)
    ]);
    setOverview(nextOverview);
    setGmailStatus(nextGmailStatus);
  }

  if (loading) {
    return <DashboardSkeleton label="Admin dashboard" />;
  }

  if (!overview) {
    return (
      <main className="page">
        <Seo title="Admin Dashboard | Phantom" description="Admin dashboard" noindex />
        <section className="glass-panel page-intro">
          <p className="eyebrow">Admin dashboard</p>
          <h1>Admin access failed</h1>
          <p>{error || "The admin overview could not be loaded."}</p>
          <button className="button button-primary" type="button" onClick={() => setReloadKey((value) => value + 1)}>Try again</button>
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
          <div className="dashboard-rail-heading">
            <p className="eyebrow">Control plane</p>
            <p className="dashboard-rail-title">Hosted operations</p>
          </div>
          <nav className="dashboard-nav" aria-label="Admin navigation">
            {adminNav.map((item) => (
              <NavLink key={item.to} to={item.to} end={item.to === "/admin"} className={({ isActive }) => isActive ? "dashboard-nav-active" : ""}>
                <span>{item.label}</span>
              </NavLink>
            ))}
          </nav>
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
            <InfoRow label="Downloads" value={String(overview?.downloadCount ?? 0)} />
            <InfoRow label="Feedback" value={String(overview?.feedbackCount ?? 0)} />
          </div>
        </aside>

        <section className="dashboard-main" aria-busy={loading || undefined}>
          <RetryNotice message={error} onRetry={() => setReloadKey((value) => value + 1)} />
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
                  inventory={inventory}
                  onManagedInventoryChanged={refreshManagedInventory}
                />
              }
            />
            <Route
              path="payments"
              element={
                <AdminPaymentsPanel
                  accessToken={adminSession.accessToken}
                  overview={overview}
                  onRefreshOverview={refreshOverview}
                />
              }
            />
            <Route
              path="tickets"
              element={
                <AdminTicketsPanel
                  accessToken={adminSession.accessToken}
                  overview={overview}
                  onRefreshOverview={refreshOverview}
                />
              }
            />
            <Route
              path="feedback"
              element={<AdminFeedbackPanel accessToken={adminSession.accessToken} />}
            />
            <Route
              path="managed-ai"
              element={
                <ManagedAiAdminPanel
                  accessToken={adminSession.accessToken}
                  inventory={inventory}
                  latencyStatus={latencyStatus}
                  onRefresh={refreshManagedAiState}
                  catalogRefreshResult={catalogRefreshResult}
                  onCatalogRefreshResult={setCatalogRefreshResult}
                />
              }
            />
            <Route
              path="managed-speech"
              element={
                <ManagedSpeechAdminPanel
                  accessToken={adminSession.accessToken}
                  inventory={speechInventory}
                  onRefresh={refreshManagedSpeechState}
                  catalogRefreshResult={speechCatalogRefreshResult}
                  onCatalogRefreshResult={setSpeechCatalogRefreshResult}
                />
              }
            />
            <Route
              path="audit"
              element={<AdminAuditPanel accessToken={adminSession.accessToken} />}
            />
            <Route
              path="settings"
              element={<AdminSettingsPanel accessToken={adminSession.accessToken} />}
            />
          </Routes>
        </section>
      </section>
    </main>
  );
}

function AdminFeedbackPanel({ accessToken }) {
  const [feedbackPage, setFeedbackPage] = useState({ items: [], page: 1, hasNextPage: false, totalCount: 0 });
  const [statusFilter, setStatusFilter] = useState("all");
  const [loading, setLoading] = useState(true);
  const [updatingId, setUpdatingId] = useState("");
  const [error, setError] = useState("");

  async function load(page = 1, status = statusFilter) {
    setLoading(true);
    setError("");
    try {
      setFeedbackPage(await fetchAdminFeedback(accessToken, { status, page: Math.max(1, page), pageSize: 20 }));
    } catch (loadError) {
      setError(loadError.message || "Could not load feedback.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load(1, statusFilter); }, [accessToken, statusFilter]);

  async function changeStatus(item, status) {
    setUpdatingId(item.feedbackId);
    setError("");
    try {
      await updateAdminFeedback(accessToken, { feedbackId: item.feedbackId, status, adminNotes: item.adminNotes || "" });
      await load(feedbackPage.page || 1, statusFilter);
    } catch (updateError) {
      setError(updateError.message || "Could not update feedback.");
    } finally {
      setUpdatingId("");
    }
  }

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Feedback and reviews</p>
        <h1>Review what people tell you before anything becomes public.</h1>
        <p>Publishing is available only when the submitter explicitly consented. Email addresses always remain private.</p>
      </article>
      <article className="glass-panel table-span-full section-switcher">
        {["all", "new", "reviewed", "published", "rejected"].map((status) => (
          <button key={status} className={`button button-compact ${statusFilter === status ? "button-primary" : "button-ghost"}`} type="button" aria-pressed={statusFilter === status} onClick={() => setStatusFilter(status)}>{status === "all" ? "All" : status[0].toUpperCase() + status.slice(1)}</button>
        ))}
      </article>
      <RetryNotice message={error} onRetry={() => load(feedbackPage.page || 1)} className="table-span-full" />
      <DataTable
        loading={loading}
        title="Feedback submissions"
        columns={["Person", "Rating", "Feedback", "Publish consent", "Status", "Submitted", "Actions"]}
        rows={(feedbackPage.items || []).map((item) => [
          <span key="person"><strong>{item.name}</strong><br /><small>{item.email}</small></span>,
          `${item.rating}/5`,
          <span className="feedback-admin-message" key="message">{item.message}<br /><small>{item.category}</small></span>,
          item.consentToPublish ? "Yes" : "No",
          item.status,
          formatDate(item.createdAtUtc),
          <div className="table-actions" key="actions">
            <button className="button button-ghost button-compact" type="button" disabled={updatingId === item.feedbackId} onClick={() => changeStatus(item, "reviewed")}>Mark reviewed</button>
            <button className="button button-primary button-compact" type="button" disabled={!item.consentToPublish || updatingId === item.feedbackId} title={!item.consentToPublish ? "Publication consent was not granted" : "Publish this review"} onClick={() => changeStatus(item, "published")}>Publish</button>
            <button className="button button-danger button-compact" type="button" disabled={updatingId === item.feedbackId} onClick={() => changeStatus(item, "rejected")}>Reject</button>
          </div>
        ])}
        emptyLabel="No feedback matches this filter."
        footer={<PaginationBar page={feedbackPage.page || 1} hasNextPage={Boolean(feedbackPage.hasNextPage)} totalCount={feedbackPage.totalCount || 0} disabled={loading} onPrevious={() => load(feedbackPage.page - 1)} onNext={() => load((feedbackPage.page || 1) + 1)} />}
      />
    </div>
  );
}

function AdminAuditPanel({ accessToken }) {
  const [auditPage, setAuditPage] = useState({ items: [], page: 1, hasNextPage: false, totalCount: 0 });
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  async function load(page = 1) {
    setLoading(true);
    setError("");
    try {
      setAuditPage(await fetchAdminAudit(accessToken, { page: Math.max(1, page), pageSize: 25 }));
    } catch (loadError) {
      setError(loadError.message || "Could not load the audit trail.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load(1); }, [accessToken]);

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Audit trail</p>
        <h1>Review security-sensitive changes made through the admin control plane.</h1>
        <p>Each mutation records the operator, route, target, reason, result, time, and correlation reference.</p>
      </article>
      <RetryNotice message={error} onRetry={() => load(auditPage.page || 1)} className="table-span-full" />
      <DataTable
        loading={loading}
        title="Recent admin actions"
        columns={["Operator", "Action", "Target", "Reason", "Result", "Time", "Reference"]}
        rows={(auditPage.items || []).map((item) => [
          item.adminEmail,
          `${item.method} ${item.path}`,
          item.targetUserId || "System",
          item.reason || "No reason field",
          item.succeeded ? "Succeeded" : `Failed${item.statusCode ? ` (${item.statusCode})` : ""}`,
          formatDate(item.createdAtUtc),
          maskIdentifier(item.correlationId)
        ])}
        emptyLabel="No admin changes have been recorded yet."
        footer={<PaginationBar page={auditPage.page || 1} hasNextPage={Boolean(auditPage.hasNextPage)} totalCount={auditPage.totalCount || 0} disabled={loading} onPrevious={() => load(auditPage.page - 1)} onNext={() => load((auditPage.page || 1) + 1)} />}
      />
    </div>
  );
}

function AdminSettingsPanel({ accessToken }) {
  const [settings, setSettings] = useState(null);
  const [phoneVerificationRequired, setPhoneVerificationRequired] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [loading, setLoading] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError("");
    fetchRegistrationSettings()
      .then((result) => {
        if (!cancelled) {
          setSettings(result);
          setPhoneVerificationRequired(Boolean(result?.phoneVerificationRequired));
          setLoading(false);
        }
      })
      .catch((loadError) => {
        if (!cancelled) {
          setError(loadError.message || "Could not load registration settings.");
          setLoading(false);
        }
      });
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  async function handleSave() {
    setSubmitting(true);
    setError("");
    setSuccess("");
    try {
      const result = await updateRegistrationSettings(accessToken, { phoneVerificationRequired });
      setSettings(result);
      setPhoneVerificationRequired(Boolean(result.phoneVerificationRequired));
      setSuccess("Signup verification settings saved.");
    } catch (saveError) {
      setError(saveError.message || "Could not save registration settings.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Registration settings</p>
        <h1>Control the signup gate without changing existing accounts.</h1>
        <p>Email verification remains required. Phone verification can be enabled for future registrations when the additional identity signal justifies the signup friction and SMS cost.</p>
      </article>

      <RetryNotice message={error} onRetry={() => setReloadKey((value) => value + 1)} className="table-span-full" />

      <article className="glass-panel admin-form-panel table-span-full">
        <div className="table-header">
          <div>
            <p className="eyebrow">New account verification</p>
            <h3>Require phone verification at signup</h3>
          </div>
          <span className={`status-pill ${phoneVerificationRequired ? "status-pill-warn" : "status-pill-good"}`}>
            {loading ? "Loading…" : phoneVerificationRequired ? "Required" : "Not required"}
          </span>
        </div>
        <p>Changing this setting only affects registrations completed after the change. Existing phone numbers and existing account access are left untouched.</p>
        <label className="admin-toggle">
          <input
            type="checkbox"
            checked={phoneVerificationRequired}
            onChange={(event) => setPhoneVerificationRequired(event.target.checked)}
            disabled={!settings || submitting}
          />
          <span>Ask new users for a phone number and OTP before account creation</span>
        </label>
        <div className="inline-actions">
          <button className="button button-primary" type="button" onClick={handleSave} disabled={!settings || submitting}>
            {submitting ? "Saving..." : "Save signup setting"}
          </button>
          <span className="inline-note">Last updated: {formatDate(settings?.updatedAtUtc)}</span>
        </div>
        {success ? <p className="status-message status-success" role="status" aria-live="polite">{success}</p> : null}
      </article>
    </div>
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
        <h1>Monitor the systems that keep Phantom available.</h1>
        <p>
          Review user activity, provider readiness, email delivery, payments, and support from one operational workspace.
        </p>
      </article>

      <MetricCard label="Live sessions" value={String(overview?.activeSessionCount ?? 0)} />
      <MetricCard label="Ledger entries" value={String(overview?.ledgerEntryCount ?? 0)} />
      <MetricCard label="Downloads" value={String(overview?.downloadCount ?? 0)} />
      <MetricCard label="30-day downloads" value={String(overview?.downloadsLast30Days ?? 0)} />
      <MetricCard label="Unique downloaders" value={String(overview?.uniqueDownloaderCount ?? 0)} />
      <MetricCard label="Windows downloads" value={String(overview?.windowsDownloadCount ?? 0)} />
      <MetricCard label="macOS downloads" value={String(overview?.macosDownloadCount ?? 0)} />
      <MetricCard label="Published reviews" value={String(overview?.publishedReviewCount ?? 0)} />
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
        {gmailMessage ? <p className={`status-message ${gmailMessage.toLowerCase().includes("could not") ? "status-error" : ""}`} role="status" aria-live="polite">{gmailMessage}</p> : null}
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

function isManagedChatEligibleModel(model) {
  return model?.eligibleForChat !== false;
}

function getChatEligibleCatalogProviders(catalogProviders) {
  return (catalogProviders || [])
    .map((provider) => ({
      ...provider,
      models: (provider.models || []).filter(isManagedChatEligibleModel)
    }))
    .filter((provider) => provider.models.length > 0);
}

function ManagedRuntimeSelectionCard({
  accessToken,
  inventory,
  onRefresh,
  updateSelection = updateManagedAiRuntimeSelection,
  className = "glass-panel admin-form-panel",
  title = "Choose the provider and model used for managed users",
  description,
  showManageLink = false,
  eyebrow = "Managed runtime selection"
}) {
  const [selectionProviderId, setSelectionProviderId] = useState("");
  const [selectionModelId, setSelectionModelId] = useState("");
  const [savingSelection, setSavingSelection] = useState(false);
  const [localError, setLocalError] = useState("");
  const [success, setSuccess] = useState("");

  const catalogProviders = inventory?.catalogs?.providers || [];
  const chatEligibleProviders = getChatEligibleCatalogProviders(catalogProviders);
  const currentSelection = inventory?.selection || null;

  useEffect(() => {
    const eligibleProviders = getChatEligibleCatalogProviders(catalogProviders);
    const catalogProviderIds = eligibleProviders.map((item) => item.providerId);
    const nextProviderId =
      (currentSelection?.providerId && catalogProviderIds.includes(currentSelection.providerId)
        ? currentSelection.providerId
        : catalogProviderIds[0]) || "";
    setSelectionProviderId(nextProviderId);

    const nextProvider = eligibleProviders.find((item) => item.providerId === nextProviderId);
    const nextModelId =
      (currentSelection?.providerId === nextProviderId &&
      nextProvider?.models?.some((model) => model.modelId === currentSelection?.modelId)
        ? currentSelection.modelId
        : nextProvider?.models?.[0]?.modelId) || "";
    setSelectionModelId(nextModelId);
  }, [catalogProviders, currentSelection?.modelId, currentSelection?.providerId]);

  const selectedCatalogProvider = chatEligibleProviders.find((item) => item.providerId === selectionProviderId);
  const selectedCatalogModels = selectedCatalogProvider?.models || [];

  function handleSelectionProviderChange(nextProviderId) {
    setSelectionProviderId(nextProviderId);
    const nextProvider = chatEligibleProviders.find((item) => item.providerId === nextProviderId);
    setSelectionModelId(nextProvider?.models?.[0]?.modelId || "");
  }

  async function handleSelectionSave(event) {
    event.preventDefault();
    setSavingSelection(true);
    setLocalError("");
    setSuccess("");
    try {
      await updateSelection(accessToken, {
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

  return (
    <article className={className}>
      <div className="table-header">
        <div>
          <p className="eyebrow">{eyebrow}</p>
          <h3>{title}</h3>
        </div>
        {showManageLink ? (
          <Link className="button button-secondary button-compact" to="/admin/managed-ai">
            Open Managed AI
          </Link>
        ) : null}
      </div>
      <p>
        {description ||
          "Premium and other managed lanes consume this selection as the global active hosted runtime. The desktop app should no longer expose provider or model switching for managed users."}
      </p>
      {localError ? <p className="status-message status-error" role="alert">{localError}</p> : null}
      {success ? <p className="status-message" role="status" aria-live="polite">{success}</p> : null}
      {chatEligibleProviders.length === 0 ? (
        <p>
          No managed model catalog is available yet. Add at least one managed credential and refresh models from the
          Managed AI page before setting the runtime.
        </p>
      ) : (
        <form className="admin-form" onSubmit={handleSelectionSave}>
          <div className="admin-form-inline">
            <label>
              Active provider
              <select value={selectionProviderId} onChange={(event) => handleSelectionProviderChange(event.target.value)}>
                {chatEligibleProviders.map((provider) => (
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
      )}
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
  );
}

function KnowledgeBaseEmbeddingConfigCard({ accessToken, inventory, onRefresh }) {
  const kbEmbedding = inventory?.kbEmbedding || null;
  const [isEnabled, setIsEnabled] = useState(true);
  const [providerId, setProviderId] = useState("openai");
  const [baseUrl, setBaseUrl] = useState("https://api.openai.com/v1");
  const [modelId, setModelId] = useState("text-embedding-3-small");
  const [dimensions, setDimensions] = useState("1536");
  const [version, setVersion] = useState("1");
  const [batchSize, setBatchSize] = useState("32");
  const [apiKey, setApiKey] = useState("");
  const [saving, setSaving] = useState(false);
  const [localError, setLocalError] = useState("");
  const [success, setSuccess] = useState("");

  useEffect(() => {
    setIsEnabled(kbEmbedding?.isEnabled ?? true);
    setProviderId(kbEmbedding?.providerId || "openai");
    setBaseUrl(kbEmbedding?.baseUrl || "https://api.openai.com/v1");
    setModelId(kbEmbedding?.modelId || "text-embedding-3-small");
    setDimensions(String(kbEmbedding?.dimensions || 1536));
    setVersion(String(kbEmbedding?.version || 1));
    setBatchSize(String(kbEmbedding?.batchSize || 32));
    setApiKey("");
  }, [
    kbEmbedding?.baseUrl,
    kbEmbedding?.batchSize,
    kbEmbedding?.dimensions,
    kbEmbedding?.isEnabled,
    kbEmbedding?.modelId,
    kbEmbedding?.providerId,
    kbEmbedding?.version
  ]);

  async function handleSubmit(event) {
    event.preventDefault();
    setSaving(true);
    setLocalError("");
    setSuccess("");
    try {
      const parsedDimensions = parseRequiredInteger(dimensions, "Embedding dimensions", 1, 16000);
      const parsedVersion = parseRequiredInteger(version, "Embedding version", 1, 1000000);
      const parsedBatchSize = parseRequiredInteger(batchSize, "Embedding batch size", 1, 256);
      await updateKnowledgeBaseEmbeddingConfig(accessToken, {
        isEnabled,
        providerId,
        baseUrl,
        modelId,
        dimensions: parsedDimensions,
        version: parsedVersion,
        batchSize: parsedBatchSize,
        apiKey
      });
      setApiKey("");
      await onRefresh();
      setSuccess("Knowledge-base embedding config updated.");
    } catch (saveError) {
      setLocalError(saveError.message || "Could not update knowledge-base embedding config.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <article className="glass-panel admin-form-panel">
      <div className="table-header">
        <div>
          <p className="eyebrow">Knowledge-base retrieval</p>
          <h3>Embedding profile</h3>
        </div>
      </div>
      <p>
        This controls the embedding model used for hosted knowledge-base indexing and semantic search. It is
        intentionally separate from the managed chat runtime so provider or pricing changes on chat do not force KB
        rework.
      </p>
      {localError ? <p className="status-message status-error" role="alert">{localError}</p> : null}
      {success ? <p className="status-message" role="status" aria-live="polite">{success}</p> : null}
      <form className="admin-form" onSubmit={handleSubmit}>
        <label className="admin-toggle">
          <input type="checkbox" checked={isEnabled} onChange={(event) => setIsEnabled(event.target.checked)} />
          <span>Enable semantic indexing and vector search</span>
        </label>
        <div className="admin-form-inline">
          <label>
            Provider
            <input
              value={providerId}
              onChange={(event) => setProviderId(event.target.value)}
              placeholder="openai, mistral, openai-compatible"
            />
          </label>
          <label>
            Base URL
            <input value={baseUrl} onChange={(event) => setBaseUrl(event.target.value)} placeholder="https://api.openai.com/v1" />
          </label>
        </div>
        <div className="admin-form-inline">
          <label>
            Model
            <input value={modelId} onChange={(event) => setModelId(event.target.value)} placeholder="text-embedding-3-small" />
          </label>
          <label>
            Dimensions
            <input type="number" min="1" max="16000" step="1" required value={dimensions} onChange={(event) => setDimensions(event.target.value)} inputMode="numeric" />
          </label>
        </div>
        <div className="admin-form-inline">
          <label>
            Version
            <input type="number" min="1" max="1000000" step="1" required value={version} onChange={(event) => setVersion(event.target.value)} inputMode="numeric" />
          </label>
          <label>
            Batch size
            <input type="number" min="1" max="256" step="1" required value={batchSize} onChange={(event) => setBatchSize(event.target.value)} inputMode="numeric" />
          </label>
        </div>
        <label>
          API key
          <textarea
            rows={3}
            value={apiKey}
            onChange={(event) => setApiKey(event.target.value)}
            placeholder={kbEmbedding?.hasApiKey ? "Leave blank to keep the stored key" : "Paste embedding API key"}
          />
        </label>
        <button className="button button-primary" type="submit" disabled={saving}>
          {saving ? "Saving..." : "Save Embedding Profile"}
        </button>
      </form>
      <p>
        Storage now supports variable embedding dimensions. Set the provider, model, and exact output dimensions for that
        model, then bump the version and trigger KB reindexing when you switch embedding profiles. The endpoint still
        needs to expose an OpenAI-compatible `/embeddings` API.
      </p>
      <div className="stack-list">
        <InfoRow label="Status" value={kbEmbedding?.isConfigured ? "Configured" : kbEmbedding?.isEnabled ? "Missing key or invalid profile" : "Disabled"} />
        <InfoRow label="Stored key" value={kbEmbedding?.hasApiKey ? "Present" : "Missing"} />
        <InfoRow label="Config source" value={kbEmbedding?.configSource || "unknown"} />
        <InfoRow label="Updated" value={formatDate(kbEmbedding?.updatedAtUtc)} />
      </div>
    </article>
  );
}

function ManagedAiTesterCard({ accessToken, catalogProviders, latencyModels, onRefresh }) {
  const [providerId, setProviderId] = useState("");
  const [modelId, setModelId] = useState("");
  const [prompt, setPrompt] = useState("");
  const [history, setHistory] = useState([]);
  const [submitting, setSubmitting] = useState(false);
  const [localError, setLocalError] = useState("");

  const latencyByModelKey = useMemo(
    () =>
      new Map(
        (latencyModels || []).map((item) => [
          `${item.providerId}::${item.modelId}`,
          item
        ])
      ),
    [latencyModels]
  );

  const eligibleProviders = useMemo(
    () =>
      (catalogProviders || [])
        .map((provider) => ({
          ...provider,
          models: (provider.models || []).filter((model) => latencyByModelKey.get(`${provider.providerId}::${model.modelId}`)?.isChatCapable !== false)
        }))
        .filter((provider) => provider.models.length > 0),
    [catalogProviders, latencyByModelKey]
  );

  useEffect(() => {
    if (eligibleProviders.length === 0) {
      setProviderId("");
      setModelId("");
      return;
    }

    const nextProvider =
      eligibleProviders.find((provider) => provider.providerId === providerId) || eligibleProviders[0];
    const nextModel =
      nextProvider.models.find((model) => model.modelId === modelId) || nextProvider.models[0];

    if (nextProvider.providerId !== providerId) {
      setProviderId(nextProvider.providerId);
      setHistory([]);
    }

    if (nextModel?.modelId !== modelId) {
      setModelId(nextModel?.modelId || "");
      setHistory([]);
    }
  }, [eligibleProviders, modelId, providerId]);

  const selectedProvider = eligibleProviders.find((provider) => provider.providerId === providerId) || null;
  const selectedModel = selectedProvider?.models?.find((model) => model.modelId === modelId) || null;
  const selectedLatency = latencyByModelKey.get(`${providerId}::${modelId}`) || null;

  function handleProviderChange(nextProviderId) {
    const nextProvider = eligibleProviders.find((provider) => provider.providerId === nextProviderId);
    setProviderId(nextProviderId);
    setModelId(nextProvider?.models?.[0]?.modelId || "");
    setHistory([]);
    setLocalError("");
  }

  async function handleSubmit(event) {
    event.preventDefault();
    const trimmedPrompt = prompt.trim();
    if (!trimmedPrompt || !providerId || !modelId) {
      return;
    }

    const nextUserEntry = {
      role: "user",
      content: trimmedPrompt,
      contextEligible: true
    };
    const nextHistory = trimAdminTesterHistory([...history, nextUserEntry]);

    setHistory(nextHistory);
    setPrompt("");
    setSubmitting(true);
    setLocalError("");

    try {
      const result = await sendManagedAiAdminTest(accessToken, {
        providerId,
        modelId,
        messages: nextHistory
          .filter((item) => item.contextEligible !== false)
          .map((item) => ({
            role: item.role,
            content: item.content
          }))
      });

      const assistantEntry = {
        role: "assistant",
        content: result.status === "ok" ? result.outputText : result.errorMessage || "Model test failed.",
        contextEligible: result.status === "ok",
        status: result.status,
        latencyMs: result.latencyMs
      };
      setHistory((current) => trimAdminTesterHistory([...current, assistantEntry]));
      await onRefresh();
    } catch (error) {
      setLocalError(error.message || "Could not run the admin model test.");
      setHistory((current) =>
        trimAdminTesterHistory([
          ...current,
          {
            role: "assistant",
            content: error.message || "Could not run the admin model test.",
            contextEligible: false,
            status: "failed",
            latencyMs: 0
          }
        ])
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <article className="glass-panel admin-form-panel table-span-full">
      <div className="table-header">
        <div>
          <p className="eyebrow">Admin model tester</p>
          <h3>Check one fetched model without touching the premium runtime</h3>
        </div>
      </div>
      <p>
        This uses the managed pipeline directly but keeps the global runtime selection unchanged. Models already marked
        as not chat-capable stay out of this tester.
      </p>
      {localError ? <p className="status-message status-error" role="alert">{localError}</p> : null}
      {eligibleProviders.length === 0 ? (
        <p>No chat-capable test candidates are available yet. Run latency checks or refresh the provider catalog first.</p>
      ) : (
        <>
          <div className="admin-form-inline">
            <label>
              Provider
              <select value={providerId} onChange={(event) => handleProviderChange(event.target.value)}>
                {eligibleProviders.map((provider) => (
                  <option key={provider.providerId} value={provider.providerId}>
                    {provider.label}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Model
              <select value={modelId} onChange={(event) => { setModelId(event.target.value); setHistory([]); setLocalError(""); }}>
                {(selectedProvider?.models || []).map((model) => (
                  <option key={model.modelId} value={model.modelId}>
                    {model.displayName}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <div className="stack-list">
            <InfoRow label="Selected model" value={selectedModel?.displayName || "n/a"} />
            <InfoRow label="Last status" value={formatManagedAiLatencyStatus(selectedLatency?.status)} />
            <InfoRow
              label="Last latency"
              value={selectedLatency?.latencyMs ? `${selectedLatency.latencyMs} ms` : "Not measured"}
            />
          </div>

          <div className="admin-chat-log">
            {history.length === 0 ? (
              <p className="admin-chat-empty">Send a prompt to verify the selected model through the managed pipeline.</p>
            ) : (
              history.map((entry, index) => (
                <div
                  className={`admin-chat-bubble admin-chat-${entry.role} ${entry.role === "assistant" && entry.status && entry.status !== "ok" ? "admin-chat-error" : ""}`}
                  key={`${entry.role}-${index}`}
                >
                  <strong>{entry.role === "user" ? "Admin" : "Model"}</strong>
                  <p>{entry.content}</p>
                  {entry.role === "assistant" && entry.latencyMs ? (
                    <small>{entry.status === "ok" ? `${entry.latencyMs} ms` : formatManagedAiLatencyStatus(entry.status)}</small>
                  ) : null}
                </div>
              ))
            )}
          </div>

          <form className="admin-form" onSubmit={handleSubmit}>
            <label>
              Prompt
              <textarea
                rows={4}
                value={prompt}
                onChange={(event) => setPrompt(event.target.value)}
                placeholder="Ask the selected model to answer a small prompt."
              />
            </label>
            <button className="button button-primary" type="submit" disabled={submitting || !prompt.trim()}>
              {submitting ? "Testing..." : "Send Test Prompt"}
            </button>
          </form>
        </>
      )}
    </article>
  );
}

function ManagedAiLatencyPanel({ accessToken, latencyStatus, onRefresh }) {
  const [submitting, setSubmitting] = useState(false);
  const [localError, setLocalError] = useState("");
  const [success, setSuccess] = useState("");

  const latestRun = latencyStatus?.latestRun || null;
  const models = latencyStatus?.models || [];

  async function handleCheckLatency() {
    setSubmitting(true);
    setLocalError("");
    setSuccess("");
    try {
      const run = await triggerManagedAiLatencyCheck(accessToken);
      await onRefresh();
      setSuccess(run?.status === "queued" ? "Latency check queued." : "Latency check is already running.");
    } catch (error) {
      setLocalError(error.message || "Could not start the latency check.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <>
      <article className="glass-panel admin-form-panel table-span-full">
        <div className="table-header">
          <div>
            <p className="eyebrow">Managed latency checks</p>
            <h3>Probe fetched models through the pipeline</h3>
          </div>
          <button className="button button-primary button-compact" type="button" onClick={handleCheckLatency} disabled={submitting}>
            {submitting ? "Queueing..." : "Check Latency"}
          </button>
        </div>
        <p>
          Each run sends a tiny chat probe through the Windows backend pipeline. Any model that takes more than 20
          seconds is marked as timeout. Non chat-capable models stay visible here and stay hidden from the tester.
        </p>
        {localError ? <p className="status-message status-error" role="alert">{localError}</p> : null}
        {success ? <p className="status-message" role="status" aria-live="polite">{success}</p> : null}
        <div className="stack-list">
          <InfoRow label="Latest run" value={latestRun ? formatManagedAiLatencyStatus(latestRun.status) : "No run yet"} />
          <InfoRow label="Processed" value={latestRun ? `${latestRun.processedModels}/${latestRun.totalModels}` : "0/0"} />
          <InfoRow label="Requested" value={formatDate(latestRun?.requestedAtUtc)} />
          <InfoRow label="Completed" value={formatDate(latestRun?.completedAtUtc)} />
        </div>
      </article>

      <DataTable
        title="Latency status by fetched model"
        scrollClassName="bounded-table-scroll"
        columns={["Provider", "Model", "Chat-capable", "Status", "Latency", "Checked", "Detail"]}
        rows={models.map((item) => [
          item.providerLabel,
          item.displayName,
          item.isChatCapable === true ? "Yes" : item.isChatCapable === false ? "No" : "Unknown",
          formatManagedAiLatencyStatus(item.status),
          item.latencyMs ? `${item.latencyMs} ms` : "n/a",
          formatDate(item.checkedAtUtc),
          item.message
        ])}
        emptyLabel="No fetched models are available yet."
      />
    </>
  );
}

function ManagedAiAdminPanel({ accessToken, inventory, latencyStatus, onRefresh, catalogRefreshResult, onCatalogRefreshResult }) {
  const [providerId, setProviderId] = useState("ChatGPT");
  const [label, setLabel] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [priority, setPriority] = useState("0");
  const [isEnabled, setIsEnabled] = useState(true);
  const [manualProviderId, setManualProviderId] = useState("ChatGPT");
  const [manualModelId, setManualModelId] = useState("");
  const [manualDisplayName, setManualDisplayName] = useState("");
  const [manualSupportsVision, setManualSupportsVision] = useState(false);
  const [manualEligibleForChat, setManualEligibleForChat] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [submittingManualModel, setSubmittingManualModel] = useState(false);
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

  useEffect(() => {
    if (providers.length > 0 && !providers.some((item) => item.providerId === manualProviderId)) {
      setManualProviderId(providers[0].providerId);
    }
  }, [manualProviderId, providers]);

  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitting(true);
    setLocalError("");
    setSuccess("");
    try {
      const parsedPriority = parseRequiredInteger(priority, "Credential priority", 0, 1000);
      await upsertManagedAiCredential(accessToken, {
        providerId,
        label,
        apiKey,
        isEnabled,
        priority: parsedPriority
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
    if (!window.confirm("Remove this managed AI credential? Requests may fail if no healthy fallback remains.")) {
      return;
    }

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

  async function handleModelFlagsToggle(nextProviderId, modelId, patch, successMessage) {
    setLocalError("");
    setSuccess("");
    try {
      await updateManagedAiModelFlags(accessToken, {
        providerId: nextProviderId,
        modelId,
        ...patch
      });
      await onRefresh();
      setSuccess(successMessage);
    } catch (toggleError) {
      setLocalError(toggleError.message || "Could not update model flags.");
    }
  }

  async function handleManualModelSubmit(event) {
    event.preventDefault();
    setSubmittingManualModel(true);
    setLocalError("");
    setSuccess("");
    try {
      await upsertManagedAiCatalogModel(accessToken, {
        providerId: manualProviderId,
        modelId: manualModelId.trim(),
        displayName: manualDisplayName.trim() || manualModelId.trim(),
        supportsVision: manualSupportsVision,
        eligibleForChat: manualEligibleForChat
      });
      setManualModelId("");
      setManualDisplayName("");
      setManualSupportsVision(false);
      setManualEligibleForChat(true);
      setSuccess("Catalog model saved.");
      await onRefresh();
    } catch (saveError) {
      setLocalError(saveError.message || "Could not add catalog model.");
    } finally {
      setSubmittingManualModel(false);
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

      <ManagedRuntimeSelectionCard accessToken={accessToken} inventory={inventory} onRefresh={onRefresh} />
      <KnowledgeBaseEmbeddingConfigCard accessToken={accessToken} inventory={inventory} onRefresh={onRefresh} />
      <ManagedAiTesterCard
        accessToken={accessToken}
        catalogProviders={catalogProviders}
        latencyModels={latencyStatus?.models || []}
        onRefresh={onRefresh}
      />
      <ManagedAiLatencyPanel accessToken={accessToken} latencyStatus={latencyStatus} onRefresh={onRefresh} />

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

        {localError ? <p className="status-message status-error" role="alert">{localError}</p> : null}
        {success ? <p className="status-message" role="status" aria-live="polite">{success}</p> : null}

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
              <input type="number" min="0" max="1000" step="1" required value={priority} onChange={(event) => setPriority(event.target.value)} />
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

      <article className="glass-panel admin-form-panel">
        <div className="table-header">
          <div>
            <p className="eyebrow">Manual catalog entry</p>
            <h3>Add model manually</h3>
          </div>
        </div>
        <p>Use this when a provider omits a model from refresh, or when you need to seed chat eligibility and vision flags before the next catalog sync.</p>
        <form className="admin-form" onSubmit={handleManualModelSubmit}>
          <label>
            Provider
            <select value={manualProviderId} onChange={(event) => setManualProviderId(event.target.value)}>
              {providers.map((provider) => (
                <option key={provider.providerId} value={provider.providerId}>
                  {provider.label}
                </option>
              ))}
            </select>
          </label>
          <label>
            Model ID
            <input
              required
              value={manualModelId}
              onChange={(event) => setManualModelId(event.target.value)}
              placeholder="provider-model-id"
            />
          </label>
          <label>
            Display name
            <input
              value={manualDisplayName}
              onChange={(event) => setManualDisplayName(event.target.value)}
              placeholder="Friendly label shown to admins"
            />
          </label>
          <div className="admin-form-inline">
            <label className="admin-toggle">
              <input
                type="checkbox"
                checked={manualSupportsVision}
                onChange={(event) => setManualSupportsVision(event.target.checked)}
              />
              <span>Supports vision</span>
            </label>
            <label className="admin-toggle">
              <input
                type="checkbox"
                checked={manualEligibleForChat}
                onChange={(event) => setManualEligibleForChat(event.target.checked)}
              />
              <span>Eligible for chat</span>
            </label>
          </div>
          <button
            className="button button-primary"
            type="submit"
            disabled={submittingManualModel || !manualProviderId || !manualModelId.trim()}
          >
            {submittingManualModel ? "Saving..." : "Add Model"}
          </button>
        </form>
      </article>

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
            <TableScroll className="bounded-table-scroll">
              <table>
                <thead>
                  <tr>
                    <th>Model ID</th>
                    <th>Display name</th>
                    <th>Chat eligible</th>
                    <th>Vision</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {providerModels.length === 0 ? (
                    <tr>
                      <td colSpan="5">No stored catalog for this provider yet.</td>
                    </tr>
                  ) : (
                    providerModels.map((model) => {
                      const chatEligible = model.eligibleForChat !== false;
                      return (
                        <tr key={`${provider.providerId}-${model.modelId}`}>
                          <td>{model.modelId}</td>
                          <td>{model.displayName}</td>
                          <td>{chatEligible ? "Yes" : "No"}</td>
                          <td>{model.supportsVision ? "Yes" : "No"}</td>
                          <td>
                            <div className="inline-actions">
                              <button
                                className="table-action"
                                type="button"
                                onClick={() =>
                                  handleModelFlagsToggle(
                                    provider.providerId,
                                    model.modelId,
                                    { eligibleForChat: !chatEligible },
                                    "Model chat eligibility updated."
                                  )
                                }
                              >
                                Mark {chatEligible ? "Chat-Ineligible" : "Chat-Eligible"}
                              </button>
                              <button
                                className="table-action"
                                type="button"
                                onClick={() =>
                                  handleModelFlagsToggle(
                                    provider.providerId,
                                    model.modelId,
                                    { supportsVision: !model.supportsVision },
                                    "Model vision support updated."
                                  )
                                }
                              >
                                Mark {model.supportsVision ? "Non-Vision" : "Vision"}
                              </button>
                            </div>
                          </td>
                        </tr>
                      );
                    })
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

function ManagedSpeechAdminPanel({ accessToken, inventory, onRefresh, catalogRefreshResult, onCatalogRefreshResult }) {
  const providers = inventory?.managedProviders || [];
  const credentials = inventory?.credentials || [];
  const catalogs = inventory?.catalogs?.providers || [];
  const [providerId, setProviderId] = useState(providers[0]?.providerId || "ChatGPT");
  const [label, setLabel] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [priority, setPriority] = useState("0");
  const [isEnabled, setIsEnabled] = useState(true);
  const [busy, setBusy] = useState("");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  useEffect(() => {
    if (providers.length && !providers.some((item) => item.providerId === providerId)) setProviderId(providers[0].providerId);
  }, [providerId, providers]);

  async function saveCredential(event) {
    event.preventDefault(); setBusy("save"); setError(""); setMessage("");
    try {
      await upsertManagedSpeechCredential(accessToken, {
        providerId, label, apiKey, isEnabled,
        priority: parseRequiredInteger(priority, "Credential priority", 0, 1000)
      });
      setLabel(""); setApiKey(""); setPriority("0");
      setMessage("Speech credential saved.");
      await onRefresh();
    } catch (nextError) { setError(nextError.message || "Could not save speech credential."); }
    finally { setBusy(""); }
  }

  async function refreshCatalog() {
    setBusy("refresh"); setError(""); setMessage("");
    try {
      const result = await triggerManagedSpeechCatalogRefresh(accessToken);
      onCatalogRefreshResult(result); await onRefresh(); setMessage("Speech model catalog refreshed.");
    } catch (nextError) { setError(nextError.message || "Could not refresh speech models."); }
    finally { setBusy(""); }
  }

  async function removeCredential(credentialId) {
    if (!window.confirm("Remove this managed speech credential?")) return;
    setError(""); setMessage("");
    try { await deleteManagedSpeechCredential(accessToken, credentialId); await onRefresh(); setMessage("Speech credential removed."); }
    catch (nextError) { setError(nextError.message || "Could not remove speech credential."); }
  }

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Managed speech recognition</p>
        <h1>Choose one global speech model for Premium accounts.</h1>
        <p>Premium audio uses backend-managed credentials and automatically returns to native recognition if this service is unavailable. BYO clients use the same dynamic catalog with their own local keys.</p>
      </article>

      <ManagedRuntimeSelectionCard
        accessToken={accessToken}
        inventory={inventory}
        onRefresh={onRefresh}
        updateSelection={updateManagedSpeechRuntimeSelection}
        title="Choose the provider and model used for Premium speech"
        eyebrow="Global speech recognizer"
      />

      <article className="glass-panel admin-form-panel">
        <div className="table-header">
          <div><p className="eyebrow">Speech credentials</p><h3>Provider rotation</h3></div>
          <div className="inline-actions">
            <button className="button button-secondary button-compact" type="button" onClick={onRefresh}>Refresh</button>
            <button className="button button-primary button-compact" type="button" onClick={refreshCatalog} disabled={busy === "refresh"}>{busy === "refresh" ? "Updating..." : "Update Models"}</button>
          </div>
        </div>
        {error ? <p className="status-message status-error" role="alert">{error}</p> : null}
        {message ? <p className="status-message" role="status">{message}</p> : null}
        <form className="admin-form" onSubmit={saveCredential}>
          <label>Provider<select value={providerId} onChange={(event) => setProviderId(event.target.value)}>{providers.map((provider) => <option key={provider.providerId} value={provider.providerId}>{provider.label}</option>)}</select></label>
          <label>Label<input value={label} onChange={(event) => setLabel(event.target.value)} placeholder="Primary / backup" /></label>
          <label>API key<textarea rows={4} required value={apiKey} onChange={(event) => setApiKey(event.target.value)} placeholder="Paste backend-managed speech key" /></label>
          <div className="admin-form-inline">
            <label>Priority<input type="number" min="0" max="1000" required value={priority} onChange={(event) => setPriority(event.target.value)} /></label>
            <label className="admin-toggle"><input type="checkbox" checked={isEnabled} onChange={(event) => setIsEnabled(event.target.checked)} /><span>Enabled for rotation</span></label>
          </div>
          <button className="button button-primary" type="submit" disabled={busy === "save"}>{busy === "save" ? "Saving..." : "Add Speech Credential"}</button>
        </form>
      </article>

      <DataTable
        title="Speech provider status"
        columns={["Provider", "Enabled creds", "Fetched models", "Catalog refreshed", "Last refresh outcome"]}
        rows={providers.map((provider) => {
          const catalog = catalogs.find((item) => item.providerId === provider.providerId);
          const result = catalogRefreshResult?.providers?.find((item) => item.providerId === provider.providerId);
          return [provider.label, credentials.filter((item) => item.providerId === provider.providerId && item.isEnabled).length, catalog?.models?.length || 0, formatDate(catalog?.refreshedAtUtc), result?.message || "No refresh run in this session."];
        })}
      />
      <DataTable
        title="Managed speech credentials"
        columns={["Provider", "Label", "Priority", "Status", "Updated", "Action"]}
        rows={credentials.length ? credentials.map((item) => [item.providerId, item.label, item.priority, item.isEnabled ? "Enabled" : "Disabled", formatDate(item.updatedAtUtc), <button className="table-action" type="button" onClick={() => removeCredential(item.credentialId)}>Remove</button>]) : null}
        emptyLabel="No managed speech credentials configured yet."
      />
      {catalogs.map((provider) => (
        <DataTable key={provider.providerId} title={`${provider.label} speech models`} columns={["Model ID", "Display name"]}
          rows={(provider.models || []).map((model) => [model.modelId, model.displayName])} emptyLabel="No speech models fetched." />
      ))}
    </div>
  );
}

function AdminUsersPanel({ accessToken }) {
  const [query, setQuery] = useState("");
  const deferredQuery = useDeferredValue(query);
  const [usersPage, setUsersPage] = useState({ items: [], page: 1, hasNextPage: false, totalCount: 0 });
  const [selectedUserId, setSelectedUserId] = useState("");
  const [selectedUser, setSelectedUser] = useState(null);
  const [ledgerPage, setLedgerPage] = useState({ items: [], page: 1, hasNextPage: false, totalCount: 0 });
  const [accountForm, setAccountForm] = useState({
    accessTier: "free",
    proAvailableCredits: "0",
    premiumAvailableCredits: "0",
    premiumNegativeCredits: "0",
    offlineModeEnabled: false,
    canUseDesktopPowerFeatures: false,
    reason: ""
  });
  const [creditForm, setCreditForm] = useState({
    proCreditsToAdd: "0",
    premiumCreditsToAdd: "0",
    reason: ""
  });
  const [interviewLockReason, setInterviewLockReason] = useState("Admin interview lock clear");
  const [debtWaiverReason, setDebtWaiverReason] = useState("");
  const [manualLockForm, setManualLockForm] = useState(() => ({
    expiresAtLocal: toDateTimeLocal(new Date(Date.now() + 24 * 60 * 60 * 1000)),
    reason: "Temporary account suspension"
  }));
  const [status, setStatus] = useState("");
  const [error, setError] = useState("");
  const [busyAction, setBusyAction] = useState("");
  const [usersLoading, setUsersLoading] = useState(true);

  async function loadUsers(page = 1) {
    setUsersLoading(true);
    setError("");
    try {
      await loadAdminUsersPage(accessToken, page, deferredQuery, setUsersPage);
    } catch (loadError) {
      setError(loadError.message || "Could not load users.");
    } finally {
      setUsersLoading(false);
    }
  }

  useEffect(() => {
    const timer = window.setTimeout(() => {
      loadUsers(1);
    }, 250);

    return () => window.clearTimeout(timer);
  }, [accessToken, deferredQuery]);

  useEffect(() => {
    if (!selectedUserId && (usersPage.items || []).length > 0) {
      setSelectedUserId(usersPage.items[0].userId);
    }
  }, [selectedUserId, usersPage.items]);

  useEffect(() => {
    let cancelled = false;

    async function loadUser() {
      if (!selectedUserId) {
        setSelectedUser(null);
        return;
      }

      try {
        const [detail, nextLedger] = await Promise.all([
          fetchAdminUser(accessToken, selectedUserId),
          fetchAdminUserLedger(accessToken, selectedUserId, { page: 1, pageSize: 10 })
        ]);
        if (!cancelled) {
          setSelectedUser(detail);
          setLedgerPage(nextLedger || { items: [], page: 1, hasNextPage: false, totalCount: 0 });
          setAccountForm({
            accessTier: detail.accessTier || "free",
            proAvailableCredits: String(detail.proAvailableCredits ?? 0),
            premiumAvailableCredits: String(detail.premiumAvailableCredits ?? 0),
            premiumNegativeCredits: String(detail.premiumNegativeCredits ?? 0),
            offlineModeEnabled: Boolean(detail.offlineModeEnabled),
            canUseDesktopPowerFeatures: Boolean(detail.canUseDesktopPowerFeatures),
            reason: ""
          });
          setManualLockForm({
            expiresAtLocal: toDateTimeLocal(detail.isManualLockActive ? detail.manualLockExpiresAtUtc : new Date(Date.now() + 24 * 60 * 60 * 1000)),
            reason: detail.manualLockReason || "Temporary account suspension"
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
    const [nextUsersPage, nextDetail, nextLedger] = await Promise.all([
      fetchAdminUsers(accessToken, { page: usersPage.page || 1, pageSize: 20, query: deferredQuery }),
      nextUserId ? fetchAdminUser(accessToken, nextUserId) : Promise.resolve(null),
      nextUserId ? fetchAdminUserLedger(accessToken, nextUserId, { page: ledgerPage.page || 1, pageSize: 10 }) : Promise.resolve({ items: [] })
    ]);
    setUsersPage(nextUsersPage);
    setSelectedUser(nextDetail);
    setLedgerPage(nextLedger || { items: [], page: 1, hasNextPage: false, totalCount: 0 });
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
      const proAvailableCredits = parseRequiredNonNegativeNumber(accountForm.proAvailableCredits, "Pro credits");
      const premiumAvailableCredits = parseRequiredNonNegativeNumber(accountForm.premiumAvailableCredits, "Premium credits");
      const premiumNegativeCredits = parseRequiredNonNegativeNumber(accountForm.premiumNegativeCredits, "Premium debt");
      if (!window.confirm(`Save account and balance changes for ${selectedUser.email}?`)) {
        return;
      }
      const updated = await updateAdminUser(accessToken, {
        userId: selectedUserId,
        accessTier: accountForm.accessTier,
        proAvailableCredits,
        premiumAvailableCredits,
        premiumNegativeCredits,
        offlineModeEnabled: accountForm.offlineModeEnabled,
        canUseDesktopPowerFeatures: accountForm.canUseDesktopPowerFeatures,
        reason: accountForm.reason
      });
      await syncSelectedUser();
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
      const proCreditsToAdd = parseRequiredNonNegativeNumber(creditForm.proCreditsToAdd, "Pro credit grant", 10000);
      const premiumCreditsToAdd = parseRequiredNonNegativeNumber(creditForm.premiumCreditsToAdd, "Premium credit grant", 10000);
      if (proCreditsToAdd === 0 && premiumCreditsToAdd === 0) {
        throw new Error("Enter at least one positive credit amount.");
      }
      if (!window.confirm(`Grant ${proCreditsToAdd} Pro and ${premiumCreditsToAdd} Premium credits to ${selectedUser.email}?`)) {
        return;
      }
      await grantAdminCredits(accessToken, {
        userId: selectedUserId,
        proCreditsToAdd,
        premiumCreditsToAdd,
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
      if (!window.confirm(`Waive ${selectedUser.premiumNegativeCredits} Premium debt credits for ${selectedUser.email}? This cannot be undone.`)) {
        return;
      }
      await waiveAdminPremiumDebt(accessToken, {
        userId: selectedUserId,
        reason: debtWaiverReason
      });
      await syncSelectedUser();
      setDebtWaiverReason("");
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
      if (!window.confirm(`Clear the active interview lock for ${selectedUser.email}?`)) {
        return;
      }
      await clearAdminLock(accessToken, {
        userId: selectedUserId,
        reason: interviewLockReason
      });
      await syncSelectedUser();
      setStatus("Active lock cleared.");
    } catch (lockError) {
      setError(lockError.message || "Could not clear the active lock.");
    } finally {
      setBusyAction("");
    }
  }

  async function handleManualLock(shouldLock) {
    if (!selectedUserId) {
      return;
    }

    setBusyAction("manual-lock");
    setStatus("");
    setError("");
    try {
      const expiresAtUtc = shouldLock ? new Date(manualLockForm.expiresAtLocal) : null;
      if (shouldLock && (!manualLockForm.expiresAtLocal || Number.isNaN(expiresAtUtc.getTime()))) {
        throw new Error("Choose a valid future lock expiry.");
      }
      if (!window.confirm(`${shouldLock ? "Temporarily lock" : "Remove the account lock for"} ${selectedUser.email}?`)) {
        return;
      }
      await setAdminManualLock(accessToken, {
        userId: selectedUserId,
        expiresAtUtc: shouldLock ? expiresAtUtc.toISOString() : null,
        reason: manualLockForm.reason
      });
      await syncSelectedUser();
      setStatus(shouldLock ? "User temporarily locked and active sessions revoked." : "Temporary account lock removed.");
    } catch (lockError) {
      setError(lockError.message || "Could not update the temporary account lock.");
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

      <RetryNotice message={error} onRetry={() => loadUsers(usersPage.page || 1)} className="table-span-full" />

      <article className="glass-panel admin-form-panel">
        <div className="table-header">
          <div>
            <p className="eyebrow">Directory</p>
            <h3>User roster</h3>
          </div>
          <button className="button button-secondary button-compact" type="button" onClick={() => loadUsers(usersPage.page || 1)} disabled={usersLoading}>
            Refresh
          </button>
        </div>
        <label>
          Search
          <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="email, user ID, or tier" />
        </label>
      </article>

      <DataTable
        loading={usersLoading}
        title="Accounts"
        columns={["User", "Tier", "Power", "Account lock", "Phone", "Detail"]}
        rows={
          (usersPage.items || []).length === 0
            ? null
            : usersPage.items.map((item) => [
                item.email,
                item.planLabel,
                item.canUseDesktopPowerFeatures ? "Enabled" : "Standard",
                item.isManualLockActive ? "Locked" : "Open",
                item.phoneVerified ? "Verified" : "Pending",
                <button className="table-action" type="button" aria-label={`Inspect account ${item.email}`} onClick={() => setSelectedUserId(item.userId)}>
                  Inspect
                </button>
              ])
        }
        emptyLabel="No users matched the current search."
        footer={
          <PaginationBar
            page={usersPage.page || 1}
            hasNextPage={Boolean(usersPage.hasNextPage)}
            totalCount={usersPage.totalCount || 0}
            disabled={usersLoading}
            onPrevious={() => loadUsers(usersPage.page - 1)}
            onNext={() => loadUsers((usersPage.page || 1) + 1)}
          />
        }
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
                  <tr><th>Power features</th><td>{selectedUser.canUseDesktopPowerFeatures ? "Enabled" : "Disabled"}</td></tr>
                  <tr><th>Temporary account lock</th><td>{selectedUser.isManualLockActive ? `Active until ${formatDate(selectedUser.manualLockExpiresAtUtc)}` : "Not active"}</td></tr>
                  <tr><th>Interview lock</th><td>{selectedUser.activeLockSessionId || "No active lock"}</td></tr>
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
                <input type="number" min="0" max="100000" step="0.01" required value={accountForm.proAvailableCredits} onChange={(event) => setAccountForm((current) => ({ ...current, proAvailableCredits: event.target.value }))} />
              </label>
              <label>
                Premium credits
                <input type="number" min="0" max="100000" step="0.01" required value={accountForm.premiumAvailableCredits} onChange={(event) => setAccountForm((current) => ({ ...current, premiumAvailableCredits: event.target.value }))} />
              </label>
              <label>
                Premium debt
                <input type="number" min="0" max="100000" step="0.01" required value={accountForm.premiumNegativeCredits} onChange={(event) => setAccountForm((current) => ({ ...current, premiumNegativeCredits: event.target.value }))} />
              </label>
              <label className="admin-toggle">
                <input type="checkbox" checked={accountForm.offlineModeEnabled} onChange={(event) => setAccountForm((current) => ({ ...current, offlineModeEnabled: event.target.checked }))} />
                <span>Offline mode enabled</span>
              </label>
              <div className="entitlement-control">
                <label className="admin-toggle">
                  <input type="checkbox" checked={accountForm.canUseDesktopPowerFeatures} onChange={(event) => setAccountForm((current) => ({ ...current, canUseDesktopPowerFeatures: event.target.checked }))} />
                  <span>Desktop power features</span>
                </label>
                <p>Shows the Legacy handoff button and its executable-path settings in the Windows app.</p>
              </div>
              <label>
                Reason
                <input value={accountForm.reason} onChange={(event) => setAccountForm((current) => ({ ...current, reason: event.target.value }))} placeholder="Why this manual update is needed" minLength={8} maxLength={500} required />
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
                <input type="number" min="0" max="10000" step="0.01" required value={creditForm.proCreditsToAdd} onChange={(event) => setCreditForm((current) => ({ ...current, proCreditsToAdd: event.target.value }))} />
              </label>
              <label>
                Add Premium credits
                <input type="number" min="0" max="10000" step="0.01" required value={creditForm.premiumCreditsToAdd} onChange={(event) => setCreditForm((current) => ({ ...current, premiumCreditsToAdd: event.target.value }))} />
              </label>
              <label>
                Reason
                <input value={creditForm.reason} onChange={(event) => setCreditForm((current) => ({ ...current, reason: event.target.value }))} placeholder="Promo credit, support fix, manual correction" minLength={8} maxLength={500} required />
              </label>
            </div>
            <button className="button button-primary" type="submit" disabled={busyAction === "credits"}>
              {busyAction === "credits" ? "Applying..." : "Grant Credits"}
            </button>
          </form>

          <article className="glass-panel admin-form-panel">
            <p className="eyebrow">Recovery controls</p>
            <div className="account-lock-indicator">
              <span className={`status-pill ${selectedUser.isManualLockActive ? "status-pill-warn" : "status-pill-good"}`}>
                {selectedUser.isManualLockActive ? "Account temporarily locked" : "Account access open"}
              </span>
              <p>
                {selectedUser.isManualLockActive
                  ? `${selectedUser.manualLockReason || "No reason recorded"} · expires ${formatDate(selectedUser.manualLockExpiresAtUtc)}`
                  : "A temporary lock revokes active sessions and blocks new login until its expiry."}
              </p>
            </div>
            <div className="admin-form">
              <label>
                Temporary lock expiry
                <input type="datetime-local" value={manualLockForm.expiresAtLocal} onChange={(event) => setManualLockForm((current) => ({ ...current, expiresAtLocal: event.target.value }))} />
              </label>
              <label>
                Temporary lock reason
                <input value={manualLockForm.reason} onChange={(event) => setManualLockForm((current) => ({ ...current, reason: event.target.value }))} />
              </label>
              <label>
                Interview lock clear reason
                <input value={interviewLockReason} onChange={(event) => setInterviewLockReason(event.target.value)} minLength={8} maxLength={500} required />
              </label>
              <label>
                Premium debt waiver reason
                <input value={debtWaiverReason} onChange={(event) => setDebtWaiverReason(event.target.value)} placeholder="Why the debt should be waived" minLength={8} maxLength={500} />
              </label>
            </div>
            <div className="inline-actions">
              <button className="button button-danger" type="button" onClick={() => handleManualLock(true)} disabled={busyAction === "manual-lock" || !manualLockForm.expiresAtLocal || !manualLockForm.reason.trim()}>
                {busyAction === "manual-lock" ? "Updating..." : "Temporarily Lock User"}
              </button>
              {selectedUser.isManualLockActive ? (
                <button className="button button-secondary" type="button" onClick={() => handleManualLock(false)} disabled={busyAction === "manual-lock" || manualLockForm.reason.trim().length < 8}>
                  Remove Account Lock
                </button>
              ) : null}
              <button className="button button-secondary" type="button" onClick={handleClearLock} disabled={busyAction === "lock" || interviewLockReason.trim().length < 8 || !selectedUser.activeLockSessionId}>
                {busyAction === "lock" ? "Clearing..." : "Clear Interview Lock"}
              </button>
              <button className="button button-ghost" type="button" onClick={handleWaiveDebt} disabled={busyAction === "waive" || debtWaiverReason.trim().length < 8 || Number(selectedUser.premiumNegativeCredits) <= 0}>
                {busyAction === "waive" ? "Waiving..." : "Waive Premium Debt"}
              </button>
            </div>
          </article>

          <DataTable
            title="Recent ledger entries"
            columns={["Ledger entry", "Session", "Credits", "Debt", "Created"]}
            rows={
              (ledgerPage.items || []).length === 0
                ? null
                : ledgerPage.items.map((item) => [
                    item.ledgerEntryId,
                    item.sessionId,
                    item.chargedCredits,
                    item.addedPremiumDebt,
                    formatDate(item.createdAtUtc)
                  ])
            }
            emptyLabel="No recent ledger activity for this user."
            footer={
              <PaginationBar
                page={ledgerPage.page || 1}
                hasNextPage={Boolean(ledgerPage.hasNextPage)}
                totalCount={ledgerPage.totalCount || 0}
                onPrevious={() => loadAdminUserLedgerPage(accessToken, selectedUserId, ledgerPage.page - 1, setLedgerPage)}
                onNext={() => loadAdminUserLedgerPage(accessToken, selectedUserId, (ledgerPage.page || 1) + 1, setLedgerPage)}
              />
            }
          />
        </>
      ) : null}

      {status ? <article className="glass-panel table-span-full"><p className="status-message" role="status" aria-live="polite">{status}</p></article> : null}
    </div>
  );
}

function AdminPaymentsPanel({ accessToken, overview, onRefreshOverview }) {
  const [paymentOrdersPage, setPaymentOrdersPage] = useState({ items: [], page: 1, hasNextPage: false, totalCount: 0 });
  const [paymentWebhooksPage, setPaymentWebhooksPage] = useState({ items: [], page: 1, hasNextPage: false, totalCount: 0 });
  const [statusFilter, setStatusFilter] = useState("all");
  const [query, setQuery] = useState("");
  const deferredQuery = useDeferredValue(query);
  const [selectedOrder, setSelectedOrder] = useState(null);
  const [selectedWebhook, setSelectedWebhook] = useState(null);
  const [ordersLoading, setOrdersLoading] = useState(true);
  const [webhooksLoading, setWebhooksLoading] = useState(true);
  const [loadError, setLoadError] = useState("");

  async function loadOrders(page = 1) {
    setOrdersLoading(true);
    setLoadError("");
    try {
      const orders = await fetchAdminPaymentOrders(accessToken, { page: Math.max(1, page), pageSize: 20, query: deferredQuery, status: statusFilter });
      setPaymentOrdersPage(orders || { items: [], page: 1, hasNextPage: false, totalCount: 0 });
    } catch (error) {
      setLoadError(error.message || "Could not load payment orders.");
    } finally {
      setOrdersLoading(false);
    }
  }

  async function loadWebhooks(page = 1) {
    setWebhooksLoading(true);
    setLoadError("");
    try {
      const webhooks = await fetchAdminPaymentWebhooks(accessToken, { page: Math.max(1, page), pageSize: 20 });
      setPaymentWebhooksPage(webhooks || { items: [], page: 1, hasNextPage: false, totalCount: 0 });
    } catch (error) {
      setLoadError(error.message || "Could not load payment callbacks.");
    } finally {
      setWebhooksLoading(false);
    }
  }

  async function handleRefresh() {
    setLoadError("");
    setOrdersLoading(true);
    setWebhooksLoading(true);
    try {
      await refreshAdminPayments(accessToken, deferredQuery, statusFilter, setPaymentOrdersPage, setPaymentWebhooksPage, onRefreshOverview);
    } catch (error) {
      setLoadError(error.message || "Could not refresh payment operations.");
    } finally {
      setOrdersLoading(false);
      setWebhooksLoading(false);
    }
  }

  useEffect(() => {
    loadOrders(1);
  }, [accessToken, deferredQuery, statusFilter]);

  useEffect(() => {
    loadWebhooks(1);
  }, [accessToken]);

  const filteredOrders = paymentOrdersPage.items || [];

  const filteredWebhooks = (paymentWebhooksPage.items || []).filter((item) => {
    const haystack = [item.externalEventId, item.eventType, item.payloadJson].join(" ").toLowerCase();
    return !deferredQuery.trim() || haystack.includes(deferredQuery.trim().toLowerCase());
  });

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Payment operations</p>
        <h1>Track checkout, confirmation, and wallet credit application in one place.</h1>
      </article>

      <RetryNotice message={loadError} onRetry={handleRefresh} className="table-span-full" />

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
          <button className="button button-secondary button-compact" type="button" onClick={handleRefresh} disabled={ordersLoading || webhooksLoading}>
            {ordersLoading || webhooksLoading ? "Refreshing…" : "Refresh"}
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
        loading={ordersLoading}
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
                <button className="table-action" type="button" aria-label={`Inspect payment ${item.checkoutId}`} onClick={() => setSelectedOrder(item)}>
                  Inspect
                </button>
              ])
        }
        emptyLabel="No payment orders matched the current filters."
        footer={
          <PaginationBar
            page={paymentOrdersPage.page || 1}
            hasNextPage={Boolean(paymentOrdersPage.hasNextPage)}
            totalCount={paymentOrdersPage.totalCount || 0}
            disabled={ordersLoading}
            onPrevious={() => loadOrders(paymentOrdersPage.page - 1)}
            onNext={() => loadOrders((paymentOrdersPage.page || 1) + 1)}
          />
        }
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
        loading={webhooksLoading}
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
                <button className="table-action" type="button" aria-label={`Inspect webhook ${item.externalEventId}`} onClick={() => setSelectedWebhook(item)}>
                  Inspect
                </button>
              ])
        }
        emptyLabel="No webhook events matched the current filters."
        footer={
          <PaginationBar
            page={paymentWebhooksPage.page || 1}
            hasNextPage={Boolean(paymentWebhooksPage.hasNextPage)}
            totalCount={paymentWebhooksPage.totalCount || 0}
            disabled={webhooksLoading}
            onPrevious={() => loadWebhooks(paymentWebhooksPage.page - 1)}
            onNext={() => loadWebhooks((paymentWebhooksPage.page || 1) + 1)}
          />
        }
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

function AdminTicketsPanel({ accessToken, overview, onRefreshOverview }) {
  const [ticketsPage, setTicketsPage] = useState({ items: [], page: 1, hasNextPage: false, totalCount: 0 });
  const [query, setQuery] = useState("");
  const deferredQuery = useDeferredValue(query);
  const [statusFilter, setStatusFilter] = useState("all");
  const [selectedTicket, setSelectedTicket] = useState(null);
  const [ticketForm, setTicketForm] = useState({
    status: "open",
    priority: "normal",
    adminNotes: "",
    resolutionSummary: ""
  });
  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState("");
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState("");

  async function load(page = 1) {
    setLoading(true);
    setLoadError("");
    try {
      await loadAdminSupportTicketsPage(accessToken, page, deferredQuery, statusFilter, setTicketsPage);
    } catch (error) {
      setLoadError(error.message || "Could not load support tickets.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load(1);
  }, [accessToken, deferredQuery, statusFilter]);

  useEffect(() => {
    if (!selectedTicket && (ticketsPage.items || []).length > 0) {
      const first = ticketsPage.items[0];
      setSelectedTicket(first);
      setTicketForm({
        status: first.status,
        priority: first.priority,
        adminNotes: first.adminNotes || "",
        resolutionSummary: first.resolutionSummary || ""
      });
    }
  }, [selectedTicket, ticketsPage.items]);

  function selectTicket(ticket) {
    setSelectedTicket(ticket);
    setTicketForm({
      status: ticket.status,
      priority: ticket.priority,
      adminNotes: ticket.adminNotes || "",
      resolutionSummary: ticket.resolutionSummary || ""
    });
  }

  async function handleSubmit(event) {
    event.preventDefault();
    if (!selectedTicket) {
      return;
    }

    setBusy(true);
    setFeedback("");
    try {
      const updated = await updateAdminSupportTicket(accessToken, {
        ticketId: selectedTicket.ticketId,
        status: ticketForm.status,
        priority: ticketForm.priority,
        adminNotes: ticketForm.adminNotes,
        resolutionSummary: ticketForm.resolutionSummary
      });
      setSelectedTicket(updated);
      await load(ticketsPage.page || 1);
      await onRefreshOverview();
      setFeedback("Support ticket updated.");
    } catch (error) {
      setFeedback(error.message || "Could not update the support ticket.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="dashboard-grid">
      <article className="glass-panel dashboard-hero table-span-full">
        <p className="eyebrow">Support tickets</p>
        <h1>Handle user-reported issues in the admin plane, not through ad hoc account edits.</h1>
      </article>

      <RetryNotice message={loadError} onRetry={() => load(ticketsPage.page || 1)} className="table-span-full" />

      <MetricCard label="Open tickets" value={String(overview?.openSupportTicketCount ?? 0)} />
      <MetricCard label="Total tickets" value={String(overview?.supportTicketCount ?? 0)} />

      <article className="glass-panel admin-form-panel table-span-full">
        <div className="admin-form">
          <label>
            Status
            <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}>
              <option value="all">All</option>
              <option value="open">Open</option>
              <option value="investigating">Investigating</option>
              <option value="waiting_for_user">Waiting for user</option>
              <option value="resolved">Resolved</option>
              <option value="closed">Closed</option>
            </select>
          </label>
          <label>
            Search
            <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="ticket ID, email, category, or subject" />
          </label>
        </div>
      </article>

      <DataTable
        loading={loading}
        title="Ticket queue"
        columns={["Ticket", "User", "Priority", "Status", "Updated", "Detail"]}
        rows={
          (ticketsPage.items || []).length === 0
            ? null
            : ticketsPage.items.map((item) => [
                item.subject,
                item.email,
                item.priority,
                item.status,
                formatDate(item.updatedAtUtc),
                <button className="table-action" type="button" aria-label={`Inspect support ticket ${item.ticketId}`} onClick={() => selectTicket(item)}>
                  Inspect
                </button>
              ])
        }
        emptyLabel="No support tickets matched the current filters."
        footer={
          <PaginationBar
            page={ticketsPage.page || 1}
            hasNextPage={Boolean(ticketsPage.hasNextPage)}
            totalCount={ticketsPage.totalCount || 0}
            disabled={loading}
            onPrevious={() => load(ticketsPage.page - 1)}
            onNext={() => load((ticketsPage.page || 1) + 1)}
          />
        }
      />

      {selectedTicket ? (
        <>
          <div className="glass-panel table-panel table-span-full">
            <div className="table-header">
              <div>
                <p className="eyebrow">Selected ticket</p>
                <h3>{selectedTicket.ticketId}</h3>
              </div>
            </div>
            <TableScroll>
              <table>
                <tbody>
                  <tr><th>Subject</th><td>{selectedTicket.subject}</td></tr>
                  <tr><th>User</th><td>{selectedTicket.email} · {selectedTicket.userId}</td></tr>
                  <tr><th>Category</th><td>{selectedTicket.category}</td></tr>
                  <tr><th>Priority</th><td>{selectedTicket.priority}</td></tr>
                  <tr><th>Status</th><td>{selectedTicket.status}</td></tr>
                  <tr><th>Created</th><td>{formatDate(selectedTicket.createdAtUtc)}</td></tr>
                  <tr><th>Updated</th><td>{formatDate(selectedTicket.updatedAtUtc)}</td></tr>
                  <tr><th>Description</th><td>{selectedTicket.description}</td></tr>
                </tbody>
              </table>
            </TableScroll>
          </div>

          <form className="glass-panel admin-form-panel table-span-full" onSubmit={handleSubmit}>
            <p className="eyebrow">Handle ticket</p>
            <div className="admin-form">
              <label>
                Status
                <select value={ticketForm.status} onChange={(event) => setTicketForm((current) => ({ ...current, status: event.target.value }))}>
                  <option value="open">Open</option>
                  <option value="investigating">Investigating</option>
                  <option value="waiting_for_user">Waiting for user</option>
                  <option value="resolved">Resolved</option>
                  <option value="closed">Closed</option>
                </select>
              </label>
              <label>
                Priority
                <select value={ticketForm.priority} onChange={(event) => setTicketForm((current) => ({ ...current, priority: event.target.value }))}>
                  <option value="low">Low</option>
                  <option value="normal">Normal</option>
                  <option value="high">High</option>
                  <option value="urgent">Urgent</option>
                </select>
              </label>
              <label className="table-span-full">
                Admin notes
                <textarea rows={4} value={ticketForm.adminNotes} onChange={(event) => setTicketForm((current) => ({ ...current, adminNotes: event.target.value }))} />
              </label>
              <label className="table-span-full">
                Resolution summary
                <textarea rows={3} value={ticketForm.resolutionSummary} onChange={(event) => setTicketForm((current) => ({ ...current, resolutionSummary: event.target.value }))} />
              </label>
            </div>
            <button className="button button-primary" type="submit" disabled={busy}>
              {busy ? "Saving..." : "Save Ticket Update"}
            </button>
            {feedback ? <p className={`status-message ${feedback.toLowerCase().includes("could not") ? "status-error" : ""}`} role="status" aria-live="polite">{feedback}</p> : null}
          </form>
        </>
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

async function loadWalletHistoryPage(accessToken, page, setter) {
  setter(await fetchWalletHistory(accessToken, Math.max(1, page), 12));
}

async function loadWalletPurchasesPage(accessToken, page, setter) {
  setter(await fetchWalletPurchases(accessToken, Math.max(1, page), 12));
}

async function loadDevicesPage(accessToken, page, setter) {
  setter(await fetchDevices(accessToken, Math.max(1, page), 10));
}

async function loadSupportTicketsPage(accessToken, page, setter) {
  setter(await fetchUserSupportTickets(accessToken, Math.max(1, page), 10));
}

async function loadQuestionBanksPage(accessToken, page, setter) {
  setter(await fetchInterviewQuestionBanks(accessToken, Math.max(1, page), 10));
}

async function loadAdminUsersPage(accessToken, page, query, setter) {
  setter(await fetchAdminUsers(accessToken, { page: Math.max(1, page), pageSize: 20, query }));
}

async function loadAdminUserLedgerPage(accessToken, userId, page, setter) {
  if (!userId) {
    setter({ items: [], page: 1, hasNextPage: false, totalCount: 0 });
    return;
  }

  setter(await fetchAdminUserLedger(accessToken, userId, { page: Math.max(1, page), pageSize: 10 }));
}

async function loadAdminPaymentOrdersPage(accessToken, page, query, status, setter) {
  setter(await fetchAdminPaymentOrders(accessToken, { page: Math.max(1, page), pageSize: 20, query, status }));
}

async function loadAdminPaymentWebhooksPage(accessToken, page, setter) {
  setter(await fetchAdminPaymentWebhooks(accessToken, { page: Math.max(1, page), pageSize: 20 }));
}

async function refreshAdminPayments(accessToken, query, status, ordersSetter, webhooksSetter, refreshOverview) {
  const [orders, webhooks] = await Promise.all([
    fetchAdminPaymentOrders(accessToken, { page: 1, pageSize: 20, query, status }),
    fetchAdminPaymentWebhooks(accessToken, { page: 1, pageSize: 20 }),
    refreshOverview()
  ]);
  ordersSetter(orders);
  webhooksSetter(webhooks);
}

async function loadAdminSupportTicketsPage(accessToken, page, query, status, setter) {
  setter(await fetchAdminSupportTickets(accessToken, {
    page: Math.max(1, page),
    pageSize: 20,
    query,
    status
  }));
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
