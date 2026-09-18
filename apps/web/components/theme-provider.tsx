import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";

export type Theme = "light" | "dark" | "system";

type ThemeContextValue = {
  theme: Theme;
  setTheme: (theme: Theme) => void;
};

const themeStorageKey = "payaffe.theme";
const colorSchemeQuery = "(prefers-color-scheme: dark)";
const themes: Theme[] = ["light", "dark", "system"];
const ThemeContext = createContext<ThemeContextValue | undefined>(undefined);

function isTheme(value: string | null): value is Theme {
  return value !== null && themes.includes(value as Theme);
}

function getStorage(): Storage | null {
  try {
    return window.localStorage ?? null;
  } catch {
    return null;
  }
}

function applyTheme(theme: Theme) {
  const root = document.documentElement;
  const resolved =
    theme === "system"
      ? window.matchMedia(colorSchemeQuery).matches
        ? "dark"
        : "light"
      : theme;

  root.classList.remove("light", "dark");
  root.classList.add(resolved);
  root.style.colorScheme = resolved;
}

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(() => {
    const stored = getStorage()?.getItem(themeStorageKey) ?? null;
    return isTheme(stored) ? stored : "system";
  });

  const setTheme = useCallback((nextTheme: Theme) => {
    getStorage()?.setItem(themeStorageKey, nextTheme);
    setThemeState(nextTheme);
  }, []);

  useEffect(() => {
    applyTheme(theme);
    if (theme !== "system") {
      return undefined;
    }

    const mediaQuery = window.matchMedia(colorSchemeQuery);
    const handleChange = () => applyTheme("system");
    mediaQuery.addEventListener("change", handleChange);
    return () => mediaQuery.removeEventListener("change", handleChange);
  }, [theme]);

  useEffect(() => {
    const handleStorage = (event: StorageEvent) => {
      if (event.key === themeStorageKey) {
        setThemeState(isTheme(event.newValue) ? event.newValue : "system");
      }
    };
    window.addEventListener("storage", handleStorage);
    return () => window.removeEventListener("storage", handleStorage);
  }, []);

  const value = useMemo(() => ({ theme, setTheme }), [setTheme, theme]);
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme() {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error("useTheme must be used within ThemeProvider");
  }
  return context;
}
