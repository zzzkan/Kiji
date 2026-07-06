// Kiji dev server live reload client.
// Reloads the page when the server broadcasts a change, and survives
// server restarts (e.g. dotnet watch rebuilds) by polling until the
// server comes back.
(() => {
  const endpoint = `${location.protocol === "https:" ? "wss" : "ws"}://${location.host}/_kiji/reload`;

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
        const response = await fetch("/_kiji/livereload.js", { cache: "no-store" });
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
