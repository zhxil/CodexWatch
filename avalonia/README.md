# Codex Watch taskbar edition

Current build: `dist/win-x64-taskbar-singleline/CodexQuotaTray.Avalonia.exe`.

- A 2 DIP blue progress bar below the text indicates weekly remaining quota; amber/red indicate low quota.
- One centered text row at the left of the primary Windows 11 taskbar: weekly remaining quota and today's local Token count (M = million). The widget is 300 DIP wide, with 14 DIP Microsoft YaHei UI at normal weight.
- Native layered child window with its own message loop. Transparent background; text follows SystemUsesLightTheme. Low quota uses amber/red.
- Hover shows one fixed summary above the widget. Left click only refreshes. No click-open detail card exists. Taskbar and tray right clicks share the same native menu (refresh, official usage, exit). Opening a menu dismisses the hover summary.
- Hidden Avalonia host keeps polling while the taskbar component runs. If the child window is destroyed it is recreated on the next five-second check.
- Existing quota and local daily Token source remain unchanged. Local token counts are not account-wide quota and include recorded input/cache usage.

Verified on this machine: Windows 11 centered taskbar, 125% scaling, transparent left placement, live quota/Token rendering, hover details, child-window recreation (without restarting Explorer).

Boundaries: taskbar placement currently targets the primary horizontal, centered taskbar. Left-aligned taskbar buttons cause the component to hide to avoid covering buttons; tray access remains available. Widgets use a conservative 180 DIP left inset. Explorer restart, auto-hide, other display scales and light/dark switching have not been tested end-to-end. No shell processes or system taskbar preferences are modified.

Build: `dotnet publish CodexQuotaTray.Avalonia.csproj --no-restore -c Release -r win-x64 --self-contained true -o dist/win-x64-taskbar-singleline`



Hover details use a compact themed card with aligned quota and token values, automatic height, and no empty short-window section.


Interaction verification: moving within the widget preserves hover coordinates; left click opens zero cards; real taskbar and tray menu contents were compared and matched.


Refresh feedback starts before local Token scanning: taskbar status text plus a blue sweep while pending, then a short result message and return to the Token total. Idle animation timer is stopped; Windows animation preference disables the sweep.



Registry-based startup registration has been removed. Automatic startup is currently disabled.

A session-local named mutex enforces a single instance before UI initialization. Verified: three repeated launches exit without replacing the original process; restart after forced termination succeeds.
