global.importMetaEnv = {
  VITE_SIGNALR_HUB_URL: "http://localhost:5000/progressHub",
  VITE_BASE_API_URL: "http://localhost:5000/api",
};

Object.defineProperty(global, 'import', {
  value: {
    meta: {
      env: global.importMetaEnv,
    },
  },
});