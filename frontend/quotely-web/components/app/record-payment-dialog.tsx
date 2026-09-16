"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { formatMoney, todayIso } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Field, Input, Select, Textarea } from "@/components/ui/field";
import {
  MANUAL_PAYMENT_METHODS,
  MANUAL_PAYMENT_METHOD_LABELS,
  type Invoice,
  type ManualPaymentMethod,
  type Payment,
} from "@/types";

/**
 * Records money the business received outside the gateway.
 *
 * The amount is prefilled with the outstanding balance because settling in full is the common
 * case, but it is an editable field and nothing is submitted until the owner presses the button —
 * a part payment is typed over the top. The server validates the amount against a balance it
 * computes itself, so what is checked here is only to save a round trip.
 */
export function RecordPaymentDialog({
  invoice,
  open,
  onClose,
  onRecorded,
}: {
  invoice: Invoice;
  open: boolean;
  onClose: () => void;
  onRecorded: () => void;
}) {
  const toast = useToast();
  const outstanding = invoice.outstanding;

  const [amount, setAmount] = useState(String(outstanding));
  const [method, setMethod] = useState<ManualPaymentMethod>("cash");
  const [paymentDate, setPaymentDate] = useState(todayIso());
  const [reference, setReference] = useState("");
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  /** Reopening after a part payment must start from the new balance, not the old input. */
  function reset() {
    setAmount(String(invoice.outstanding));
    setMethod("cash");
    setPaymentDate(todayIso());
    setReference("");
    setNotes("");
    setError(null);
  }

  function close() {
    reset();
    onClose();
  }

  async function submit() {
    const value = Number(amount);

    if (!Number.isFinite(value) || value <= 0) {
      setError("Enter an amount greater than zero.");
      return;
    }
    if (value > outstanding) {
      setError(`That is more than the ${formatMoney(outstanding, invoice.currency)} still outstanding.`);
      return;
    }
    if (paymentDate > todayIso()) {
      setError("A payment date cannot be in the future.");
      return;
    }

    setSaving(true);
    setError(null);
    try {
      await api.post<Payment>(`/api/invoices/${invoice.id}/payments`, {
        amount: value,
        method,
        paymentDate,
        reference: reference.trim() || null,
        notes: notes.trim() || null,
      });
      toast(`${formatMoney(value, invoice.currency)} recorded`, "success");
      reset();
      onRecorded();
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not record the payment.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Dialog
      open={open}
      title="Record payment"
      description={`${formatMoney(outstanding, invoice.currency)} outstanding on ${invoice.invoiceNumber}.`}
      onClose={saving ? () => {} : close}
      footer={
        <>
          <Button variant="secondary" onClick={close} disabled={saving} className="w-full sm:w-auto">
            Cancel
          </Button>
          <Button onClick={submit} loading={saving} className="w-full sm:w-auto">
            Record payment
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {error && (
          <div role="alert" className="rounded-input border border-ash bg-rose-wash px-3 py-2">
            <p className="text-body text-rose-ink">{error}</p>
          </div>
        )}

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Amount" htmlFor="payment-amount" required>
            <Input
              id="payment-amount"
              type="number"
              min={0}
              max={outstanding}
              step="0.01"
              inputMode="decimal"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              className="text-right tabular-nums"
            />
          </Field>

          <Field label="Method" htmlFor="payment-method" required>
            <Select
              id="payment-method"
              value={method}
              onChange={(e) => setMethod(e.target.value as ManualPaymentMethod)}
            >
              {MANUAL_PAYMENT_METHODS.map((value) => (
                <option key={value} value={value}>
                  {MANUAL_PAYMENT_METHOD_LABELS[value]}
                </option>
              ))}
            </Select>
          </Field>

          <Field
            label="Payment date"
            htmlFor="payment-date"
            required
            hint="When the money arrived. Can be back-dated."
          >
            <Input
              id="payment-date"
              type="date"
              max={todayIso()}
              value={paymentDate}
              onChange={(e) => setPaymentDate(e.target.value)}
            />
          </Field>

          <Field label="Reference" htmlFor="payment-reference" hint="Cheque number, bank UTR…">
            <Input
              id="payment-reference"
              value={reference}
              maxLength={100}
              onChange={(e) => setReference(e.target.value)}
              placeholder="Optional"
            />
          </Field>
        </div>

        <Field label="Note" htmlFor="payment-notes">
          <Textarea
            id="payment-notes"
            value={notes}
            maxLength={500}
            rows={2}
            onChange={(e) => setNotes(e.target.value)}
            placeholder="Optional — only you see this"
          />
        </Field>
      </div>
    </Dialog>
  );
}
