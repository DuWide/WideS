# WideS / Dev Cockpit - context for Claude

This folder is a sanitized source snapshot for review. It intentionally excludes user data, build output, publish output, and secrets.

## What this app is

WideS is a local Windows desktop cockpit for a developer/support workflow. It is a WPF app on .NET, stores local JSON data, and keeps connection passwords through Windows DPAPI.

The user wants practical, high-leverage features, not more manual forms, CRM-like reporting, or extra bookkeeping.

## Current product shape

- Projects: local project/work folders, workspace launch, backup, context builder, project notes/tasks/connections.
- Notes: global notes and project-specific notes.
- Tasks: due dates/times, importance color, reminders, archive, task attachments.
- Connections: AnyDesk/RDP cards, DPAPI password storage, RDP `cmdkey` integration.
- Browser links: categorized links such as AI, mail, common services.
- Command recipes: commands with variables, execute/copy flow.
- DropZone: route dropped files into work-day folders.
- Backup/context: create zip backups and selected context files.
- Global hotkeys: quick note/task creation.
- First run/login/settings: username, password, auth toggle.
- Setup packaging: `setup/` contains installer-related scripts.

## Real usage pattern

The user works with local client/project folders, remote connections, 1C/Hikvision/support-like tasks, notes, screenshots, archives, and quick browser links.

Useful proposals should reduce context switching, automate capture/routing, or make daily work faster. Avoid proposals that require the user to manually fill in reports/incidents just to generate a summary later.

## Strong preference from user

- Dark UI, calm colors, compact layout.
- Icons are preferred over large text buttons, but icons must look polished.
- Project-local items must stay inside project views; global sections are only for general/non-project items.
- List mode should be a real narrow one-column list, not tile cards.
- Secrets must not appear in logs.
- Setup for sharing must not contain the user's personal data.

## Files to inspect first

- `src/MainWindow.xaml`
- `src/MainWindow.xaml.cs`
- `src/MainWindow.Projects.cs`
- `src/MainWindow.Notes.cs`
- `src/MainWindow.Tasks.cs`
- `src/MainWindow.Connections.cs`
- `src/MainWindow.Browser.cs`
- `src/Models.cs`
- `src/ProjectStore.cs`
- `src/AppPaths.cs`
- `src/ThemeService.cs`
- `src/RdpHelper.cs`
- `src/TaskNotificationService.cs`
- `src/CopyForAiService.cs`
- `setup/README.md`
- `setup/WideS.iss`

## What to propose

Please propose 3-5 genuinely new "wow" product directions that fit this workflow. Each proposal should include:

1. What new section or capability appears in the app.
2. Why it is useful specifically for this user workflow.
3. What data it needs.
4. What can be implemented locally without cloud services.
5. MVP scope for a first version.

Do not propose generic CRM, reports, calendars, ticket trackers, or dashboards that require extra manual input.
