export type QuotationStatus = "Draft" | "Sent" | "Accepted" | "Rejected" | "Expired";

export const QUOTATION_STATUSES: QuotationStatus[] = [
  "Draft",
  "Sent",
  "Accepted",
  "Rejected",
  "Expired",
];

export interface User {
  id: string;
  email: string;
  fullName: string;
}

export interface AuthResponse {
  accessToken: string;
  expiresAtUtc: string;
  user: User;
}

export interface BusinessProfile {
  id: string;
  businessName: string;
  businessEmail?: string | null;
  phone?: string | null;
  addressLine?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  country?: string | null;
  taxNumber?: string | null;
  logoUrl?: string | null;
  currency: string;
}

export interface Customer {
  id: string;
  name: string;
  companyName?: string | null;
  email?: string | null;
  phone?: string | null;
  addressLine?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  country?: string | null;
  notes?: string | null;
  createdAt: string;
}

export interface Product {
  id: string;
  name: string;
  description?: string | null;
  unit: string;
  price: number;
  taxRate: number;
  createdAt: string;
}

export interface QuotationItem {
  id: string;
  productId?: string | null;
  name: string;
  description?: string | null;
  unit: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  taxRate: number;
  lineSubtotal: number;
  lineTax: number;
  lineTotal: number;
}

export interface QuotationListItem {
  id: string;
  quotationNumber: string;
  customerId: string;
  customerName: string;
  quotationDate: string;
  validUntil: string;
  status: QuotationStatus;
  grandTotal: number;
  currency: string;
  respondedAt?: string | null;
}

