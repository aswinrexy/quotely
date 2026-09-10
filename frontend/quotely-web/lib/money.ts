import type { SaveQuotationItemRequest } from "@/types";

/**
 * Mirrors Quotely.Api.Services.QuotationCalculator so the form can show live totals.
 * The server recalculates everything before saving; these numbers are for display only.
 */
export function round2(value: number) {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

export interface LineTotals {
  lineSubtotal: number;
  discount: number;
  lineTax: number;
  lineTotal: number;
}

export function calculateLine(item: Pick<SaveQuotationItemRequest, "quantity" | "unitPrice" | "discount" | "taxRate">): LineTotals {
  const gross = round2((item.quantity || 0) * (item.unitPrice || 0));
  const discount = round2(Math.min(Math.max(item.discount || 0, 0), gross));
  const net = gross - discount;
  const lineTax = round2((net * (item.taxRate || 0)) / 100);

  return { lineSubtotal: gross, discount, lineTax, lineTotal: round2(net + lineTax) };
}

export interface QuotationTotals {
  subtotal: number;
  discountTotal: number;
  taxTotal: number;
  grandTotal: number;
}

export function calculateTotals(
  items: Pick<SaveQuotationItemRequest, "quantity" | "unitPrice" | "discount" | "taxRate">[],
): QuotationTotals {
  let subtotal = 0;
  let discountTotal = 0;
  let taxTotal = 0;

  for (const item of items) {
    const line = calculateLine(item);
    subtotal += line.lineSubtotal;
    discountTotal += line.discount;
    taxTotal += line.lineTax;
  }

  subtotal = round2(subtotal);
  discountTotal = round2(discountTotal);
  taxTotal = round2(taxTotal);

  return { subtotal, discountTotal, taxTotal, grandTotal: round2(subtotal - discountTotal + taxTotal) };
}
