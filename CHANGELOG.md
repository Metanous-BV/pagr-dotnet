# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## Unreleased

### Added

- `GetProjectsAsync` (`GET /v1/projects`) and the `Project` model: list the organisation's projects
  (paged, with the usual `ListOptions`), to get the `projectId` that template creation needs.
- `CreateTemplateAsync` (`POST /v1/projects/{projectId}/templates`): create a template, optionally
  with its template definition, sample data, translations and default language. Version 1 is a draft.
- `UpdateTemplateVersionAsync` (`PATCH /v1/templates/{templateId}/versions/{n}`): edit a version's
  content; only the arguments you set are sent.
- `UpdateDefaultLanguageAsync` (`PATCH .../versions/{n}/default-language`): set or clear a version's
  default language.
- `TemplateVersion.DefaultLanguage` and `TemplateVersion.AvailableLanguages`.

The `template`, `data` and `translations` arguments are sent as JSON objects; they accept a
`JsonElement`, a `JsonNode`, a JSON string (parsed client-side) or any serialisable object, and throw
`ArgumentException` for anything that is not a JSON object.

### Changed (API behaviour)

- `AvailableLanguages` lists every language a version can be rendered in: the default language
  first, then each translated language. Previously the API listed only the translation keys.
- A render without `language` now renders in the version's default language, and
  `RenderedDocument.Language` reports it instead of `null`. Requesting the default language is
  accepted even when the translations have no entry for it.

## 1.0.0 - 2026-08-19

Initial public release on GitHub.
