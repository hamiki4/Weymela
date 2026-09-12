import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    host: "127.0.0.1",
    proxy: { "/api": process.env.WEYMELA_V3_API ?? "http://127.0.0.1:5185" },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./tests/setup.ts"],
    include: ["tests/**/*.test.{ts,tsx}"],
    maxWorkers: 2,
  },
  build: { target: "es2022", sourcemap: false },
});
