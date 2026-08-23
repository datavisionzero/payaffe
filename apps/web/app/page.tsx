import Link from "next/link";

export default function HomePage() {
  return (
    <main className="mx-auto flex min-h-screen w-full max-w-3xl flex-col justify-center px-6 py-12">
      <h1 className="text-3xl font-semibold">payaffe</h1>
      <p className="mt-3 max-w-xl text-base text-[var(--muted)]">
        Payment pages are available through payment-specific links.
      </p>
      <div className="mt-8 flex flex-wrap gap-4 text-sm font-medium">
        <Link className="text-[var(--accent)]" href="/pay/example">
          Open example route
        </Link>
        <a className="text-[var(--accent)]" href="/admin">
          Admin
        </a>
      </div>
    </main>
  );
}
