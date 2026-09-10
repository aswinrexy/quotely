"use client";

import { ProductForm } from "@/components/app/product-form";
import { PageHeader } from "@/components/app/page-header";

export default function NewProductPage() {
  return (
    <>
      <PageHeader title="New product or service" description="Save the work you quote for regularly." />
      <ProductForm />
    </>
  );
}
