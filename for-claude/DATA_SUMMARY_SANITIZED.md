# Sanitized data summary

The real JSON data is not included because it can contain client names, addresses, logins, passwords, and local paths.

At the time of packaging, the app data roughly contains:

- Projects: multiple local development/support projects.
- Connections: a mix of AnyDesk and RDP entries.
- Notes: global notes plus project-imported/project-specific text notes.
- Tasks: active task reminders with importance and project links.
- Browser links: AI services, mail/web services, and user-added links.
- Command recipes: examples include ping, RDP, and port checks.

Important: passwords are expected to be stored only through DPAPI in the real app data. Do not design anything that logs or exports them as plaintext.

## Workflow interpretation

The user switches between:

- project folder/files;
- remote access;
- notes;
- screenshots;
- task reminders;
- browser services;
- command snippets;
- backup/context generation.

Good feature ideas should connect these surfaces instead of adding another isolated list to maintain.
