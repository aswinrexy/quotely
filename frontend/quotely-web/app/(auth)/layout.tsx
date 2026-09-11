export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-paper px-4 py-12">
      <div className="w-full max-w-sm">
        <div className="mb-6 text-center">
          <p className="font-display text-heading-sm text-charcoal">
            Quote<span className="text-electric">ly</span>
          </p>
          <p className="mt-1 text-body text-fog">Professional quotations in minutes.</p>
        </div>
        <div className="rounded-card border border-ash bg-canvas p-6">{children}</div>
      </div>
    </div>
  );
}
