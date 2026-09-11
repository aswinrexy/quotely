"use client";

import { ProductForm } from "@/components/app/product-form";
import { PageHeader } from "@/components/app/page-header";

export default function NewProductPage() {
  return (
    <>
      <PageHeader title="Add item" description="Save the work you quote for regularly." />
      <ProductForm />
    </>
  );
}
