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
