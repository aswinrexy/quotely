"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { Field, Input, Textarea } from "@/components/ui/field";
import { checkEmail, checkPhone, checkRequired } from "@/lib/validation";
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
  const [touched, setTouched] = useState<Record<string, boolean>>({});

  const checks = {
    name: checkRequired(form.name, "A customer name"),
    email: checkEmail(form.email ?? "", false),
    phone: checkPhone(form.phone ?? ""),
  };

  function touch(key: string) {
    setTouched((current) => ({ ...current, [key]: true }));
  }

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
    <Card className="max-w-3xl">
      <form onSubmit={onSubmit} noValidate>
        <CardHeader title={customer ? "Edit customer" : "New customer"} />
        <CardBody className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field
              label="Customer name"
              htmlFor="name"
              required
              error={errors.name}
              check={checks.name}
              touched={touched.name}
            >
              <Input
                id="name"
                value={form.name}
                onChange={(e) => update("name", e.target.value)}
                onBlur={() => touch("name")}
              />
            </Field>
            <Field label="Company name" htmlFor="companyName">
              <Input
                id="companyName"
                value={form.companyName ?? ""}
                onChange={(e) => update("companyName", e.target.value)}
              />
            </Field>
            <Field
              label="Email"
              htmlFor="email"
              error={errors.email}
              check={checks.email}
              touched={touched.email}
            >
              <Input
                id="email"
                type="email"
                inputMode="email"
                value={form.email ?? ""}
                onChange={(e) => update("email", e.target.value)}
                onBlur={() => touch("email")}
              />
            </Field>
            <Field label="Phone" htmlFor="phone" check={checks.phone} touched={touched.phone}>
              <Input
                id="phone"
                type="tel"
                // Brings up the phone keypad on a mobile, which is most of where these get typed.
                inputMode="tel"
                autoComplete="tel"
                value={form.phone ?? ""}
                onChange={(e) => update("phone", e.target.value)}
                onBlur={() => touch("phone")}
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
            />
          </Field>
        </CardBody>

        <div className="flex flex-col-reverse gap-2 border-t border-ash px-4 py-3 sm:flex-row sm:justify-end">
          <Button type="button" variant="secondary" onClick={() => router.push("/customers")} className="w-full sm:w-auto">
            Cancel
          </Button>
          <Button type="submit" loading={saving} className="w-full sm:w-auto">
            {customer ? "Save changes" : "Create customer"}
          </Button>
        </div>
      </form>
    </Card>
  );
}
