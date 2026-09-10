"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { Field, Input, Textarea } from "@/components/ui/field";
import type { Customer } from "@/types";

type CustomerFormValues = Omit<Customer, "id" | "createdAt">;

const EMPTY: CustomerFormValues = {
  name: "",
  companyName: "",
  email: "",
  phone: "",
  addressLine: "",
  city: "",
  state: "",
  postalCode: "",
  country: "",
  notes: "",
};

export function CustomerForm({ customer }: { customer?: Customer }) {
  const router = useRouter();
  const toast = useToast();
  const [form, setForm] = useState<CustomerFormValues>({ ...EMPTY, ...customer });
  const [errors, setErrors] = useState<Partial<Record<keyof CustomerFormValues, string>>>({});
  const [saving, setSaving] = useState(false);

  function update<K extends keyof CustomerFormValues>(key: K, value: CustomerFormValues[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  function validate() {
    const next: Partial<Record<keyof CustomerFormValues, string>> = {};
    if (!form.name.trim()) next.name = "Customer name is required.";
    if (form.email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email)) next.email = "Enter a valid email address.";
    setErrors(next);
    return Object.keys(next).length === 0;
  }

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (!validate()) return;

    const payload = {
      ...form,
      name: form.name.trim(),
      companyName: form.companyName || null,
      email: form.email || null,
      phone: form.phone || null,
      addressLine: form.addressLine || null,
      city: form.city || null,
      state: form.state || null,
      postalCode: form.postalCode || null,
      country: form.country || null,
      notes: form.notes || null,
    };

    setSaving(true);
    try {
      if (customer) {
        await api.put(`/api/customers/${customer.id}`, payload);
        toast("Customer updated.", "success");
      } else {
        await api.post("/api/customers", payload);
        toast("Customer created.", "success");
      }
      router.push("/customers");
      router.refresh();
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not save the customer.", "error");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Card>
      <form onSubmit={onSubmit} noValidate>
        <CardHeader title={customer ? "Edit customer" : "New customer"} />
        <CardBody className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Customer name" htmlFor="name" required error={errors.name}>
              <Input
                id="name"
                value={form.name}
                onChange={(e) => update("name", e.target.value)}
                placeholder="John Smith"
              />
            </Field>
            <Field label="Company name" htmlFor="companyName">
              <Input
                id="companyName"
                value={form.companyName ?? ""}
                onChange={(e) => update("companyName", e.target.value)}
                placeholder="John Smith Construction"
              />
            </Field>
            <Field label="Email" htmlFor="email" error={errors.email}>
              <Input
                id="email"
                type="email"
                value={form.email ?? ""}
                onChange={(e) => update("email", e.target.value)}
                placeholder="john@example.com"
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
          </div>

          <Field label="Address" htmlFor="addressLine">
            <Input
              id="addressLine"
              value={form.addressLine ?? ""}
              onChange={(e) => update("addressLine", e.target.value)}
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
              <Input id="country" value={form.country ?? ""} onChange={(e) => update("country", e.target.value)} />
            </Field>
          </div>

          <Field label="Notes" htmlFor="notes">
            <Textarea
              id="notes"
              value={form.notes ?? ""}
              onChange={(e) => update("notes", e.target.value)}
              placeholder="Anything worth remembering about this customer."
            />
          </Field>
        </CardBody>

        <div className="flex justify-end gap-2 border-t border-slate-200 px-5 py-4">
          <Button type="button" variant="secondary" onClick={() => router.push("/customers")}>
            Cancel
          </Button>
          <Button type="submit" loading={saving}>
            {customer ? "Save changes" : "Create customer"}
          </Button>
        </div>
      </form>
    </Card>
  );
}
