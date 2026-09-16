import messages from "../messages/en.json";

type Namespace = keyof typeof messages;
type Values = Record<string, string | number | Date>;
type Translator = (key: string, values?: Values) => string;

export function useTranslations<SelectedNamespace extends Namespace>(
  namespace: SelectedNamespace
): Translator {
  return (key, values = {}) => {
    const message = readMessage(messages[namespace], key);
    return formatMessage(message ?? key, values);
  };
}

export function useLocale(): string {
  return "en";
}

export function useFormatter() {
  return {
    dateTime(value: Date, options?: Intl.DateTimeFormatOptions): string {
      return new Intl.DateTimeFormat("en", options).format(value);
    }
  };
}

function readMessage(root: object, key: string): string | undefined {
  let current: unknown = root;
  for (const part of key.split(".")) {
    if (typeof current !== "object" || current === null || !(part in current)) {
      return undefined;
    }
    current = (current as Record<string, unknown>)[part];
  }
  return typeof current === "string" ? current : undefined;
}

function formatMessage(message: string, values: Values): string {
  let result = message;
  for (const [name, value] of Object.entries(values)) {
    const marker = `{${name}, plural,`;
    const start = result.indexOf(marker);
    if (start >= 0 && typeof value === "number") {
      const end = matchingBrace(result, start);
      if (end >= 0) {
        const choices = result.slice(start + marker.length, end);
        const selected = selectPlural(choices, value);
        result = `${result.slice(0, start)}${selected}${result.slice(end + 1)}`;
      }
    }
  }

  return result.replace(/\{([A-Za-z][A-Za-z0-9]*)\}/g, (placeholder, name: string) =>
    name in values ? String(values[name]) : placeholder
  );
}

function matchingBrace(value: string, start: number): number {
  let depth = 0;
  for (let index = start; index < value.length; index += 1) {
    if (value[index] === "{") depth += 1;
    if (value[index] === "}") depth -= 1;
    if (depth === 0) return index;
  }
  return -1;
}

function selectPlural(choices: string, count: number): string {
  const options = new Map<string, string>();
  for (const match of choices.matchAll(/(=\d+|one|other)\s*\{([^{}]*)\}/g)) {
    options.set(match[1], match[2]);
  }
  const selected = options.get(`=${count}`) ?? options.get(count === 1 ? "one" : "other") ?? "";
  return selected.replaceAll("#", String(count));
}
