"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { CURRENCIES } from "@/lib/format";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { Field, Input, Select } from "@/components/ui/field";
import { ErrorState, LoadingState } from "@/components/ui/states";
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
      toast("Business profile saved.", "success");
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not save your profile.", "error");
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <PageHeader
        title="Business Profile"
        description="These details appear on every quotation PDF you generate."
      />

      <Card>
        {loading ? (
          <LoadingState />
        ) : error ? (
          <ErrorState message={error} onRetry={load} />
        ) : (
          <form onSubmit={onSubmit}>
            <CardHeader title="Business details" />
            <CardBody className="space-y-5">
              <div className="grid gap-4 sm:grid-cols-2">
                <Field label="Business name" htmlFor="businessName" required error={nameError}>
                  <Input
                    id="businessName"
                    value={form.businessName}
                    onChange={(e) => update("businessName", e.target.value)}
                    placeholder="ABC Electricals"
                  />
                </Field>
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
                    value={form.phone ?? ""}
                    onChange={(e) => update("phone", e.target.value)}
                    placeholder="+91 98765 43210"
                  />
                </Field>
                <Field label="Tax / GST number" htmlFor="taxNumber">
                  <Input
                    id="taxNumber"
                    value={form.taxNumber ?? ""}
                    onChange={(e) => update("taxNumber", e.target.value)}
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

              <div className="grid gap-4 sm:grid-cols-2">
                <Field label="Currency" htmlFor="currency" required hint="Used across the app and in PDFs.">
                  <Select
                    id="currency"
                    value={form.currency}
                    onChange={(e) => update("currency", e.target.value)}
                  >
                    {CURRENCIES.map((code) => (
                      <option key={code} value={code}>
                        {code}
                      </option>
                    ))}
                  </Select>
                </Field>

                <Field label="Logo" htmlFor="logo" hint="PNG or JPG, up to 300 KB. Appears on the PDF header.">
                  <div className="flex items-center gap-3">
                    {form.logoUrl ? (
                      // eslint-disable-next-line @next/next/no-img-element
                      <img
                        src={form.logoUrl}
                        alt="Business logo"
                        className="h-12 w-12 rounded-lg border border-slate-200 object-contain p-1"
                      />
                    ) : (
                      <div className="flex h-12 w-12 items-center justify-center rounded-lg border border-dashed border-slate-300 text-xs text-slate-400">
                        None
                      </div>
                    )}
                    <input
                      ref={fileInput}
                      id="logo"
                      type="file"
                      accept="image/png,image/jpeg"
                      onChange={onLogoChange}
                      className="hidden"
                    />
                    <Button type="button" variant="secondary" size="sm" onClick={() => fileInput.current?.click()}>
                      Upload
                    </Button>
                    {form.logoUrl && (
                      <Button type="button" variant="ghost" size="sm" onClick={() => update("logoUrl", "")}>
                        Remove
                      </Button>
                    )}
                  </div>
                </Field>
              </div>
            </CardBody>

            <div className="flex justify-end gap-2 border-t border-slate-200 px-5 py-4">
              <Button type="submit" loading={saving}>
                Save changes
              </Button>
            </div>
          </form>
        )}
      </Card>
    </>
  );
}
