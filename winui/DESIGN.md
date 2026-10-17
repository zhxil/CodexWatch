# Codex Watch monitoring design

The first view must answer, without navigation: remaining quota, automatic reset date/time, manual reset opportunities and their expiry, today's local Token count, and whether the values are fresh.

Domain: weekly quota, short quota windows, automatic reset, banked/manual reset credits, credit expiry, local Token aggregates, cached vs live snapshots.
Color world: quiet Windows light/dark surfaces; neutral text; blue for available quota; amber for low quota; restrained red for near depletion. Color carries status rather than decorating the surface.
Signature: a remaining-quota ring paired with an explicit automatic reset date and countdown; separate manual reset credit count and earliest expiry below it.
Avoided defaults: a large hero number above a redundant bar; settings appended below monitoring content; a large expanding popover; hiding local statistics behind an account network request.

Window: 360 x 440 logical pixels, bounded to monitor work area. Pages share the same size and position. Taskbar click opens a popover. Pinning keeps it available while using another application. The overview and settings are exclusive pages; Back and Escape from settings return to overview.

Overview order:
1. Quiet header and account plan.
2. Compact quota card: ring on the left (percent remaining), automatic reset date/time and countdown on the right. Short quota data appears only if the server returns a genuine short window.
3. Manual reset card: authoritative availableCount, earliest expiry if details exist. Never infer count from detail array length. Unknown is distinct from zero. Open official usage to use a reset; this tool does not redeem reset credits.
4. Today's local Token value with local update time. Load independently, keep today's last valid value during account failures, and label reading/unavailable states clearly.
5. Fixed freshness/refresh/official-usage footer.

Settings: alerts on/off; remaining-quota warning and critical thresholds; automatic reset lead time; manual reset expiry lead time; refresh interval; quiet hours. Save/test actions stay fixed at the bottom. Only the parameter region scrolls.

Data checks: the installed app-server protocol and a real read both returned rateLimitResetCredits. The live read confirmed availableCount=3 and three separate expiry timestamps. The earlier client discarded this top-level field while selecting rateLimitsByLimitId.codex. The earlier UI also awaited account success before publishing its separately-started Token task.

Taskbar choice: option B, native segmented capsule, 14 DIP medium text, 204 DIP total width / 32 DIP capsule height; quota region measures its rendered text and adds 7 DIP padding per side. Quota and Token are separate regions, with configured low-quota warning colors. Existing click-to-open behavior is retained.
