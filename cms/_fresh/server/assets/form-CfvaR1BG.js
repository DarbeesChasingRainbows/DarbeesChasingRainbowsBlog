import { a, s, u, l } from "../server-entry.js";
import { c as collections } from "./content-BXNbgoEc.js";
const $$_tpl_1 = ['<form method="post" class="space-y-8">', '<section class="rounded-xl border border-stone-200 bg-white p-6 shadow-sm"><h2 class="text-lg font-semibold text-stone-900">File</h2><div class="mt-4 grid gap-4 md:grid-cols-2"><label class="block"><span class="text-sm font-medium text-stone-700">Collection</span><input class="mt-1 w-full rounded-md border border-stone-300 bg-stone-100 px-3 py-2" ', ' disabled></label><label class="block"><span class="text-sm font-medium text-stone-700">Slug</span><input class="mt-1 w-full rounded-md border border-stone-300 px-3 py-2" name="slug" required pattern="[a-z0-9]+(-[a-z0-9]+)*" ', " ", ' placeholder="my-kebab-case-title"></label></div></section><section class="rounded-xl border border-stone-200 bg-white p-6 shadow-sm"><h2 class="text-lg font-semibold text-stone-900">Frontmatter</h2><div class="mt-4 grid gap-4 md:grid-cols-2">', '</div></section><section class="rounded-xl border border-stone-200 bg-white p-6 shadow-sm"><label class="block"><span class="text-lg font-semibold text-stone-900">MDX body</span><textarea class="mt-4 min-h-[28rem] w-full rounded-md border border-stone-300 px-3 py-2 font-mono text-sm" name="body">', '</textarea></label></section><div class="sticky bottom-0 flex items-center justify-between border-t border-stone-200 bg-stone-50/95 px-4 py-4 backdrop-blur">', '<button class="rounded-md bg-emerald-700 px-5 py-2.5 text-sm font-semibold text-white hover:bg-emerald-800" type="submit">', "</button></div></form>"];
const $$_tpl_2 = ['<div class="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-800">', "</div>"];
const $$_tpl_3 = ['<label class="flex items-center gap-3 rounded-md border border-stone-200 p-3"><input type="checkbox" ', ' value="true" ', '><span class="text-sm font-medium text-stone-700">', "</span></label>"];
const $$_tpl_4 = ['<label class="block md:col-span-2"><span class="text-sm font-medium text-stone-700">', "", "</span><textarea ", " ", " ", ">", "</textarea>", "</label>"];
const $$_tpl_5 = ['<span class="mt-1 block text-xs text-stone-500">', "</span>"];
const $$_tpl_6 = ['<label class="block"><span class="text-sm font-medium text-stone-700">', "", "</span><select ", " ", " ", ">", "</select></label>"];
const $$_tpl_7 = ["<option ", " ", " ", ">", "</option>"];
const $$_tpl_8 = ['<label class="block"><span class="text-sm font-medium text-stone-700">', "", "</span><input ", " ", " ", " ", " ", ">", "</label>"];
const $$_tpl_9 = ['<span class="mt-1 block text-xs text-stone-500">', "</span>"];
function ContentForm({
  collection,
  slug,
  frontmatter,
  body,
  mode,
  error
}) {
  return a($$_tpl_1, s(error && a($$_tpl_2, s(error))), l("value", collection.label), l("value", slug ?? ""), mode === "edit" ? "readonly" : "", s(collection.fields.map((field) => u(FieldInput, {
    field,
    value: frontmatter[field.name]
  }, field.name))), s(body), u("a", {
    class: "text-sm font-medium text-stone-600 hover:text-stone-900",
    href: "/",
    children: "Cancel"
  }), s(mode === "create" ? "Create MDX file" : "Save MDX file"));
}
function FieldInput({
  field,
  value
}) {
  const common = "mt-1 w-full rounded-md border border-stone-300 px-3 py-2";
  const stringValue = Array.isArray(value) ? value.join("\n") : String(value ?? defaultValue(field));
  if (field.kind === "checkbox") {
    return a($$_tpl_3, l("name", field.name), value === true || value === void 0 && field.name === "draft" ? "checked" : "", s(field.label));
  }
  if (field.kind === "textarea" || field.kind === "list") {
    return a($$_tpl_4, s(field.label), s(field.required ? " *" : ""), l("class", `${common} min-h-24`), l("name", field.name), field.required ? "required" : "", s(stringValue), s(field.help && a($$_tpl_5, s(field.help))));
  }
  if (field.kind === "select") {
    return a($$_tpl_6, s(field.label), s(field.required ? " *" : ""), l("class", common), l("name", field.name), field.required ? "required" : "", s((field.options ?? []).map((option) => a($$_tpl_7, l("key", option), l("value", option), option === stringValue ? "selected" : "", s(option || "None")))));
  }
  return a($$_tpl_8, s(field.label), s(field.required ? " *" : ""), l("class", common), l("type", field.kind === "date" ? "date" : "text"), l("name", field.name), l("value", stringValue), field.required ? "required" : "", s(field.help && a($$_tpl_9, s(field.help))));
}
function defaultValue(field) {
  if (field.name === "draft") return true;
  if (field.kind === "date") return (/* @__PURE__ */ new Date()).toISOString().slice(0, 10);
  return "";
}
async function frontmatterFromRequest(collection, req) {
  const form = await req.formData();
  const slug = String(form.get("slug") ?? "");
  const body = String(form.get("body") ?? "");
  const frontmatter = {};
  for (const field of collections[collection].fields) {
    const raw = form.get(field.name);
    if (field.kind === "checkbox") {
      frontmatter[field.name] = raw === "true";
      continue;
    }
    if (raw === null) {
      continue;
    }
    const value = String(raw).trim();
    if (!value) {
      continue;
    }
    if (field.kind === "tags") {
      frontmatter[field.name] = splitList(value.replaceAll(",", "\n"));
      continue;
    }
    if (field.kind === "list") {
      frontmatter[field.name] = splitList(value);
      continue;
    }
    if (field.name === "faq" || field.name === "sources" || field.name === "partsList") {
      frontmatter[field.name] = value;
      continue;
    }
    frontmatter[field.name] = value;
  }
  return {
    slug,
    frontmatter,
    body
  };
}
function splitList(value) {
  return value.split(/\r?\n/).map((item) => item.trim()).filter(Boolean);
}
export {
  ContentForm as C,
  frontmatterFromRequest as f
};
