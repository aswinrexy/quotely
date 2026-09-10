"use client";

import { CustomerForm } from "@/components/app/customer-form";
import { PageHeader } from "@/components/app/page-header";

export default function NewCustomerPage() {
  return (
    <>
      <PageHeader title="New customer" description="Add someone you can quote for." />
      <CustomerForm />
    </>
  );
}
