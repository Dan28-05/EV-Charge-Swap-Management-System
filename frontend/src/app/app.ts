import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AppDialogComponent } from './core/components/app-dialog/app-dialog.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, AppDialogComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {}
