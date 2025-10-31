import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'kds', pathMatch: 'full' },
  {
    path: 'kds',
    loadComponent: () => import('./kds/kds-board.component').then((m) => m.KdsBoardComponent),
  },
  { path: '**', redirectTo: 'kds' },
];
