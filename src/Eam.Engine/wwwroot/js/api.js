// Small wrapper around fetch for the EAM engine API.
export class ApiError extends Error {
  constructor(status, body) {
    super(body?.error || `http_${status}`);
    this.status = status;
    this.code = body?.error || (status === 0 ? "network" : "generic");
    this.body = body;
  }
}

async function call(method, url, data) {
  let res;
  try {
    res = await fetch(url, {
      method,
      credentials: "same-origin",
      headers: { "X-EAM": "1", ...(data !== undefined ? { "Content-Type": "application/json" } : {}) },
      body: data !== undefined ? JSON.stringify(data) : undefined,
    });
  } catch {
    throw new ApiError(0, { error: "network" });
  }
  const text = await res.text();
  let body = null;
  try { body = text ? JSON.parse(text) : null; } catch { body = null; }
  if (!res.ok) {
    if (res.status === 401 && !url.startsWith("/api/login")) window.dispatchEvent(new Event("eam:unauthorized"));
    throw new ApiError(res.status, body);
  }
  return body;
}

async function upload(url, file) {
  const fd = new FormData();
  fd.append("file", file, file.name);
  let res;
  try { res = await fetch(url, { method: "POST", credentials: "same-origin", headers: { "X-EAM": "1" }, body: fd }); }
  catch { throw new ApiError(0, { error: "network" }); }
  let body = null;
  try { body = await res.json(); } catch { body = null; }
  if (!res.ok) throw new ApiError(res.status, body);
  return body;
}

export const api = {
  upload,
  get: (u) => call("GET", u),
  post: (u, d = {}) => call("POST", u, d),
  put: (u, d = {}) => call("PUT", u, d),
  del: (u) => call("DELETE", u),
};
