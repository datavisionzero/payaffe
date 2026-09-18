import { MonitorIcon, MoonIcon, SunIcon } from "lucide-react";
import { type Theme, useTheme } from "./theme-provider";

const options: Array<{ label: string; value: Theme }> = [
  { label: "System theme", value: "system" },
  { label: "Light theme", value: "light" },
  { label: "Dark theme", value: "dark" }
];

export function ThemeSelect({ compact = false }: { compact?: boolean }) {
  const { setTheme, theme } = useTheme();
  const Icon = theme === "light" ? SunIcon : theme === "dark" ? MoonIcon : MonitorIcon;

  if (compact) {
    return (
      <label className="relative inline-flex size-8 shrink-0 items-center justify-center rounded-md border border-[var(--border)] bg-[var(--background)] focus-within:ring-3 focus-within:ring-[var(--ring)]/50">
        <span className="sr-only">Color theme</span>
        <Icon aria-hidden="true" className="size-3.5" />
        <select
          aria-label="Color theme"
          className="absolute inset-0 size-8 cursor-pointer opacity-0"
          onChange={(event) => setTheme(event.target.value as Theme)}
          title={options.find((option) => option.value === theme)?.label}
          value={theme}
        >
          {options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </label>
    );
  }

  return (
    <label className="relative inline-flex shrink-0 items-center">
      <span className="sr-only">Color theme</span>
      <Icon aria-hidden="true" className="pointer-events-none absolute left-2 size-3.5" />
      <select
        aria-label="Color theme"
        className="h-8 appearance-none rounded-md border border-[var(--border)] bg-[var(--background)] py-1 pr-7 pl-7 text-xs font-medium"
        onChange={(event) => setTheme(event.target.value as Theme)}
        title={options.find((option) => option.value === theme)?.label}
        value={theme}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
  );
}
