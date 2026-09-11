"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { useToast } from "@/components/ui/toast";
import { Button } from "@/components/ui/button";
import { Card, CardBody, CardHeader } from "@/components/ui/card";
import { Field, Input, Select, Textarea } from "@/components/ui/field";
import type { Product } from "@/types";

export const UNITS = ["Service", "Hour", "Day", "Piece", "Visit", "Metre", "Sq. ft", "Kg", "Lot"];

interface FormValues {
  name: string;
  description: string;
  unit: string;
  price: string;
  taxRate: string;
}

export function ProductForm({ product }: { product?: Product }) {
  const router = useRouter();
  const toast = useToast();
  const [form, setForm] = useState<FormValues>({
    name: product?.name ?? "",
    description: product?.description ?? "",
    unit: product?.unit ?? "Service",
    price: product ? String(product.price) : "",
    taxRate: product ? String(product.taxRate) : "0",
  });
  const [errors, setErrors] = useState<Partial<Record<keyof FormValues, string>>>({});
  const [saving, setSaving] = useState(false);

  function update<K extends keyof FormValues>(key: K, value: FormValues[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  function validate() {
    const next: Partial<Record<keyof FormValues, string>> = {};
    const price = Number(form.price);
    const taxRate = Number(form.taxRate);

    if (!form.name.trim()) next.name = "Name is required.";
    if (form.price === "" || Number.isNaN(price)) next.price = "Enter a price.";
    else if (price < 0) next.price = "Price cannot be negative.";
    if (Number.isNaN(taxRate) || taxRate < 0) next.taxRate = "Tax rate cannot be negative.";
    else if (taxRate > 100) next.taxRate = "Tax rate cannot exceed 100%.";

    setErrors(next);
    return Object.keys(next).length === 0;
  }

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (!validate()) return;

    const payload = {
      name: form.name.trim(),
      description: form.description || null,
      unit: form.unit,
      price: Number(form.price),
      taxRate: Number(form.taxRate),
    };

    setSaving(true);
    try {
      if (product) {
        await api.put(`/api/products/${product.id}`, payload);
        toast("Product updated.", "success");
      } else {
        await api.post("/api/products", payload);
        toast("Product created.", "success");
      }
      router.push("/products");
      router.refresh();
    } catch (err) {
      toast(err instanceof Error ? err.message : "Could not save the product.", "error");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Card className="max-w-3xl">
      <form onSubmit={onSubmit} noValidate>
        <CardHeader title={product ? "Edit product or service" : "New product or service"} />
        <CardBody className="space-y-5">
          <Field label="Name" htmlFor="name" required error={errors.name}>
            <Input
              id="name"
              value={form.name}
              onChange={(e) => update("name", e.target.value)}
              placeholder="AC Installation"
            />
          </Field>

          <Field label="Description" htmlFor="description">
            <Textarea
              id="description"
              value={form.description}
              onChange={(e) => update("description", e.target.value)}
              placeholder="What is included in this service?"
            />
          </Field>

          <div className="grid gap-4 sm:grid-cols-3">
            <Field label="Unit" htmlFor="unit" required>
              <Select id="unit" value={form.unit} onChange={(e) => update("unit", e.target.value)}>
                {UNITS.map((unit) => (
                  <option key={unit} value={unit}>
                    {unit}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label="Price" htmlFor="price" required error={errors.price}>
              <Input
                id="price"
                type="number"
                min={0}
                step="0.01"
                inputMode="decimal"
                value={form.price}
                onChange={(e) => update("price", e.target.value)}
                placeholder="5000"
              />
            </Field>

            <Field label="Tax rate (%)" htmlFor="taxRate" required error={errors.taxRate}>
              <Input
                id="taxRate"
                type="number"
                min={0}
                max={100}
                step="0.01"
                inputMode="decimal"
                value={form.taxRate}
                onChange={(e) => update("taxRate", e.target.value)}
                placeholder="18"
              />
            </Field>
          </div>
        </CardBody>

        <div className="flex flex-col-reverse gap-2 border-t border-ash px-4 py-3 sm:flex-row sm:justify-end">
          <Button type="button" variant="secondary" onClick={() => router.push("/products")} className="w-full sm:w-auto">
            Cancel
          </Button>
          <Button type="submit" loading={saving} className="w-full sm:w-auto">
            {product ? "Save changes" : "Create"}
          </Button>
        </div>
      </form>
    </Card>
  );
}
