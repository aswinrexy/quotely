"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { CURRENCIES } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, SectionCard } from "@/components/ui/card";
import { Field, Input, Select } from "@/components/ui/field";
import { DetailSkeleton, ErrorState } from "@/components/ui/states";
import { PageHeader } from "@/components/app/page-header";
import type { BusinessProfile } from "@/types";

const EMPTY: BusinessProfile = {
  id: "",
  businessName: "",
  businessEmail: "",
  phone: "",
  addressLine: "",
  city: "",
  state: "",
  postalCode: "",
  country: "",
  taxNumber: "",
  logoUrl: "",
  currency: "INR",
};

const MAX_LOGO_BYTES = 300 * 1024;

export default function BusinessProfilePage() {
  const toast = useToast();
  const [form, setForm] = useState<BusinessProfile>(EMPTY);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [nameError, setNameError] = useState<string | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const profile = await api.get<BusinessProfile>("/api/business-profile");
      setForm({ ...EMPTY, ...profile, currency: profile.currency || "INR" });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load your business profile.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  function update<K extends keyof BusinessProfile>(key: K, value: BusinessProfile[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  async function onLogoChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;

    if (!["image/png", "image/jpeg"].includes(file.type)) {
      toast("Please choose a PNG or JPG image.", "error");
      return;
    }
    if (file.size > MAX_LOGO_BYTES) {
      toast("Logo must be smaller than 300 KB.", "error");
      return;
    }

    // V1 stores the logo inline as a data URI; swapping in blob storage only changes this step.
    const reader = new FileReader();
    reader.onload = () => update("logoUrl", String(reader.result));
    reader.readAsDataURL(file);
  }

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    setNameError(null);

    if (!form.businessName.trim()) {
      setNameError("Business name is required.");
      return;
    }

    setSaving(true);
    try {
      const saved = await api.put<BusinessProfile>("/api/business-profile", {
        businessName: form.businessName.trim(),
        businessEmail: form.businessEmail || null,
        phone: form.phone || null,
        addressLine: form.addressLine || null,
        city: form.city || null,
        state: form.state || null,
        postalCode: form.postalCode || null,
        country: form.country || null,
        taxNumber: form.taxNumber || null,
        logoUrl: form.logoUrl || null,
        currency: form.currency,
      });
      setForm({ ...EMPTY, ...saved });
      toast("Changes saved", "success");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not save your profile.", "error");
    } finally {
      setSaving(false);
    }
  }

  if (loading) return <DetailSkeleton />;

  if (error) {
    return (
      <Card>
        <ErrorState message={error} onRetry={load} />
      </Card>
    );
  }

  return (
    <form onSubmit={onSubmit}>
      <PageHeader
        title="Business Profile"
        description="These details appear on every quotation and invoice you generate."
        action={
          <Button type="submit" loading={saving}>
            Save changes
          </Button>
        }
      />

      <div className="space-y-4">
        <SectionCard
          title="Business information"
          description="The name and tax details printed at the top of your documents."
        >
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Business name" htmlFor="businessName" required error={nameError}>
              <Input
                id="businessName"
                value={form.businessName}
                onChange={(e) => update("businessName", e.target.value)}
                placeholder="ABC Electricals"
                aria-invalid={nameError ? true : undefined}
              />
            </Field>
            <Field label="Tax / GST number" htmlFor="taxNumber">
              <Input
                id="taxNumber"
                value={form.taxNumber ?? ""}
                onChange={(e) => update("taxNumber", e.target.value)}
                placeholder="33ABCDE1234F1Z5"
              />
            </Field>
          </div>
        </SectionCard>

        <SectionCard
          title="Contact information"
          description="How customers reach you. Shown on every document you send."
        >
          <div className="space-y-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Business email" htmlFor="businessEmail">
                <Input
                  id="businessEmail"
                  type="email"
                  value={form.businessEmail ?? ""}
                  onChange={(e) => update("businessEmail", e.target.value)}
                  placeholder="hello@business.com"
                />
              </Field>
              <Field label="Phone" htmlFor="phone">
                <Input
                  id="phone"
                  type="tel"
                  value={form.phone ?? ""}
                  onChange={(e) => update("phone", e.target.value)}
                  placeholder="+91 98765 43210"
                />
              </Field>
            </div>

            <Field label="Address" htmlFor="addressLine">
              <Input
                id="addressLine"
                value={form.addressLine ?? ""}
                onChange={(e) => update("addressLine", e.target.value)}
                placeholder="123 Main Street"
              />
            </Field>

            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <Field label="City" htmlFor="city">
                <Input id="city" value={form.city ?? ""} onChange={(e) => update("city", e.target.value)} />
              </Field>
              <Field label="State" htmlFor="state">
                <Input id="state" value={form.state ?? ""} onChange={(e) => update("state", e.target.value)} />
              </Field>
              <Field label="Postal code" htmlFor="postalCode">
                <Input
                  id="postalCode"
                  value={form.postalCode ?? ""}
                  onChange={(e) => update("postalCode", e.target.value)}
                />
              </Field>
              <Field label="Country" htmlFor="country">
                <Input
                  id="country"
                  value={form.country ?? ""}
                  onChange={(e) => update("country", e.target.value)}
                />
              </Field>
            </div>
          </div>
        </SectionCard>

        <SectionCard
          title="Document defaults"
          description="Applied to new quotations and to every invoice raised from one."
        >
          <Field
            label="Currency"
            htmlFor="currency"
            required
            hint="Used across the app and on generated PDFs. Invoices keep the currency they were raised in."
            className="sm:max-w-xs"
          >
            <Select id="currency" value={form.currency} onChange={(e) => update("currency", e.target.value)}>
              {CURRENCIES.map((code) => (
                <option key={code} value={code}>
                  {code}
                </option>
              ))}
            </Select>
          </Field>
        </SectionCard>

        <SectionCard
          title="Branding"
          description="Your logo is placed in the header of every generated PDF."
          footer={
            <Button type="submit" loading={saving}>
              Save changes
            </Button>
          }
        >
          <Field label="Logo" htmlFor="logo" hint="PNG or JPG, up to 300 KB.">
            <div className="flex flex-wrap items-center gap-3">
              {form.logoUrl ? (
                // eslint-disable-next-line @next/next/no-img-element
                <img
                  src={form.logoUrl}
                  alt="Your business logo"
                  className="h-12 w-12 rounded-input border border-ash object-contain p-1"
                />
              ) : (
                <div className="flex h-12 w-12 items-center justify-center rounded-input border border-dashed border-smoke text-caption text-fog">
                  None
                </div>
              )}
              <input
                ref={fileInput}
                id="logo"
                type="file"
                accept="image/png,image/jpeg"
                onChange={onLogoChange}
                className="sr-only"
              />
              <Button type="button" variant="secondary" size="sm" onClick={() => fileInput.current?.click()}>
                {form.logoUrl ? "Replace" : "Upload"}
              </Button>
              {form.logoUrl && (
                <Button type="button" variant="ghost" size="sm" onClick={() => update("logoUrl", "")}>
                  Remove
                </Button>
              )}
            </div>
          </Field>
        </SectionCard>
      </div>
    </form>
  );
}
