import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

/** Builds the React client into the Web host and proxies development API calls. */
export default defineConfig({
  plugins: [react()],
  build: { outDir: '../wwwroot', emptyOutDir: true },
  server: { port: 5173, proxy: { '/api': 'http://localhost:5080' } },
});
