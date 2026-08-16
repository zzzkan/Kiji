// Kiji dev server live reload client.
// Reloads the page when the server broadcasts a change, and survives
// server restarts (e.g. dotnet watch rebuilds) by polling until the
// server comes back.
(() => {
  // Endpoints are derived from this script's own URL rather than from the page,
  // so they stay correct when the site is served under a base path.
  const script = new URL(document.currentScript?.src ?? "/_kiji/livereload.js", location.href);
  const base = new URL(".", script);
  const endpoint = base.href.replace(/^http/, "ws") + "reload";

  const connect = () => {
    const socket = new WebSocket(endpoint);
    socket.onmessage = (event) => {
      if (event.data === "reload") {
        location.reload();
      }
    };
    socket.onclose = () => pollUntilAlive();
  };

  const pollUntilAlive = () => {
    const timer = setInterval(async () => {
      try {
        const response = await fetch(base.href + "livereload.js", { cache: "no-store" });
        if (response.ok) {
          clearInterval(timer);
          location.reload();
        }
      } catch {
        // Server still down; keep polling.
      }
    }, 500);
  };

  connect();
})();
