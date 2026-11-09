# Documentation deployment

Public URL: https://lestercorderomurillo.github.io/radiant-game-engine/

`.github/workflows/docs.yml` regenerates the API from the C# sources and XML documentation on `main`, builds the guides and website, and deploys to GitHub Pages on every push to `main`. It can also be run manually from Actions → Deploy documentation. Repository Settings → Pages must use **GitHub Actions** as its source.

The workflow installs .NET 8, Node 22 and pnpm 10, restores engine package metadata, and installs the frozen pnpm lockfile. No personal access token or deployment secret is needed; deployment uses the scoped `GITHUB_TOKEN` and OIDC permissions.

For a local production build matching this project URL:

```sh
DOCS_BASE_PATH=/radiant-game-engine/ eng/docs.sh build
```

`DOCS_BASE_PATH` defaults to `/` for local development. Pages supplies the production base path automatically, including when a custom domain is configured. The router, API data, images and bundled assets use that prefix. The build emits an HTML entry point for every guide and API type, so direct links and refreshes work on static hosting, plus a custom 404 page.

Check deployment status in the repository's Actions tab. The deploy job reports the public URL; an unsuccessful build does not replace the last successful deployment.
