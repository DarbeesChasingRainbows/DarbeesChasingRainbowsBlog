import { App, staticFiles } from "jsr:@fresh/core@^2.3.3";
import type { State } from "./utils.ts";

export const app = new App<State>();

app.use(staticFiles());

// Include file-system based routes here
app.fsRoutes();
