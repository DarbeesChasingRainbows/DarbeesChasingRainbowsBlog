import { createDefine } from "jsr:@fresh/core@^2.3.3";
import type { State } from "../utils.ts";

const define = createDefine<State>();

export default define.page(function App({ Component }) {
  return (
    <html>
      <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1.0" />
        <title>cms</title>
      </head>
      <body>
        <Component />
      </body>
    </html>
  );
});
