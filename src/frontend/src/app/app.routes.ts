import { Routes } from '@angular/router';
import { administratorGuard, authGuard, guestGuard } from './core/guards';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'workspaces' },
  {
    path: 'login',
    canActivate: [guestGuard],
    title: 'Sign in · TraceForge AI',
    loadComponent: () => import('./features/auth/login.page').then((module) => module.LoginPage),
  },
  {
    path: 'register',
    canActivate: [guestGuard],
    title: 'Create account · TraceForge AI',
    loadComponent: () =>
      import('./features/auth/register.page').then((module) => module.RegisterPage),
  },
  {
    path: 'workspaces',
    canActivate: [authGuard],
    title: 'Workspaces · TraceForge AI',
    loadComponent: () =>
      import('./features/workspaces/workspace-list.page').then(
        (module) => module.WorkspaceListPage,
      ),
  },
  {
    path: 'workspaces/:workspaceId',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/workspaces/workspace-layout.page').then(
        (module) => module.WorkspaceLayoutPage,
      ),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'overview' },
      {
        path: 'overview',
        title: 'Workspace · TraceForge AI',
        loadComponent: () =>
          import('./features/workspaces/overview.page').then((module) => module.OverviewPage),
      },
      {
        path: 'members',
        title: 'Members · TraceForge AI',
        loadComponent: () =>
          import('./features/workspaces/members.page').then((module) => module.MembersPage),
      },
      {
        path: 'scopes',
        title: 'Authorized scopes · TraceForge AI',
        loadComponent: () =>
          import('./features/workspaces/scopes.page').then((module) => module.ScopesPage),
      },
      {
        path: 'evaluate',
        title: 'Scope evaluation · TraceForge AI',
        loadComponent: () =>
          import('./features/workspaces/evaluate.page').then((module) => module.EvaluatePage),
      },
    ],
  },
  {
    path: 'admin',
    canActivate: [administratorGuard],
    title: 'Administration · TraceForge AI',
    loadComponent: () => import('./features/admin/admin.page').then((module) => module.AdminPage),
  },
  {
    path: '**',
    title: 'Not found · TraceForge AI',
    loadComponent: () => import('./features/not-found.page').then((module) => module.NotFoundPage),
  },
];
