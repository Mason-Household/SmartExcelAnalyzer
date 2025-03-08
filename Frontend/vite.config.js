import { getEnv } from "./src/utils/getEnv"
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig(({ mode }) => {
  // Set base URLs using environment variables or defaults
  // These will be used with the proxy configuration
  const baseApiUrl = getEnv('VITE_BASE_API_URL', '/api');
  const signalRHubUrl = getEnv('VITE_SIGNALR_HUB_URL', '/progressHub');

  return {
    plugins: [react()],
    server: {
      port: 3000,
      host: "0.0.0.0",
      strictPort: true,
      proxy: {
        // Proxy API requests to the backend
        '/api': {
          target: 'http://backend:5001', // Changed to port 5001
          changeOrigin: true,
          secure: false,
        },
        // Proxy SignalR WebSocket requests to the backend
        '/progressHub': {
          target: 'http://backend:5001', // Changed to port 5001
          ws: true, // Important for WebSocket support
          changeOrigin: true,
          secure: false,
        },
      },
    },
    build: {
      outDir: "dist",
      sourcemap: true,
    },
  };
});