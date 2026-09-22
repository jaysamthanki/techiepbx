# Techie.Pbx.Web

UI conventions (see also the root CLAUDE.md).

**UI**
- **Allowed client libraries (and nothing else without asking):** Bootstrap, **bootstrap-table**,
  **sweetalert2**, and **htmx**. All vendored locally under wwwroot/lib — no CDN, no npm build step.
- **Modals:** create/edit/delete **forms live in Bootstrap modals** — a server-rendered partial
  (htmx `hx-get` loads it into the modal body, the form posts via htmx and swaps back validation
  errors or a 204 + `HX-Trigger`). **sweetalert2 is only for alerts, confirms and toasts** —
  never for forms.
- **htmx** is the workhorse: Razor Pages returns HTML partials, and htmx handles table refresh,
  modal submit, and polling (`hx-trigger="every 5s"`) for live status like registration state.
  JavaScript we write ourselves stays minimal — a few lines of glue, not a framework.
- Lists are **tables** (bootstrap-table); **rows are clickable and open the edit form** - no stack
  of action buttons on the right of each row. Other row-level actions (show secret, regenerate,
  delete) live as buttons in the edit modal's footer, so the table stays clean and edit is one
  click anywhere on the row.
- API controllers use the same Entra ID cookie as the pages. Only our own pages call the API.
