export type TextValues = Record<string, string | number | Date>;

export function createText(messages: Record<string, string>) {
  return (key: string, values: TextValues = {}): string => {
    const message = messages[key] ?? key;
    let result = message;
    for (const [name, value] of Object.entries(values)) {
      const marker = `{${name}, plural,`;
      const start = result.indexOf(marker);
      if (start >= 0 && typeof value === "number") {
        const end = matchingBrace(result, start);
        if (end >= 0) {
          const choices = result.slice(start + marker.length, end);
          result = `${result.slice(0, start)}${selectPlural(choices, value)}${result.slice(end + 1)}`;
        }
      }
    }

    return result.replace(/\{([A-Za-z][A-Za-z0-9]*)\}/g, (placeholder, name: string) =>
      name in values ? String(values[name]) : placeholder
    );
  };
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