export interface Quotation {
  id: string;
  quotationNumber: string;
  customer: Customer;
  business?: BusinessProfile | null;
  quotationDate: string;
  validUntil: string;
  notes?: string | null;
  terms?: string | null;
  status: QuotationStatus;
  subtotal: number;
  discountTotal: number;
  taxTotal: number;
  grandTotal: number;
  currency: string;
  items: QuotationItem[];
  invoiceId?: string | null;
  invoiceNumber?: string | null;
  canConvertToInvoice: boolean;
  hasPublicLink: boolean;
  publicLinkCreatedAt?: string | null;
  respondedAt?: string | null;
  respondedByName?: string | null;
  respondedByEmail?: string | null;
  responseComment?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface DashboardStats {
  totalQuotations: number;
  draftCount: number;
  sentCount: number;
  acceptedCount: number;
  totalValue: number;
  currency: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface SaveQuotationItemRequest {
  productId?: string | null;
  name: string;
  description?: string | null;
  unit: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  taxRate: number;
}

export interface SaveQuotationRequest {
  customerId: string;
  quotationDate: string;
  validUntil: string;
  notes?: string | null;
  terms?: string | null;
  status?: QuotationStatus;
  items: SaveQuotationItemRequest[];
}

// ---- V2.1 customer-facing share link ----------------------------------

export interface PublicQuotationLink {
  url: string;
  createdAt: string;
  /** Ready-made share material, composed server-side while the URL still exists. */
  share: DocumentShare;
}

export interface PublicQuotationItem {
  name: string;
  description?: string | null;
  unit: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  taxRate: number;
  lineTotal: number;
}

export interface PublicQuotationBusiness {
  businessName: string;
  email?: string | null;
  phone?: string | null;
  addressLine?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  country?: string | null;
  taxNumber?: string | null;
  logoUrl?: string | null;
}

export interface PublicQuotationCustomer {
  name: string;
  companyName?: string | null;
  email?: string | null;
  phone?: string | null;
  addressLine?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  country?: string | null;
}

/** Mirrors PublicQuotationDto: no internal identifiers of any kind. */
export interface PublicQuotation {
  quotationNumber: string;
  quotationDate: string;
  validUntil: string;
  business: PublicQuotationBusiness;
  customer: PublicQuotationCustomer;
  items: PublicQuotationItem[];
  subtotal: number;
  discountTotal: number;
  taxTotal: number;
  grandTotal: number;
  currency: string;
  notes?: string | null;
  terms?: string | null;
  status: QuotationStatus;
  isExpired: boolean;
  canRespond: boolean;
  respondedAt?: string | null;
  respondedByName?: string | null;
}

export interface PublicResponseRequest {
  name: string;
  email?: string | null;
  comment?: string | null;
}

// ---- V2.2 invoicing ---------------------------------------------------

export type InvoiceStatus =
  | "Draft"
  | "Sent"
  | "PartiallyPaid"
  | "Paid"
  | "Overdue"
  | "Cancelled";

export const INVOICE_STATUSES: InvoiceStatus[] = [
  "Draft",
  "Sent",
  "PartiallyPaid",
  "Paid",
  "Overdue",
  "Cancelled",
];

export const INVOICE_STATUS_LABELS: Record<InvoiceStatus, string> = {
  Draft: "Draft",
  Sent: "Sent",
  PartiallyPaid: "Partially paid",
  Paid: "Paid",
  Overdue: "Overdue",
  Cancelled: "Cancelled",
};

export interface InvoiceItem {
  id: string;
  name: string;
  description?: string | null;
  unit: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  taxRate: number;
  lineSubtotal: number;
  lineTax: number;
  lineTotal: number;
}

/** The billing details frozen onto the invoice — not the live customer record. */
export interface InvoiceCustomer {
  name: string;
  companyName?: string | null;
  email?: string | null;
  phone?: string | null;
  addressLine?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  country?: string | null;
}

export interface InvoiceListItem {
  id: string;
  invoiceNumber: string;
  customerName: string;
  invoiceDate: string;
  dueDate: string;
  status: InvoiceStatus;
  grandTotal: number;
  currency: string;
  /** Summed from payments that count, by the server. */
  paid: number;
  outstanding: number;
  /** Derived per request from the due date and the balance — never a stored status. */
  isOverdue: boolean;
  /** The source quotation's number, or empty when the invoice was raised directly. */
  quotationNumber: string;
}

export interface Invoice {
  id: string;
  invoiceNumber: string;
  /** Null for an invoice raised directly — the only thing separating the two creation paths. */
  quotationId?: string | null;
  quotationNumber: string;
  customer: InvoiceCustomer;
  business?: BusinessProfile | null;
  invoiceDate: string;
  dueDate: string;
  status: InvoiceStatus;
  isOverdue: boolean;
  canEdit: boolean;
  canEditItems: boolean;
  canDelete: boolean;
  /** Summed from captured payments by the server; never computed in the browser. */
  paid: number;
  outstanding: number;
  hasPublicLink: boolean;
  publicLinkCreatedAt?: string | null;
  notes?: string | null;
  terms?: string | null;
  subtotal: number;
  discountTotal: number;
  taxTotal: number;
  grandTotal: number;
  currency: string;
  items: InvoiceItem[];
  createdAt: string;
  updatedAt: string;
}

export interface SaveInvoiceItemRequest {
  name: string;
  description?: string | null;
  unit: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  taxRate: number;
}

/** V2.4 — direct creation. Totals and the invoice number are the server's to decide. */
export interface CreateInvoiceRequest {
  customerId: string;
  invoiceDate: string;
  /** Optional; the server defaults it to the invoice date plus the standard payment term. */
  dueDate?: string | null;
  notes?: string | null;
  terms?: string | null;
  items: SaveInvoiceItemRequest[];
}

export interface SaveInvoiceRequest {
  invoiceDate: string;
  dueDate: string;
  status?: InvoiceStatus;
  notes?: string | null;
  terms?: string | null;
  /** Only accepted while the invoice is a draft; omitted otherwise. */
  items?: SaveInvoiceItemRequest[];
}

// ---- V2.3 payments ----------------------------------------------------

export type PaymentStatus = "Created" | "Pending" | "Captured" | "Failed" | "Cancelled";

/** Where the money came from. Only a manual payment can be voided. */
export type PaymentSource = "Gateway" | "Manual";

/** The ways money arrives outside the gateway. Must match ManualPaymentMethods on the server. */
export const MANUAL_PAYMENT_METHODS = [
  "cash",
  "bank_transfer",
  "upi",
  "cheque",
  "other",
] as const;

export type ManualPaymentMethod = (typeof MANUAL_PAYMENT_METHODS)[number];

export const MANUAL_PAYMENT_METHOD_LABELS: Record<ManualPaymentMethod, string> = {
  cash: "Cash",
  bank_transfer: "Bank transfer",
  upi: "UPI",
  cheque: "Cheque",
  other: "Other",
};

/** What the browser sends. Note the absence of any total: the server validates against its own. */
export interface RecordManualPaymentRequest {
  amount: number;
  method: ManualPaymentMethod;
  paymentDate: string;
  reference?: string | null;
  notes?: string | null;
}

export interface PaymentSummary {
  total: number;
  paid: number;
  outstanding: number;
  /** Non-zero only when the provider captured more than the invoice total — an anomaly. */
  overpaidBy: number;
  currency: string;
  invoiceStatus: InvoiceStatus;
  canPay: boolean;
  hasPendingPayment: boolean;
}

export interface Payment {
  id: string;
  amount: number;
  currency: string;
  status: PaymentStatus;
  source: PaymentSource;
  provider: string;
  /** The provider's reference for gateway money; the owner's own for a manual payment. */
  reference?: string | null;
  orderReference?: string | null;
  method?: string | null;
  notes?: string | null;
  failureReason?: string | null;
  paidAt?: string | null;
  createdAt: string;
  voidedAt?: string | null;
  isVoided: boolean;
  /** True only for a manual payment that still stands — the one row that offers Void. */
  canVoid: boolean;
}

export interface InvoicePayments {
  summary: PaymentSummary;
  payments: Payment[];
}

export interface PublicInvoiceLink {
  url: string;
  createdAt: string;
  /** Ready-made share material, composed server-side while the URL still exists. */
  share: DocumentShare;
}

/**
 * Deep links for handing a document to a customer — an invoice (V2.4) or a quotation (V2.6).
 * Quotely sends nothing: these open WhatsApp and the owner's own mail client with the message
 * already written, and every figure inside was composed by the server.
 */
export interface DocumentShare {
  url: string;
  message: string;
  emailSubject: string;
  whatsAppUrl: string;
  mailtoUrl: string;
  /** Null when the customer snapshot holds no usable number / email. */
  customerPhone?: string | null;
  customerEmail?: string | null;
}

export interface PublicInvoiceItem {
  name: string;
  description?: string | null;
  unit: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  taxRate: number;
  lineTotal: number;
}

export interface PublicPayment {
  amount: number;
  method?: string | null;
  reference?: string | null;
  paidAt?: string | null;
}

/** Mirrors PublicInvoiceDto: no internal identifiers of any kind. */
export interface PublicInvoice {
  invoiceNumber: string;
  invoiceDate: string;
  dueDate: string;
  business: PublicQuotationBusiness;
  customer: PublicQuotationCustomer;
  items: PublicInvoiceItem[];
  subtotal: number;
  discountTotal: number;
  taxTotal: number;
  grandTotal: number;
  currency: string;
  notes?: string | null;
  terms?: string | null;
  status: InvoiceStatus;
  isOverdue: boolean;
  paid: number;
  outstanding: number;
  canPay: boolean;
  hasPendingPayment: boolean;
  payments: PublicPayment[];
}

/**
 * Everything the browser needs to open checkout. Note the absence of anything the browser could
 * tamper with to change what is charged — the amount here was decided by the server.
 */
export interface PaymentOrder {
  keyId: string;
  orderId: string;
  /** Minor units (paise), as registered with the provider. */
  amount: number;
  currency: string;
  invoiceNumber: string;
  businessName: string;
  customerName?: string | null;
  customerEmail?: string | null;
  customerContact?: string | null;
}

/** The server's verdict. The only thing the page may treat as the truth about a payment. */
export interface VerifyPaymentResponse {
  success: boolean;
  paymentStatus: PaymentStatus;
  invoiceStatus: InvoiceStatus;
  total: number;
  paid: number;
  outstanding: number;
  amountPaid: number;
  currency: string;
  paymentReference?: string | null;
  message?: string | null;
}

// ---- V2.5 receivables --------------------------------------------------

/** What the business is owed, aggregated by the server across every issued invoice. */
export interface Receivables {
  totalOutstanding: number;
  totalOverdue: number;
  countOutstanding: number;
  countOverdue: number;
  currency: string;
  needsAttention: ReceivableInvoice[];
}

export interface ReceivableInvoice {
  id: string;
  invoiceNumber: string;
  customerName: string;
  dueDate: string;
  outstanding: number;
  currency: string;
  isOverdue: boolean;
}

/** One customer's financial standing, with their invoices paged server-side. */
export interface CustomerSummary {
  customerId: string;
  customerName: string;
  totalInvoiced: number;
  totalPaid: number;
  totalOutstanding: number;
  totalOverdue: number;
  invoiceCount: number;
  overdueCount: number;
  currency: string;
  invoices: PagedResult<InvoiceListItem>;
}

// ---- payments: the business's own Razorpay account -------------------------

export type MerchantConnectionStatus =
  | "Disconnected"
  | "Connected"
  | "Error"
  | "Expired"
  | "Pending";

export type MerchantConnectionMode = "KeyPair" | "Oauth";

export type PaymentEnvironment = "Test" | "Live";

/**
 * What the API is willing to tell an owner about their payment connection.
 *
 * Note what is absent and always will be: the key secret, the OAuth tokens, and the connection's
 * own row id. `webhookSecret` is the single exception — it is present in the response that
 * creates it and null on every read afterwards, because only the encrypted form is kept.
 */
export interface MerchantConnection {
  provider: string;
  status: MerchantConnectionStatus;
  mode?: MerchantConnectionMode | null;
  environment: PaymentEnvironment;
  /** An acc_ identifier or a truncated publishable key — never a secret. */
  accountLabel?: string | null;
  displayName?: string | null;
  statusMessage?: string | null;
  connectedAt?: string | null;
  disconnectedAt?: string | null;
  lastVerifiedAt?: string | null;
  accessTokenExpiresAt?: string | null;
  /** The one question the invoice page asks. */
  canAcceptPayments: boolean;
  /** False until Quotely is an approved Razorpay Technology Partner. */
  oauthAvailable: boolean;
  keyPairAvailable: boolean;
  webhookUrl?: string | null;
  /** Shown once, at creation. Null forever after. */
  webhookSecret?: string | null;
}

export interface MerchantConnectionStart {
  authorizationUrl: string;
  state: string;
}

// ---- billing: what this business pays Quotely ------------------------------

export type SubscriptionStatus = "Trialing" | "Active" | "PastDue" | "Cancelled" | "Expired";

/**
 * The business's own subscription to Quotely. Note what is absent: no Razorpay subscription id,
 * no plan id, no key. The owner is told what they pay and when, not how we integrate.
 */
export interface Subscription {
  planCode: string;
  planName: string;
  planDescription?: string | null;
  price: number;
  currency: string;
  interval: string;

  status: SubscriptionStatus;
  statusMessage?: string | null;

  trialStart?: string | null;
  trialEnd?: string | null;
  inTrial: boolean;

  currentPeriodStart?: string | null;
  currentPeriodEnd?: string | null;

  cancelRequestedAt?: string | null;
  cancelledAt?: string | null;
  lastPaymentAt?: string | null;

  hasActiveMandate: boolean;
  hasAccess: boolean;
  accessEndsAt?: string | null;
  nextPaymentAt?: string | null;

  /** False when this deployment does not charge for anything. */
  billingEnabled: boolean;

  couponCode?: string | null;
  couponRedeemedAt?: string | null;
  couponFreeMonths?: number | null;
}

export interface SubscriptionCheckout {
  keyId: string;
  subscriptionId: string;
  planName: string;
  price: number;
  currency: string;
  firstChargeAt?: string | null;
  shortUrl?: string | null;
}

// ---- entitlements: what this business may do without paying -----------------

export interface UsageQuota {
  used: number;
  limit: number | null;
  hasLimit: boolean;
  exhausted: boolean;
  remaining: number | null;
}

/**
 * What the server says this account may currently do. Advisory only — every gate is enforced
 * again on the endpoint that does the work, so this decides what to SHOW, never what to allow.
 */
export interface Entitlements {
  hasAccess: boolean;
  status: SubscriptionStatus;
  accessEndsAt?: string | null;
  enforcementEnabled: boolean;

  canCreateQuotation: boolean;
  canConnectPayments: boolean;
  canAcceptPayments: boolean;
  canExportData: boolean;

  invoices: UsageQuota;
  customers: UsageQuota;
  products: UsageQuota;
}
