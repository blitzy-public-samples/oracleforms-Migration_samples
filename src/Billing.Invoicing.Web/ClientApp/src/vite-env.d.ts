/// <reference types="vite/client" />

/** Build-time environment variables read by the client. */
interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string;
}

/** Vite `import.meta` with the typed environment. */
interface ImportMeta {
  readonly env: ImportMetaEnv;
}
