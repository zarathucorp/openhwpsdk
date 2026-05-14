# Documentation Site

The documentation site is built with MkDocs and deployed to GitHub Pages through GitHub Actions.

## Local Preview

```powershell
python -m pip install -r requirements-docs.txt
mkdocs serve
```

Open the local URL printed by MkDocs, usually:

```text
http://127.0.0.1:8000/
```

Build the static site:

```powershell
mkdocs build --clean --site-dir site
```

The generated `site/` directory is ignored by Git.

## GitHub Pages Deployment

The workflow is defined in:

```text
.github/workflows/docs.yml
```

On pull requests, it builds the site to catch broken configuration and Markdown problems. On pushes to `main`, it uploads the generated `site/` directory as a GitHub Pages artifact and deploys it with the official Pages action.

In the repository settings, configure Pages to use GitHub Actions as the source:

```text
Settings > Pages > Build and deployment > Source: GitHub Actions
```

The workflow still builds documentation when Pages is not enabled, but it skips deployment and emits a notice. After enabling Pages, re-run the workflow or push another documentation change to publish the site.

## Documentation Structure

```text
docs/
|-- index.md
|-- getting-started.md
|-- cli-reference.md
|-- image-replacement.md
|-- markdown-hwpx-validation-workflow.md
|-- hwp-authoring-feature-roadmap.md
|-- submission-hwpx-code-issues.md
|-- table-edit-improvement-plan.md
`-- assets/
```

Keep `README.md` short and user-facing. Put long workflows, command matrices, and design notes under `docs/` so the GitHub front page stays readable.
