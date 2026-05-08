import server, { registerStaticFile } from "./server/server-entry.js";



export default {
  fetch: server.fetch
};
