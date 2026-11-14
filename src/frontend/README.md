# TraceForge AI — web console

Angular console for the TraceForge AI API: workspaces, membership, authorized scopes and the scope
evaluation console.

```powershell
npm install
npm start            # http://localhost:4200, proxies /api and /health to http://localhost:5080
npm run build        # production bundle in dist/
npm run build:api    # production bundle copied into ../backend/TraceForge.Api/wwwroot
npm test             # vitest
npm run format:check # prettier
```

Start the API first (`dotnet run --project ..\backend\TraceForge.Api\TraceForge.Api.csproj`).

See [the console guide](../../docs/development/frontend.md) for the architecture, the authentication and
CSRF flow, and why the console must be served from the same origin as the API.
