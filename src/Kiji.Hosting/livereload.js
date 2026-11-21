(() => {
  const script = new URL(document.currentScript?.src ?? "/_kiji/livereload.js", location.href);
  const base = new URL(".", script);
  const endpoint = base.href.replace(/^http/, "ws") + "reload";

  const show = (id, lines, overlayStyle, lineStyle) => {
    document.getElementById(id)?.remove();
    if (lines.length === 0) {
      return;
    }
    const overlay = document.body.appendChild(document.createElement("div"));
    overlay.id = id;
    overlay.style.cssText = "position:fixed;z-index:2147483647;overflow:auto;" + overlayStyle;
    for (const line of lines) {
      const item = overlay.appendChild(document.createElement("div"));
      item.style.cssText = "white-space:pre-wrap;" + lineStyle;
      item.textContent = line;
    }
  };

  const showCompileErrors = (errors) => show("kiji-compile-errors", errors,
    "inset:0;background:rgba(0,0,0,.5)",
    "padding:8px;border:2px solid red;background:#faa;color:#000;font:14px/1.4 monospace");

  const showDisconnected = () => show("kiji-disconnected",
    ["Kiji dev server disconnected. This page reloads when it is back."],
    "left:0;right:0;bottom:0",
    "padding:8px 12px;background:#333;color:#fff;font:14px/1.4 system-ui,sans-serif");

  const connect = () => {
    const socket = new WebSocket(endpoint);
    socket.onmessage = (event) => {
      if (event.data === "reload") {
        location.reload();
      } else {
        showCompileErrors(JSON.parse(event.data));
      }
    };
    socket.onclose = () => {
      showDisconnected();
      pollUntilAlive();
    };
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
      }
    }, 500);
  };

  connect();
})();
