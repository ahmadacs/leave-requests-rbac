/**
 * Only accepts in-app absolute paths, so a crafted `returnUrl` cannot turn into
 * an open redirect (no `//evil.com`, no `https://evil.com`, no `javascript:`).
 */
export function safeReturnUrl(value: string | null | undefined, fallback = '/'): string {
  if (!value) {
    return fallback;
  }

  if (!value.startsWith('/') || value.startsWith('//') || value.includes('://')) {
    return fallback;
  }

  return value;
}
