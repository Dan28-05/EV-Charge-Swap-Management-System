import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { BrandShowcaseComponent } from './brand-showcase/brand-showcase.component';

@Component({
  selector: 'app-login-page',
  imports: [BrandShowcaseComponent, RouterOutlet],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPageComponent {}
