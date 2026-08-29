// The admin surface renders every timestamp and every fiat amount in the
// locale the document declares, so that one page cannot disagree with another
// about what "10:30" or "19,99" means.

export function clientLocale(): string {
  return typeof document === "undefined" ? "en" : document.documentElement.lang || "en";
}

export function formatDateTime(value: string): string {
  return new Date(value).toLocaleString(clientLocale());
}

export function formatFiatAmount(currency: string, minorUnits: number | string): string {
  return new Intl.NumberFormat(clientLocale(), {
    style: "currency",
    currency
  }).format(Number(minorUnits) / 100);
}
